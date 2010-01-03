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
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);


        private int m_SuspendCount = 0;
        private bool m_Running = true;
        private bool m_Scheduled = false;
        private XMRLoader m_Loader = null;
        private IScriptEngine m_Engine = null;
        private SceneObjectPart m_Part = null;
        private uint m_LocalID = 0;
        private UUID m_ItemID;
        private UUID m_AssetID;
        private string m_DllName;
        private bool m_IsIdle = true;
        private Object m_RunLock = new Object();
        private bool m_TimerQueued = false;
        private Queue<EventParams> m_EventQueue = new Queue<EventParams>();
        private DetectParams[] m_DetectParams = null;
        private DateTime m_Suspend = new DateTime(0);
        private bool m_Reset = false;
        private bool m_Die = false;
        private int m_StateCode = 0;
        private int m_StartParam = 0;

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
            lock (m_RunLock)
            {
                m_SuspendCount++;

                CheckRunStatus();
            }
        }

        public void Resume()
        {
            if (m_SuspendCount > 0)
                m_SuspendCount--;

            CheckRunStatus();
        }

        public bool Running
        {
            get
            {
                return m_Running;
            }

            set
            {
                lock (m_RunLock)
                {
                    m_Running = value;

                    if (!m_Running)
                        m_EventQueue.Clear();

                    CheckRunStatus();
                }
            }
        }

        private void CheckRunStatus()
        {
            if ((!m_Running) || (m_SuspendCount > 0))
            {
                if (m_Scheduled)
                    Deschedule();
            }
            else
            {
                if (!m_Scheduled)
                    Schedule();
            }
        }

        private void Schedule()
        {
            m_Scheduled = true;
            ((XMREngine)m_Engine).WakeUp();
        }

        private void Deschedule()
        {
            m_Scheduled = false;
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
            lock (m_RunLock)
            {
                m_Part.RemoveScriptEvents(m_ItemID);
                AsyncCommandManager.RemoveScript(m_Engine, m_LocalID, m_ItemID);
                m_Loader.Dispose();
                m_Loader = null;
            }
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

            lock (m_RunLock)
            {
                if (!m_Running)
                    return;

                if (evt.EventName == "timer")
                {
                    if (m_TimerQueued)
                        return;
                    m_TimerQueued = true;
                }

                m_EventQueue.Enqueue(evt);

                CheckRunStatus();
            }
        }

        public DateTime RunOne()
        {
            lock (m_RunLock)
            {
                if (!m_Scheduled)
                    return DateTime.MaxValue;

                if (m_Suspend > DateTime.UtcNow)
                    return m_Suspend;

                if (!m_IsIdle)
                {
                    try
                    {
                        m_IsIdle = m_Loader.RunOne();
                    }
                    catch (Exception e)
                    {
                        m_log.Error("[XMREngine]: Exception while running script event. Disabling script.\n" + e.ToString());
                        m_SuspendCount++;
                        CheckRunStatus();
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
                        Monitor.Exit(m_RunLock);
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
                else
                {
                    if (m_EventQueue.Count < 1)
                    {
                        Deschedule();
                        return DateTime.MinValue;
                    }

                    EventParams evt = m_EventQueue.Dequeue();

                    if (evt.EventName == "timer")
                        m_TimerQueued = false;

                    m_IsIdle = false;

                    m_DetectParams = evt.DetectParams;

                    m_Loader.PostEvent(evt.EventName, evt.Params);
                }
            }
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
            m_Suspend = DateTime.UtcNow + TimeSpan.FromMilliseconds(ms);
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

            m_EventQueue.Clear();
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
            lock (m_RunLock)
            {
                return m_Loader.GetSnapshot();
            }
        }

        public void RestoreSnapshot(Byte[] data)
        {
            m_IsIdle = !m_Loader.RestoreSnapshot(data);

            if (!m_IsIdle)
                CheckRunStatus();
        }

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
