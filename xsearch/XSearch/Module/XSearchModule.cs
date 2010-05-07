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

[assembly: Addin("XSearch.Module", "1.0")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace Careminster.Modules.XEstate
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "XSearch")]
    public class XSearchModule : INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected Scene m_Scene;
        protected bool m_Enabled = false;

        public void Initialise(IConfigSource config)
        {
            IConfig searchConfig = config.Configs["Search"];
            if (searchConfig != null)
            {
                m_Enabled = true;
            }
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
            m_Scene = scene;

            scene.EventManager.OnNewClient += OnNewClient;
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public void RemoveRegion(Scene scene)
        {
            scene.EventManager.OnNewClient -= OnNewClient;
        }

        public string Name
        {
            get { return "SearchModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        private void OnNewClient(IClientAPI client)
        {
        }
    }
}
