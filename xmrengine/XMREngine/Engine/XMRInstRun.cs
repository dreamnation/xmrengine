//////////////////////////////////////////////////////////////
//
// Copyright (c) 2009 Careminster Limited and Melanie Thielker
// Copyright (c) 2010 Mike Rieker, Beverly, MA, USA
//
// All rights reserved
//

using System;
using System.Threading;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Remoting.Lifetime;
using System.Security.Policy;
using System.IO;
using System.Xml;
using System.Text;
using Mono.Tasklets;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenSim.Region.ScriptEngine.Shared;
using OpenSim.Region.ScriptEngine.Shared.Api;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Region.ScriptEngine.XMREngine;
using OpenSim.Region.Framework.Scenes;
using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;
using log4net;

namespace OpenSim.Region.ScriptEngine.XMREngine
{
    public partial class XMRInstance
    {
        /************************************************************************************\
         * This module contains two externally useful methods:                              *
         *   PostEvent() - queues an event to script and wakes script thread to process it  *
         *   RunOne() - runs script for a time slice or until it volunteers to give up cpu  *
        \************************************************************************************/

        /**
         * @brief This can be called in any thread (including the script thread itself)
         *        to queue event to script for processing.
         */
        public void PostEvent(EventParams evt)
        {
            /*
             * Strip off any LSL type wrappers.
             */
            for (int i = 0 ; i < evt.Params.Length ; i++)
            {
                if (evt.Params[i] is LSL_Integer)
                    evt.Params[i] = (int)((LSL_Integer)evt.Params[i]);
                else if (evt.Params[i] is LSL_Float)
                    evt.Params[i] = (float)((LSL_Float)evt.Params[i]);
                else if (evt.Params[i] is LSL_String)
                    evt.Params[i] = (string)((LSL_String)evt.Params[i]);
                else if (evt.Params[i] is LSL_Key)
                    evt.Params[i] = (string)((LSL_Key)evt.Params[i]);
            }

            /*
             * Put event on end of event queue.
             */
            bool startIt = false;
            lock (m_QueueLock)
            {
                /*
                 * Not running means we ignore any incoming events.
                 */
                if (!m_Running)
                {
                    return;
                }

                ScriptEventCode eventCode = (ScriptEventCode)Enum.Parse (typeof (ScriptEventCode), 
                                                                         evt.EventName);

                /*
                 * Only so many of each event type allowed to queue.
                 */
                int maxAllowed = MAXEVENTQUEUE;
                if (eventCode == ScriptEventCode.timer) maxAllowed = 1;
                if (m_EventCounts[(int)eventCode] >= maxAllowed)
                {
                    return;
                }
                m_EventCounts[(int)eventCode] ++;

                /*
                 * Put event on end of instance's event queue.
                 */
                m_EventQueue.Enqueue(evt);

                /*
                 * If instance is idle (ie, not running or waiting to run),
                 * flag it to be on m_StartQueue as we are about to do so.
                 * Flag it now before unlocking so another thread won't try
                 * to do the same thing right now.
                 */
                if (m_IState == XMRInstState.IDLE) {
                    m_IState = XMRInstState.ONSTARTQ;
                    startIt = true;
                }
            }

            /*
             * If transitioned from IDLE->ONSTARTQ, actually go insert it
             * on m_StartQueue and give the RunScriptThread() a wake-up.
             */
            if (startIt) {
                m_Engine.QueueToStart(this);
            }
        }

