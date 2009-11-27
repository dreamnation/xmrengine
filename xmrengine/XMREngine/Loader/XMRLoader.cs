//////////////////////////////////////////////////////////////
//
// Copyright (c) 2009 Careminster Limited and Melanie Thielker
//
// All rights reserved
//

using System;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Lifetime;
using OpenSim.Region.ScriptEngine.Shared.Api.Runtime;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using MMR;

namespace OpenSim.Region.ScriptEngine.XMREngine.Loader
{
    public class XMRLoader : MarshalByRefObject, IDisposable, ISponsor
    {
        private ScriptBaseClass m_ScriptBase;
        private int m_Renew = 10;
        private ScriptWrapper m_Wrapper = null;
        private string m_DllName;

        public override Object InitializeLifetimeService()
        {
            ILease lease = (ILease)base.InitializeLifetimeService();

            if (lease.CurrentState == LeaseState.Initial)
            {
                lease.InitialLeaseTime = TimeSpan.FromSeconds(10);
                lease.RenewOnCallTime = TimeSpan.FromSeconds(10);
                lease.SponsorshipTimeout = TimeSpan.FromSeconds(20);

                lease.Register(this);
            }

            return lease;
        }

        public XMRLoader()
        {
            m_ScriptBase = new ScriptBaseClass();
        }

        public void Dispose()
        {
            if (m_Wrapper != null)
                m_Wrapper.Dispose();
            m_Wrapper = null;

            ILease lease = (ILease)GetLifetimeService();

            lease.Unregister(this);
            m_Renew = 0;
        }

        public TimeSpan Renewal(ILease lease)
        {
            if (m_Renew == 0)
                lease.Unregister(this);
            return TimeSpan.FromSeconds(m_Renew);
        }

        public void InitApi(string name, IScriptApi api)
        {
            m_ScriptBase.InitApi(name, api);
        }

        public bool Load(string dllName)
        {
            m_DllName = dllName;

            try
            {
                m_Wrapper = ScriptWrapper.CreateScriptInstance(dllName);
            }
            catch (Exception e)
            {
                System.Console.WriteLine("[XMREngine]: Error loading script\n" + e.ToString());
                return false;
            }

            if (m_Wrapper == null)
                return false;

            m_Wrapper.beAPI = m_ScriptBase;
            m_Wrapper.alwaysSuspend = true;

            return true;
        }

        public void PostEvent(string eventName, Object[] args)
        {
            m_Wrapper.StartEventHandler(eventName, args);
        }

        public bool RunOne()
        {
            return m_Wrapper.ResumeEventHandler();
        }

        public void Reset()
        {
            m_Wrapper.Dispose();
            m_Wrapper = null;

            Load(m_DllName);
        }
    }
}
