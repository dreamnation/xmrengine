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

// This class exists in the main app domain
//
namespace OpenSim.Region.ScriptEngine.XMREngine
{
    public partial class XMRInstance
    {

        // In case Dispose() doesn't get called, we want to be sure to clean
        // up.  This makes sure we decrement m_CompiledScriptRefCount.
        ~XMRInstance()
        {
            Dispose();
        }

        /**
         * @brief Clean up stuff.
         *        We specifically leave m_DescName intact for 'xmr test ls' command.
         */
        public void Dispose()
        {
            /*
             * Tell script stop executing next time it calls CheckRun().
             */
            suspendOnCheckRunHold = true;

            /*
             * Wait for it to stop executing and prevent it from starting again
             * as it can't run without a microthread.  Take its continuation
             * away from it too while we're at it.
             */
            lock (m_RunLock)
            {
                if (microthread != null)
                {
                    CheckRunLockInvariants(true);
                    microthread.Dispose ();
                    microthread = null;
                }
                continuation = null;
            }

            /*
             * Don't send us any more events.
             */
            if (m_Part != null)
            {
                m_Part.RemoveScriptEvents(m_ItemID);
                AsyncCommandManager.RemoveScript(m_Engine, m_LocalID, m_ItemID);
                m_Part = null;
            }

            /*
             * Let script methods get garbage collected if no one else is using
             * them.
             */
            if (m_ObjCode != null)
            {
                lock (m_CompileLock)
                {
                    ScriptObjCode objCode;

                    if (m_CompiledScriptObjCode.TryGetValue(m_AssetID, 
                                                            out objCode) &&
                        (objCode == m_ObjCode) && 
                        (-- m_CompiledScriptRefCount[m_AssetID] == 0))
                    {
                        m_CompiledScriptObjCode.Remove(m_AssetID);
                        m_CompiledScriptRefCount.Remove(m_AssetID);
                    }
                }
                m_ObjCode = null;
            }
        }

        // Called by 'xmr test top' console command
        // to dump this script's state to console
        //  Sacha 
        public void RunTestTop()
        {
          if (m_InstEHSlice > 0){
            Console.WriteLine(m_DescName);
            Console.WriteLine("    m_LocalID      = " + m_LocalID);
            Console.WriteLine("    m_ItemID       = " + m_ItemID);
            Console.WriteLine("    m_AssetID      = " + m_AssetID);
            Console.WriteLine("    m_StartParam   = " + m_StartParam);
            Console.WriteLine("    m_PostOnRez    = " + m_PostOnRez);
            Console.WriteLine("    m_StateSource  = " + m_StateSource);
            Console.WriteLine("    m_SuspendCount = " + m_SuspendCount);
            Console.WriteLine("    m_SleepUntil   = " + m_SleepUntil);
            Console.WriteLine("    m_IState       = " + m_IState.ToString());
            Console.WriteLine("    m_Die          = " + m_Die);
            Console.WriteLine("    m_StateCode    = " + GetStateName(stateCode));
            Console.WriteLine("    eventCode      = " + eventCode.ToString());
            Console.WriteLine("    m_LastRanAt    = " + m_LastRanAt.ToString());
            Console.WriteLine("    heapLeft/Limit = " + heapLeft + "/" + heapLimit);
            Console.WriteLine("    m_InstEHEvent  = " + m_InstEHEvent.ToString());
            Console.WriteLine("    m_InstEHSlice  = " + m_InstEHSlice.ToString());
          }
        }

