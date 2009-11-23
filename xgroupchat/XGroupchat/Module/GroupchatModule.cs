// ******************************************************************
// Copyright (c) 2008, 2009 Melanie Thielker
//
// All rights reserved
//
using System;
using System.Collections.Generic;
using log4net;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Text.RegularExpressions;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Region.CoreModules.Framework.EventQueue;
using OpenSim.Region.Framework.Interfaces;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Framework.Communications.Cache;

namespace Careminster.Modules.Groups
{
    struct PresenceData
    {
        public UUID userID;
        public string simID;
        public bool canVoiceChat;
        public bool isModerator;
        public bool mutesText;
        public bool windowOpen;
    }

    public interface IGroupchatModule
    {
        void SendRefresh(UUID groupID);
    }

    public class GroupchatModule : ISharedRegionModule, IGroupchatModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private List<Scene> m_Scenes = new List<Scene>();
        private bool m_Enabled = false;
        private IConfigSource m_Config;

        private string m_Server = String.Empty;
        private int m_Port = 6667;
        private string m_Nick = String.Empty;
        private string m_User = String.Empty;
        private string m_Pass = String.Empty;

        private Dictionary<UUID, List<UUID>> m_Channels =
                new Dictionary<UUID, List<UUID>>();
        private Dictionary<UUID, UUID> m_Roots = new Dictionary<UUID, UUID>();
        private Dictionary<UUID, string> m_GroupNames =
                new Dictionary<UUID, string>();
        private Dictionary<UUID, Dictionary<UUID, PresenceData>> m_PresenceData=
                new Dictionary<UUID, Dictionary<UUID, PresenceData>>();

        private IMessageTransferModule m_TransferModule = null;
        private IGroupsModule m_GroupsModule = null;

        private Object m_ConnectionLock = new Object();
        private Thread m_Receiver = null;
        private TcpClient m_TcpClient;
        private NetworkStream m_Stream = null;
        private StreamReader m_Reader;
        private StreamWriter m_Writer;

        private int m_LastConnectAttempt = 0;

        public void Initialise(IConfigSource config)
        {
            IConfig groupsConfig = config.Configs["Groups"];

            if (groupsConfig == null)
            {
                return;
            }
            else
            {
                m_Enabled = groupsConfig.GetBoolean("UseChat", false);
                if (!m_Enabled)
                    return;

                if (groupsConfig.GetString("ChatModule", "Default") != "XGroupchat")
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
            lock (m_Scenes)
            {
                if (m_Scenes.Count == 0)
                {
                    if (!m_Enabled)
                        return;

                    m_Enabled = Configure(scene);
                }
                m_Scenes.Add(scene);
                scene.RegisterModuleInterface<IGroupchatModule>(this);
            }

            scene.EventManager.OnNewClient += OnNewClient;
            scene.EventManager.OnClientClosed += OnClientClosed;
            scene.EventManager.OnIncomingInstantMessage +=
                    OnIncomingInstantMessage;
            scene.EventManager.OnMakeRootAgent += OnMakeRootAgent;
            scene.EventManager.OnMakeChildAgent += OnMakeChildAgent;
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
                    m_log.Error("[GROUPCHAT]: No transfer module could be found. "+
                            "Group messages will not work.");
                    m_Enabled = false;
                }

            }

            if (m_GroupsModule == null)
            {
                m_GroupsModule =
                        m_Scenes[0].RequestModuleInterface<IGroupsModule>();

                if (m_GroupsModule == null)
                {
                    m_log.Error("[GROUPCHAT]: No groups module could be found. "+
                            "Group messages will not work.");
                    m_Enabled = false;
                }
                else
                {
                    m_GroupsModule.OnNewGroupNotice += NewGroupNotice;
                }
            }

            if (m_Enabled)
                m_log.Info("[GROUPCHAT] Activated XGroupchat module");
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            List<UUID> regionUsers = new List<UUID>();

            foreach (KeyValuePair<UUID, UUID> kvp in m_Roots)
            {
                if (kvp.Value == scene.RegionInfo.RegionID)
                    regionUsers.Add(kvp.Key);
            }

