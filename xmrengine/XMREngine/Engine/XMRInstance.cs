//////////////////////////////////////////////////////////////
//
// Copyright (c) 2009 Careminster Limited and Melanie Thielker
//
// All rights reserved
//

using System;
using System.Threading;
using System.Reflection;
using System.Collections.Generic;
using OpenMetaverse;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenSim.Region.ScriptEngine.Shared;
using OpenSim.Region.ScriptEngine.Shared.Api;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Region.ScriptEngine.XMREngine.Loader;
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
    public class XMRInstance : MarshalByRefObject, IDisposable
    {
        public const int MAXEVENTQUEUE = 64;

        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private XMRLoader m_Loader = null;
        private IScriptEngine m_Engine = null;
        private SceneObjectPart m_Part = null;
        private uint m_LocalID = 0;
        private UUID m_ItemID;
        private UUID m_AssetID;
        private string m_DllName;
        private DetectParams[] m_DetectParams = null;
        private bool m_Reset = false;
        private bool m_Die = false;
        private int m_StateCode = 0;
        private int m_StartParam = 0;

        // If code needs to have both m_QueueLock and m_RunLock,
        // be sure to lock m_RunLock first then m_QueueLock, as
        // that is the order used in RunOne().
        // These locks are currently separated to allow the script
        // to call API routines that queue events back to the script.
        // If we just had one lock, then the queuing would deadlock.

        // guards m_EventQueue, m_TimerQueued, m_Running
        private Object m_QueueLock = new Object();

        // true iff allowed to accept new events
        private bool m_Running = true;

        // queue of events that haven't been acted upon yet
        private Queue<EventParams> m_EventQueue = new Queue<EventParams>();

        // true iff m_EventQueue contains a timer() event
        private bool m_TimerQueued = false;


        // guards m_IsIdle (locked whilst in ScriptWrapper running the script)
        private Object m_RunLock = new Object();

        // false iff script is running an event handler, ie, from the time its
        // event handler entrypoint is called until its event handler returns
        private bool m_IsIdle = true;

        // script won't step while > 0.  bus-atomic updates only.
        private int m_SuspendCount = 0;

        // don't run any of script until this time
        private DateTime m_SuspendUntil = DateTime.MinValue;


        private Dictionary<string,IScriptApi> m_Apis =
                new Dictionary<string,IScriptApi>();

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

        public void Suspend()
        {
            Interlocked.Increment(ref m_SuspendCount);
        }

        public void Resume()
        {
            int nowIs = Interlocked.Decrement(ref m_SuspendCount);
            if (nowIs < 0)
            {
                throw new Exception("m_SuspendCount negative");
            }
            if (nowIs == 0)
            {
                KickScheduler();
            }
        }

        public bool Running
        {
            get
            {
                return m_Running;
            }

            set
            {
                lock (m_QueueLock)
                {
                    m_Running = value;
                    if (!value)
                    {
                        m_EventQueue.Clear();
                        m_TimerQueued = false;
                    }
                }
            }
        }

        /*
         * Kick the scheduler to call our RunOne() if it is asleep.
         */
        private void KickScheduler()
        {
            ((XMREngine)m_Engine).WakeUp();
        }

        public XMRInstance(XMRLoader loader, IScriptEngine engine,
                SceneObjectPart part, uint localID, UUID itemID, UUID assetID,
                string dllName)
        {
            m_Loader = loader;
            m_Engine = engine;
            m_Part = part;
            m_LocalID = localID;
            m_ItemID = itemID;
            m_AssetID = assetID;
            m_DllName = dllName;

            ApiManager am = new ApiManager();

            foreach (string api in am.GetApis())
            {
                if (api != "LSL")
                    m_Apis[api] = am.CreateApi(api);
                else
                    m_Apis[api] = new XMRLSL_Api();

                m_Apis[api].Initialize(m_Engine, m_Part, m_LocalID, m_ItemID);
                m_Loader.InitApi(api, m_Apis[api]);
            }

            Exception loadException = m_Loader.Load(m_DllName);
            if (loadException != null)
            {
                m_Loader.Dispose();
                throw loadException;
            }

            m_Part.SetScriptEvents(m_ItemID, m_Loader.GetStateEventFlags(0));
        }

        public void Dispose()
        {
            m_Part.RemoveScriptEvents(m_ItemID);
            AsyncCommandManager.RemoveScript(m_Engine, m_LocalID, m_ItemID);
            m_Loader.Dispose();
            m_Loader = null;
        }

        public void PostEvent(EventParams evt)
        {
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

            lock (m_QueueLock)
            {
                if (!m_Running)
                    return;

                if (m_EventQueue.Count >= MAXEVENTQUEUE)
                {
                    m_log.DebugFormat("[XMREngine]: event queue overflow, {0} -> {1}:{2}\n", 
                            evt.EventName, m_Part.Name, 
                            m_Part.Inventory.GetInventoryItem(m_ItemID).Name);
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
            KickScheduler();
        }

        /*
         * This is called in the script thread to step script until it calls CheckRun().
         */
        public DateTime RunOne()
        {
            /*
             * If script has called llSleep(), don't do any more until time is up.
             */
            if (m_SuspendUntil > DateTime.UtcNow)
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
             * whilst we are in here.
             */
            lock (m_RunLock)
            {

                /*
                 * Maybe we can dequeue a new event and start processing it.
                 */
                if (m_IsIdle)
                {
                    EventParams evt = null;
                    lock (m_QueueLock)
                    {
                        if (m_EventQueue.Count > 0)
                        {
                            evt = m_EventQueue.Dequeue();
                            if (evt.EventName == "timer")
                                m_TimerQueued = false;
                        }
                    }

                    if (evt == null)
                    {
                        return DateTime.MaxValue;
                    }

                    m_IsIdle = false;
                    m_DetectParams = evt.DetectParams;

                    try
                    {
                        m_Loader.PostEvent(evt.EventName, evt.Params);
                    }
                    catch (Exception e)
                    {
                        m_log.Error("[XMREngine]: Exception while starting script event. Disabling script.\n" + e.ToString());
                        Interlocked.Increment(ref m_SuspendCount);
                    }
                }

                /*
                 * New or old event, step script until it calls CheckRun().
                 */
                try
                {
                    m_IsIdle = m_Loader.RunOne();
                }
                catch (Exception e)
                {
                    m_log.Error("[XMREngine]: Exception while running script event. Disabling script.\n" + e.ToString());
                    Interlocked.Increment(ref m_SuspendCount);
                }
                if (m_IsIdle)
                    m_DetectParams = null;

                if (m_Reset)
                {
                    m_Reset = false;
                    Reset();
                }

                if (m_Die)
                {
                    m_Engine.World.DeleteSceneObject(m_Part.ParentGroup, false);
                    return DateTime.MinValue;
                }

                if (m_Loader.StateCode != m_StateCode)
                {
                    m_StateCode = m_Loader.StateCode;
                    m_Part.SetScriptEvents(m_ItemID,
                            m_Loader.GetStateEventFlags(m_StateCode));
                }
            }

            /*
             * Call this one again asap.
             */
            return DateTime.MinValue;
        }

        public DetectParams GetDetectParams(int number)
        {
            if (m_DetectParams == null)
                return null;

            if (number < 0 || number >= m_DetectParams.Length)
                return null;

            return m_DetectParams[number];
        }

        public void Suspend(int ms)
        {
            m_SuspendUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(ms);
            ((XMREngine)m_Engine).WakeUp();
        }

        public void ApiReset()
        {
            m_Reset = true;
        }

        public void Reset()
        {
            ReleaseControls();

            SceneObjectPart part=m_Engine.World.GetSceneObjectPart(m_LocalID);
            part.Inventory.GetInventoryItem(m_ItemID).PermsMask = 0;
            part.Inventory.GetInventoryItem(m_ItemID).PermsGranter = UUID.Zero;
            AsyncCommandManager.RemoveScript(m_Engine, m_LocalID, m_ItemID);

            lock (m_QueueLock)
            {
                m_EventQueue.Clear();
                m_TimerQueued = false;
            }
            m_DetectParams = null;

            m_Loader.Reset();

            PostEvent(new EventParams("state_entry", new Object[0], new DetectParams[0]));
        }

        public void Die()
        {
            m_Die = true;
        }

        private void ReleaseControls()
        {
            SceneObjectPart part = m_Engine.World.GetSceneObjectPart(m_LocalID);

            if (part != null)
            {
                int permsMask;
                UUID permsGranter;
                part.TaskInventory.LockItemsForRead(true);
                if (!part.TaskInventory.ContainsKey(m_ItemID))
                {
                    part.TaskInventory.LockItemsForRead(false);
                    return;
                }
                permsGranter = part.TaskInventory[m_ItemID].PermsGranter;
                permsMask = part.TaskInventory[m_ItemID].PermsMask;
                part.TaskInventory.LockItemsForRead(false);

                if ((permsMask & ScriptBaseClass.PERMISSION_TAKE_CONTROLS) != 0)
                {
                    ScenePresence presence = m_Engine.World.GetScenePresence(permsGranter);
                    if (presence != null)
                        presence.UnRegisterControlEventsToScript(m_LocalID, m_ItemID);
                }
            }
        }

        public Byte[] GetSnapshot()
        {
            /*
             * Make sure we aren't executing part of the script so it stays stable.
             */
            lock (m_RunLock)
            {
                return m_Loader.GetSnapshot();
            }
        }

        public void RestoreSnapshot(Byte[] data)
        {
            /*
             * Make sure we aren't executing part of the script so we can 
             * write it.
             */
            lock (m_RunLock)
            {
                m_IsIdle = !m_Loader.RestoreSnapshot(data);
            }

            /*
             * If we restored it in the middle of an event handler, resume 
             * event handler.
             */
            if (!m_IsIdle)
            {
                KickScheduler();
            }
        }

        /*
         * The script has executed a 'state <newState>;' command.
         * Tell outer layers to cancel any event triggers, like llListen().
         */
        public void StateChange(string newState)
        {
            AsyncCommandManager.RemoveScript(m_Engine, m_LocalID, m_ItemID);
        }
    }

    public class XMRLSL_Api : LSL_Api
    {
        protected override void ScriptSleep(int delay)
        {
            if (m_ScriptEngine is XMREngine)
            {
                XMREngine e = (XMREngine)m_ScriptEngine;

                e.Suspend(m_itemID, delay);
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

                e.Suspend(m_itemID, (int)(sec * 1000.0));
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

                e.Die(m_itemID);
            }
            else
            {
                base.llDie();
            }
        }
    }
}
