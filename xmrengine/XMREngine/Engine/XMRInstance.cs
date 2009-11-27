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
                m_Apis[api] = am.CreateApi(api);
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

                if (!m_IsIdle)
                {
                    m_IsIdle = m_Loader.RunOne();
                    if (m_IsIdle)
                        m_DetectParams = null;
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
    }
}
