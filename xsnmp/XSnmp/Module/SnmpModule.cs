// ******************************************************************
// Copyright (c) 2010 Careminster Limited, Melanie Thielker and
// the Meta7 Team
//
// All rights reserved
//
using System;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.CoreModules.Framework.EventQueue;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Framework.Communications.Cache;
using System.Data;
using OpenSim.Services.Interfaces;
using Lextm.SharpSnmpLib.Messaging;


namespace Careminster.Modules.Snmp
{
    public class SnmpAgent : ISharedRegionModule, ISnmpModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private List<Scene> m_Scenes = new List<Scene>();
        private bool m_Enabled = false;
        private IConfigSource m_Config;
        
        public void Initialise(IConfigSource config)
        {
            IConfig snmpConfig = config.Configs["Snmp"];

            if (snmpConfig == null)
            {
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

        public void Alert(string message)
        {
        }

        public void Trap(string message)
        {
        }
    }
}

