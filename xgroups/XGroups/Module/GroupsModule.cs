// ******************************************************************
// Copyright (c) 2008, 2009 Melanie Thielker
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
using MySql.Data.MySqlClient;
using System.Data;
using OpenSim.Services.Interfaces;

namespace Careminster.Modules.Groups
{
    public class GroupsModule : ISharedRegionModule, IGroupsModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private List<Scene> m_Scenes = new List<Scene>();
        private bool m_Enabled = false;
        private string m_ConnectionString;
        private IConfigSource m_Config;
        private Dictionary<UUID, UUID> m_ActiveRoles = new
                Dictionary<UUID, UUID>();
        private Dictionary<UUID, UUID> m_LastNoticeRequest = new
                Dictionary<UUID, UUID>();

        private List<UUID> m_PendingInvites = new
                List<UUID>();

        private IMessageTransferModule m_TransferModule = null;
        private IGroupchatModule m_Groupchat = null;

        public event NewGroupNotice OnNewGroupNotice;

        public void Initialise(IConfigSource config)
        {
            IConfig groupsConfig = config.Configs["Groups"];

            if (groupsConfig == null)
            {
                return;
            }
            else
            {
                m_Enabled = groupsConfig.GetBoolean("Enabled", false);
                if (!m_Enabled)
                    return;

                if (groupsConfig.GetString("Module", "Default") != "XGroups")
                {
                    m_Enabled = false;
                    return;
                }
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

            m_log.Info("[GROUPS] Activated XGroups module");

            scene.RegisterModuleInterface<IGroupsModule>(this);
            scene.EventManager.OnNewClient += OnNewClient;
            scene.EventManager.OnMakeRootAgent += OnMakeRootAgent;
            scene.EventManager.OnClientClosed += OnClientClosed;
            scene.EventManager.OnIncomingInstantMessage +=
                    OnIncomingInstantMessage;
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

            if (m_TransferModule == null)
            {
                m_TransferModule =
                        m_Scenes[0].RequestModuleInterface<IMessageTransferModule>();

                if (m_TransferModule == null)
                {
                    m_log.Error("[GROUPS]: No transfer module could be found. "+
                            "Group invites will not work.");
                }
            }

            if (m_Groupchat == null)
            {
                m_Groupchat = m_Scenes[0].RequestModuleInterface<IGroupchatModule>();
                if (m_Groupchat != null)
                    m_log.Info("[GROUPS] Found Group Chat module");
            }
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
            m_log.Info("[GROUP]: Shutting down group module.");
        }

        public string Name
        {
            get { return "XGroupsModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        private void FirstTimeInit()
        {
            IConfig groupsConfig = m_Config.Configs["Groups"];

            m_ConnectionString = groupsConfig.GetString("ConnectionString", "");
            if (m_ConnectionString == "")
            {
                m_log.ErrorFormat("[GROUPS]: No connection string");
                m_Enabled = false;
                return;
            }

            if (!OpenDatabase())
            {
                m_Enabled = false;
                return;
            }
        }

        private ScenePresence FindPresence(UUID agentID)
        {
            foreach (Scene s in m_Scenes)
            {
                ScenePresence p = s.GetScenePresence(agentID);
                if (p != null)
                    return p;
            }
            return null;
        }

        private bool OpenDatabase()
        {
            MySqlConnection connection;
            try
            {
                connection = new MySqlConnection(m_ConnectionString);

                connection.Open();
            }
            catch (MySqlException e)
            {
                m_log.ErrorFormat("[GROUPS]: Can't connect to database: {0}",
                        e.Message.ToString());

                return false;
            }

            connection.Close();
            connection.Dispose();

            return true;
        }

        private string GetRoleTitle(UUID GroupID, UUID RoleID)
        {
            MySqlConnection c =
                    new MySqlConnection(m_ConnectionString);

            c.Open();

            MySqlCommand cmd = c.CreateCommand();

            cmd.CommandText = "select RoleTitle from roles where "+
                    "GroupID = ?GroupID and RoleID = ?RoleID";

            cmd.Parameters.AddWithValue("RoleID", RoleID.ToString());
            cmd.Parameters.AddWithValue("GroupID", GroupID.ToString());

            IDataReader r = cmd.ExecuteReader();

            if (r.Read())
            {
                string name = r["RoleTitle"].ToString();

                r.Close();
                c.Close();

                return name;
            }

            r.Close();
            cmd.Dispose();
            c.Close();

            return "";
        }

        private string GetRoleName(UUID RoleID)
        {
            MySqlConnection c =
                    new MySqlConnection(m_ConnectionString);

            c.Open();

            MySqlCommand cmd = c.CreateCommand();

            cmd.CommandText = "select RoleName from roles where "+
                    "RoleID = ?RoleID";

            cmd.Parameters.AddWithValue("RoleID", RoleID.ToString());

            IDataReader r = cmd.ExecuteReader();

            if (r.Read())
            {
                string name = r["RoleName"].ToString();

                r.Close();
                c.Close();

                return name;
            }

            r.Close();
            cmd.Dispose();
            c.Close();

            return "";
        }

        private bool IsMember(UUID GroupID, UUID RoleID, UUID MemberID)
        {
            MySqlConnection c =
                    new MySqlConnection(m_ConnectionString);

            c.Open();

            MySqlCommand cmd = c.CreateCommand();

            cmd.CommandText = "select count(*) as count from rolemembers where "+
                    "GroupID = ?GroupID and "+
                    "MemberID = ?MemberID and "+
                    "RoleID = ?RoleID";

            cmd.Parameters.AddWithValue("RoleID", RoleID.ToString());
            cmd.Parameters.AddWithValue("GroupID", GroupID.ToString());
            cmd.Parameters.AddWithValue("MemberID", MemberID.ToString());

            IDataReader r = cmd.ExecuteReader();

            if (r.Read())
            {
                int count = Convert.ToInt32(r["count"]);

                r.Close();
                cmd.Dispose();
                c.Close();

                if (count > 0)
                    return true;
                return false;
            }

            r.Close();
            cmd.Dispose();
            c.Close();

            return false;
        }

        private int GetMembersCount(UUID GroupID)
        {
            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            cmd.CommandText = "select count(*) as count from members where "+
                    "GroupID = ?GroupID";

            cmd.Parameters.AddWithValue("GroupID", GroupID.ToString());

            IDataReader r = cmd.ExecuteReader();

            if (r.Read())
            {
                int count = Convert.ToInt32(r["count"]);

                r.Close();
                cmd.Dispose();

                return count;
            }

            r.Close();
            cmd.Dispose();

            return 0;
        }

        private List<GroupTitlesData> GetGroupTitles(UUID UserID, UUID GroupID)
        {
            List<GroupTitlesData> titles = new List<GroupTitlesData>();

            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            cmd.CommandText = "select RoleTitle,RoleID from roles where "+
                    "GroupID = ?GroupID";

            cmd.Parameters.AddWithValue("GroupID", GroupID.ToString());

            IDataReader r = cmd.ExecuteReader();

            while (r.Read())
            {
                UUID RoleID;
                UUID.TryParse(r["RoleID"].ToString(), out RoleID);

                if (!IsMember(GroupID, RoleID, UserID))
                    continue;

                GroupTitlesData d = new GroupTitlesData();
                d.Name = r["RoleTitle"].ToString();
                d.UUID=RoleID;
                if (m_ActiveRoles.ContainsKey(UserID))
                {
                    if (m_ActiveRoles[UserID] == d.UUID)
                        d.Selected = true;
                }
                titles.Add(d);
            }

            r.Close();
            cmd.Dispose();

            return titles;
        }

        public GroupRecord GetGroupRecord(string GroupName)
        {
            MySqlCommand cmd = new MySqlCommand();

            cmd.CommandText = "select GroupID, GroupName, AllowPublish, MaturePublish,"+
                    "Charter, FounderID, GroupPicture, MembershipFee, "+
                    "OpenEnrollment, OwnerRoleID, ShowInList from groups "+
                    "where GroupName = ?GroupName";
            cmd.Parameters.AddWithValue("GroupName", GroupName);

            return RealGetGroupRecord(cmd);
        }

        public GroupRecord GetGroupRecord(UUID GroupID)
        {
            MySqlCommand cmd = new MySqlCommand();

            cmd.CommandText = "select GroupID, GroupName, AllowPublish, MaturePublish,"+
                    "Charter, FounderID, GroupPicture, MembershipFee, "+
                    "OpenEnrollment, OwnerRoleID, ShowInList from groups "+
                    "where GroupID = ?GroupID";
            cmd.Parameters.AddWithValue("GroupID", GroupID.ToString());

            return RealGetGroupRecord(cmd);
        }

        private GroupRecord RealGetGroupRecord(MySqlCommand cmd)
        {
            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            cmd.Connection = conn;

            IDataReader r = cmd.ExecuteReader();

            if (r.Read())
            {
                GroupRecord g = new GroupRecord();
                g.GroupID = new UUID(r["GroupID"].ToString());
                g.GroupName = r["GroupName"].ToString();
                g.AllowPublish = Convert.ToInt32(r["AllowPublish"]) > 0 ?
                        true : false;
                g.MaturePublish = Convert.ToInt32(r["MaturePublish"]) > 0 ?
                        true : false;
                g.Charter = r["Charter"].ToString();
                UUID.TryParse(r["FounderID"].ToString(), out g.FounderID);
                UUID.TryParse(r["GroupPicture"].ToString(),
                        out g.GroupPicture);
                g.MembershipFee = Convert.ToInt32(r["MembershipFee"]);
                g.OpenEnrollment = Convert.ToInt32(r["OpenEnrollment"]) > 0 ?
                        true : false;
                g.ShowInList = Convert.ToInt32(r["ShowInList"]) > 0 ?
                        true : false;
                UUID.TryParse(r["OwnerRoleID"].ToString(),
                        out g.OwnerRoleID);

                r.Close();
                cmd.Dispose();

                return g;
            }

            r.Close();
            cmd.Dispose();
            return null;
        }

        public GroupMembershipData GetMembershipData(UUID GroupID, UUID UserID)
        {
            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            cmd.CommandText = "select groups.*, members.AcceptNotices, "+
                    "members.ListInProfile, members.Contribution, "+
                    "members.Active, members.ActiveRole,"+
                    "bit_or(roles.RolePowers) as GroupPowers "+
                    "from members left join groups on "+
                    "groups.GroupID=members.GroupID left join "+
                    "rolemembers on rolemembers.MemberID = members.MemberID "+
                    "left join roles on groups.GroupID=roles.GroupID and "+
                    "roles.RoleID = rolemembers.RoleID "+
                    "where members.MemberID=?MemberID and "+
                    "groups.GroupID=?GroupID group by groups.GroupID";

            cmd.Parameters.AddWithValue("MemberID", UserID.ToString());
            cmd.Parameters.AddWithValue("GroupID", GroupID.ToString());
            IDataReader r = cmd.ExecuteReader();

            if (r.Read())
            {
                GroupMembershipData g = new GroupMembershipData();

                UUID.TryParse(r["GroupID"].ToString(), out g.GroupID);
                g.GroupName = r["GroupName"].ToString();
                g.AllowPublish = Convert.ToInt32(r["AllowPublish"]) > 0 ?
                        true : false;
                g.MaturePublish = Convert.ToInt32(r["MaturePublish"]) > 0 ?
                        true : false;
                g.Charter = r["Charter"].ToString();
                UUID.TryParse(r["FounderID"].ToString(), out g.FounderID);
                UUID.TryParse(r["GroupPicture"].ToString(),
                        out g.GroupPicture);
                g.MembershipFee = Convert.ToInt32(r["MembershipFee"]);
                g.OpenEnrollment = Convert.ToInt32(r["OpenEnrollment"]) > 0 ?
                        true : false;
                g.AcceptNotices = Convert.ToInt32(r["AcceptNotices"]) > 0 ?
                        true : false;
                g.ListInProfile = Convert.ToInt32(r["ListInProfile"]) > 0 ?
                        true : false;
                g.Contribution = Convert.ToInt32(r["Contribution"]);
                g.GroupPowers = Convert.ToUInt64(r["GroupPowers"]);
                g.Active = Convert.ToInt32(r["Active"]) > 0 ?
                        true : false;
                UUID.TryParse(r["ActiveRole"].ToString(), out g.ActiveRole);
                g.GroupTitle = GetRoleTitle(g.GroupID, g.ActiveRole);

                m_ActiveRoles[UserID] = g.ActiveRole;

                r.Close();
                cmd.Dispose();

                return g;
            }

            r.Close();
            cmd.Dispose();

            return null;
        }

        public GroupMembershipData[] GetMembershipData(UUID UserID)
        {
            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            List<GroupMembershipData> m = new List<GroupMembershipData>();

            cmd.CommandText = "select groups.*, members.AcceptNotices, "+
                    "members.ListInProfile, members.Contribution, "+
                    "members.Active, members.ActiveRole,"+
                    "bit_or(roles.RolePowers) as GroupPowers "+
                    "from members left join groups on "+
                    "groups.GroupID=members.GroupID left join "+
                    "rolemembers on rolemembers.MemberID = members.MemberID "+
                    "left join roles on groups.GroupID=roles.GroupID and "+
                    "roles.RoleID = rolemembers.RoleID "+
                    "where members.MemberID=?MemberID and groups.GroupID is not null group by groups.GroupID";

            cmd.Parameters.AddWithValue("MemberID", UserID.ToString());

            IDataReader r = cmd.ExecuteReader();

            while (r.Read())
            {
                GroupMembershipData g = new GroupMembershipData();

                UUID.TryParse(r["GroupID"].ToString(), out g.GroupID);
                g.GroupName = r["GroupName"].ToString();
                g.AllowPublish = Convert.ToInt32(r["AllowPublish"]) > 0 ?
                        true : false;
                g.MaturePublish = Convert.ToInt32(r["MaturePublish"]) > 0 ?
                        true : false;
                g.Charter = r["Charter"].ToString();
                UUID.TryParse(r["FounderID"].ToString(), out g.FounderID);
                UUID.TryParse(r["GroupPicture"].ToString(),
                        out g.GroupPicture);
                g.MembershipFee = Convert.ToInt32(r["MembershipFee"]);
                g.OpenEnrollment = Convert.ToInt32(r["OpenEnrollment"]) > 0 ?
                        true : false;
                g.AcceptNotices = Convert.ToInt32(r["AcceptNotices"]) > 0 ?
                        true : false;
                g.ListInProfile = Convert.ToInt32(r["ListInProfile"]) > 0 ?
                        true : false;
                g.Contribution = Convert.ToInt32(r["Contribution"]);
                g.GroupPowers = Convert.ToUInt64(r["GroupPowers"]);
                g.Active = Convert.ToInt32(r["Active"]) > 0 ?
                        true : false;
                UUID.TryParse(r["ActiveRole"].ToString(), out g.ActiveRole);
                g.GroupTitle = GetRoleTitle(g.GroupID, g.ActiveRole);

                m_ActiveRoles[UserID] = g.ActiveRole;
                m.Add(g);
            }
            r.Close();
            cmd.Dispose();

            return m.ToArray();
        }

        private void OnNewClient(IClientAPI client)
        {
            m_LastNoticeRequest[client.AgentId] = UUID.Zero;

            // Subscribe to instant messages
            client.OnInstantMessage += OnInstantMessage;
            client.OnAgentDataUpdateRequest += OnAgentDataUpdateRequest;
            client.OnUUIDGroupNameRequest += HandleUUIDGroupNameRequest;
            client.OnRequestAvatarProperties += OnRequestAvatarProperties;
            client.OnDirFindQuery += OnDirFindQuery;
            client.OnGroupAccountSummaryRequest += OnGroupAccountSummaryRequest;
            client.OnGroupAccountTransactionsRequest += OnGroupAccountTransactionsRequest;

            SendGroupMembershipCaps(GetMembershipData(client.AgentId), client);
        }

        private void OnMakeRootAgent(ScenePresence presence)
        {
            OnAgentDataUpdateRequest(presence.ControllingClient, presence.ControllingClient.AgentId, UUID.Zero);
        }

        private void OnAgentDataUpdateRequest(IClientAPI remoteClient, UUID AgentID, UUID SessionID)
        {
            string firstname = remoteClient.FirstName;
            string lastname = remoteClient.LastName;

            GroupMembershipData[] m = GetMembershipData(remoteClient.AgentId);

            foreach (GroupMembershipData g in m)
            {
                if (g.Active)
                {

                    remoteClient.SendAgentDataUpdate(remoteClient.AgentId,
                            g.GroupID, firstname, lastname, g.GroupPowers,
                            g.GroupName, GetRoleTitle(g.GroupID, g.ActiveRole));
                    return;
                }
            }

            remoteClient.SendAgentDataUpdate(remoteClient.AgentId, UUID.Zero,
                    firstname, lastname, 0, "", "");
        }

        private void OnInstantMessage(IClientAPI client, GridInstantMessage im)
        {
            UUID fromAgentID = new UUID(im.fromAgentID);
            UUID toAgentID = new UUID(im.toAgentID);
            UUID imSessionID = new UUID(im.imSessionID);
            uint timestamp = im.timestamp;
            string fromAgentName = im.fromAgentName;
            string message = im.message;
            byte dialog = im.dialog;
            bool fromGroup = im.fromGroup;
            byte offline = im.offline;
            uint ParentEstateID = im.ParentEstateID;
            Vector3 Position = im.Position;
            UUID RegionID = new UUID(im.RegionID);
            byte[] binaryBucket = im.binaryBucket;

            if (dialog == (byte)InstantMessageDialog.GroupNoticeInventoryAccepted)
            {
                UUID id = new UUID(binaryBucket, 0);

                MySqlConnection conn =
                        new MySqlConnection(m_ConnectionString);

                conn.Open();

                MySqlCommand cmd = conn.CreateCommand();

                cmd.CommandText = "select * from notices where "+
                        "NoticeID = ?NoticeID";

                cmd.Parameters.AddWithValue("NoticeID", imSessionID.ToString());
                IDataReader r = cmd.ExecuteReader();
                if (!r.Read())
                {
                    r.Close();
                    cmd.Dispose();
                    return;
                }

                IInventoryService invService = client.Scene.RequestModuleInterface<IInventoryService>();
                if (invService == null)
                {
                    r.Close();
                    cmd.Dispose();
                    return;
                }

                InventoryItemBase item = new InventoryItemBase();
                item.ID = UUID.Random();
                item.InvType = Convert.ToInt32(r["InvType"]);
                item.Owner = client.AgentId;
                item.CreatorId = r["CreatorID"].ToString();
                item.Name = r["AttachmentName"].ToString();
                item.Description = r["Description"].ToString();
                item.NextPermissions = Convert.ToUInt32(r["NextOwnerPerms"]);
                item.CurrentPermissions = Convert.ToUInt32(r["NextOwnerPerms"]);
                item.BasePermissions = Convert.ToUInt32(r["NextOwnerPerms"]);
                item.EveryOnePermissions = 0;
                item.AssetType = Convert.ToInt32(r["AssetType"]);
                item.AssetID = new UUID(r["AssetID"].ToString());
                item.GroupID = UUID.Zero;
                item.Flags = Convert.ToUInt32(r["flags"]);
                item.CreationDate = Util.UnixTimeSinceEpoch();
                item.Folder = invService.GetFolderForType(client.AgentId, (AssetType)item.AssetType).ID;

                invService.AddItem(item);
                client.SendBulkUpdateInventory(item);

                byte[] itembytes = item.ID.GetBytes();
                byte[] data = new byte[17];
                data[0] = (byte)item.AssetType;
                Array.Copy(itembytes, 0, data, 1, 16);

                GridInstantMessage msg = new GridInstantMessage(
                        client.Scene,
                        new UUID(r["GroupID"].ToString()),
                        r["FromName"].ToString(),
                        client.AgentId,
                        (byte)InstantMessageDialog.InventoryOffered,
                        true,
                        r["AttachmentName"].ToString(),
                        UUID.Random(),
                        false,
                        Vector3.Zero,
                        data);

                client.SendInstantMessage(msg);

                r.Close();
                cmd.Dispose();

                return;
            }
            if (dialog == (byte)InstantMessageDialog.GroupNotice)
            {
                MySqlConnection conn =
                        new MySqlConnection(m_ConnectionString);

                conn.Open();

                MySqlCommand cmd = conn.CreateCommand();

                cmd.CommandText = "select * from members where "+
                        "GroupID = ?GroupID and "+
                        "MemberID = ?MemberID";

                cmd.Parameters.AddWithValue("GroupID", toAgentID.ToString());
                cmd.Parameters.AddWithValue("MemberID", client.AgentId.ToString());

                IDataReader r = cmd.ExecuteReader();

                if (!r.Read())
                {
                    r.Close();
                    cmd.Dispose();
                    return;
                }
                r.Close();

                cmd.Parameters.Clear();

                cmd.CommandText="insert into notices (GroupID, NoticeID, "+
                        "Stamp, FromName, Subject, HasAttachment, "+
                        "AssetType, AttachmentName, NoticeText, NextOwnerPerms, CreatorID, Flags, AssetID, InvType, Description) values ("+
                        "?GroupID, "+
                        "?NoticeID, "+
                        "unix_timestamp(now()), "+
                        "?FromName, "+
                        "?Subject, "+
                        "?HasAttachment, "+
                        "?AssetType, "+
                        "?AttachmentName, "+
                        "?NoticeText, "+
                        "?NextOwnerPerms, "+
                        "?CreatorID, "+
                        "?Flags, "+
                        "?AssetID, "+
                        "?InvType, "+
                        "?Description )";
                
                int AssetType = 0;
                string AttachmentName = String.Empty;
                int HasAttachment = 0;
                int NextOwnerPerms = 0;
                UUID CreatorID = UUID.Zero;
                int Flags = 0;
                UUID AssetID = UUID.Zero;
                int InvType = 0;
                string Description = String.Empty;

                string data = Utils.BytesToString(binaryBucket);
                if (data.StartsWith("<? LLSD/XML ?>"))
                {
                    data = data.Substring(14).Trim();
                }

                if (data.Trim() != "")
                {
                    OSDMap llsd = (OSDMap)OSDParser.DeserializeLLSDXml(data);

                    if (llsd.ContainsKey("item_id") &&
                        llsd.ContainsKey("owner_id"))
                    {
                        UUID itemID = llsd["item_id"].AsUUID();
                        UUID ownerID = llsd["owner_id"].AsUUID();

                        IInventoryService invService = client.Scene.RequestModuleInterface<IInventoryService>();
                        InventoryItemBase item = new InventoryItemBase(itemID, client.AgentId);
                        item = invService.GetItem(item);

                        if (item != null)
                        {
                            AssetType = item.AssetType;
                            AttachmentName = item.Name;
                            HasAttachment = 1;
                            InvType = item.InvType;
                            AssetID = item.AssetID;
                            CreatorID = item.CreatorIdAsUuid;
                            NextOwnerPerms = (int)item.NextPermissions;
                            Flags = (int)item.Flags;
                            Description = item.Description;
                        }
                    }
                }

                UUID noticeID = UUID.Random();

                string[] parts = message.Split(new Char[] {'|'});

                cmd.Parameters.AddWithValue("GroupID", toAgentID.ToString());
                cmd.Parameters.AddWithValue("NoticeID", noticeID);
                cmd.Parameters.AddWithValue("FromName", fromAgentName);
                cmd.Parameters.AddWithValue("Subject", parts[0]);
                cmd.Parameters.AddWithValue("HasAttachment", HasAttachment);
                cmd.Parameters.AddWithValue("AssetType", AssetType);
                cmd.Parameters.AddWithValue("AttachmentName", AttachmentName);
                cmd.Parameters.AddWithValue("NoticeText", parts[1]);
                cmd.Parameters.AddWithValue("NextOwnerPerms", NextOwnerPerms);
                cmd.Parameters.AddWithValue("CreatorID", CreatorID.ToString());
                cmd.Parameters.AddWithValue("Flags", Flags);
                cmd.Parameters.AddWithValue("AssetID", AssetID.ToString());
                cmd.Parameters.AddWithValue("InvType", InvType);
                cmd.Parameters.AddWithValue("Description", Description);

                cmd.ExecuteNonQuery();
                cmd.Dispose();

                NewGroupNotice handlerNewGroupNotice = OnNewGroupNotice;

                if (handlerNewGroupNotice != null)
                    handlerNewGroupNotice(toAgentID, noticeID);
            }
            if (dialog == (byte)InstantMessageDialog.GroupInvitationAccept)
            {
                UUID GroupID = toAgentID;
                UUID RoleID = imSessionID;

                UUID invID = toAgentID ^ fromAgentID ^ imSessionID;
                if (!m_PendingInvites.Contains(invID))
                {
                    m_log.DebugFormat("Invitation data is {0} but was not found in pending list", invID.ToString());
                    m_log.DebugFormat("Agent is {0}, role is {1}", fromAgentID.ToString(), imSessionID.ToString());
                }

                m_PendingInvites.Remove(invID);

                GroupRecord g = GetGroupRecord(GroupID);

                if (g.MembershipFee > 0)
                {
                    IMoneyModule money = client.Scene.RequestModuleInterface<IMoneyModule>();

                    if (money != null)
                    {
                        if (!money.AmountCovered(client, g.MembershipFee))
                        {
                            client.SendAgentAlertMessage("You do not have sufficient funds, request denied", false);
                            return;
                        }

                        money.ApplyCharge(client.AgentId, g.MembershipFee,
                                "Group join fee");
                    }
                }

                if (RoleID == GroupID)
                    RoleID = UUID.Zero;

                AddToGroup(client.AgentId, GroupID, RoleID, true);

                ActivateGroup(client, GroupID);

                SendAgentGroupDataUpdate(client);

                client.SendAgentAlertMessage("You have been added to the group", false);
                client.RefreshGroupMembership();
            }
            if (dialog == (byte)InstantMessageDialog.GroupInvitationDecline)
            {
                UUID invID = toAgentID ^ fromAgentID ^ imSessionID;
                if (m_PendingInvites.Contains(invID))
                    m_PendingInvites.Remove(invID);

                client.SendAgentAlertMessage("You have declined the request", false);
            }
        }

        private void HandleUUIDGroupNameRequest(UUID id, IClientAPI remote_client)
        {
            string groupnamereply = "(Hippos)";

            GroupRecord g = GetGroupRecord(id);

            if (g != null)
                groupnamereply = g.GroupName;

            remote_client.SendGroupNameReply(id, groupnamereply);
        }

        private void SetActiveGroup(UUID agentID, UUID groupID)
        {
            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            cmd.CommandText = "update members set Active = case when GroupID = ?GroupID then 1 else 0 end where MemberID = ?MemberID";
            cmd.Parameters.AddWithValue("GroupID", groupID.ToString());
            cmd.Parameters.AddWithValue("MemberID", agentID);

            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }

        public void ActivateGroup(IClientAPI remoteClient, UUID groupID)
        {
            SetActiveGroup(remoteClient.AgentId, groupID);

            UpdateGroupTitle(remoteClient);
        }

        public List<GroupTitlesData> GroupTitlesRequest(IClientAPI remoteClient,
                UUID groupID)
        {
            return GetGroupTitles(remoteClient.AgentId, groupID);
        }

        public GroupProfileData GroupProfileRequest(IClientAPI remoteClient, UUID groupID)
        {
            GroupProfileData d = new GroupProfileData();

            GroupRecord gr = GetGroupRecord(groupID);
            if (gr == null)
                return d;

            GroupMembershipData m = GetMembershipData(groupID,
                    remoteClient.AgentId);

            d.GroupID = gr.GroupID;
            d.Name = gr.GroupName;
            d.Charter = gr.Charter;
            d.ShowInList = gr.ShowInList;
            if (m != null)
            {
                d.MemberTitle = GetRoleTitle(groupID, m.ActiveRole);
                d.PowersMask = m.GroupPowers;
            }
            d.InsigniaID = gr.GroupPicture;
            d.FounderID = gr.FounderID;
            d.MembershipFee = gr.MembershipFee;
            d.OpenEnrollment = gr.OpenEnrollment;
            d.Money = 0;
            d.GroupMembershipCount = GetMembersCount(gr.GroupID);
            d.GroupRolesCount = GetGroupTitles(remoteClient.AgentId,
                    groupID).Count;
            d.AllowPublish = gr.AllowPublish;
            d.MaturePublish = gr.MaturePublish;
            d.OwnerRole = gr.OwnerRoleID;

            return d;
        }

        public List<GroupMembersData> GroupMembersRequest(IClientAPI remoteClient, UUID groupID)
        {
            List<GroupMembersData> m = new List<GroupMembersData>();

            GroupRecord gr = GetGroupRecord(groupID);
            if (gr == null)
                return m;

            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            cmd.CommandText = "select *,bit_or(roles.RolePowers) as "+
                    "AgentPowers from members left join rolemembers on "+
                    "members.MemberID = rolemembers.Memberid and "+
                    "members.GroupID = rolemembers.GroupID left join "+
                    "roles on rolemembers.RoleID = roles.RoleID and "+
                    "roles.GroupID = members.GroupID where "+
                    "members.GroupID = ?GroupID group by members.MemberID";

            cmd.Parameters.AddWithValue("GroupID", groupID.ToString());

            IDataReader r = cmd.ExecuteReader();

            while(r.Read())
            {
                GroupMembersData d = new GroupMembersData();
                UUID.TryParse(r["MemberID"].ToString(), out d.AgentID);
                d.Contribution = 0;
                d.OnlineStatus = "Unknown";
                d.AgentPowers = Convert.ToUInt64(r["AgentPowers"]);
                UUID roleID = UUID.Zero;
                UUID.TryParse(r["ActiveRole"].ToString(), out roleID);
                d.Title = GetRoleTitle(groupID, roleID);
                d.IsOwner = IsMember(groupID, gr.OwnerRoleID, d.AgentID);
                d.AcceptNotices = Convert.ToInt32(r["AcceptNotices"]) > 0 ?
                        true : false;

                m.Add(d);
            }

            r.Close();
            cmd.Dispose();

            return m;
        }

        public List<GroupRolesData> GroupRoleDataRequest(IClientAPI remoteClient, UUID groupID)
        {
            List<GroupRolesData> rd = new List<GroupRolesData>();

            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            cmd.CommandText = "select roles.*, count(MemberID) as Members "+
                    "from roles left join rolemembers on "+
                    "roles.RoleID = rolemembers.RoleID where "+
                    "roles.GroupID = ?GroupID group by roles.RoleID";
            cmd.Parameters.AddWithValue("GroupID", groupID.ToString());

            IDataReader r = cmd.ExecuteReader();

            while (r.Read())
            {
                GroupRolesData rr = new GroupRolesData();

                UUID.TryParse(r["RoleID"].ToString(), out rr.RoleID);
                rr.Name = r["RoleName"].ToString();
                rr.Title = r["RoleTitle"].ToString();
                rr.Description = r["RoleDescription"].ToString();
                rr.Powers = Convert.ToUInt64(r["RolePowers"]);
                rr.Members = Convert.ToInt32(r["Members"]);

                rd.Add(rr);
            }

            r.Close();
            cmd.Dispose();

            return rd;
        }

        public List<GroupRoleMembersData> GroupRoleMembersRequest(IClientAPI remoteClient, UUID groupID)
        {
            List<GroupRoleMembersData> rm = new List<GroupRoleMembersData>();

            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            cmd.CommandText = "select * from rolemembers where "+
                    "GroupID = ?GroupID";
            cmd.Parameters.AddWithValue("GroupID", groupID.ToString());

            IDataReader r = cmd.ExecuteReader();

            while (r.Read())
            {
                GroupRoleMembersData rr = new GroupRoleMembersData();

                UUID.TryParse(r["RoleID"].ToString(), out rr.RoleID);
                UUID.TryParse(r["MemberID"].ToString(), out rr.MemberID);

                rm.Add(rr);
            }

            r.Close();
            cmd.Dispose();

            return rm;
        }

        public UUID CreateGroup(IClientAPI remoteClient, string name,
                string charter, bool showInList, UUID insigniaID,
                int membershipFee, bool openEnrollment, bool allowPublish,
                bool maturePublish)
        {
            IMoneyModule money = remoteClient.Scene.RequestModuleInterface<IMoneyModule>();
            if (money != null)
            {
                if (!money.AmountCovered(remoteClient, money.GroupCreationCharge))
                {
                    remoteClient.SendCreateGroupReply(UUID.Zero, false, "You do not have sufficient funds to create a group");
                    return UUID.Zero;
                }
            }

            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            cmd.CommandText = "insert ignore into groups (GroupID, GroupName, "+
                    "Charter, GroupPicture, MembershipFee, OpenEnrollment, "+
                    "AllowPublish, MaturePublish, FounderID, ShowInList, "+
                    "OwnerRoleID) "+
                    "values ("+
                    "?GroupID , "+
                    "?GroupName , "+
                    "?Charter , "+
                    "?GroupPicture , "+
                    "?MembershipFee , "+
                    "?OpenEnrollment , "+
                    "?AllowPublish , "+
                    "?MaturePublish , "+
                    "?FounderID , "+
                    "?ShowInList , "+
                    "?OwnerRoleID )";

            UUID groupID = UUID.Random();
            UUID ownerRoleID = UUID.Random();

            cmd.Parameters.AddWithValue("GroupID", groupID.ToString());
            cmd.Parameters.AddWithValue("GroupName", name);
            cmd.Parameters.AddWithValue("Charter", charter);
            cmd.Parameters.AddWithValue("GroupPicture", insigniaID.ToString());
            cmd.Parameters.AddWithValue("MembershipFee", membershipFee);
            cmd.Parameters.AddWithValue("OpenEnrollment", openEnrollment ? 1 : 0);
            cmd.Parameters.AddWithValue("ShowInList", showInList ? 1 : 0);
            cmd.Parameters.AddWithValue("AllowPublish", allowPublish ? 1 : 0);
            cmd.Parameters.AddWithValue("MaturePublish", maturePublish ? 1 : 0);
            cmd.Parameters.AddWithValue("FounderID", remoteClient.AgentId.ToString());
            cmd.Parameters.AddWithValue("OwnerRoleID", ownerRoleID.ToString());

            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (Exception)
            {
                remoteClient.SendCreateGroupReply(UUID.Zero, false, "Group creation failed. Try another name");
                return UUID.Zero;
            }

            cmd.Parameters.Clear();

            cmd.CommandText = "insert ignore into roles (GroupID, RoleID, RoleName, "+
                    "RoleTitle, RolePowers) values ("+
                    "?GroupID, "+
                    "?RoleID, "+
                    "?RoleName, "+
                    "?RoleTitle, "+
                    "?RolePowers )";

            cmd.Parameters.AddWithValue("GroupID", groupID.ToString());
            cmd.Parameters.AddWithValue("RoleID", UUID.Zero.ToString());
            cmd.Parameters.AddWithValue("RoleName", "Everyone");
            cmd.Parameters.AddWithValue("RoleTitle", "Member");
            cmd.Parameters.AddWithValue("RolePowers", 0);

            cmd.ExecuteNonQuery();

            cmd.Parameters.Clear();

            cmd.Parameters.AddWithValue("GroupID", groupID.ToString());
            cmd.Parameters.AddWithValue("RoleID", ownerRoleID.ToString());
            cmd.Parameters.AddWithValue("RoleName", "Owners");
            cmd.Parameters.AddWithValue("RoleTitle", "Owner");
            cmd.Parameters.AddWithValue("RolePowers", "9223372036854775807");

            cmd.ExecuteNonQuery();

            cmd.Parameters.Clear();
            cmd.Dispose();

            AddToGroup(remoteClient.AgentId, groupID, ownerRoleID, true);

            if (money != null)
                money.ApplyCharge(remoteClient.AgentId, money.GroupCreationCharge, "Group creation");

            ActivateGroup(remoteClient, groupID);

            remoteClient.SendCreateGroupReply(groupID, true, name);

            System.Threading.Thread.Sleep(3000);

            SendGroupMembershipCaps(GetMembershipData(remoteClient.AgentId), remoteClient);
            remoteClient.RefreshGroupMembership();

            return groupID;
        }
	    public void AddToGroup(UUID agentID, UUID groupID, UUID roleID)
	    {
		    AddToGroup(agentID, groupID, roleID, false);
	    }
        public void AddToGroup(UUID agentID, UUID groupID, UUID roleID, bool OverrideOpenEnrollment)
        {
	        if (!OverrideOpenEnrollment)
	        {
		        GroupRecord grp = GetGroupRecord(groupID);
                if (grp.OpenEnrollment == false) return;
 	        }
            List<GroupRolesData> roles = GroupRoleDataRequest(null, groupID);

            bool roleGood = false;

            foreach (GroupRolesData rd in roles)
            {
                if (rd.RoleID == roleID)
                {
                    roleGood = true;
                    break;
                }
            }

            if (!roleGood)
                roleID = UUID.Zero;

            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            cmd.CommandText = "insert ignore into members (GroupID, MemberID, Active,"+
                    "ActiveRole, AcceptNotices, ListInProfile) values ("+
                    "?GroupID, "+
                    "?MemberID, "+
                    "?Active, "+
                    "?ActiveRole, "+
                    "?AcceptNotices, "+
                    "?ListInProfile )";

            cmd.Parameters.AddWithValue("GroupID", groupID.ToString());
            cmd.Parameters.AddWithValue("MemberID", agentID.ToString());
            cmd.Parameters.AddWithValue("Active", 1);
            cmd.Parameters.AddWithValue("ActiveRole", roleID.ToString());
            cmd.Parameters.AddWithValue("AcceptNotices", 1);
            cmd.Parameters.AddWithValue("ListInProfile", 1);

            cmd.ExecuteNonQuery();

            cmd.Parameters.Clear();

            cmd.CommandText = "insert ignore into rolemembers (GroupID, RoleID, "+
                    "MemberID) values ("+
                    "?GroupID, "+
                    "?RoleID, "+
                    "?MemberID )";

            if (roleID != UUID.Zero)
            {
                cmd.Parameters.AddWithValue("GroupID", groupID.ToString());
                cmd.Parameters.AddWithValue("RoleID", roleID.ToString());
                cmd.Parameters.AddWithValue("MemberID", agentID.ToString());

                cmd.ExecuteNonQuery();

                cmd.Parameters.Clear();
            }

            cmd.Parameters.AddWithValue("GroupID", groupID.ToString());
            cmd.Parameters.AddWithValue("RoleID", UUID.Zero);
            cmd.Parameters.AddWithValue("MemberID", agentID.ToString());

            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }

        public void UpdateGroupInfo(IClientAPI remoteClient, UUID groupID,
                string charter, bool showInList, UUID insigniaID,
                int membershipFee, bool openEnrollment, bool allowPublish,
                bool maturePublish)
        {
            if (((GetGroupPowers(remoteClient.AgentId, groupID) & (ulong)GroupPowers.ChangeOptions) != 0) || (FindPresence(remoteClient.AgentId).GodLevel >= 200))
            {
                MySqlConnection conn =
                        new MySqlConnection(m_ConnectionString);

                conn.Open();

                MySqlCommand cmd = conn.CreateCommand();

                cmd.CommandText = "update groups set " +
                        "Charter = ?Charter, " +
                        "GroupPicture = ?GroupPicture, " +
                        "MembershipFee = ?MembershipFee, " +
                        "OpenEnrollment = ?OpenEnrollment, " +
                        "AllowPublish = ?AllowPublish, " +
                        "MaturePublish = ?MaturePublish, " +
                        "ShowInList = ?ShowInList " +
                        "where GroupID = ?GroupID";

                cmd.Parameters.AddWithValue("Charter", charter);
                cmd.Parameters.AddWithValue("GroupPicture", insigniaID.ToString());
                cmd.Parameters.AddWithValue("MembershipFee", membershipFee);
                cmd.Parameters.AddWithValue("OpenEnrollment", openEnrollment ? 1 : 0);
                cmd.Parameters.AddWithValue("ShowInList", showInList ? 1 : 0);
                cmd.Parameters.AddWithValue("AllowPublish", allowPublish ? 1 : 0);
                cmd.Parameters.AddWithValue("MaturePublish", maturePublish ? 1 : 0);
                cmd.Parameters.AddWithValue("GroupID", groupID.ToString());

                cmd.ExecuteNonQuery();
                cmd.Dispose();

                SendGroupMembershipCaps(GetMembershipData(remoteClient.AgentId), remoteClient);
            }
        }

        public void SetGroupAcceptNotices(IClientAPI remoteClient,
                UUID groupID, bool acceptNotices, bool listInProfile)
        {
            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            cmd.CommandText = "update members set "+
                    "AcceptNotices = ?AcceptNotices, "+
                    "ListInProfile = ?ListInProfile "+
                    "where GroupID = ?GroupID and "+
                    "MemberID = ?MemberID";

            cmd.Parameters.AddWithValue("AcceptNotices", acceptNotices ? 1 : 0);
            cmd.Parameters.AddWithValue("ListInProfile", listInProfile ? 1 : 0);
            cmd.Parameters.AddWithValue("MemberID", remoteClient.AgentId.ToString());
            cmd.Parameters.AddWithValue("GroupID", groupID.ToString());

            cmd.ExecuteNonQuery();
            cmd.Dispose();

            SendGroupMembershipCaps(GetMembershipData(remoteClient.AgentId), remoteClient);
        }

        public void GroupTitleUpdate(IClientAPI remoteClient, UUID GroupID, UUID TitleRoleID)
        {
            if (!IsMember(GroupID, TitleRoleID, remoteClient.AgentId))
                return;

            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            cmd.CommandText = "update members set "+
                    "ActiveRole = ?ActiveRole "+
                    "where GroupID = ?GroupID and "+
                    "MemberID = ?MemberID";

            cmd.Parameters.AddWithValue("ActiveRole", TitleRoleID.ToString());
            cmd.Parameters.AddWithValue("MemberID", remoteClient.AgentId.ToString());
            cmd.Parameters.AddWithValue("GroupID", GroupID.ToString());

            cmd.ExecuteNonQuery();
            cmd.Dispose();

            OnAgentDataUpdateRequest(remoteClient, remoteClient.AgentId, UUID.Zero);
            SendGroupMembershipCaps(GetMembershipData(remoteClient.AgentId), remoteClient);
            UpdateGroupTitle(remoteClient);
        }

        private void OnRequestAvatarProperties(IClientAPI remoteClient, UUID avatarID)
        {
            GroupMembershipData[] data = GetMembershipData(avatarID);
            if (remoteClient.AgentId == avatarID)
            {
                remoteClient.SendAvatarGroupsReply(avatarID, data);
                return;
            }
            List<GroupMembershipData> outdata = new List<GroupMembershipData>();
            foreach (GroupMembershipData g in data)
            {
                if (g.ListInProfile ){
                    outdata.Add(g);
		}
            }
            remoteClient.SendAvatarGroupsReply(avatarID, outdata.ToArray());
        }

        private void OnClientClosed(UUID agentID, Scene scene)
        {
            if (m_ActiveRoles.ContainsKey(agentID))
                m_ActiveRoles.Remove(agentID);
            if (m_LastNoticeRequest.ContainsKey(agentID))
                m_LastNoticeRequest.Remove(agentID);
        }

        void SendGroupMembershipCaps(GroupMembershipData[] data, IClientAPI remoteClient)
        {
            OSDMap llsd = new OSDMap(3);

            OSDArray AgentData = new OSDArray(1);

            OSDMap AgentDataMap = new OSDMap(1);

            AgentDataMap.Add("AgentID", OSD.FromUUID(remoteClient.AgentId));

            AgentData.Add(AgentDataMap);

            llsd.Add("AgentData", AgentData);

            OSDArray GroupData = new OSDArray(data.Length);
            OSDArray NewGroupData = new OSDArray(data.Length);

            foreach (GroupMembershipData m in data)
            {
                OSDMap GroupDataMap = new OSDMap(6);
                OSDMap NewGroupDataMap = new OSDMap(1);

                GroupDataMap.Add("GroupID", OSD.FromUUID(m.GroupID));
                GroupDataMap.Add("GroupPowers", OSD.FromULong(m.GroupPowers));
                GroupDataMap.Add("AcceptNotices", OSD.FromBoolean(m.AcceptNotices));
                GroupDataMap.Add("GroupInsigniaID", OSD.FromUUID(m.GroupPicture));
                GroupDataMap.Add("Contribution", OSD.FromInteger(m.Contribution));
                GroupDataMap.Add("GroupName", OSD.FromString(m.GroupName));

                NewGroupDataMap.Add("ListInProfile", OSD.FromBoolean(m.ListInProfile));

                GroupData.Add(GroupDataMap);
                NewGroupData.Add(NewGroupDataMap);
            }
            
            llsd.Add("GroupData", GroupData);
            llsd.Add("NewGroupData", NewGroupData);

            IEventQueue eq = remoteClient.Scene.RequestModuleInterface<IEventQueue>();

            if (eq == null)
                return;

            eq.Enqueue(EventQueueHelper.buildEvent("AgentGroupDataUpdate",
                    llsd), remoteClient.AgentId);
        }

        public void SendAgentGroupDataUpdate(IClientAPI remoteClient)
        {
            SendGroupMembershipCaps(GetMembershipData(remoteClient.AgentId),
                    remoteClient);

            OnAgentDataUpdateRequest(remoteClient, remoteClient.AgentId, UUID.Zero);
        }

        public GroupNoticeData[] GroupNoticesListRequest(IClientAPI remoteClient, UUID GroupID)
        {
            List<GroupNoticeData> notices = new List<GroupNoticeData>();

            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            cmd.CommandText = "select * from notices where "+
                    "GroupID = ?GroupID order by Stamp desc";

            cmd.Parameters.AddWithValue("GroupID", GroupID.ToString());

            IDataReader r = cmd.ExecuteReader();

            while (r.Read())
            {
                GroupNoticeData d = new GroupNoticeData();

                UUID.TryParse(r["NoticeID"].ToString(), out d.NoticeID);
                d.Timestamp = Convert.ToUInt32(r["Stamp"]);
                d.FromName = r["FromName"].ToString();
                d.Subject = r["Subject"].ToString();
                d.HasAttachment = Convert.ToInt32(r["HasAttachment"]) > 0 ? true : false;
                d.AssetType = Convert.ToByte(r["AssetType"]);

                notices.Add(d);
            }

            r.Close();
            cmd.Dispose();

            return notices.ToArray();
        }

        public void GroupNoticeRequest(IClientAPI remoteClient, UUID groupNoticeID)
        {
            GridInstantMessage im = CreateGroupNoticeIM(remoteClient.AgentId,
                    groupNoticeID, (byte)InstantMessageDialog.GroupNoticeRequested);
            if (im != null)
                remoteClient.SendInstantMessage(im);
        }

        public GridInstantMessage CreateGroupNoticeIM(UUID agentID, UUID groupNoticeID, byte dialog)
        {
            if (m_LastNoticeRequest.ContainsKey(agentID) && m_LastNoticeRequest[agentID] == groupNoticeID)
                return null;

            m_LastNoticeRequest[agentID] = groupNoticeID;

            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            cmd.CommandText = "select * from notices where "+
                    "NoticeID = ?NoticeID";

            cmd.Parameters.Add("NoticeID", groupNoticeID.ToString());

            IDataReader r = cmd.ExecuteReader();

            if (r.Read())
            {
                UUID groupID = UUID.Zero;
                UUID.TryParse(r["GroupID"].ToString(), out groupID);

                UUID itemID = UUID.Zero;
                UUID.TryParse(r["NoticeID"].ToString(), out itemID);

                string attachmentName = r["AttachmentName"].ToString();

                Byte[] att = Utils.StringToBytes(attachmentName);

                Byte[] BinData = new Byte[19 + att.Length];
                BinData[BinData.Length - 1] = 0;

                BinData[0] = Convert.ToByte(r["HasAttachment"]);
                BinData[1] = Convert.ToByte(r["AssetType"]);

                Array.Copy(groupID.GetBytes(), 0, BinData, 2, 16);

                Array.Copy(att, 0, BinData, 18, att.Length);

                string message = r["Subject"].ToString()+"|"+r["NoticeText"].ToString();
                string fromName = r["FromName"].ToString();

                r.Close();
                cmd.Dispose();

                return new GridInstantMessage(
                        null, groupID, fromName, agentID,
                        dialog, true, message, itemID, true,
                        new Vector3(), BinData);
            }

            r.Close();
            cmd.Dispose();
            return null;
        }

        public string GetGroupTitle(UUID avatarID)
        {
            GroupMembershipData[] data = GetMembershipData(avatarID);

            foreach (GroupMembershipData d in data)
            {
                if (d.Active)
                {
                    return d.GroupTitle;
                }
            }
            return "";
        }

        public ulong GetGroupPowers(UUID avatarID, UUID GroupID)
        {
            GroupMembershipData data = GetMembershipData(GroupID, avatarID);

            if (data != null)
                return data.GroupPowers;

            return 0L;
        }

        public void UpdateGroupTitle(IClientAPI remoteClient)
        {
            ScenePresence sp = ((Scene)(remoteClient.Scene)).GetScenePresence(remoteClient.AgentId);
            if (sp != null)
            {
                sp.Grouptitle = GetGroupTitle(remoteClient.AgentId);
                sp.SendAvatarDataToAllAgents();
            }
        }

        public void GroupRoleUpdate(IClientAPI remoteClient, UUID GroupID, UUID RoleID, string name, string description, string title, ulong powers, byte updateType)
        {
            if (updateType == 0)
                return;

            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            if (updateType == 5) // Delete
            {
                if ((GetGroupPowers(remoteClient.AgentId, GroupID) &
                        (ulong)GroupPowers.DeleteRole) == 0L)
                    return;

                cmd.CommandText = "delete from roles where RoleID = ?RoleID";
                cmd.Parameters.AddWithValue("RoleID", RoleID.ToString());

                cmd.ExecuteNonQuery();

                cmd.Parameters.Clear();

                cmd.CommandText = "update members set ActiveRole = '00000000-0000-0000-0000-000000000000' where ActiveRole = ?RoleID";
                cmd.Parameters.AddWithValue("RoleID", RoleID.ToString());

                cmd.ExecuteNonQuery();
                // TODO: Iterate SPs and send updates

                cmd.Dispose();
                UpdateGroupTitle(remoteClient);
                return;
            }

            if (updateType == 4) // Create
            {
                if ((GetGroupPowers(remoteClient.AgentId, GroupID) &
                        (ulong)GroupPowers.CreateRole) == 0L)
                    return;

                cmd.CommandText = "insert ignore into roles (GroupID, RoleID, "+
                        "RoleName, RoleTitle, RolePowers, RoleDescription) "+
                        "values ("+
                        "?GroupID, "+
                        "?RoleID, "+
                        "?RoleName, "+
                        "?RoleTitle, "+
                        "?RolePowers, "+
                        "?RoleDescription )";

                cmd.Parameters.AddWithValue("GroupID", GroupID.ToString());
                cmd.Parameters.AddWithValue("RoleID", RoleID);
                cmd.Parameters.AddWithValue("RoleName", name).ToString();
                cmd.Parameters.AddWithValue("RoleTitle", title);
                cmd.Parameters.AddWithValue("RolePowers", powers.ToString());
                cmd.Parameters.AddWithValue("RoleDescription", description);

                cmd.ExecuteNonQuery();
                cmd.Dispose();

                return;
            }

            string datafields = "RoleName = ?RoleName, RoleTitle = ?RoleTitle, RoleDescription= ?RoleDescription";
            string powerfields = "RolePowers = ?RolePowers";
            string fields = "";

            ulong currentPowers = GetGroupPowers(remoteClient.AgentId, GroupID);

            if (((int)updateType & 1) != 0) // Update data
            {
                if ((currentPowers & (ulong)GroupPowers.RoleProperties) != 0)
                {
                    fields = datafields;
                    cmd.Parameters.AddWithValue("RoleName", name);
                    cmd.Parameters.AddWithValue("RoleTitle", title);
                    cmd.Parameters.AddWithValue("RoleDescription", description);
                }
            }

            if (((int)updateType & 2) != 0) // Update data
            {
                if ((currentPowers & (ulong)GroupPowers.ChangeActions) != 0)
                {
                    if (fields != "")
                        fields += ",";
                    fields += powerfields;
                    cmd.Parameters.AddWithValue("RolePowers", powers.ToString());
                }
            }
            
            if (fields != String.Empty)
            {
                cmd.Parameters.AddWithValue("GroupID", GroupID.ToString());
                cmd.Parameters.AddWithValue("RoleID", RoleID.ToString());

                cmd.CommandText = "update roles set " + fields + " where GroupID = ?GroupID and RoleID = ?RoleID";

                cmd.ExecuteNonQuery();
                cmd.Dispose();
            }

            UpdateGroupTitle(remoteClient);

            return;
        }

        public List<UUID> GetMemberRoles(UUID GroupID, UUID MemberID)
        {
            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            cmd.CommandText = "select RoleID from rolemembers where GroupID = ?GroupID and MemberID = ?MemberID";

            cmd.Parameters.AddWithValue("GroupID", GroupID.ToString());
            cmd.Parameters.AddWithValue("MemberID", MemberID.ToString());

            IDataReader r = cmd.ExecuteReader();

            List<UUID> ret = new List<UUID>();

            while(r.Read())
            {
                ret.Add(new UUID(r["RoleID"].ToString()));
            }

            r.Close();
            cmd.Dispose();

            return ret;
        }

        public void GroupRoleChanges(IClientAPI remoteClient, UUID GroupID, UUID RoleID, UUID MemberID, uint change)
        {
            if (change == 2)
                return; // No change requested

            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            ulong powers = GetGroupPowers(remoteClient.AgentId, GroupID);

            if (change == 0) // Add
            {
                List<UUID> roles = GetMemberRoles(GroupID, remoteClient.AgentId);

                if (((powers & (ulong)GroupPowers.AssignMember) > 0) || ((powers & (ulong)GroupPowers.AssignMemberLimited) > 0 && roles.Contains(RoleID)))
                {
                    cmd.CommandText = "delete from rolemembers where GroupID = ?GroupID and RoleID = ?RoleID and MemberID = ?MemberID";

                    cmd.Parameters.AddWithValue("GroupID", GroupID.ToString());
                    cmd.Parameters.AddWithValue("RoleID", RoleID.ToString());
                    cmd.Parameters.AddWithValue("MemberID", MemberID.ToString());

                    cmd.ExecuteNonQuery();

                    cmd.CommandText = "insert ignore into rolemembers (GroupID, RoleID, MemberID) values (?GroupID, ?RoleID, ?MemberID)";

                    cmd.ExecuteNonQuery();
                    cmd.Dispose();
                    return;
                }
            }

            if (change == 1) // remove
            {
                if ((powers & (ulong)GroupPowers.RemoveMember) == 0)
                    return;

                cmd.CommandText = "delete from rolemembers where GroupID = ?GroupID and RoleID = ?RoleID and MemberID = ?MemberID";
                cmd.Parameters.AddWithValue("GroupID", GroupID.ToString());
                cmd.Parameters.AddWithValue("RoleID", RoleID.ToString());
                cmd.Parameters.AddWithValue("MemberID", MemberID.ToString());

                cmd.ExecuteNonQuery();
                cmd.Dispose();
                return;
            }
        }

        private void RemoveFromGroup(UUID groupID, UUID memberID)
        {
            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            cmd.CommandText = "delete from members where "+
                    "GroupID = ?GroupID and MemberID = ?memberID";

            cmd.Parameters.AddWithValue("GroupID", groupID.ToString());
            cmd.Parameters.AddWithValue("MemberID", memberID.ToString());

            cmd.ExecuteNonQuery();

            cmd.CommandText = "delete from rolemembers where "+
                    "GroupID = ?GroupID and MemberID = ?memberID";

            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }

        public void JoinGroupRequest(IClientAPI remoteClient, UUID GroupID)
        {
            GroupProfileData d = GroupProfileRequest(remoteClient, GroupID);
            if (d.GroupID == UUID.Zero || d.OpenEnrollment == false)
            {
                remoteClient.SendJoinGroupReply(GroupID, false);
                return;
            }

            if (d.MembershipFee > 0)
            {
                IMoneyModule money = remoteClient.Scene.RequestModuleInterface<IMoneyModule>();
                if (money != null)
                {
                    if (!money.AmountCovered(remoteClient, d.MembershipFee))
                    {
                        remoteClient.SendAgentAlertMessage("You don't have sufficient funds to join this group", false);
                        remoteClient.SendJoinGroupReply(GroupID, false);

                        return;
                    }

                    money.ApplyCharge(remoteClient.AgentId, d.MembershipFee,
                            "Group join fee");
                }
            }

            AddToGroup(remoteClient.AgentId, GroupID, UUID.Zero);

            remoteClient.SendJoinGroupReply(GroupID, true);

            ActivateGroup(remoteClient, GroupID);

            SendAgentGroupDataUpdate(remoteClient);

            remoteClient.SendAgentAlertMessage("You have been added to the group", false);
            remoteClient.RefreshGroupMembership();
        }

        public void LeaveGroupRequest(IClientAPI remoteClient, UUID GroupID)
        {
            GroupProfileData d = GroupProfileRequest(remoteClient, GroupID);
            if (d.GroupID == UUID.Zero)
            {
                remoteClient.SendLeaveGroupReply(GroupID, false);
                return;
            }

            if (!IsMember(GroupID, UUID.Zero, remoteClient.AgentId))
            {
                remoteClient.SendLeaveGroupReply(GroupID, false);
                return;
            }

            RemoveFromGroup(GroupID, remoteClient.AgentId);

            remoteClient.SendLeaveGroupReply(GroupID, true);

            remoteClient.SendAgentAlertMessage("You have left the group", false);
            remoteClient.SendAgentDropGroup(GroupID);

            remoteClient.RefreshGroupMembership();
        }

        public void EjectGroupMemberRequest(IClientAPI remoteClient,
                UUID GroupID, UUID EjecteeID)
        {
            ulong powers = GetGroupPowers(remoteClient.AgentId, GroupID);

            if ((powers & (ulong)GroupPowers.Eject) != 0)
            {
                RemoveFromGroup(GroupID, EjecteeID);

                byte dialog = 210;

                ScenePresence presence = FindPresence(EjecteeID);
                if (presence != null)
                {
                    presence.ControllingClient.SendAgentDropGroup(GroupID);
                    presence.ControllingClient.RefreshGroupMembership();
                    dialog = (byte)InstantMessageDialog.MessageFromAgent;
                }

                // TODO: Send IM to victim
                if (m_TransferModule != null)
                {
                    GroupRecord g = GetGroupRecord(GroupID);

                    m_TransferModule.SendInstantMessage(new GridInstantMessage(
                            remoteClient.Scene, remoteClient.AgentId,
                            remoteClient.FirstName+" "+remoteClient.LastName,
                            EjecteeID, dialog, false,
                            "You have been removed from "+g.GroupName+".",
                            g.GroupID, true, new Vector3(), new byte[0]),
                            delegate(bool success) {} );
                }

                remoteClient.SendEjectGroupMemberReply(EjecteeID, GroupID,
                        true);
            }
        }

        public void InviteGroupRequest(IClientAPI remoteClient, UUID GroupID,
                UUID InviteeID, UUID RoleID)
        {
            if (m_TransferModule == null)
                return;

            GroupRecord g = GetGroupRecord(GroupID);

            Int32 price = g.MembershipFee;
            byte[] price_data = BitConverter.GetBytes(price);

            if (BitConverter.IsLittleEndian)
                Array.Reverse(price_data);

            byte[] data = new byte[20];

            Array.Copy(price_data, 0, data, 0, 4);
            Array.Copy(RoleID.GetBytes(), 0, data, 4, 16);

            string text = remoteClient.FirstName+" "+remoteClient.LastName+
                    " has invited you to join the group "+g.GroupName+".\n";
            if (price > 0)
                text += "Joining this group costs D$ "+price.ToString()+"\n";
            if (RoleID != UUID.Zero)
            {
                text += "You are being invited into the role "+
                        GetRoleName(RoleID);
            }

            while (m_PendingInvites.Count >= 10)
                m_PendingInvites.RemoveAt(0);

            if (RoleID == UUID.Zero)
                RoleID = GroupID;

            m_PendingInvites.Add(GroupID ^ InviteeID ^ RoleID);
            m_log.DebugFormat("Adding invitiation {0}, agent {1}, role {2}", (InviteeID ^ RoleID).ToString(), InviteeID.ToString(), RoleID.ToString());

            m_TransferModule.SendInstantMessage(new GridInstantMessage(
                    remoteClient.Scene, GroupID,
                    remoteClient.FirstName+" "+remoteClient.LastName,
                    InviteeID,
                    (byte)InstantMessageDialog.GroupInvitation, true,
                    text, RoleID, true, new Vector3(), data),
                    delegate(bool success) {} );
        }

        public void OnIncomingInstantMessage(GridInstantMessage msg)
        {
            if (msg.dialog == (uint)InstantMessageDialog.GroupInvitation ||
                msg.dialog == (uint)InstantMessageDialog.GroupNotice)
            {
                // Local delivery of remote messages
                //
                if (m_TransferModule != null)
                {
                    while (m_PendingInvites.Count >= 10)
                        m_PendingInvites.RemoveAt(0);

                    m_PendingInvites.Add(new UUID(msg.fromAgentID) ^ new UUID(msg.toAgentID) ^
                            new UUID(msg.imSessionID));

                    m_TransferModule.SendInstantMessage(msg,
                        delegate(bool success) {} );
                }
            }

            // We received a nonlocal group eject.
            // So, tell the user to drop the group and forward the IM
            // as a normal message
            //
            if (msg.dialog == (uint)210)
            {
                msg.dialog = (byte)InstantMessageDialog.MessageFromAgent;
                if (m_TransferModule != null)
                {
                    m_TransferModule.SendInstantMessage(msg,
                        delegate(bool success) {} );
                }

                ScenePresence presence = FindPresence(new UUID(msg.toAgentID));
                if (presence != null)
                {
                    presence.ControllingClient.SendAgentDropGroup(new UUID(msg.imSessionID));
                    presence.ControllingClient.RefreshGroupMembership();
                }
            }
        }

        private void OnDirFindQuery(IClientAPI remoteClient, UUID queryID,
                string queryText, uint queryFlags, int queryStart)
        {
            if ((queryFlags & 16) == 0) // Groups only
                return;

            string[] words = queryText.Split(new char[] {' '});
            string query = "%"+string.Join("%", words)+"%";

            MySqlConnection conn =
                    new MySqlConnection(m_ConnectionString);

            conn.Open();

            MySqlCommand cmd = conn.CreateCommand();

            cmd.CommandText = "select groups.*, count(members.MemberID) as members from groups left join members on groups.GroupID = members.GroupID where "+
                    "GroupName like ?GroupName and (ScopeID='00000000-0000-0000-0000-000000000000' or ScopeID=?ScopeID)";
            if ((queryFlags & 0x400000) != 0)
                cmd.CommandText += " and MaturePublish = 0";

            cmd.CommandText += " and ShowInList <> 0 group by groups.GroupID order by GroupName limit ?queryStart, 101";

            cmd.Parameters.Add("GroupName", query);
            cmd.Parameters.Add("queryStart", queryStart);
            cmd.Parameters.Add("ScopeID", remoteClient.Scene.RegionInfo.ScopeID);

            IDataReader r = cmd.ExecuteReader();

            List<DirGroupsReplyData> reply = new List<DirGroupsReplyData>();

            int order = 0;

            while(r.Read())
            {
                DirGroupsReplyData d = new DirGroupsReplyData();
                d.groupID = new UUID(r["GroupID"].ToString());
                d.groupName = r["GroupName"].ToString();
                d.members = Convert.ToInt32(r["members"]);
                d.searchOrder = queryStart + order;
                order++;

                reply.Add(d);
            }

            r.Close();
            cmd.Dispose();

            remoteClient.SendDirGroupsReply(queryID, reply.ToArray());
        }

        public void NotifyChange(UUID GroupID)
        {
            if (m_Groupchat != null)
            {
                m_Groupchat.SendRefresh(GroupID);
            }

            foreach (Scene scene in m_Scenes)
            {
                scene.ForEachScenePresence(delegate(ScenePresence p)
                {
                    if (p.IsChildAgent)
                        return;
                    IClientAPI client = p.ControllingClient;
                    if (client.IsGroupMember(GroupID))
                        client.RefreshGroupMembership();
                });
            }
        }

        public void OnGroupAccountSummaryRequest(IClientAPI client, UUID agentID, UUID groupID)
        {
            //SendGroupAccountingSummary(client, groupID, amount, tierTotal, tierUsed);
        }

        public void OnGroupAccountDetailsRequest(IClientAPI client, UUID agentID, UUID groupID, UUID requestID, UUID sessionID)
        {
            //SendGroupAccountingDetails(client, groupID, transactionID, sessionID, amount);
        }

        public void OnGroupAccountTransactionsRequest(IClientAPI client, UUID agentID, UUID groupID, UUID requestID, UUID sessionID)
        {
            //SendGroupTransactionsSummaryDetails(client, groupID, transactionID, sessionID, amount);
        }
    }
}
