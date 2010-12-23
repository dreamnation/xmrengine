using System;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Communications;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Client;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using Mono.Addins;

[assembly: Addin("XMute.Module", "1.0")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace Careminster.Modules.XMute
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "XMute")]
    public class XMuteModule : ISharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private bool m_Enabled = true;
        private List<Scene> m_SceneList = new List<Scene>();

        public void Initialise(IConfigSource config)
        {
            IConfig cnf = config.Configs["Messaging"];
            if (cnf == null)
            {
                m_Enabled = false;
                return;
            }

            if (cnf != null && cnf.GetString("MuteListModule", "None") !=
                    "XMute")
            {
                m_Enabled = false;
                return;
            }
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            lock (m_SceneList)
            {
                m_SceneList.Add(scene);

                scene.EventManager.OnNewClient += OnNewClient;
            }
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            lock (m_SceneList)
            {
                m_SceneList.Remove(scene);
            }
        }

        public void PostInitialise()
        {
            if (!m_Enabled)
                return;

            m_log.Debug("[MUTE LIST] Mute list enabled");
        }

        public string Name
        {
            get { return "XMuteModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }
        
        public void Close()
        {
        }
       
        private void OnNewClient(IClientAPI client)
        {
            client.OnMuteListRequest += OnMuteListRequest;
        }

        private void OnMuteListRequest(IClientAPI client, uint crc)
        {
            m_log.DebugFormat("[MUTE LIST] Got mute list request for crc {0}", crc);
            string filename = "mutes"+client.AgentId.ToString();

            IXfer xfer = client.Scene.RequestModuleInterface<IXfer>();
            if (xfer != null)
            {
                xfer.AddNewFile(filename, new Byte[0]);
                client.SendMuteListUpdate(filename);
            }
        }
    }
}