        // Called by 'xmr test ls' console command
        // to dump this script's state to console
        public void RunTestLs(bool flagFull)
        {
            if (flagFull) {
                Console.WriteLine(m_DescName);
                Console.WriteLine("    m_LocalID       = " + m_LocalID);
                Console.WriteLine("    m_ItemID        = " + m_ItemID);
                Console.WriteLine("    m_AssetID       = " + m_AssetID);
                Console.WriteLine("    m_StartParam    = " + m_StartParam);
                Console.WriteLine("    m_PostOnRez     = " + m_PostOnRez);
                Console.WriteLine("    m_StateSource   = " + m_StateSource);
                Console.WriteLine("    m_SuspendCount  = " + m_SuspendCount);
                Console.WriteLine("    m_SleepUntil    = " + m_SleepUntil);
                Console.WriteLine("    m_IState        = " + m_IState.ToString());
                Console.WriteLine("    m_Die           = " + m_Die);
                Console.WriteLine("    m_StateCode     = " + GetStateName(stateCode));
                Console.WriteLine("    eventCode       = " + eventCode.ToString());
                Console.WriteLine("    m_LastRanAt     = " + m_LastRanAt.ToString());
                Console.WriteLine("    m_RunOnePhase   = " + m_RunOnePhase);
                Console.WriteLine("    m_CheckRunLine  = " + m_CheckRunLine.ToString());
                Console.WriteLine("    suspOnCkRunHold = " + suspendOnCheckRunHold);
                Console.WriteLine("    suspOnCkRunTemp = " + suspendOnCheckRunTemp);
                Console.WriteLine("    m_CheckRunPhase = " + m_CheckRunPhase);
                Console.WriteLine("    heapLeft/Limit  = " + heapLeft + "/" + heapLimit);
                Console.WriteLine("    m_InstEHEvent   = " + m_InstEHEvent.ToString());
                Console.WriteLine("    m_InstEHSlice   = " + m_InstEHSlice.ToString());
                Console.WriteLine("    m_CPUTime       = " + m_CPUTime);
                lock (m_QueueLock)
                {
                    Console.WriteLine("    m_Running       = " + m_Running);
                    foreach (EventParams evt in m_EventQueue)
                    {
                        Console.WriteLine("        evt.EventName   = " + evt.EventName);
                    }
                }
            } else {
                Console.WriteLine("{0} {1} {2} {3} {4}", 
                        m_ItemID, 
                        m_CPUTime.ToString().PadLeft(9), 
                        m_InstEHEvent.ToString().PadLeft(9), 
                        m_IState.ToString().PadRight(10), 
                        m_DescName);
            }
        }

        /**
         * @brief For a given stateCode, get a mask of the low 32 event codes
         *        that the state has handlers defined for.
         */
        public int GetStateEventFlags(int stateCode)
        {
            if ((stateCode < 0) ||
                (stateCode >= objCode.scriptEventHandlerTable.GetLength(0)))
            {
                return 0;
            }

            int code = 0;
            for (int i = 0 ; i < 32; i ++)
            {
                if (objCode.scriptEventHandlerTable[stateCode, i] != null)
                {
                    code |= 1 << i;
                }
            }

            return code;
        }

        /**
         * @brief Get the .state file name.
         */
        public static string GetStateFileName(string scriptBasePath, UUID itemID)
        {
            return Path.Combine(scriptBasePath, itemID.ToString() + ".state");
        }

        /**
         * @brief Decode state code (int) to state name (string).
         */
        public string GetStateName(int stateCode)
        {
            if ((objCode.stateNames != null) && (stateCode >= 0) && (stateCode < objCode.stateNames.Length))
            {
                return objCode.stateNames[stateCode];
            }
            return stateCode.ToString();
        }

        /**
         * @brief various gets & sets.
         */
        public int StartParam
        {
            get { return m_StartParam; }
            set { m_StartParam = value; }
        }

        public SceneObjectPart SceneObject
        {
            get { return m_Part; }
        }

        public DetectParams[] DetectParams
        {
            get { return m_DetectParams; }
            set { m_DetectParams = value; }
        }

        public UUID ItemID
        {
            get { return m_ItemID; }
        }

        public UUID AssetID
        {
            get { return m_AssetID; }
        }

        public bool Running
        {
            get { return m_Running; }
            set
            {
                lock (m_QueueLock)
                {
                    m_Running = value;
                    if (!value)
                    {
                        m_EventQueue.Clear();
                        for (int i = m_EventCounts.Length; -- i >= 0;) m_EventCounts[i] = 0;
                    }
                }
            }
        }
    }
}
