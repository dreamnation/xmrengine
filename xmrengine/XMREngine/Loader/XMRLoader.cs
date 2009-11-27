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
        ScriptBaseClass m_ScriptBase;
        private int m_Renew = 10;

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
    }
}
