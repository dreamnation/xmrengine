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

//
// Mib Description (prototype)
// Sacha 040810 Creation
// root : 1.3.6.1.3   (experimental branch)
//  root.gridid (1 meta7, 2 xxxx, 3 yyyyy)
//  root.gridid.1 ActualBootStatus 
//  root.gridid.1000 Trap data
//  root.gridid.1000.1 SimName
//  root.gridid.1000.2 Text 
//  
//  The AlertCode is part of the TrapHeader event
//


namespace Careminster.Modules.Snmp
{
    public class SnmpModule : ISharedRegionModule, ISnmpModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private List<Scene> m_Scenes = new List<Scene>();
        private bool m_Enabled = false;
        private IConfigSource m_Config;
        
        // 
        // Snmp related stuf
        //
        //private TrapV1Message m_trap = new TrapV1Message(VersionCode.V1);
        private List<IPEndPoint> m_nms = new List<IPEndPoint>();
        private IPAddress m_ipLocal;
        
        private int m_port;

        public enum gravity { ok, warning, minor, major, crital };
		
        
        public void Initialise(IConfigSource config)
        {
            Watchdog.OnWatchdogTimeout += WatchdogTimeout;

            IConfig snmpConfig = config.Configs["Snmp"];

            if (snmpConfig == null)
            {
                m_log.Info("[XSnmp] module not found");
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
            
            int m_tempPort = snmpConfig.GetInt("Port", 162);
            string m_tempIp = snmpConfig.GetString("IP", "127.0.0.1");
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

        /**
                 * @brief Send a trap event to a supevisor.
                 * @param code = Code gravity 
                 * @param simname = Region Name
                 * @param Message = Message sent in the event
                 * @return : void 
                 */
        public void Trap(int code, string Message, Scene scene)
        {
            
            Variable vmes = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 3, 1, 1000,2 }),
                                      new OctetString(Message));
            Variable vsim = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 3, 1, 1000, 1 }),
                          new OctetString(scene.RegionInfo.RegionName));

            List<Variable> vList = new List<Variable>();
            vList.Add(vmes);
            vList.Add(vsim);
            TrapV1Message m_trap = new TrapV1Message(VersionCode.V1, IPAddress.Loopback,
                                            new OctetString("public"),
                                            new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 3 }),
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

            Variable vsim = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 3, 1, 1000, 1 }),
                                          new OctetString(scene.RegionInfo.RegionName));
            Variable vdata = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 3, 1, 1000, 1 }),
                                          new OctetString("Boot step "+step));

            List<Variable> vList = new List<Variable>();
            vList.Add(vsim);
            vList.Add(vdata);
            TrapV1Message m_trap = new TrapV1Message(VersionCode.V1, IPAddress.Loopback,
                                            new OctetString("public"),
                                            new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 3,1 }),
                                            GenericCode.ColdStart,
                                            step,
                                            0,
                                            vList);
            //
            foreach (IPEndPoint ip in m_nms)
                m_trap.Send(ip);
          
            

            //m_log.DebugFormat("[XSnmp] Trap sent to {0}:{1} ", m_tempIp, m_tempPort);            
        }

        public void Shutdown(int step, Scene scene)
        {

            Variable vsim = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 3, 1, 1000, 1 }),
                                          new OctetString(scene.RegionInfo.RegionName));
            Variable vdata = new Variable(new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 3, 1, 1000, 1 }),
                                          new OctetString("Shutdown step " + step));

            List<Variable> vList = new List<Variable>();
            vList.Add(vsim);
            vList.Add(vdata);
            TrapV1Message m_trap = new TrapV1Message(VersionCode.V1, IPAddress.Loopback,
                                            new OctetString("public"),
                                            new ObjectIdentifier(new uint[] { 1, 3, 6, 1, 3, 1 }),
                                            GenericCode.LinkDown,
                                            step,
                                            0,
                                            vList);
            //
            foreach (IPEndPoint ip in m_nms)
                m_trap.Send(ip);



            //m_log.DebugFormat("[XSnmp] Trap sent to {0}:{1} ", m_tempIp, m_tempPort);            
        }

        private void WatchdogTimeout(Thread thread, int lastTick)
        {
        }
    }
}

