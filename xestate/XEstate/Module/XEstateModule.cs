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
using OpenSim.Server.Base;
using OpenSim.Framework.Servers.HttpServer;
using Mono.Addins;

[assembly: Addin("XEstate.Module", "1.0")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace Careminster.Modules.XEstate
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "XEstate")]
    public class XEstateModule : ISharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected List<Scene> m_Scenes = new List<Scene>();
        protected bool m_InInfoUpdate = false;

        public bool InInfoUpdate
        {
            get { return m_InInfoUpdate; }
            set { m_InInfoUpdate = value; }
        }

        public List<Scene> Scenes
        {
            get { return m_Scenes; }
        }

        protected EstateConnector m_EstateConnector;

        public void Initialise(IConfigSource config)
        {
            int port = 0;

            IConfig estateConfig = config.Configs["Estate"];
            if (estateConfig != null)
            {
                port = estateConfig.GetInt("Port", 0);
            }

            m_EstateConnector = new EstateConnector(this);

            // Instantiate the request handler
            IHttpServer server = MainServer.GetHttpServer((uint)port);
            server.AddStreamHandler(new EstateRequestHandler(this));
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
            m_Scenes.Add(scene);

            scene.EventManager.OnNewClient += OnNewClient;
        }

        public void RegionLoaded(Scene scene)
        {
            IEstateModule em = scene.RequestModuleInterface<IEstateModule>();

            em.OnRegionInfoChange += OnRegionInfoChange;
            em.OnEstateInfoChange += OnEstateInfoChange;
            em.OnEstateMessage += OnEstateMessage;
        }

        public void RemoveRegion(Scene scene)
        {
            scene.EventManager.OnNewClient -= OnNewClient;

            m_Scenes.Remove(scene);
        }

        public string Name
        {
            get { return "EstateModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        private Scene FindScene(UUID RegionID)
        {
            foreach (Scene s in Scenes)
            {
                if (s.RegionInfo.RegionID == RegionID)
                    return s;
            }

            return null;
        }

        private void OnRegionInfoChange(UUID RegionID)
        {
            Scene s = FindScene(RegionID);
            if (s == null)
                return;

            if (!m_InInfoUpdate)
                m_EstateConnector.SendUpdateCovenant(s.RegionInfo.EstateSettings.EstateID, s.RegionInfo.RegionSettings.Covenant);
        }

        private void OnEstateInfoChange(UUID RegionID)
        {
            Scene s = FindScene(RegionID);
            if (s == null)
                return;

            if (!m_InInfoUpdate)
                m_EstateConnector.SendUpdateEstate(s.RegionInfo.EstateSettings.EstateID);
        }

        private void OnEstateMessage(UUID RegionID, UUID FromID, string FromName, string Message)
        {
            Scene senderScenes = FindScene(RegionID);
            if (senderScenes == null)
                return;

            uint estateID = senderScenes.RegionInfo.EstateSettings.EstateID;

            foreach (Scene s in Scenes)
            {
                if (s.RegionInfo.EstateSettings.EstateID == estateID)
                {
                    IDialogModule dm = s.RequestModuleInterface<IDialogModule>();

                    if (dm != null)
                    {
                        dm.SendNotificationToUsersInRegion(FromID, FromName,
                                Message);
                    }
                }
            }
            if (!m_InInfoUpdate)
                m_EstateConnector.SendEstateMessage(estateID, FromID, FromName, Message);
        }

        private void OnNewClient(IClientAPI client)
        {
            client.OnEstateTeleportOneUserHomeRequest += OnEstateTeleportOneUserHomeRequest;
            client.OnEstateTeleportAllUsersHomeRequest += OnEstateTeleportAllUsersHomeRequest;

        }

        private void OnEstateTeleportOneUserHomeRequest(IClientAPI client, UUID invoice, UUID senderID, UUID prey)
        {
            if (prey == UUID.Zero)
                return;

            if (!(client.Scene is Scene))
                return;

            Scene scene = (Scene)client.Scene;

            uint estateID = scene.RegionInfo.EstateSettings.EstateID;

            if (!scene.Permissions.CanIssueEstateCommand(client.AgentId, false))
                return;

            foreach (Scene s in Scenes)
            {
                if (s == scene)
                    continue; // Already handles by estate module
                if (s.RegionInfo.EstateSettings.EstateID != estateID)
                    continue;

                ScenePresence p = scene.GetScenePresence(prey);
                if (p != null && !p.IsChildAgent)
                {
                    p.ControllingClient.SendTeleportStart(16);
                    scene.TeleportClientHome(prey, p.ControllingClient);
                }
            }

            m_EstateConnector.SendTeleportHomeOneUser(estateID, prey);
        }

        private void OnEstateTeleportAllUsersHomeRequest(IClientAPI client, UUID invoice, UUID senderID)
        {
            if (!(client.Scene is Scene))
                return;

            Scene scene = (Scene)client.Scene;

            uint estateID = scene.RegionInfo.EstateSettings.EstateID;

            if (!scene.Permissions.CanIssueEstateCommand(client.AgentId, false))
                return;

            foreach (Scene s in Scenes)
            {
                if (s == scene)
                    continue; // Already handles by estate module
                if (s.RegionInfo.EstateSettings.EstateID != estateID)
                    continue;

                scene.ForEachScenePresence(delegate(ScenePresence p) {
                    if (p != null && !p.IsChildAgent)
                    {
                        p.ControllingClient.SendTeleportStart(16);
                        scene.TeleportClientHome(p.ControllingClient.AgentId, p.ControllingClient);
                    }
                });
            }

            m_EstateConnector.SendTeleportHomeAllUsers(estateID);
        }
    }
}
