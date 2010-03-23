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
    /****************************************************\
     *  This file contains routines called by scripts.  *
    \****************************************************/

    public class XMRLSL_Api : LSL_Api
    {
        protected override void ScriptSleep(int delay)
        {
            if (m_ScriptEngine is XMREngine)
            {
                XMREngine e = (XMREngine)m_ScriptEngine;

                e.GetInstance(m_itemID).Sleep(delay);
            }
            else
            {
                base.ScriptSleep(delay);
            }
        }

        public override void llSleep(double sec)
        {
            if (m_ScriptEngine is XMREngine)
            {
                XMREngine e = (XMREngine)m_ScriptEngine;

                e.GetInstance(m_itemID).Sleep((int)(sec * 1000.0));
            }
            else
            {
                base.llSleep(sec);
            }
        }

        public override void llDie()
        {
            if (m_ScriptEngine is XMREngine)
            {
                XMREngine e = (XMREngine)m_ScriptEngine;

                e.GetInstance(m_itemID).Die();
            }
            else
            {
                base.llDie();
            }
        }
    }

    public partial class XMRInstance
    {
        /**
         * @brief The script is calling llReset().
         *        We want to set a flag and exit out of the script immediately.
         *        The script will exit immediately as we compile in a call to
         *        CheckRun() immediately following the llResetScript() api call.
         */
        public void ApiReset()
        {
            // tell RunOne() that script called llResetScript()
            m_Reset = true;

            // tell CheckRun() to suspend microthread so RunOne() will check
            // m_Reset
            suspendOnCheckRunHold = true;
        }

        /**
         * @brief Called by consoleWrite(string) call.
         */
        public static void ConsoleWrite (XMRInstance instance, string s)
        {
            Console.WriteLine ("XMRInstance.ConsoleWrite: " + instance.m_DescName + ": " + s);
        }

        /**
         * @brief The script is calling one of the llDetected...(int number)
         *        functions.  Return corresponding DetectParams pointer.
         */
        public DetectParams GetDetectParams(int number)
        {
            if (m_DetectParams == null)
                return null;

            if (number < 0 || number >= m_DetectParams.Length)
                return null;

            return m_DetectParams[number];
        }

        /**
         * @brief Script is calling llDie, so flag the run loop to delete script
         *        once we are off the microthread stack.
         */
        public void Die()
        {
            m_Die = true;
            suspendOnCheckRunHold = true;
        }

        /**
         * @brief Called by script to sleep for the given number of milliseconds.
         */
        public void Sleep(int ms)
        {
            /*
             * Say how long to sleep.
             */
            m_SleepUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(ms);
            suspendOnCheckRunTemp = true;

            /*
             * Wake in case it is shorter than before.
             */
            m_Engine.WakeUp();
        }

        /**
         * @brief The script is executing a 'state <newState>;' command.
         * Tell outer layers to cancel any event triggers, like llListen(),
         * then tell outer layers which events the new state has handlers for.
         */
        public void StateChange()
        {
            AsyncCommandManager.RemoveScript(m_Engine, m_LocalID, m_ItemID);
            m_Part.SetScriptEvents(m_ItemID, 
                                   GetStateEventFlags(stateCode));
        }
    }
}
