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
        private XMREngine m_Engine = null;

        public XMRSched(XMREngine engine)
        {
            m_Engine = engine;
            m_Thread = new Thread(Run);
            m_Thread.Priority = ThreadPriority.BelowNormal;
            m_Thread.Start();
        }

        public void Stop()
        {
            m_RunIt = false;
        }

        public void Shutdown()
        {
            m_Thread.Join();
            m_Thread = null;
            m_Engine = null;
        }

        public void Run()
        {
            m_log.Debug("[XMREngine]: Scheduler running");

            while (m_RunIt)
            {
                m_Engine.RunOneCycle();
                ///Thread.Sleep(10);
            }
        }
    }
}