            foreach (UUID user in regionUsers)
                OnClientClosed(user, scene);

            m_Scenes.Remove(scene);
        }

        public void PostInitialise()
        {
            Startup();
        }

        public void Close()
        {
            if (!m_Enabled)
                return;
            m_log.Info("[GROUP]: Shutting down groupchat module.");
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public string Name
        {
            get { return "XGroupchatModule"; }
        }

        public bool Configure(Scene scene)
        {
            IConfig groupsConfig = m_Config.Configs["Groups"];

            string oneRegion = scene.RegionInfo.RegionName.Replace(" ", "_");

            m_Server = groupsConfig.GetString("Server", "");
            m_Port = groupsConfig.GetInt("Port", 6667);
            m_Nick = groupsConfig.GetString("Nickname", oneRegion);
            m_User = groupsConfig.GetString("Username", oneRegion);
            m_Pass = groupsConfig.GetString("Password", String.Empty);

            if (m_Server == String.Empty || m_Nick == String.Empty ||
                m_User == String.Empty)
            {
                return false;
            }

            m_User += " 8 * :Region server group chat module";

            return true;
        }

        private void Startup()
        {
            m_LastConnectAttempt = System.Environment.TickCount;

            m_Receiver = new Thread(new ThreadStart(ReceiverLoop));
            m_Receiver.Name = "GroupChatThread";
            m_Receiver.IsBackground = true;
            m_Receiver.Start();
        }


        private void Connect()
        {
            try
            {
                m_Stream = null;

                m_log.InfoFormat("[GROUPCHAT] Connecting to {0}", m_Server);
                m_TcpClient = new TcpClient(m_Server, m_Port);
                m_Stream = m_TcpClient.GetStream();
                m_Reader = new StreamReader(m_Stream);
                m_Writer = new StreamWriter(m_Stream);

                m_log.InfoFormat("[GROUPCHAT] Connected to {0}", m_Server);

                if (m_Pass != String.Empty)
                    m_Writer.WriteLine(String.Format("PASS {0}",
                            m_Pass));
                m_Writer.WriteLine(String.Format("NICK {0}",
                        m_Nick));
                m_Writer.WriteLine(String.Format("USER {0}",
                        m_User));
                m_Writer.Flush();
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[GROUPCHAT] Error connecting: "+e.ToString());
                m_Stream = null;
            }
        }

        private void ReceiverLoop()
        {
            while(m_Enabled)
            {
                lock (m_ConnectionLock)
                {
                    if (m_Stream == null)
                    {
                        if ((System.Environment.TickCount -
                                m_LastConnectAttempt) < 2000)
                        {
                            System.Threading.Thread.Sleep(1000);
                            continue;
                        }

                        m_LastConnectAttempt = System.Environment.TickCount;

                        if (m_Server != String.Empty)
                            Connect();

                        continue;
                    }
                }

                string line;

                try
                {
                    if ((line = m_Reader.ReadLine()) == null)
                    {
                        lock (m_ConnectionLock)
                        {
                            m_Stream = null;
                            continue;
                        }
                    }
                }
                catch (Exception e)
                {
                    // IO Exception (server has gone away)
                    //
                    lock (m_ConnectionLock)
                    {
                        m_Stream = null;
                        continue;
                    }
                }
                
                ProcessMessage(line);
            }
        }

        private void ProcessMessage(string line)
        {
            ProcessServerMessage(line);
        }

        private void ProcessServerMessage(string line)
        {
            // m_log.DebugFormat("[GROUPCHAT] Line: {0}", line);
            string[] commArgs;
            string pfx = String.Empty;
            string cmd = String.Empty;
            string parms = String.Empty;

            commArgs = line.Split(new char[] {' '},2);

            if (commArgs[0].StartsWith(":"))
            {
                pfx = commArgs[0].Substring(1);
                commArgs = commArgs[1].Split(new char[] {' '},2);
            }

            cmd = commArgs[0];
            parms = commArgs[1];

            switch (cmd)
            {
                case "004": // Server info, mens we are fully connected
                    JoinChannels();
                    break;
                case "433":
                    m_Nick = m_Nick + Util.RandomClass.Next(1, 99);
                    lock (m_ConnectionLock)
                    {
                        m_TcpClient.Close();
                        m_Stream = null;
                    }
                    break;
                case "479":
                    m_Enabled = false;
                    break;
                case "PING":
                    m_Writer.WriteLine(String.Format("PONG {0}", parms));
                    m_Writer.Flush();
                    break;
                case "PRIVMSG":
                    if (parms.Length >= 40)
                        ProcessChannelMessage(pfx, parms);
                    break;
                case "JOIN":
                    ProcessChannelJoin(pfx, parms);
                    break;
                case "PART":
                    ProcessChannelPart(pfx, parms);
                    break;
                default:
                    break;
            }
        }

        private void ProcessChannelMessage(string pfx, string parms)
        {
            string group = parms.Substring(1, 36);
            UUID groupID;

            if (!UUID.TryParse(group, out groupID))
                return;

            if (groupID == UUID.Zero)
                return;

            string msg = parms.Substring(39);

            if (msg.StartsWith("SS"))
            {
                string[] parts = msg.Split(new char[] {'|'}, 4);
                if (parts.Length != 4)
                    return;

                UUID fromAgentID;
                string fromAgentName = parts[2];
                if (!UUID.TryParse(parts[1], out fromAgentID))
                    return;

                SendLocal(fromAgentID, fromAgentName, groupID, parts[3]);
            }
            else if (msg.StartsWith("NO"))
            {
                string[] parts = msg.Split(new char[] {'|'}, 4);
                if (parts.Length != 4)
                    return;

                UUID noticeID;
                if (!UUID.TryParse(parts[3], out noticeID))
                    return;

                if (m_Channels.ContainsKey(groupID))
                {
                    List<UUID> locals = m_Channels[groupID];
                    foreach (UUID member in locals)
                    {
                        ScenePresence p = FindPresence(member);
                        if (p.IsChildAgent)
                            continue;

                        GroupMembershipData membership =
                                m_GroupsModule.GetMembershipData(groupID,
                                member);

                        if (membership != null && membership.AcceptNotices)
                        {
                            GridInstantMessage im =
                                    m_GroupsModule.CreateGroupNoticeIM(
                                    member, noticeID,
                                    (byte)InstantMessageDialog.GroupNotice);
                            p.ControllingClient.SendInstantMessage(im);
                            break;
                        }
                    }
                }
            }
            else if(msg.StartsWith("PR"))
            {
                string[] parts = msg.Split(new char[] {'|'});
                if (parts.Length < 6)
                    return;

                bool present = Convert.ToBoolean(parts[2]);
                if (!present)
                {
                    RemovePresenceData(groupID, new UUID(parts[1]));
                }
                else
                {
                    PresenceData p = new PresenceData();
                    p.userID = new UUID(parts[1]);
                    p.simID = pfx;
                    p.canVoiceChat = Convert.ToBoolean(parts[3]);
                    p.isModerator = Convert.ToBoolean(parts[4]);
                    p.mutesText = Convert.ToBoolean(parts[5]);

                    AddPresenceData(groupID, p);
                }
            }
            else if(msg.StartsWith("RF"))
            {
                if (m_Channels.ContainsKey(groupID))
                {
                    List<UUID> locals = m_Channels[groupID];
                    foreach (UUID member in locals)
                    {
                        ScenePresence p = FindPresence(member);
                        if (p.IsChildAgent)
                            continue;

                        p.ControllingClient.RefreshGroupMembership();
                    }
                }
            }
        }

        private void JoinChannels()
        {
            foreach (KeyValuePair<UUID, List<UUID>> kvp in m_Channels)
            {
                string channel = kvp.Key.ToString();
                if (kvp.Value.Count > 0)
                {
                    m_Writer.WriteLine(String.Format("JOIN #{0}", channel));
                    m_Writer.Flush();
                }
            }
        }

        private void Send(UUID fromAgentID, string fromAgentName,
                string channel, string message)
        {
            string msg = String.Format("SS|{0}|{1}|{2}", fromAgentID,
                    fromAgentName, message);

            SendText(channel, msg);

            // Local delivery
            UUID sessionID;
            if (!UUID.TryParse(channel, out sessionID))
                return;

            SendLocal(fromAgentID, fromAgentName, sessionID, message);
        }

        private void SendNotice(UUID groupID, UUID noticeID)
        {
            string msg = String.Format("NO|{0}|{1}|{2}", groupID.ToString(),
                    "*", noticeID.ToString());

            SendText(groupID.ToString(), msg);
        }

        public void SendRefresh(UUID groupID)
        {
            string msg = String.Format("RF|{0}|{1}|", groupID.ToString(),
                    "*");
            
            SendText(groupID.ToString(), msg);
        }

        private void SendText(string channel, string message)
        {
            string chanmsg = String.Format("PRIVMSG #{0} :{1}", channel,
                    message);

            SendCommand(chanmsg);
        }

        private void SendLocal(UUID fromAgentID, string fromAgentName,
                UUID sessionID, string message)
        {
            lock(m_PresenceData)
            {
                if (!m_PresenceData.ContainsKey(sessionID))
                    return;
                List<UUID> locals = GetLocals(sessionID);

                foreach (UUID agentID in locals)
                {
                    ScenePresence p = FindPresence(agentID);
                    if (p.IsChildAgent)
                        continue;

                    Scene scene = FindScene(agentID);
                    if (scene == null)
                        continue;

                    if (!m_GroupNames.ContainsKey(sessionID))
                        continue;

                    SendToClient(scene, agentID, fromAgentID, fromAgentName,
                            sessionID, message, m_GroupNames[sessionID]);

                }
            }
        }

        private void SendCommand(string cmd)
        {
            lock (m_ConnectionLock)
            {
                if (m_Stream != null)
                {
                    m_Writer.WriteLine(cmd);
                    m_Writer.Flush();
                }
            }
        }

        // Utilities
        //

        private void AddSession(UUID user, UUID session)
        {
            GroupMembershipData membership =
                    m_GroupsModule.GetMembershipData(session, user);

            if ((membership.GroupPowers & (ulong)GroupPowers.JoinChat) == 0)
                return;

            if (m_Channels.ContainsKey(session))
            {
                if (!m_Channels[session].Contains(user))
                    m_Channels[session].Add(user);
            }
            else
            {
                m_Channels[session] = new List<UUID>();
                m_Channels[session].Add(user);

                string cmd = String.Format("JOIN #{0}", session.ToString());
                SendCommand(cmd);
            }

            lock(m_PresenceData)
            {
                PresenceData p = new PresenceData();
                p.userID = user;
                p.simID = String.Empty;
                p.isModerator = (membership.GroupPowers & (ulong)GroupPowers.ModerateChat) != 0;
                p.canVoiceChat = (membership.GroupPowers & (ulong)GroupPowers.AllowVoiceChat) != 0;
                p.mutesText = false;

                if (!m_PresenceData.ContainsKey(session))
                    m_PresenceData[session] = new Dictionary<UUID, PresenceData>();

                m_PresenceData[session][user] = p;

                string pn = String.Format("PR|{0}|true|{1}|{2}|{3}",
                        user.ToString(), p.canVoiceChat, p.isModerator, p.mutesText);
                SendText(session.ToString(), pn);

                Dictionary<string, List<PresenceData>> updates =
                        new Dictionary<string, List<PresenceData>>();

                updates["ENTER"] = new List<PresenceData>();
                updates["ENTER"].Add(p);

                SendAgentListUpdates(session, updates);
                
                SendSessionAgentListToUser(session, user);
            }
        }

        private void RemoveSession(UUID user, UUID session)
        {
            lock(m_PresenceData)
            {
                if (m_PresenceData.ContainsKey(session))
                {
                    if (m_PresenceData[session].ContainsKey(user))
                    {
                        Dictionary<string, List<PresenceData>> updates =
                                new Dictionary<string, List<PresenceData>>();

                        updates["LEAVE"] = new List<PresenceData>();
                        updates["LEAVE"].Add(m_PresenceData[session][user]);

                        m_PresenceData[session].Remove(user);
                        if (m_PresenceData[session].Count == 0)
                            m_PresenceData.Remove(session);
                        
                        string pn = String.Format("PR|{0}|false|false|false|false", user.ToString());
                        SendText(session.ToString(), pn);

                        SendAgentListUpdates(session, updates);
                    }
                }
            }
        }

        private void RemoveChannel(UUID user, UUID session)
        {
            if (m_Channels.ContainsKey(session))
            {
                if (m_Channels[session].Contains(user))
                {
                    m_Channels[session].Remove(user);
                    if (m_Channels[session].Count == 0)
                    {
                        m_Channels.Remove(session);
                        string cmd = String.Format("PART #{0}", session.ToString());
                        SendCommand(cmd);
                    }
                }
            }

            lock(m_PresenceData)
            {
                if (m_PresenceData.ContainsKey(session))
                    m_PresenceData.Remove(session);
            }
        }

        private ScenePresence FindPresence(UUID agentID)
        {
            foreach (Scene s in m_Scenes)
            {
                ScenePresence p = s.GetScenePresence(agentID);
                if (p != null && !p.IsChildAgent)
                    return p;
            }
            foreach (Scene s in m_Scenes)
            {
                ScenePresence p = s.GetScenePresence(agentID);
                if (p != null)
                    return p;
            }
            return null;
        }

        private Scene FindScene(UUID agentID)
        {
            // Prefer root
            //
            foreach (Scene s in m_Scenes)
            {
                ScenePresence p = s.GetScenePresence(agentID);
                if (p != null && (!p.IsChildAgent))
                    return s;
            }
            foreach (Scene s in m_Scenes)
            {
                ScenePresence p = s.GetScenePresence(agentID);
                if (p != null)
                    return s;
            }
            return null;
        }

        private void OnNewClient(IClientAPI client)
        {
            // Subscribe to instant messages
            client.OnInstantMessage += OnInstantMessage;
        }

        private void OnClientClosed(UUID agentID, Scene scene)
        {
            lock (m_Roots)
            {
                if (m_Roots.ContainsKey(agentID) &&
                        m_Roots[agentID] == scene.RegionInfo.RegionID)
                {
                    m_Roots.Remove(agentID);

                    foreach (UUID groupID in new List<UUID>(m_Channels.Keys))
                    {
                        if (m_Channels[groupID].Contains(agentID))
                        {
                            RemoveSession(agentID, groupID);
                            RemoveChannel(agentID, groupID);
                        }
                        if (!m_Channels.ContainsKey(groupID))
                        {
                            if (m_GroupNames.ContainsKey(groupID))
                                m_GroupNames.Remove(groupID);
                        }
                    }
                }
            }
        }

        private void OnMakeRootAgent(ScenePresence presence)
        {
            lock (m_Roots)
            {
                if (!m_Roots.ContainsKey(presence.ControllingClient.AgentId))
                {
                    GroupMembershipData[] memberships =
                            m_GroupsModule.GetMembershipData(
                            presence.ControllingClient.AgentId);
                    
                    foreach (GroupMembershipData m in memberships)
                    {
                        if ((m.GroupPowers & (ulong)GroupPowers.JoinChat) == 0)
                            continue;
                        if (!m_GroupNames.ContainsKey(m.GroupID))
                            m_GroupNames[m.GroupID] = m.GroupName;
                        AddSession(presence.ControllingClient.AgentId, m.GroupID);
                    }
                }

                m_Roots[presence.ControllingClient.AgentId] =
                        presence.Scene.RegionInfo.RegionID;
            }
        }

        private void OnMakeChildAgent(ScenePresence presence)
        {
            OnClientClosed(presence.ControllingClient.AgentId, presence.Scene);
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

            if (dialog == (byte)InstantMessageDialog.SessionGroupStart)
            {
                // A group chat was opened manually, or a client logged in
                OSDMap llsd = new OSDMap();

                llsd.Add("temp_session_id", OSD.FromUUID(imSessionID));
                llsd.Add("session_id", OSD.FromUUID(imSessionID));
                llsd.Add("success", OSD.FromBoolean(true));
                // llsd.Add("error", );
                // Map: agent_info
                // index = agent ID, data = map with "is_moderator" and "mutes"
                // Optional and not yet implemented

                Scene scene = FindScene(fromAgentID);

                if (scene == null)
                    return;

                IEventQueue eq = scene.RequestModuleInterface<IEventQueue>();
                if (eq == null)
                    return;

                eq.Enqueue(EventQueueHelper.buildEvent("ChatterBoxSessionStartReply",
                        llsd), fromAgentID);

                AddSession(fromAgentID, imSessionID);
                lock(m_PresenceData)
                {
                    if (!m_PresenceData.ContainsKey(imSessionID) ||
                            !m_PresenceData[imSessionID].ContainsKey(fromAgentID))
                        return;

                    PresenceData p = m_PresenceData[imSessionID][fromAgentID];
                    p.windowOpen = true;
                    m_PresenceData[imSessionID][fromAgentID] = p;
                }
            }
            else if (dialog == (byte)InstantMessageDialog.SessionSend)
            {
                lock(m_PresenceData)
                {
                    if (!m_PresenceData.ContainsKey(imSessionID) ||
                            !m_PresenceData[imSessionID].ContainsKey(fromAgentID))
                        return;
                    
                    Send(fromAgentID, fromAgentName, imSessionID.ToString(),
                            message);
                }
            }
            else if (dialog == (byte)InstantMessageDialog.SessionDrop)
            {
                RemoveSession(fromAgentID, imSessionID);
            }
        }

        private void OnIncomingInstantMessage(GridInstantMessage im)
        {
            if (im.dialog != (byte)InstantMessageDialog.GroupNotice)
                return;

            ScenePresence p = FindPresence(new UUID(im.toAgentID));
            if (p.IsChildAgent)
                return;

            p.ControllingClient.SendInstantMessage(im);
        }

        private void SendToClient(Scene scene, UUID agentID, UUID fromID, string fromAgentName, UUID sessionID, string message, string sessionName)
        {
            OSDMap llsd = new OSDMap();
            OSDMap instantmessage = new OSDMap();
            OSDMap message_params = new OSDMap();
            OSDMap data = new OSDMap();

            Byte[] binary = Utils.StringToBytes(sessionName);

            message_params.Add("message", OSD.FromString(message));
            message_params.Add("from_name", OSD.FromString(fromAgentName));
            message_params.Add("from_id", OSD.FromUUID(fromID));
            message_params.Add("id", OSD.FromUUID(sessionID));
            message_params.Add("offline", OSD.FromBoolean(false));
            message_params.Add("timestamp", OSD.FromInteger((uint)Util.UnixTimeSinceEpoch()));
            message_params.Add("parent_estate_id", OSD.FromInteger(1));
            message_params.Add("position", OSD.FromVector3(Vector3.Zero));

            data.Add("binary_bucket", OSD.FromBinary(binary));
            message_params.Add("data", data);

            instantmessage.Add("message_params", message_params);
            llsd.Add("instantmessage", instantmessage);

            IEventQueue eq = scene.RequestModuleInterface<IEventQueue>();
            if (eq == null)
                return;

            eq.Enqueue(EventQueueHelper.buildEvent("ChatterBoxInvitation",
                    llsd), agentID);

            lock(m_PresenceData)
            {
                if (m_PresenceData[sessionID][agentID].windowOpen == false)
                {
                    SendSessionAgentListToUser(sessionID, agentID);
                    PresenceData p = m_PresenceData[sessionID][agentID];
                    p.windowOpen = true;
                    m_PresenceData[sessionID][agentID] = p;
                }
            }
        }

        private void NewGroupNotice(UUID groupID, UUID noticeID)
        {
            if (!m_Channels.ContainsKey(groupID))
                return;

            SendNotice(groupID, noticeID);

            SendIMNotice(groupID, noticeID);
        }

        private void SendIMNotice(UUID groupID, UUID noticeID)
        {
            List<GroupMembersData> members = m_GroupsModule.GroupMembersRequest(null, groupID);

            List<UUID> inChat = new List<UUID>();
            
            foreach (GroupMembersData gd in members)
            {
                if (!gd.AcceptNotices)
                    continue;

                GridInstantMessage im = m_GroupsModule.CreateGroupNoticeIM(
                        gd.AgentID, noticeID,
                        (byte)InstantMessageDialog.GroupNotice);

                ScenePresence p = FindPresence(gd.AgentID);
                if (p != null)
                {
                    if (!p.IsChildAgent)
                    {
                        p.ControllingClient.SendInstantMessage(im);
                    }
                }
                else
                {
                    lock(m_PresenceData)
                    {
                        if (!m_PresenceData.ContainsKey(groupID) ||
                            !m_PresenceData[groupID].ContainsKey(gd.AgentID))
                        {
                            m_TransferModule.SendInstantMessage(im,
                                    delegate(bool success) {});
                        }
                    }
                }
            }
        }

        private void SendPresences(UUID channel)
        {
            lock (m_PresenceData)
            {
                if (!m_PresenceData.ContainsKey(channel))
                    return;

                foreach (PresenceData p in m_PresenceData[channel].Values)
                {
                    if (p.simID != String.Empty)
                        continue;
                    string text = String.Format("PR|{0}|true|{1}|{2}|{3}",
                            p.userID.ToString(), p.canVoiceChat, p.isModerator,
                            p.mutesText);
                    SendText(channel.ToString(), text);
                }
            }
        }

        private void ProcessChannelJoin(string pfx, string parms)
        {
            if (!parms.StartsWith(":#"))
                return;

            UUID groupID;

            if (!UUID.TryParse(parms.Substring(2), out groupID))
                return;

            RemovePresenceData(groupID, pfx);

            SendPresences(groupID);
        }

        private void ProcessChannelPart(string pfx, string parms)
        {
            if (!parms.StartsWith("#"))
                return;

            string[] parts = parms.Split(new char[] {' '}, 2);

            UUID groupID;

            if (!UUID.TryParse(parts[0].Substring(1), out groupID))
                return;

            RemovePresenceData(groupID, pfx);
        }

        private void RemovePresenceData(UUID groupID, string simID)
        {
            lock(m_PresenceData)
            {
                if (!m_PresenceData.ContainsKey(groupID))
                    return;

                Dictionary<string, List<PresenceData>> updates =
                        new Dictionary<string, List<PresenceData>>();
                updates["LEAVE"] = new List<PresenceData>();

                Dictionary<UUID, PresenceData> newPresences =
                        new Dictionary<UUID, PresenceData>();

                foreach (PresenceData p in m_PresenceData[groupID].Values)
                {
                    if (p.simID == simID)
                    {
                        updates["LEAVE"].Add(p);
                        continue;
                    }

                    newPresences[p.userID] = p;
                }

                m_PresenceData[groupID]=newPresences;

                if (updates["LEAVE"].Count > 0)
                {
                    SendAgentListUpdates(groupID, updates);
                }
            }
        }


        private void RemovePresenceData(UUID groupID, UUID userID)
        {
            lock (m_PresenceData)
            {
                if (!m_PresenceData.ContainsKey(groupID))
                    return;
                if (!m_PresenceData[groupID].ContainsKey(userID))
                    return;
                if(m_PresenceData[groupID].ContainsKey(userID))
                {
                    Dictionary<string, List<PresenceData>> updates =
                            new Dictionary<string, List<PresenceData>>();
                    updates["LEAVE"] = new List<PresenceData>();
                    updates["LEAVE"].Add(m_PresenceData[groupID][userID]);

                    SendAgentListUpdates(groupID, updates);

                    m_PresenceData[groupID].Remove(userID);
                }
            }
        }

        private void AddPresenceData(UUID groupID, PresenceData p)
        {
            lock(m_PresenceData)
            {
                if (!m_PresenceData.ContainsKey(groupID))
                    m_PresenceData[groupID] = new Dictionary<UUID, PresenceData>();

                Dictionary<string, List<PresenceData>> updates =
                        new Dictionary<string, List<PresenceData>>();

                string action = String.Empty;
                if (!m_PresenceData[groupID].ContainsKey(p.userID))
                    action = "ENTER";

                updates[action] = new List<PresenceData>();
                updates[action].Add(p);

                SendAgentListUpdates(groupID, updates);

                m_PresenceData[groupID][p.userID] = p;
            }
        }

        private void SendAgentListUpdates(UUID groupID,
                Dictionary<string, List<PresenceData>> data)
        {
            if (!m_Channels.ContainsKey(groupID))
                return;
            List<UUID> locals = m_Channels[groupID];

            if (locals.Count == 0)
                return;

            OSDMap body = new OSDMap();
            OSDMap agentUpdates = new OSDMap();

            foreach (KeyValuePair<string, List<PresenceData>> kvp in data)
            {
                foreach (PresenceData p in kvp.Value)
                {
                    OSDMap info = new OSDMap();
                    OSDMap mutes = new OSDMap();

                    mutes.Add("text", OSD.FromBoolean(p.mutesText));
                    info.Add("can_voice_chat", OSD.FromBoolean(p.canVoiceChat));
                    info.Add("is_moderator", OSD.FromBoolean(p.isModerator));
                    info.Add("mutes", mutes);

                    OSDMap agent = new OSDMap();
                    agent.Add("info", info);

                    if (kvp.Key != String.Empty)
                        agent.Add("transition", OSD.FromString(kvp.Key));
                    agentUpdates.Add(p.userID.ToString(), agent);
                }
            }

            body.Add("session_id", OSD.FromUUID(groupID));
            body.Add("updates", new OSD());
            body.Add("agent_updates", agentUpdates);

            foreach(UUID toAgentID in locals)
            {
                Scene scene = FindScene(toAgentID);
                if (scene == null)
                    return;

                IEventQueue eq = scene.RequestModuleInterface<IEventQueue>();
                if (eq == null)
                    return;

                eq.Enqueue(EventQueueHelper.buildEvent(
                        "ChatterBoxSessionAgentListUpdates",
                        body), toAgentID);
            }
        }

        private List<UUID> GetLocals(UUID channel)
        {
            lock(m_PresenceData)
            {
                if (!m_PresenceData.ContainsKey(channel))
                    return new List<UUID>();

                List<UUID> locals = new List<UUID>();

                foreach (PresenceData p in m_PresenceData[channel].Values)
                {
                    if (p.simID == String.Empty) // Local
                        locals.Add(p.userID);
                }

                return locals;
            }
        }

        private void SendSessionAgentListToUser(UUID groupID, UUID toAgentID)
        {
            lock(m_PresenceData)
            {
                if (!m_PresenceData.ContainsKey(groupID))
                    return;

                OSDMap body = new OSDMap();
                OSDMap agentUpdates = new OSDMap();

                foreach (PresenceData p in m_PresenceData[groupID].Values)
                {
                    OSDMap info = new OSDMap();
                    OSDMap mutes = new OSDMap();

                    mutes.Add("text", OSD.FromBoolean(p.mutesText));
                    info.Add("can_voice_chat", OSD.FromBoolean(p.canVoiceChat));
                    info.Add("is_moderator", OSD.FromBoolean(p.isModerator));
                    info.Add("mutes", mutes);

                    OSDMap agent = new OSDMap();
                    agent.Add("info", info);

                    agent.Add("transition", OSD.FromString("ENTER"));
                    agentUpdates.Add(p.userID.ToString(), agent);
                }

                body.Add("session_id", OSD.FromUUID(groupID));
                body.Add("updates", new OSD());
                body.Add("agent_updates", agentUpdates);

                Scene scene = FindScene(toAgentID);
                if (scene == null)
                    return;

                IEventQueue eq = scene.RequestModuleInterface<IEventQueue>();
                if (eq == null)
                    return;

                eq.Enqueue(EventQueueHelper.buildEvent(
                        "ChatterBoxSessionAgentListUpdates",
                        body), toAgentID);
            }
        }
    }
}
