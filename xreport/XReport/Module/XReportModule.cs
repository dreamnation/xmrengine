///////////////////////////////////////////////////////////////////
//
// (c) 2010 Melanie Thielker and Careminster, Limited
//
// All rights reserved
//
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Communications;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using Mono.Addins;

[assembly: Addin("XReport.Module", "1.0")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace Careminster.Modules.XEstate
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "Report")]
    public class XReportModule : INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected Scene m_Scene;
        protected DateTime m_LastCheck = DateTime.Now;

        public void Initialise(IConfigSource config)
        {
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
            m_Scene = scene;

            scene.EventManager.OnFrame += OnFrame;
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public void RemoveRegion(Scene scene)
        {
            scene.EventManager.OnFrame -= OnFrame;
        }

        public string Name
        {
            get { return "ReportModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        private void OnFrame()
        {
            TimeSpan elapsed = DateTime.Now - m_LastCheck;
            if (elapsed.Minutes >= 3)
            {
                m_LastCheck = DateTime.Now;

                m_Scene.ForEachScenePresence(delegate (ScenePresence sp)
                        {
                            if (!sp.IsChildAgent)
                            {
                                m_Scene.PresenceService.ReportAgent(sp.ControllingClient.SessionId, m_Scene.RegionInfo.RegionID);
                            }
                        });
            }
        }
    }
}
