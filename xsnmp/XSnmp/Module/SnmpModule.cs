// ******************************************************************
// Copyright (c) 2010 Careminster Limited, Melanie Thielker and
// the Meta7 Team ( MagneMetaverse Research Inc )
//
// All rights reserved
//
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Net;
using System.Threading;
using log4net;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.CoreModules.Framework.EventQueue;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Region.Framework.Scenes;
using System.Data;
using OpenSim.Services.Interfaces;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib;


// 
// Note to self :
// ISnmpModule snmp = Scene.RequestModuleInterface<ISnmpModule>();
//[06:55] Melanie_t: if (snmp != null)
//[06:55] Melanie_t: {0
//[06:55] Melanie_t: snmp->Trap("Help!");
//[06:55] Melanie_t: }

// Mib Description : see m7mib.txt

namespace Careminster.Modules.Snmp
{
    public class SnmpModule : ISharedRegionModule, ISnmpModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private List<Scene> m_Scenes = new List<Scene>();
        private bool m_Enabled = false;
        private IConfigSource m_Config;

        private ObjectIdentifier ctrapBoot = new ObjectIdentifier(new uint[] { 1, 3, 6, 1,4,1,1212,3}) ;
        private ObjectIdentifier ctrapColdStart = new ObjectIdentifier(new uint[] { 1, 3, 6, 1,4,1,1212,1,3}) ;
        private ObjectIdentifier ctrapLinkUpDown = new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4,1,1212,1,4 } );
        private ObjectIdentifier ctrapWatchDog = new ObjectIdentifier(new uint[] { 1, 3, 6, 1,4,1,1212,4}) ;
        private ObjectIdentifier ctrapDebug = new ObjectIdentifier(new uint[] { 1, 3, 6, 1,4,1,1212,5}) ;
        private ObjectIdentifier ctrapXMRE = new ObjectIdentifier(new uint[] { 1, 3, 6, 1,4,1,1212,6}) ;

        // 
        // Snmp related stuf
        //
        //private TrapV1Message m_trap = new TrapV1Message(VersionCode.V1);
        private List<IPEndPoint> m_nms = new List<IPEndPoint>();
        private IPAddress m_ipLocal;
	private IPAddress m_ipDebug ;
	private IPAddress m_ipNode ;				/* This is a Fake IP */
								/* It should be used to enable NMS discovery */
								/* This IP should be unique per region and will never be used directly */

        
        private int m_port;

        public enum gravity { ok, warning, minor, major, crital };
		
        
        public void Initialise(IConfigSource config)
        {
            Watchdog.OnWatchdogTimeout += WatchdogTimeout;

            IConfig snmpConfig = config.Configs["Snmp"];

            if (snmpConfig == null)
            {
                m_log.Info("[XSnmp] module not configured");
                return;
            }
            else
            {
                m_Enabled = snmpConfig.GetBoolean("Enabled", false);
               
                if (!m_Enabled)
                    return;
            }

            m_Enabled = true;

            m_Config = config;
            //
            // Settings our stuff
            //
            
	    m_ipDebug=IPAddress.Parse("192.168.0.202");
            int m_tempPort = snmpConfig.GetInt("Port", 162);
            string m_tempIp = snmpConfig.GetString("IPNode", "192.168.0.201");
	    m_ipNode=IPAddress.Parse(m_tempIp);
            m_tempIp = snmpConfig.GetString("IP", "127.0.0.1");
            string[] nmslist = m_tempIp.Split(new char[] { ' ' });
            foreach (string ip in nmslist)
            {
                m_nms.Add(new IPEndPoint(IPAddress.Parse(ip), m_tempPort));
                m_log.InfoFormat("[XSnmp] NMS set to {0}:{1} ", m_tempIp, m_tempPort);
            }
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            lock (m_Scenes)
            {
                if (m_Scenes.Count == 0)
                {
                    FirstTimeInit();

                    if (!m_Enabled)
                        return;
                }
                m_Scenes.Add(scene);
            }

            m_log.Info("[SNMP] Activated SNMP module");

            scene.RegisterModuleInterface<ISnmpModule>(this);
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;
			
			m_log.Debug("[Snmp] Agent loaded");

            
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_Scenes.Remove(scene);
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
            if (!m_Enabled)
                return;
            m_log.Info("[SNMP]: Shutting down SNMP module.");
        }

        public string Name
        {
            get { return "XSnmpModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        private void FirstTimeInit()
        {
            IConfig snmpConfig = m_Config.Configs["Snmp"];
        }
/*

Generic Trap Events

*/
        public void Critical(string message, Scene scene)
        {
            Trap((int)gravity.crital, message, scene);
        }

        public void Warning(string message, Scene scene)
        {
            Trap((int)gravity.warning, message, scene);
        }

        public void Major(string message, Scene scene)
        {
            Trap((int)gravity.major, message, scene);
        }
/*

Specific bootTrap events
 trap 	: ctrapBoot
 key	: 2
 Param1 : SimName
 Param2 : data (string)
*/

	public void BootInfo(string data, Scene scene){
	    Variable vmes = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212,2 }),
                                      new OctetString(data));
            Variable vsim = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212, 1 }),
                          new OctetString(scene.RegionInfo.RegionName));

            List<Variable> vList = new List<Variable>();
            vList.Add(vmes);
            vList.Add(vsim);
            TrapV1Message m_trap = new TrapV1Message(VersionCode.V1, m_ipNode,
                                            new OctetString("public"),
					    ctrapBoot,
                                            GenericCode.EnterpriseSpecific,
                                            2,
                                            0,
                                            vList);
            //
            foreach (IPEndPoint ip in m_nms)
                m_trap.Send(ip);

}


