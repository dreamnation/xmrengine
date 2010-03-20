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
            lock (m_QueueLock)
            {
                if (!m_Running)
                    return;

                if (m_EventQueue.Count >= MAXEVENTQUEUE)
                {
                    m_log.DebugFormat("[XMREngine]: event queue overflow, {0} -> {1}\n", 
                            evt.EventName, m_DescName);
                    return;
                }

                if (evt.EventName == "timer")
                {
                    if (m_TimerQueued)
                        return;
                    m_TimerQueued = true;
                }

                m_EventQueue.Enqueue(evt);
            }

            /*
             * Wake scheduler so it will call RunOne().
             */
            m_Engine.WakeUp();
        }

        /*
         * This is called in the script thread to step script until it calls
         * CheckRun().  It returns the DateTime that it should be called next.
         */
        public DateTime RunOne()
        {
            DateTime now = DateTime.UtcNow;

            /*
             * If script has called llSleep(), don't do any more until time is
             * up.
             */
            if (m_SuspendUntil > now)
            {
                return m_SuspendUntil;
            }

            /*
             * Also, someone may have called Suspend().
             */
            if (m_SuspendCount > 0) {
                return DateTime.MaxValue;
            }

            /*
             * Make sure we aren't being migrated in or out and prevent that 
             * whilst we are in here.  If migration has it locked, don't call
             * back right away, delay a bit so we don't get in infinite loop.
             */
            if (!Monitor.TryEnter (m_RunLock)) {
                return now.AddMilliseconds(3);
            }
            try
            {
                Exception e = null;

                /*
                 * Maybe we have been disposed.
                 */
                if (microthread == null)
                {
                    return DateTime.MaxValue;
                }

                /*
                 * Do some more of the last event if it didn't finish.
                 */
                if (this.eventCode != ScriptEventCode.None)
                {
                    m_LastRanAt = now;
                    e = microthread.ResumeEx (null);
                }

                /*
                 * Otherwise, maybe we can dequeue a new event and start 
                 * processing it.
                 */
                else
                {
                    EventParams evt = null;
                    lock (m_QueueLock)
                    {
                        if (m_EventQueue.Count > 0)
                        {
                            evt = m_EventQueue.Dequeue();
                            if (evt.EventName == "timer")
                            {
                                m_TimerQueued = false;
                            }
                        }
                    }

                    /*
                     * If there is no event to dequeue, don't run this script
                     * again for a very long time (unless someone queues another
                     * event before then).
                     */
                    if (evt == null)
                    {
                        return DateTime.MaxValue;
                    }

                    /*
                     * Dequeued an event, so start it going until it either 
                     * finishes or it calls CheckRun().
                     */
                    m_DetectParams = evt.DetectParams;
                    m_LastRanAt = now;
                    e = StartEventHandler ((ScriptEventCode)Enum.Parse (typeof (ScriptEventCode), 
                                                                        evt.EventName), 
                                           evt.Params);
                }

                /*
                 * Maybe it puqued.
                 */
                if (e != null)
                {
                    return HandleScriptException(e);
                }

                /*
                 * If event handler completed, get rid of detect params.
                 */
                if (this.eventCode == ScriptEventCode.None)
                {
                    m_DetectParams = null;
                }

                /*
                 * Maybe script called llResetScript().
                 * If so, reset script to initial state.
                 */
                if (m_Reset)
                {
                    m_Reset = false;
                    ResetLocked();
                }

                /*
                 * Maybe script called llDie().
                 * If so, perform deletion and get out.
                 */
                if (m_Die)
                {
                    m_SuspendUntil = DateTime.MaxValue;
                    m_Engine.World.DeleteSceneObject(m_Part.ParentGroup, false);
                    return DateTime.MaxValue;
                }
            }
            finally
            {
                Monitor.Exit(m_RunLock);
            }

            /*
             * Call this one again asap.
             */
            return DateTime.MinValue;
        }

        /*
         * Start event handler.
         * Event handler is put in suspended state at its entrypoint.
         * This method runs in minimal time.
         * Call ResumeEventHandler() to keep it running.
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
             * or when Main() returns, whichever occurs first.  It should return quickly.
             */
            return microthread.StartEx ();
        }

        /**
         * @brief There was an exception whilst starting/running a script event handler.
         *        Maybe we handle it directly or just print an error message.
         */
        private DateTime HandleScriptException(Exception e)
        {
            if (e is ScriptDeleteException)
            {
                /*
                 * Script did something like llRemoveInventory(llGetScriptName());
                 * ... to delete itself from the object.
                 */
                m_SuspendUntil = DateTime.MaxValue;
                m_log.DebugFormat("[XMREngine]: script self-delete {0}", m_ItemID);
                m_Part.Inventory.RemoveInventoryItem(m_ItemID);
                return DateTime.MaxValue;
            }

            /*
             * Some general script error.
             */
            SendErrorMessage(e);
            return DateTime.MaxValue;
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
            m_SuspendUntil = DateTime.MaxValue;
        }

        /**
         * @brief The script called llResetScript() while it was running and
         *        has suspended.  We want to reset the script to a never-has-
         *        ever-run-before state.
         *
         *        Caller must have m_RunLock locked so we know script isn't
         *        running.
         */
        public void Reset()
        {
            lock (m_RunLock) {
                ResetLocked();
            }
        }

        private void ResetLocked()
        {
            ReleaseControls();

            m_Part.Inventory.GetInventoryItem(m_ItemID).PermsMask = 0;
            m_Part.Inventory.GetInventoryItem(m_ItemID).PermsGranter = UUID.Zero;
            AsyncCommandManager.RemoveScript(m_Engine, m_LocalID, m_ItemID);

            lock (m_QueueLock)
            {
                m_EventQueue.Clear();               // no events queued
                m_TimerQueued = false;              // ... not even a timer event
            }
            this.eventCode = ScriptEventCode.None;  // not processing an event
            m_DetectParams = null;                  // not processing an event
            m_SuspendUntil = DateTime.MinValue;     // not doing llSleep()

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

                /*
                 * See if MigrateOutEventHandler() has been called.
                 * If so, dump our stack to the stream then suspend.
                 */
                if ((migrateOutStream != null) && !migrateComplete) {

                    /*
                     * Puque our stack to the output stream.
                     * But otherwise, our state remains intact.
                     */
                    subsArray[(int)SubsArray.BEAPI] = beAPI;
                    continuation.Save (migrateOutStream, subsArray, dllsArray);

                    /*
                     * We return here under two circumstances:
                     *  1) the script state has been written out to the migrateOutStream
                     *  2) the script state has been read in from the migrateOutStream
                     */
                    migrateComplete = true;
                }

                /*
                 * Now we are ready to suspend the microthread.
                 * This is like a longjmp() to the most recent StartEx() or ResumeEx().
                 */
                suspendOnCheckRunTemp = false;
                MMRUThread.Suspend ();
            }
        }
    }
}
