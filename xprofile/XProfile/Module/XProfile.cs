//////////////////////////////////////////////////////////////////////////////
//
// (c) 2009, 2001 Careminster Linited and Melanie Thielker
//
// All Rights Reserved.
//

using System;
using System.Globalization;
using System.Collections.Generic;
using System.Reflection;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Data;
using OpenSim.Data.MySQL;
using OpenMetaverse;
using Nini.Config;
using log4net;
using Mono.Addins;
using OpenSim.Services.Interfaces;
using PresenceInfo = OpenSim.Services.Interfaces.PresenceInfo;

[assembly: Addin("XProfile", "0.1")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace Careminster.Profile
{
    public class XProfileData
    {
        public UUID UserID;
        public Dictionary<string, string> Data;
    }

    public class XProfilePick
    {
        public UUID UserID;
        public UUID PickID;
        public Dictionary<string, string> Data;
    }

    public class XProfileNote
    {
        public UUID UserID;
        public UUID AvatarID;
        public Dictionary<string, string> Data;
    }

    public class XProfileClassified
    {
        public UUID UserID;
        public UUID ClassifiedID;
        public Dictionary<string, string> Data;
    }

    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "XProfile")]
    public class XProfile : INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private IConfig m_Config;
        private bool m_Enabled = false;
        private string m_ConnectionString;
        private MySQLGenericTableHandler<XProfileData> m_ProfileTable;
        private MySQLGenericTableHandler<XProfilePick> m_PicksTable;
        private MySQLGenericTableHandler<XProfileData> m_InterestsTable;
        private MySQLGenericTableHandler<XProfileNote> m_NotesTable;
        private MySQLGenericTableHandler<XProfileClassified> m_ClassifiedsTable;
        private MySQLGenericTableHandler<XProfileData> m_PrefsTable;
        private Scene m_Scene;
        private IMoneyModule m_MoneyModule = null;

        public void Initialise(IConfigSource config)
        {
            m_Config = config.Configs["Profile"];
            if (m_Config == null)
                return;

            if (m_Config.GetString("Module", String.Empty) != Name)
                return;


            m_ConnectionString = m_Config.GetString("DatabaseConnect",
                    String.Empty);
            if (m_ConnectionString == String.Empty)
            {
                m_log.Error("[XProfile]: XProfile module enabled but no DatabaseConnect in [Profile]");
                return;
            }

            m_ProfileTable = new MySQLGenericTableHandler<XProfileData>(
                    m_ConnectionString, "XProfile", String.Empty);
            m_PicksTable = new MySQLGenericTableHandler<XProfilePick>(
                    m_ConnectionString, "XProfilePicks", String.Empty);
            m_InterestsTable = new MySQLGenericTableHandler<XProfileData>(
                    m_ConnectionString, "XProfileInterests", String.Empty);
            m_NotesTable = new MySQLGenericTableHandler<XProfileNote>(
                    m_ConnectionString, "XProfileNotes", String.Empty);
            m_ClassifiedsTable = new MySQLGenericTableHandler<XProfileClassified>(
                    m_ConnectionString, "XProfileClassifieds", String.Empty);
            m_PrefsTable = new MySQLGenericTableHandler<XProfileData>(
                    m_ConnectionString, "XProfilePrefs", String.Empty);

            m_Enabled = true;

            m_log.Info("[XProfile]: Module enabled");
        }

        public void AddRegion(Scene scene)
        {
            m_Scene = scene;

            m_Scene.EventManager.OnNewClient += OnNewClient;
        }

        public void RemoveRegion(Scene scene)
        {
            m_Scene.EventManager.OnNewClient -= OnNewClient;
            m_Scene = null;
        }

        public void RegionLoaded(Scene scene)
        {
            m_MoneyModule = m_Scene.RequestModuleInterface<IMoneyModule>();
        }

        public string Name
        {
            get { return "XProfile"; }
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
            client.OnRequestAvatarProperties += OnRequestAvatarProperties;
            client.OnUpdateAvatarProperties += OnUpdateAvatarProperties;

            client.AddGenericPacketHandler("avatarpicksrequest",
                    OnAvatarPicksRequest);
            client.AddGenericPacketHandler("pickinforequest",
                    OnPickInfoRequest);
            client.OnPickInfoUpdate += OnPickInfoUpdate;
            client.OnPickDelete += OnPickDelete;

            client.OnAvatarInterestUpdate += OnAvatarInterestsUpdate;

            client.AddGenericPacketHandler("avatarnotesrequest",
                    OnAvatarNotesRequest);
            client.OnAvatarNotesUpdate += OnAvatarNotesUpdate;

            client.AddGenericPacketHandler("avatarclassifiedsrequest",
                    OnAvatarClassifiedsRequest);
            client.OnClassifiedInfoRequest += OnClassifiedInfoRequest;
            client.OnClassifiedInfoUpdate += OnClassifiedInfoUpdate;
            client.OnClassifiedDelete += OnClassifiedDelete;
            client.OnUserInfoRequest += UserPreferencesRequest;
            client.OnUpdateUserInfo += UpdateUserPreferences;

            client.OnLogout += OnClientClosed;
        }

        private void OnClientClosed(IClientAPI client)
        {
            client.OnRequestAvatarProperties -= OnRequestAvatarProperties;
            client.OnUpdateAvatarProperties -= OnUpdateAvatarProperties;

            client.OnPickInfoUpdate -= OnPickInfoUpdate;
            client.OnPickDelete -= OnPickDelete;

            client.OnAvatarInterestUpdate -= OnAvatarInterestsUpdate;

            client.OnClassifiedInfoRequest -= OnClassifiedInfoRequest;
            client.OnClassifiedInfoUpdate -= OnClassifiedInfoUpdate;
            client.OnClassifiedDelete -= OnClassifiedDelete;

            client.OnLogout -= OnClientClosed;
        }

        private void OnRequestAvatarProperties(IClientAPI remoteClient,
                UUID avatarID)
        {
            UserAccount account = m_Scene.UserAccountService.GetUserAccount(
                    m_Scene.RegionInfo.ScopeID, avatarID);

            if (account == null)
                return;

            Byte[] charterMember;
            if (account.UserTitle == "")
            {
                charterMember = new Byte[1];
                charterMember[0] = (Byte)((account.UserFlags & 0xf00) >> 8);
            }
            else
            {
                charterMember = Utils.StringToBytes(account.UserTitle);
            }

            XProfileData[] data = m_ProfileTable.Get("UserID", avatarID.ToString());
            uint flags = (uint)account.UserFlags & 0x0c;

            if (data.Length == 0)
            {
                remoteClient.SendAvatarProperties(account.PrincipalID,
                        String.Empty,
                        Util.ToDateTime(account.Created).ToString("M/d/yyyy",
                                CultureInfo.InvariantCulture),
                        charterMember, String.Empty,
                        flags,
                        UUID.Zero, UUID.Zero, String.Empty, UUID.Zero);
            }
            else
            {
                flags |= Convert.ToUInt32(data[0].Data["Flags"]);

                PresenceInfo[] presences = m_Scene.PresenceService.GetAgents(new string[] { avatarID.ToString() } );
                if (presences.Length > 0)
                    flags |= 16;

                remoteClient.SendAvatarProperties(account.PrincipalID,
                        data[0].Data["ProfileText"],
                        Util.ToDateTime(account.Created).ToString("M/d/yyyy",
                                CultureInfo.InvariantCulture),
                        charterMember, data[0].Data["FirstLifeText"],
                        flags,
                        new UUID(data[0].Data["FirstLifeImageID"]),
                        new UUID(data[0].Data["ImageID"]),
                        data[0].Data["ProfileUrl"],
                        new UUID(data[0].Data["PartnerID"]));

            }

            XProfileData[] interests = m_InterestsTable.Get("UserID",
                    avatarID.ToString());

            if (interests.Length > 0)
            {
                remoteClient.SendAvatarInterestsReply(avatarID,
                        Convert.ToUInt32(interests[0].Data["WantMask"]),
                        interests[0].Data["WantText"],
                        Convert.ToUInt32(interests[0].Data["SkillsMask"]),
                        interests[0].Data["SkillsText"],
                        interests[0].Data["Languages"]);
            }
            else
            {
                remoteClient.SendAvatarInterestsReply(avatarID, 0, String.Empty,
                        0, String.Empty, String.Empty);
            }
        }

        private void OnUpdateAvatarProperties(IClientAPI remoteClient,
                UserProfileData newProfile)
        {
            if (newProfile.ID != remoteClient.AgentId)
                return;

            XProfileData profileData;

            XProfileData[] data = m_ProfileTable.Get("UserID", remoteClient.AgentId.ToString());
            if (data.Length > 0)
            {
                profileData = data[0];
            }
            else
            {
                profileData = new XProfileData();
                profileData.Data = new Dictionary<string, string>();
                profileData.UserID = remoteClient.AgentId;
            }

            profileData.Data["ImageID"] = newProfile.Image.ToString();
            profileData.Data["ProfileText"] = newProfile.AboutText;
            profileData.Data["FirstLifeText"] = newProfile.FirstLifeAboutText;
            profileData.Data["FirstLifeImageID"] = newProfile.FirstLifeImage.ToString();
            profileData.Data["ProfileUrl"] = newProfile.ProfileUrl;

            if (m_ProfileTable.Store(profileData))
                OnRequestAvatarProperties(remoteClient, newProfile.ID);
        }

        private void OnAvatarPicksRequest(Object sender, string method,
                List<String> args)
        {
            if (!(sender is IClientAPI))
                return;

            IClientAPI remoteClient = (IClientAPI)sender;

            XProfilePick[] picks = m_PicksTable.Get("UserID", args[0]);

            Dictionary<UUID,string> picklist =
                    new Dictionary<UUID,string>();

            foreach (XProfilePick p in picks)
                picklist[p.PickID] = p.Data["Name"];

            remoteClient.SendAvatarPicksReply(new UUID(args[0]),
                    picklist);
        }

        private void OnPickInfoUpdate(IClientAPI remoteClient, UUID pickID,
                UUID creatorID, bool topPick, string name, string desc,
                UUID snapshotID, int sortOrder, bool enabled)
        {
            XProfilePick[] picks = m_PicksTable.Get(
                    new string[] { "PickID" },
                    new string[] { pickID.ToString() });

            ScenePresence p = m_Scene.GetScenePresence(remoteClient.AgentId);

            if (p == null)
                return;

            XProfilePick pick = new XProfilePick();

            pick.UserID = remoteClient.AgentId;
            pick.PickID = pickID;
            
            if (picks.Length > 0)
            {
                if (picks[0].UserID != remoteClient.AgentId)
                    return;

                pick.Data = picks[0].Data;
            }
            else
            {
                picks = m_PicksTable.Get(
                        new string[] { "UserID" },
                        new string[] { remoteClient.AgentId.ToString() } );

                // Store no more than 12 picks
                if (picks.Length >= 12)
                    return;

                // Don't store a pick we didn't create if it
                // doesn't exist
                if (creatorID != remoteClient.AgentId)
                    return;

                pick.Data = new Dictionary<string,string>();
                pick.Data["CreatorID"] = creatorID.ToString();
                pick.Data["RegionName"] = remoteClient.Scene.RegionInfo.RegionName;
                pick.Data["UserName"] = remoteClient.Name;

                ILandObject parcel = m_Scene.LandChannel.GetLandObject(
                        p.AbsolutePosition.X, p.AbsolutePosition.Y);

                pick.Data["OriginalName"] = parcel.LandData.Name;

                Vector3 agentPosition = p.AbsolutePosition;

                pick.Data["ParcelID"] = p.currentParcelUUID.ToString();

                Vector3 posGlobal =
                         new Vector3(remoteClient.Scene.RegionInfo.RegionLocX *
                         Constants.RegionSize + agentPosition.X,
                         remoteClient.Scene.RegionInfo.RegionLocY *
                         Constants.RegionSize + agentPosition.Y,
                         agentPosition.Z);

                pick.Data["GlobalPosition"] = posGlobal.ToString();

            }

            pick.Data["TopPick"] = topPick.ToString();
            pick.Data["Name"] = name;
            pick.Data["Description"] = desc;
            pick.Data["SnapshotID"] = snapshotID.ToString();
            pick.Data["SortOrder"] = sortOrder.ToString();
            pick.Data["Enabled"] = enabled.ToString();

            m_PicksTable.Store(pick);
        }

        private void OnPickDelete(IClientAPI remoteClient, UUID queryPickID)
        {
            XProfilePick[] picks = m_PicksTable.Get(
                    new string[] { "PickID" },
                    new string[] { queryPickID.ToString() });

            if (picks.Length == 0 || picks[0].UserID != remoteClient.AgentId)
                return;

            m_PicksTable.Delete("PickID", queryPickID.ToString());
        }

        private void OnPickInfoRequest(Object sender, string method,
                List<String> args)
        {
            if (!(sender is IClientAPI))
                return;

            IClientAPI remoteClient = (IClientAPI)sender;

            XProfilePick[] picks = m_PicksTable.Get(
                    new string[] { "UserID", "PickID" },
                    new string[] { args[0], args[1] });

            if (picks.Length == 0)
                return;

            remoteClient.SendPickInfoReply(
                new UUID(picks[0].PickID),
                new UUID(picks[0].Data["CreatorID"]),
                Convert.ToBoolean(picks[0].Data["TopPick"]),
                new UUID(picks[0].Data["ParcelID"]),
                picks[0].Data["Name"],
                picks[0].Data["Description"],
                new UUID(picks[0].Data["SnapshotID"]),
                picks[0].Data["UserName"],
                picks[0].Data["OriginalName"],
                picks[0].Data["RegionName"],
                Vector3.Parse(picks[0].Data["GlobalPosition"]),
                Convert.ToInt32(picks[0].Data["SortOrder"]),
                Convert.ToBoolean(picks[0].Data["Enabled"]));
        }

        private void OnAvatarInterestsUpdate(IClientAPI remoteClient,
                uint wantMask, string wantText, uint skillsMask,
                string skillsText, string languages)
        {
            XProfileData data = new XProfileData();

            data.UserID = remoteClient.AgentId;
            data.Data = new Dictionary<string,string>();

            data.Data["WantMask"] = wantMask.ToString();
            data.Data["WantText"] = wantText;
            data.Data["SkillsMask"] = skillsMask.ToString();
            data.Data["SkillsText"] = skillsText;
            data.Data["Languages"] = languages;

            m_InterestsTable.Store(data);
        }

        private void OnAvatarNotesRequest(Object sender, string method,
                List<String> args)
        {
            if (!(sender is IClientAPI))
                return;

            IClientAPI remoteClient = (IClientAPI)sender;

            XProfileNote[] notes = m_NotesTable.Get(
                    new string[] { "UserID", "AvatarID" },
                    new string[] { remoteClient.AgentId.ToString(),
                                   args[0] });

            if (notes.Length == 0)
            {
                remoteClient.SendAvatarNotesReply(new UUID(args[0]),
                        String.Empty);
                return;
            }
            remoteClient.SendAvatarNotesReply(new UUID(args[0]),
                    notes[0].Data["Note"]);
        }

        private void OnAvatarNotesUpdate(IClientAPI remoteClient,
                UUID queryTargetID, string queryNotes)
        {
            XProfileNote note = new XProfileNote();

            note.UserID = remoteClient.AgentId;
            note.AvatarID = queryTargetID;

            note.Data = new Dictionary<string,string>();

            note.Data["Note"] = queryNotes;

            m_NotesTable.Store(note);
        }

        public void OnAvatarClassifiedsRequest(Object sender, string method,
                List<String> args)
        {
            if (!(sender is IClientAPI))
                return;

            IClientAPI remoteClient = (IClientAPI)sender;

            UUID targetID = remoteClient.AgentId;
            if (args.Count > 0 && args[0] != null)
                targetID = new UUID(args[0]);

            XProfileClassified[] classifieds = m_ClassifiedsTable.Get( "UserID",
                    targetID.ToString());

            Dictionary<UUID,string> ret =
                    new Dictionary<UUID,string>();

            foreach (XProfileClassified c in classifieds)
                ret[c.ClassifiedID] = c.Data["Name"];

            remoteClient.SendAvatarClassifiedReply(targetID,
                    ret);
        }

        void OnClassifiedInfoRequest(UUID classifiedID, IClientAPI remoteClient)
        {
            XProfileClassified[] classifieds = m_ClassifiedsTable.Get(
                    "ClassifiedID", classifiedID.ToString());

            if (classifieds.Length == 0)
                return;

            remoteClient.SendClassifiedInfoReply(classifiedID,
                    new UUID(classifieds[0].Data["CreatorID"]),
                    Convert.ToUInt32(classifieds[0].Data["CreationDate"]),
                    Convert.ToUInt32(classifieds[0].Data["ExpirationDate"]),
                    Convert.ToUInt32(classifieds[0].Data["Category"]),
                    classifieds[0].Data["Name"],
                    classifieds[0].Data["Description"],
                    new UUID(classifieds[0].Data["ParcelID"]),
                    Convert.ToUInt32(classifieds[0].Data["ParentEstate"]),
                    new UUID(classifieds[0].Data["SnapshotID"]),
                    classifieds[0].Data["RegionName"],
                    Vector3.Parse(classifieds[0].Data["GlobalPosition"]),
                    classifieds[0].Data["ParcelName"],
                    (byte)Convert.ToUInt32(classifieds[0].Data["ClassifiedFlags"]),
                    Convert.ToInt32(classifieds[0].Data["Price"]));
        }

        public void OnClassifiedInfoUpdate(UUID classifiedID,
                uint category, string name, string description,
                UUID parcelID, uint parentEstate,
                UUID snapshotID, Vector3 globalPos,
                byte classifiedFlags, int classifiedPrice,
                IClientAPI remoteClient)
        {
        try
        {
            XProfileClassified[] classifieds = m_ClassifiedsTable.Get(
                    new string[] { "ClassifiedID" },
                    new string[] { classifiedID.ToString() });

            ScenePresence p = m_Scene.GetScenePresence(remoteClient.AgentId);

            if (p == null)
                return;

            XProfileClassified cl = new XProfileClassified();
            cl.UserID = remoteClient.AgentId;
            cl.ClassifiedID = classifiedID;

            if (classifieds.Length == 0)
            {
                // This will happen if people try for a free classified,
                // or the profile is closed with "OK", rather then using
                // publish.
                if (classifiedPrice == 0)
                    return;

                if (m_MoneyModule != null)
                {
                    if (!m_MoneyModule.AmountCovered(remoteClient,
                            classifiedPrice))
                    {
                        remoteClient.SendAgentAlertMessage("You don't have sufficient funds to place this advert", false);
                        return;
                    }

                    m_MoneyModule.ApplyCharge(remoteClient.AgentId,
                            classifiedPrice, "Classified charge");
                }

                cl.Data = new Dictionary<string,string>();
                cl.Data["CreatorID"] = remoteClient.AgentId.ToString();
                cl.Data["CreationDate"] = Util.UnixTimeSinceEpoch().ToString();
                cl.Data["ExpirationDate"] = (Util.UnixTimeSinceEpoch() + 86400 * 7).ToString();
                cl.Data["Price"] = classifiedPrice.ToString();
                cl.Data["GlobalPosition"] = String.Empty;
            }
            else
            {
                if (classifieds[0].UserID != remoteClient.AgentId)
                    return;
                cl.Data = classifieds[0].Data;
            }

            cl.Data["Category"] = category.ToString();
            cl.Data["Name"] = name;
            cl.Data["Description"] = description;
            cl.Data["SnapshotID"] = snapshotID.ToString();
            cl.Data["ClassifiedFlags"] = classifiedFlags.ToString();
            if (cl.Data["GlobalPosition"] != globalPos.ToString())
            {
                cl.Data["GlobalPosition"] = globalPos.ToString();
                cl.Data["RegionName"] = remoteClient.Scene.RegionInfo.RegionName;
                cl.Data["ParentEstate"] = remoteClient.Scene.RegionInfo.EstateSettings.ParentEstateID.ToString();
                ILandObject parcel = m_Scene.LandChannel.GetLandObject(
                        p.AbsolutePosition.X, p.AbsolutePosition.Y);

                cl.Data["ParcelName"] = parcel.LandData.Name;

                Vector3 agentPosition = p.AbsolutePosition;

                cl.Data["ParcelID"] = p.currentParcelUUID.ToString();
            }

            cl.Data["ScopeID"] = m_Scene.RegionInfo.ScopeID.ToString();

            if(m_ClassifiedsTable.Store(cl))
                OnClassifiedInfoRequest(classifiedID, remoteClient);
        }
        catch(Exception e)
        {
            System.Console.WriteLine(e.ToString());
        }
        }

        public void OnClassifiedDelete (UUID queryClassifiedID,
                IClientAPI remoteClient)
        {
            XProfileClassified[] classifieds = m_ClassifiedsTable.Get(
                    new string[] { "UserID", "ClassifiedID" },
                    new string[] { remoteClient.AgentId.ToString(),
                                   queryClassifiedID.ToString() });

            if (classifieds.Length < 1)
                return;

            m_ClassifiedsTable.Delete("ClassifiedID",
                    queryClassifiedID.ToString());
        }
        
        public void UserPreferencesRequest(IClientAPI remoteClient)
        {
            XProfileData[] prefs = m_PrefsTable.Get("UserID",
                    remoteClient.AgentId.ToString());

            if (prefs.Length == 0)
            {
                remoteClient.SendUserInfoReply(true, false, String.Empty);
                return;
            }

            bool visible = false;
            if (Convert.ToInt32(prefs[0].Data["Visible"]) > 0)
                visible = true;

            bool imtoemail = false;
            if (Convert.ToInt32(prefs[0].Data["IMToEmail"]) > 0)
                imtoemail = true;

            remoteClient.SendUserInfoReply(imtoemail, visible, String.Empty);
        }

        public void UpdateUserPreferences(bool imViaEmail, bool visible, IClientAPI remoteClient)
        {
            XProfileData[] prefs = m_PrefsTable.Get("UserID",
                    remoteClient.AgentId.ToString());

            XProfileData p;
            if (prefs.Length == 0)
            {
                p = new XProfileData();
                p.Data = new Dictionary<string, string>();
                p.UserID = remoteClient.AgentId;
            }
            else
            {
                p = prefs[0];
            }

            p.Data["Visible"] = "0";
            if (visible)
                p.Data["Visible"] = "1";

            p.Data["IMToEmail"] = "0";
            if (imViaEmail)
                p.Data["IMToEmail"] = "1";

            m_PrefsTable.Store(p);
        }
    }
}
