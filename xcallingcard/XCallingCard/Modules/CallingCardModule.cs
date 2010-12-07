using System;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using Mono.Addins;

[assembly: Addin("XAbuse.Module", "1.0")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace Careminster.XCallingCard.Modules
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "XCallingCard")]
    public class CallingCardModule : ISharedRegionModule, ICallingCardModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        protected List<Scene> m_Scenes = new List<Scene>();
        protected bool m_Enabled = true;

        public void Initialise(IConfigSource source)
        {
            IConfig ccConfig = source.Configs["XCallingCard"];
            if (ccConfig != null)
                m_Enabled = ccConfig.GetBoolean("Enabled", true);
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_Scenes.Add(scene);

            scene.RegisterModuleInterface<ICallingCardModule>(this);
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_Scenes.Remove(scene);

            scene.EventManager.OnNewClient -= OnNewClient;

            scene.UnregisterModuleInterface<ICallingCardModule>(this);
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;
            scene.EventManager.OnNewClient += OnNewClient;
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public string Name
        {
            get { return "XCallingCardModule"; }
        }

        private void OnNewClient(IClientAPI client)
        {
            client.OnOfferCallingCard += OnOfferCallingCard;
            client.OnAcceptCallingCard += OnAcceptCallingCard;
            client.OnDeclineCallingCard += OnDeclineCallingCard;
        }

        private void OnOfferCallingCard(IClientAPI client, UUID destID, UUID transactionID)
        {
        }

        // Create a calling card in the user's inventory. This is called
        // from direct calling card creation, when the offer is forwarded,
        // and from the friends module when the friend is confirmed.
        // Because of the latter, it will send a bulk inventory update
        // if the receiving user is in the same simulator.
        public void CreateCallingCard(UUID userID, UUID creatorID, UUID folderID)
        {
            IUserAccountService userv = m_Scenes[0].UserAccountService;
            if (userv == null)
                return;

            UserAccount info = userv.GetUserAccount(UUID.Zero, creatorID);
            if (info == null)
                return;

            IInventoryService inv = m_Scenes[0].InventoryService;
            if (inv == null)
                return;

            if (folderID == UUID.Zero)
            {
                InventoryFolderBase folder = inv.GetFolderForType(userID,
                        AssetType.CallingCard);

                if (folder == null) // Nowhere to put it
                    return;

                folderID = folder.ID;
            }

            m_log.DebugFormat("[XCALLINGCARD]: Creating calling card for {0} in inventory of {1}", info.Name, userID);

            InventoryItemBase item = new InventoryItemBase();
            item.AssetID = UUID.Zero;
            item.AssetType = (int)AssetType.CallingCard;
            item.BasePermissions = (uint)(PermissionMask.Copy | PermissionMask.Modify);
            item.EveryOnePermissions = (uint)PermissionMask.None;
            item.CurrentPermissions = item.BasePermissions;
            item.NextPermissions = item.EveryOnePermissions;

            item.ID = UUID.Random();
            item.CreatorId = creatorID.ToString();
            item.Owner = userID;
            item.GroupID = UUID.Zero;
            item.GroupOwned = false;
            item.Folder = folderID;

            item.CreationDate = Util.UnixTimeSinceEpoch();
            item.InvType = (int)InventoryType.CallingCard;
            item.Flags = 0;

            item.Name = info.Name;
            item.Description = "";

            item.SalePrice = 10;
            item.SaleType = (byte)SaleType.Not;

            inv.AddItem(item);

            IClientAPI client = LocateClientObject(userID);
            if (client != null)
                client.SendBulkUpdateInventory(item);
        }

        private void OnAcceptCallingCard(IClientAPI client, UUID transactionID, UUID folderID)
        {
        }

        private void OnDeclineCallingCard(IClientAPI client, UUID transactionID)
        {
        }

        public IClientAPI LocateClientObject(UUID agentID)
        {
            Scene scene = GetClientScene(agentID);
            if (scene == null)
                return null;

            ScenePresence presence = scene.GetScenePresence(agentID);
            if (presence == null)
                return null;

            return presence.ControllingClient;
        }

        private Scene GetClientScene(UUID agentId)
        {
            lock (m_Scenes)
            {
                foreach (Scene scene in m_Scenes)
                {
                    ScenePresence presence = scene.GetScenePresence(agentId);
                    if (presence != null)
                    {
                        if (!presence.IsChildAgent)
                            return scene;
                    }
                }
            }
            return null;
        }
    }
}