//
// SimBoot related stuff
//
//  Event Seq : 
//   ColdStart event 
//   LinkDown Event ( raised a critcal alarm )
//	bootTrap events for each critical steps
//   LinkUp Event (Clear the previous Linkdown alarm
//


        /**
                 * @brief Send a trap event to a supevisor.
                 * @param code = Code gravity 
                 * @param simname = Region Name
                 * @param Message = Message sent in the event
                 * @return : void 
                 */
        public void Trap(int code, string Message, Scene scene)
        {
            
            Variable vmes = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212,2 }),
                                      new OctetString(Message));
            Variable vsim = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212, 1 }),
                          new OctetString(scene.RegionInfo.RegionName));

            List<Variable> vList = new List<Variable>();
            vList.Add(vmes);
            vList.Add(vsim);
            TrapV1Message m_trap = new TrapV1Message(VersionCode.V1, m_ipNode,
                                            new OctetString("public"),
                                            new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4 }),
                                            GenericCode.EnterpriseSpecific,
                                            code,
                                            0,
                                            vList);
            //
            foreach (IPEndPoint ip in m_nms)
                m_trap.Send(ip);

            // m_log.DebugFormat("[XSnmp] Trap sent to {0}:{1} ", m_tempIp, m_tempPort);            
        }

        /**
         * @brief Send a Coldstart trap event to a supevisor.
         * @param step  = Describe wich step in the boot process we are.
         *              0 : Boot done
         *              1 : Step xxxxx
         *              2 : Step yyyyy
         * @param simname = Region Name
         * @return : void 
         */

        public void ColdStart(int step, Scene scene)
        {

            Variable vsim = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212, 1 }),
                                          new OctetString(scene.RegionInfo.RegionName));
            Variable vdata = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212, 2 }),
                                          new OctetString("Boot step "+step));

            List<Variable> vList = new List<Variable>();
            vList.Add(vsim);
            vList.Add(vdata);
            TrapV1Message m_trap = new TrapV1Message(VersionCode.V1, m_ipNode,
                                            new OctetString("public"),
					    ctrapColdStart,
                                            GenericCode.ColdStart,
                                            step,
                                            0,
                                            vList);
            //
            foreach (IPEndPoint ip in m_nms)
                m_trap.Send(ip);
        }


	public void LinkDown(Scene scene)
        {

            Variable vsim = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212, 1 }),
                                          new OctetString(scene.RegionInfo.RegionName));

            List<Variable> vList = new List<Variable>();
            vList.Add(vsim);
            TrapV1Message m_trap = new TrapV1Message(VersionCode.V1, m_ipNode,
                                            new OctetString("public"),
                                            ctrapLinkUpDown,
					    GenericCode.EnterpriseSpecific,
                                            98,
                                            0,
                                            vList);
            //
            foreach (IPEndPoint ip in m_nms)
                m_trap.Send(ip);
        }

	public void LinkUp(Scene scene)
        {

            Variable vsim = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212, 1 }),
                                          new OctetString(scene.RegionInfo.RegionName));
            
            List<Variable> vList = new List<Variable>();
            vList.Add(vsim);
            TrapV1Message m_trap = new TrapV1Message(VersionCode.V1, m_ipNode,
                                            new OctetString("public"),
                                            ctrapLinkUpDown,
					    GenericCode.EnterpriseSpecific,
                                            99,
                                            0,
                                            vList);
            //
            foreach (IPEndPoint ip in m_nms)
                m_trap.Send(ip);
        }





        public void Shutdown(int step, Scene scene)
        {

            Variable vsim = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212, 2 }),
                                          new OctetString(scene.RegionInfo.RegionName));
            Variable vdata = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212, 1 }),
                                          new OctetString("Shutdown step " + step));

            List<Variable> vList = new List<Variable>();
            vList.Add(vsim);
            vList.Add(vdata);
            TrapV1Message m_trap = new TrapV1Message(VersionCode.V1, IPAddress.Loopback,
                                            new OctetString("public"),
						ctrapBoot,
                                            //new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1 }),
                                            GenericCode.LinkDown,
                                            step,
                                            0,
                                            vList);
            //
            foreach (IPEndPoint ip in m_nms)
                m_trap.Send(ip);



            //m_log.DebugFormat("[XSnmp] Trap sent to {0}:{1} ", m_tempIp, m_tempPort);            
        }

