//////////////////////////////////////////////////////////////
//
// Copyright (c) 2009 Careminster Limited and Melanie Thielker
//
// All rights reserved
//

using System;
using System.Collections.Generic;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenSim.Region.ScriptEngine.Shared.Api;
using OpenSim.Region.ScriptEngine.XMREngine.Loader;

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

        private Dictionary<string,IScriptApi> m_Apis =
                new Dictionary<string,IScriptApi>();

        public void Suspend()
        {
            m_SuspendCount++;

            CheckRunStatus();
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
                m_Running = value;

                CheckRunStatus();
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

        public XMRInstance(XMRLoader loader)
        {
            m_Loader = loader;

            ApiManager am = new ApiManager();

            foreach (string api in am.GetApis())
            m_Apis[api] = am.CreateApi(api);
        }

        public void Dispose()
        {
            m_Loader.Dispose();
            m_Loader = null;
        }
    }
}
