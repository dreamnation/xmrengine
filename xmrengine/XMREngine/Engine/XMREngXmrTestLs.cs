/***************************************************\
 *  COPYRIGHT 2010, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using OpenSim.Region.ScriptEngine.XMREngine;
using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenSim.Region.ScriptEngine.XMREngine
{
    public partial class XMREngine {

        private void XmrTestLs(string[] args)
        {
            bool flagFull   = false;
            bool flagQueues = false;
            bool flagTopCPU = false;
            int maxScripts  = 0x7FFFFFFF;
            int numScripts  = 0;
            XMRInstance[] instances;

            /*
             * Decode command line options.
             */
            for (int i = 0; i < args.Length; i ++) {
                if (args[i] == "-full") {
                    flagFull = true;
                    continue;
                }
                if (args[i].StartsWith("-max=")) {
                    try {
                        maxScripts = Convert.ToInt32(args[i].Substring(5));
                    } catch (Exception e) {
                        m_log.Error("[XMREngine]: bad max " + args[i].Substring(5) + ": " + e.Message);
                        return;
                    }
                    continue;
                }
                if (args[i] == "-queues") {
                    flagQueues = true;
                    continue;
                }
                if (args[i] == "-topcpu") {
                    flagTopCPU = true;
                    continue;
                }
                if (args[i][0] == '-') {
                    m_log.Error("[XMREngine]: unknown option " + args[i]);
                    m_log.Info("[XMREngine]: options: -full -max=<number> -queues -topcpu");
                    return;
                }
            }

            /*
             * Scan instance list to find those that match selection criteria.
             */
            if (!Monitor.TryEnter(m_InstancesDict, 100)) {
                m_log.Error("[XMREngine]: deadlock m_LockedDict=" + m_LockedDict);
                XMRInstance ins = m_RunInstance;
                if (ins != null) {
                    m_log.Info("[XMREngine]: running...");
                    ins.RunTestLs(true);
                }
                ins = m_RemovingInstance;
                if (ins != null) {
                    m_log.Info("[XMREngine]: removing...");
                    ins.RunTestLs(true);
                }
                return;
            }
            try
            {
                instances = new XMRInstance[m_InstancesDict.Count];
                foreach (XMRInstance ins in m_InstancesDict.Values)
                {
                    if (InstanceMatchesArgs(ins, args)) {
                        instances[numScripts++] = ins;
                    }
                }
            } finally {
                Monitor.Exit(m_InstancesDict);
            }

            /*
             * Maybe sort by descending CPU time.
             */
            if (flagTopCPU) {
                Array.Sort<XMRInstance>(instances, CompareInstancesByCPUTime);
            }

            /*
             * Print the entries.
             */
            for (int i = 0; (i < numScripts) && (i < maxScripts); i ++) {
                instances[i].RunTestLs(flagFull);
            }

            /*
             * Print number of scripts that match selection criteria,
             * even if we were told to print fewer.
             */
            Console.WriteLine("total of {0} script(s)", numScripts);

            /*
             * If -queues given, print out queue contents too.
             */
            if (flagQueues) {
                XMRInstance rins = m_RunInstance;
                if (rins != null) {
                    Console.WriteLine("running {0} {1}",
                            rins.m_ItemID.ToString(),
                            rins.m_DescName);
                }
                DateTime suntil = m_SleepUntil;
                if (suntil > DateTime.MinValue) {
                    Console.WriteLine("sleeping until {0}", suntil.ToString());
                }
                Console.WriteLine("last ran at {0}", m_LastRanAt.ToString());
                LsQueue("start", m_StartQueue, args);
                LsQueue("sleep", m_SleepQueue, args);
                LsQueue("yield", m_YieldQueue, args);
            }
        }

        private static int CompareInstancesByCPUTime(XMRInstance a, XMRInstance b)
        {
            if (a == null) {
                return (b == null) ? 0 : 1;
            }
            if (b == null) {
                return -1;
            }
            return b.m_CPUTime - a.m_CPUTime;
        }

        private void LsQueue(string name, XMRInstQueue queue, string[] args)
        {
            Console.WriteLine("Queue " + name + ":");
            lock (queue) {
                for (XMRInstance inst = queue.PeekHead(); inst != null; inst = inst.m_NextInst) {
                    try {

                        /*
                         * Try to print instance name.
                         */
                        if (InstanceMatchesArgs(inst, args)) {
                            Console.WriteLine("   " + inst.m_ItemID.ToString() + " " + inst.m_DescName);
                        }
                    } catch (Exception e) {

                        /*
                         * Sometimes there are instances in the queue that are disposed.
                         */
                        Console.WriteLine("   " + inst.m_ItemID.ToString() + " " + inst.m_DescName + ": " + e.Message);
                    }
                }
            }
        }

        private bool InstanceMatchesArgs(XMRInstance ins, string[] args)
        {
            bool hadSomethingToCompare = false;

            for (int i = 0; i < args.Length; i ++)
            {
                if (args[i][0] != '-') {
                    hadSomethingToCompare = true;
                    if (ins.m_DescName.Contains(args[i])) return true;
                    if (ins.m_ItemID.ToString().Contains(args[i])) return true;
                    if (ins.m_AssetID.ToString().Contains(args[i])) return true;
                }
            }
            return !hadSomethingToCompare;
        }
    }
}