        /*
         * This is called in the script thread to step script until it calls
         * CheckRun().  It returns what the instance's next state should be,
         * ONSLEEPQ, ONYIELDQ, SUSPENDED or FINISHED.
         */
        public XMRInstState RunOne()
        {
            DateTime now = DateTime.UtcNow;

            /*
             * If script has called llSleep(), don't do any more until time is
             * up.
             */
            m_RunOnePhase = "check m_SleepUntil";
            if (m_SleepUntil > now)
            {
                m_RunOnePhase = "return is sleeping";
                return XMRInstState.ONSLEEPQ;
            }

            /*
             * Also, someone may have called Suspend().
             */
            m_RunOnePhase = "check m_SuspendCount";
            if (m_SuspendCount > 0) {
                m_RunOnePhase = "return is suspended";
                return XMRInstState.SUSPENDED;
            }

            /*
             * Make sure we aren't being migrated in or out and prevent that 
             * whilst we are in here.  If migration has it locked, don't call
             * back right away, delay a bit so we don't get in infinite loop.
             */
            m_RunOnePhase = "lock m_RunLock";
            if (!Monitor.TryEnter (m_RunLock)) {
                m_SleepUntil = now.AddMilliseconds(3);
                m_RunOnePhase = "return was locked";
                return XMRInstState.ONSLEEPQ;
            }
            try
            {
                m_RunOnePhase = "check entry invariants";
                CheckRunLockInvariants(true);
                Exception e = null;

                /*
                 * Maybe we have been disposed.
                 */
                m_RunOnePhase = "check disposed";
                if (microthread == null)
                {
                    m_RunOnePhase = "return disposed";
                    return XMRInstState.DISPOSED;
                }

                /*
                 * Do some more of the last event if it didn't finish.
                 */
                if (this.eventCode != ScriptEventCode.None)
                {
                    m_RunOnePhase = "resume old event handler";
                    m_LastRanAt   = now;
                    m_InstEHSlice ++;
                    e = microthread.ResumeEx (null);
                }

                /*
                 * Otherwise, maybe we can dequeue a new event and start 
                 * processing it.
                 */
                else
                {
                    m_RunOnePhase = "lock event queue";
                    EventParams evt = null;
                    ScriptEventCode eventCode = ScriptEventCode.None;

                    lock (m_QueueLock)
                    {
                        m_RunOnePhase = "dequeue event";
                        if (m_EventQueue.Count > 0)
                        {
                            evt = m_EventQueue.Dequeue();
                            eventCode = (ScriptEventCode)Enum.Parse (typeof (ScriptEventCode), 
                                                                     evt.EventName);
                            m_EventCounts[(int)eventCode] --;
                        }
                    }

                    /*
                     * If there is no event to dequeue, don't run this script
                     * until another event gets queued.
                     */
                    if (evt == null)
                    {
                        m_RunOnePhase = "nothing to do";
                        return XMRInstState.FINISHED;
                    }

                    /*
                     * Dequeued an event, so start it going until it either 
                     * finishes or it calls CheckRun().
                     */
                    m_RunOnePhase  = "start event handler";
                    m_DetectParams = evt.DetectParams;
                    m_LastRanAt    = now;
                    m_InstEHEvent ++;
                    e = StartEventHandler (eventCode, evt.Params);
                }
                m_RunOnePhase = "done running";

                /*
                 * Maybe it puqued.
                 */
                if (e != null)
                {
                    HandleScriptException(e);
                    m_RunOnePhase = "return had exception";
                    return XMRInstState.FINISHED;
                }

                /*
                 * If event handler completed, get rid of detect params.
                 */
                if (this.eventCode == ScriptEventCode.None)
                {
                    m_DetectParams = null;
                }
            }
            finally
            {
                m_RunOnePhase += "; checking exit invariants and unlocking";
                CheckRunLockInvariants(false);
                Monitor.Exit(m_RunLock);
            }

            /*
             * Cycle script through the yield queue and call it back asap.
             */
            m_RunOnePhase = "last return";
            return XMRInstState.ONYIELDQ;
        }

        /**
         * @brief Immediately after taking m_RunLock or just before releasing it, check invariants.
         */
        public void CheckRunLockInvariants(bool throwIt)
        {
            /*
             * If not executing any event handler, active should be 0 indicating the microthread stack is not in use.
             * If executing an event handler, active should be -1 indicating stack is in use but suspended.
             */
            ScriptUThread uth = microthread;
            if (uth != null) {
                int active = uth.Active ();
                ScriptEventCode ec = this.eventCode;
                if (((ec == ScriptEventCode.None) && (active != 0)) ||
                    ((ec != ScriptEventCode.None) && (active >= 0))) {
                    Console.WriteLine("CheckRunLockInvariants: eventcode=" + ec.ToString() + ", active=" + active.ToString());
                    Console.WriteLine("CheckRunLockInvariants: m_RunOnePhase=" + m_RunOnePhase);
                    if (throwIt) {
                        throw new Exception("CheckRunLockInvariants: eventcode=" + ec.ToString() + ", active=" + active.ToString());
                    }
                    MMRUThread.tkill(MMRUThread.gettid(), 11); // SIGSEGV
                }
            }
        }

