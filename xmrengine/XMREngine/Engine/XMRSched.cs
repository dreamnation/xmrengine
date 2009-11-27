//////////////////////////////////////////////////////////////
//
// Copyright (c) 2009 Careminster Limited and Melanie Thielker
//
// All rights reserved
//

using System.Reflection;
using System.Threading;
using log4net;

namespace OpenSim.Region.ScriptEngine.XMREngine
{
    public class XMRSched
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private bool m_RunIt = true;
        private Thread m_Thread = null;

        public XMRSched()
        {
            m_Thread = new Thread(Run);
            m_Thread.Start();
        }

        public void Stop()
        {
            m_RunIt = false;

            m_Thread.Join();
            m_Thread = null;
        }

        public void Run()
        {
            m_log.Debug("[XMREngine]: Scheduler running");

            while (m_RunIt)
            {
                Thread.Sleep(100);
            }
        }
    }
}
