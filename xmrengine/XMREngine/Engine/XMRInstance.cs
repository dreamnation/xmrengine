//////////////////////////////////////////////////////////////
//
// Copyright (c) 2009 Careminster Limited and Melanie Thielker
//
// All rights reserved
//

using System;
using System.Threading;
using System.Collections.Generic;
using OpenMetaverse;
using System.Collections.Generic;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenSim.Region.ScriptEngine.Shared;
using OpenSim.Region.ScriptEngine.Shared.Api;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Region.ScriptEngine.XMREngine.Loader;
using OpenSim.Region.Framework.Scenes;

// This class exists in the main app domain
//
namespace OpenSim.Region.ScriptEngine.XMREngine
{
    public class XMRInstance : IDisposable
    {
        private int m_SuspendCount = 0;
        private bool m_Running = true;
        private bool m_Scheduled = false;
        private XMRLoader m_Loader = null;
        private IScriptEngine m_Engine = null;
        private SceneObjectPart m_Part = null;
        private uint m_LocalID = 0;
        private UUID m_ItemID;
        private string m_DllName;
        private bool m_IsIdle = true;
        private Object m_RunLock = new Object();
        private bool m_TimerQueued = false;
        private Queue<EventParams> m_EventQueue = new Queue<EventParams>();
        private DetectParams[] m_DetectParams = null;
        private DateTime m_Suspend = new DateTime(0);
        private bool m_Reset = false;

        private Dictionary<string,IScriptApi> m_Apis =
                new Dictionary<string,IScriptApi>();

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
        }

        private void Deschedule()
        {
            m_Scheduled = false;
        }

        public XMRInstance(XMRLoader loader, IScriptEngine engine,
                SceneObjectPart part, uint localID, UUID itemID, string dllName)
        {
            m_Loader = loader;
            m_Engine = engine;
            m_Part = part;
            m_LocalID = localID;
            m_ItemID = itemID;
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

            if(!m_Loader.Load(m_DllName))
            {
                m_Loader.Dispose();
                throw new Exception("Error loading script");
            }

            m_Part.SetScriptEvents(m_ItemID, 8); // TODO (Touch)
        }

        public void Dispose()
        {
            m_Part.SetScriptEvents(m_ItemID, 0);
            AsyncCommandManager.RemoveScript(m_Engine, m_LocalID, m_ItemID);
            m_Loader.Dispose();
            m_Loader = null;
        }

        public void PostEvent(EventParams evt)
        {
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

        public void RunOne()
        {
            lock (m_RunLock)
            {
                if (!m_Scheduled)
                    return;

                if (m_Suspend > DateTime.UtcNow)
                    return;

                if (!m_IsIdle)
                {
                    m_IsIdle = m_Loader.RunOne();
                    if (m_IsIdle)
                        m_DetectParams = null;

                    if (m_Reset)
                    {
                        m_Reset = false;
                        Reset();
                    }
                }
                else
                {
                    if (m_EventQueue.Count < 1)
                    {
                        Deschedule();
                        return;
                    }

                    EventParams evt = m_EventQueue.Dequeue();

                    if (evt.EventName == "timer")
                        m_TimerQueued = false;

                    m_IsIdle = false;

                    m_DetectParams = evt.DetectParams;

                    m_Loader.PostEvent(evt.EventName, evt.Params);
                }
            }
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
    }
}