        /*
         * Start event handler.
         *
         * Input:
         *  eventCode  = code of event to be processed
         *  ehArgs     = arguments for the event handler
         *  this.beAPI = 'this' pointer passed to things like llSay()
         *
         * Caution:
         *  It is up to the caller to make sure ehArgs[] is correct for
         *  the particular event handler being called.  The first thing
         *  a script event handler method does is to unmarshall the args
         *  from ehArgs[] and will throw an array bounds or cast exception 
         *  if it can't.
         */
        private Exception StartEventHandler (ScriptEventCode eventCode, object[] ehArgs)
        {
            /*
             * We use this.eventCode == ScriptEventCode.None to indicate we are idle.
             * So trying to execute ScriptEventCode.None might make a mess.
             */
            if (eventCode == ScriptEventCode.None) {
                return new Exception ("Can't process ScriptEventCode.None");
            }

            /*
             * Silly to even try if there is no handler defined for this event.
             */
            if (this.objCode.scriptEventHandlerTable[this.stateCode,(int)eventCode] == null) {
                return null;
            }

            /*
             * The microthread shouldn't be processing any event code.
             * These are assert checks so we throw them directly as exceptions.
             */
            if (this.eventCode != ScriptEventCode.None) {
                throw new Exception ("still processing event " + this.eventCode.ToString ());
            }
            int active = microthread.Active ();
            if (active != 0) {
                throw new Exception ("microthread is active " + active.ToString ());
            }

            /*
             * Save eventCode so we know what event handler to run in the microthread.
             * And it also marks us busy so we can't be started again and this event lost.
             */
            this.eventCode = eventCode;
            this.ehArgs    = ehArgs;

            /*
             * We are starting from beginning of event handler, no migration streams.
             */
            this.migrateInReader  = null;
            this.migrateInStream  = null;
            this.migrateOutWriter = null;
            this.migrateOutStream = null;

            /*
             * This calls ScriptUThread.Main() directly, and returns when Main() calls Suspend()
             * or when Main() returns, whichever occurs first.
             */
            return microthread.StartEx ();
        }


        /**
         * @brief There was an exception whilst starting/running a script event handler.
         *        Maybe we handle it directly or just print an error message.
         */
        private void HandleScriptException(Exception e)
        {
            if (e is ScriptDeleteException)
            {
                /*
                 * Script did something like llRemoveInventory(llGetScriptName());
                 * ... to delete itself from the object.
                 */
                m_SleepUntil = DateTime.MaxValue;
                m_log.DebugFormat("[XMREngine]: script self-delete {0}", m_ItemID);
                m_Part.Inventory.RemoveInventoryItem(m_ItemID);
            }
            else if (e is XMRScriptResetException)
            {
                /*
                 * Script did an llDie() or llResetScript().
                 */
                if (m_Die) {
                    m_SleepUntil = DateTime.MaxValue;
                    m_Engine.World.DeleteSceneObject(m_Part.ParentGroup, false);
                } else {
                    ResetLocked("HandleScriptException");
                }
            }
            else
            {
                /*
                 * Some general script error.
                 */
                SendErrorMessage(e);
            }
            return;
        }

        /**
         * @brief There was an exception running script event handler.
         *        Display error message and disable script (in a way
         *        that the script can be reset to be restarted).
         */
        private void SendErrorMessage(Exception e)
        {
            StringBuilder msg = new StringBuilder();

            msg.Append("[XMREngine]: Exception while running script event handler.\n");

            /*
             * Display full exception message in log.
             */
            m_log.Debug(msg.ToString() + e.ToString());

            /*
             * Tell script owner what to do.
             */
            msg.Append("Part: <");
            msg.Append(m_Part.Name);
            msg.Append(">, Item: <");
            msg.Append(m_Item.Name);
            msg.Append(">\nYou must Reset script to re-enable.");

            /*
             * Remove extra garbage from error message that includes stack 
             * dump.
             */
            string[] lines = e.ToString().Split(new char[] { '\n' });
            for (int i = 0; i < lines.Length; i ++)
            {
                string line = lines[i];
                if (line != "") {
                    msg.Append('\n');
                    msg.Append(line.Replace("$MMRContableAttribute$", "$"));
                }
            }

            /*
             * Send sanitized error message to owner.
             */
            beAPI.llOwnerSay(msg.ToString());

            /*
             * Say script is sleeping for a very long time.
             * Reset() is able to cancel this sleeping.
             */
            m_SleepUntil = DateTime.MaxValue;
        }