//
//
// Critical events : One threads is down 
// means the sim is unstable
// No recovery possible
//
        private void WatchdogTimeout(Thread thread, int lastTick)
        {
	  Variable vdata = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212, 1 }),
	                                            new OctetString("the thread "+thread.Name +" is "+thread.ThreadState) );
	  

            List<Variable> vList = new List<Variable>();
            vList.Add(vdata);
            TrapV1Message m_trap = new TrapV1Message(VersionCode.V1, m_ipNode,
                                            new OctetString("public"),
						ctrapWatchDog,
                                            //new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1 }),
					    GenericCode.EnterpriseSpecific,
                                            1,
                                            0,
                                            vList);
            //
            foreach (IPEndPoint ip in m_nms)
                m_trap.Send(ip);



            //m_log.DebugFormat("[XSnmp] Trap sent to {0}:{1} ", m_tempIp, m_tempPort);            
    }


        public void trapDebug(string Module, string Message, Scene scene)
        {
            
m_log.Info("SNMP trapDebug sent");
            Variable vmes = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212,2 }),
                                      new OctetString(Message));
            Variable vsim = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212, 1 }),
                          	      new OctetString(scene.RegionInfo.RegionName));


            Variable vmodule = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212, 3 }),
                          	      new OctetString(Module));

            List<Variable> vList = new List<Variable>();
            vList.Add(vmes);
            vList.Add(vsim);
            vList.Add(vmodule);
            TrapV1Message m_trap = new TrapV1Message(VersionCode.V1, m_ipNode,
                                            new OctetString("public"),
                                            ctrapDebug,
                                            GenericCode.EnterpriseSpecific,
                                            0,
                                            0,
                                            vList);
            //
            foreach (IPEndPoint ip in m_nms)
                m_trap.Send(ip);

            // m_log.DebugFormat("[XSnmp] Trap sent to {0}:{1} ", m_tempIp, m_tempPort);            

	}
public void trapXMRE(int data, string Message, Scene scene)
        {

m_log.Info("SNMP trapDebug sent");
            Variable vmes = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212,2 }),
                                      new OctetString(Message));
            Variable vsim = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212, 1 }),
                                      new OctetString(scene.RegionInfo.RegionName));


            Variable vmodule = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 4, 1, 1212, 3 }),
                                      new OctetString("XMREngine"));

            List<Variable> vList = new List<Variable>();
            vList.Add(vmes);
            vList.Add(vsim);
            vList.Add(vmodule);
            TrapV1Message m_trap = new TrapV1Message(VersionCode.V1, m_ipNode,
                                            new OctetString("public"),
                                            ctrapXMRE,
                                            GenericCode.EnterpriseSpecific,
                                            data,
                                            0,
                                            vList);
            //
            foreach (IPEndPoint ip in m_nms)
                m_trap.Send(ip);

            // m_log.DebugFormat("[XSnmp] Trap sent to {0}:{1} ", m_tempIp, m_tempPort);            

        }

  }
}