        /**
         * @brief The user clicked the Reset Script button.
         *        We want to reset the script to a never-has-ever-run-before state.
         */
        public void Reset()
        {
        checkstate:
            XMRInstState iState = m_IState;
            Console.WriteLine("XMRInstance.Reset*: {0} : {1}", m_DescName, iState);
            switch (iState) {

                /*
                 * If it's really being constructed now, that's about as reset as we get.
                 */
                case XMRInstState.CONSTRUCT: {
                    return;
                }

                /*
                 * If it's idle, that means it is ready to receive a new event.
                 * So we lock the event queue to prevent another thread from taking
                 * it out of idle, verify that it is still in idle then transition
                 * it to resetting so no other thread will touch it.
                 */
                case XMRInstState.IDLE: {
                    lock (m_QueueLock) {
                        if (m_IState == XMRInstState.IDLE) {
                            m_IState = XMRInstState.RESETTING;
                            break;
                        }
                    }
                    goto checkstate;
                }

                /*
                 * If it's on the start queue, that means it is about to dequeue an
                 * event and start processing it.  So we lock the start queue so it
                 * can't be started and transition it to resetting so no other thread
                 * will touch it.
                 */
                case XMRInstState.ONSTARTQ: {
                    lock (m_Engine.m_StartQueue) {
                        if (m_IState == XMRInstState.ONSTARTQ) {
                            m_Engine.m_StartQueue.Remove(this);
                            m_IState = XMRInstState.RESETTING;
                            break;
                        }
                    }
                    goto checkstate;
                }

                /*
                 * If it's running, tell CheckRun() to suspend the thread then go back
                 * to see what it got transitioned to.
                 */
                case XMRInstState.RUNNING: {
                    suspendOnCheckRunHold = true;
                    lock (m_QueueLock) { }
                    goto checkstate;
                }

                /*
                 * If it's sleeping, remove it from sleep queue and transition it to
                 * resetting so no other thread will touch it.
                 */
                case XMRInstState.ONSLEEPQ: {
                    lock (m_Engine.m_SleepQueue) {
                        if (m_IState == XMRInstState.ONSLEEPQ) {
                            m_Engine.m_SleepQueue.Remove(this);
                            m_IState = XMRInstState.RESETTING;
                            break;
                        }
                    }
                    goto checkstate;
                }

                /*
                 * If it's yielding, remove it from yield queue and transition it to
                 * resetting so no other thread will touch it.
                 */
                case XMRInstState.ONYIELDQ: {
                    lock (m_Engine.m_SleepQueue) {
                        if (m_IState == XMRInstState.ONYIELDQ) {
                            m_Engine.m_YieldQueue.Remove(this);
                            m_IState = XMRInstState.RESETTING;
                            break;
                        }
                    }
                    goto checkstate;
                }

                /*
                 * If it just finished running something, let that thread transition it
                 * to its next state then check again.
                 */
                case XMRInstState.FINISHED: {
                    Sleep(1);
                    goto checkstate;
                }

                /*
                 * If it's disposed, that's about as reset as it gets.
                 */
                case XMRInstState.DISPOSED: {
                    return;
                }

                /*
                 * Some other thread is already resetting it, let it finish.
                 */
                case XMRInstState.RESETTING: {
                    return;
                }

                default: throw new Exception("bad state");
            }

            /*
             * This thread transitioned the instance to RESETTING so reset it.
             */
            Console.WriteLine("XMRInstance.Reset*: {0} : performing reset");
            lock (m_RunLock) {
                CheckRunLockInvariants(true);
                ResetLocked("Reset");
                if (m_IState != XMRInstState.RESETTING) throw new Exception("bad state");
                if (microthread.Active () < 0) {
                    microthread.Dispose ();
                    microthread = new ScriptUThread (m_StackSize, m_DescName);
                    microthread.instance = this;
                }
                m_IState = XMRInstState.ONSTARTQ;
                CheckRunLockInvariants(true);
            }

            /*
             * Queue it to start running its default start_entry() event handler.
             */
            lock (m_Engine.m_StartQueue) {
                m_Engine.m_StartQueue.InsertTail(this);
            }
        }

        /**
         * @brief The script called llResetScript() while it was running and
         *        has suspended.  We want to reset the script to a never-has-
         *        ever-run-before state.
         *
         *        Caller must have m_RunLock locked so we know script isn't
         *        running.
         */
        private void ResetLocked(string from)
        {
            ReleaseControls();

            m_Part.Inventory.GetInventoryItem(m_ItemID).PermsMask = 0;
            m_Part.Inventory.GetInventoryItem(m_ItemID).PermsGranter = UUID.Zero;
            AsyncCommandManager.RemoveScript(m_Engine, m_LocalID, m_ItemID);

            lock (m_QueueLock)
            {
                m_EventQueue.Clear();               // no events queued
                for (int i = m_EventCounts.Length; -- i >= 0;) m_EventCounts[i] = 0;
            }
            this.eventCode = ScriptEventCode.None;  // not processing an event
            m_DetectParams = null;                  // not processing an event
            m_SleepUntil   = DateTime.MinValue;     // not doing llSleep()

            /*
             * Tell next call do 'default state_entry()' to reset all global
             * vars to their initial values.
             */
            doGblInit = true;

            /*
             * Set script to 'default' state and queue call to its 
             * 'state_entry()' event handler.
             */
            stateCode = 0;
            m_Part.SetScriptEvents(m_ItemID, GetStateEventFlags(0));
            PostEvent(new EventParams("state_entry", 
                                      zeroObjectArray, 
                                      zeroDetectParams));

            /*
             * Tell CheckRun() to let script run.
             */
            suspendOnCheckRunHold = false;
            suspendOnCheckRunTemp = false;
        }

        private void ReleaseControls()
        {
            if (m_Part != null)
            {
                int permsMask;
                UUID permsGranter;
                m_Part.TaskInventory.LockItemsForRead(true);
                if (!m_Part.TaskInventory.ContainsKey(m_ItemID))
                {
                    m_Part.TaskInventory.LockItemsForRead(false);
                    return;
                }
                permsGranter = m_Part.TaskInventory[m_ItemID].PermsGranter;
                permsMask = m_Part.TaskInventory[m_ItemID].PermsMask;
                m_Part.TaskInventory.LockItemsForRead(false);

                if ((permsMask & ScriptBaseClass.PERMISSION_TAKE_CONTROLS) != 0)
                {
                    ScenePresence presence = m_Engine.World.GetScenePresence(permsGranter);
                    if (presence != null)
                    {
                        presence.UnRegisterControlEventsToScript(m_LocalID, m_ItemID);
                    }
                }
            }
        }

        /**
         * @brief The script code should call this routine whenever it is
         *        convenient to perform a migation or switch microthreads.
         *
         * @param line = script source line number (for debugging)
         */
        [MMRContableAttribute ()]
        public void CheckRun (int line)
        {
            m_CheckRunPhase = "entered";
            m_CheckRunLine = line;

            /*
             * We should never try to stop with stateChanged as once stateChanged is set to true,
             * the compiled script functions all return directly out without calling CheckRun().
             *
             * Thus any checkpoint/restart save/resume code can assume stateChanged = false.
             */
            if (stateChanged) {
                throw new Exception ("CheckRun() called with stateChanged set");
            }

            /*
             * Make sure script isn't about to run out of stack.
             */
            if ((uint)MMRUThread.StackLeft () < stackLimit) {
                throw ScriptCodeGen.outOfStackException;
            }

            while (suspendOnCheckRunHold || suspendOnCheckRunTemp) {
                m_CheckRunPhase = "top of while";

                /*
                 * See if MigrateOutEventHandler() has been called.
                 * If so, dump our stack to the stream then suspend.
                 */
                if ((migrateOutStream != null) && !migrateComplete) {

                    /*
                     * Puque our stack to the output stream.
                     * But otherwise, our state remains intact.
                     */
                    m_CheckRunPhase = "saving";
                    subsArray[(int)SubsArray.BEAPI] = beAPI;
                    continuation.Save (migrateOutStream, subsArray, dllsArray);
                    m_CheckRunPhase = "saved";

                    /*
                     * We return here under two circumstances:
                     *  1) the script state has been written out to the migrateOutStream
                     *  2) the script state has been read in from the migrateOutStream
                     */
                    migrateComplete = true;
                }

                /*
                 * Now we are ready to suspend the microthread.
                 * This is like a longjmp() to the most recent StartEx() or ResumeEx()
                 * with a simultaneous setjmp() so ResumeEx() can longjmp() back here.
                 */
                m_CheckRunPhase = "suspending";
                suspendOnCheckRunTemp = false;
                MMRUThread.Suspend ();
                m_CheckRunPhase = "resumed";
            }
            m_CheckRunPhase = "returning";
        }
    }
}
