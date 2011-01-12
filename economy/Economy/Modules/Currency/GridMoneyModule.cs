// *************************************************************************
// Copyright (c) 2008, 2009, 2010 Careminster Limited and  Melanie Thielker
//
// All rights reserved
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Xml;
using OpenMetaverse;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Framework.Servers;
using OpenSim.Services.Interfaces;
using OpenSim.Region.Framework.Scenes;
using System.Data;
using MySql.Data.MySqlClient;
using Mono.Addins;
using OpenSim.Data;
using OpenSim.Data.MySQL;

[assembly: Addin("Economy.Modules", "1.8")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace Careminster.Modules.Currency
{
    public class AccountData
    {
        public UUID a_agent;
        public Dictionary<string,string> Data;
    }

    public class LedgerData
    {
        public Dictionary<string,string> Data;
    }

    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "GridMoneyModule")]
    public class GridMoneyModule : ISharedRegionModule, IMoneyModule
    {
        private const string currency = "L$";
        //
        // Log module
        //
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);


        private const string m_YouGotPaidFor = "{0} paid you "+currency+" {1} for {2}";
        private const string m_YouPaidFor = "You paid {0} "+currency+" {1} for {2}";

        private const string m_YouGotPaid = "{0} paid you "+currency+" {1}";
        private const string m_YouPaid = "You paid {0} "+currency+" {1}";

        private const string m_YouPaidObject = "You paid {0} "+currency+" {1} through "+
                "object {2}";

        private const string m_YouGotPaidObject = "{0} paid you "+currency+" {1} "+
                "through your object {2}";

        private const string m_YourObjectPaid = "Your object {2} paid {0} "+currency+" "+
                "{1}";
        private const string m_YourObjectGotPaid = "{0} paid you "+currency+" {1} via "+
                "object {2}";

        private const string m_YouPaidForLand = "You paid {0} "+currency+" {1} for a "+
                "parcel of land";

        private const string m_YouGotPaidForLand = "{0} paid you "+currency+" {1} for "+
                "a parcel of {2} sq m";

        //
        // Module vars
        //
        private IConfigSource m_Config;

        // Scenes by Region Handle
        private Dictionary<UUID, Scene> m_Scenes =
                new Dictionary<UUID, Scene>();

        //
        // Economy config params
        //
        private UUID EconomyBaseAccount = UUID.Zero;
        private bool m_Enabled = true;
        private string m_DatabaseConnect = String.Empty;
        private string m_MoneyAddress = String.Empty;

        private float EnergyEfficiency = 0f;
        private int ObjectCount = 0;
        private int PriceEnergyUnit = 0;
        private int PriceGroupCreate = 0;
        private int PriceObjectClaim = 0;
        private float PriceObjectRent = 0f;
        private float PriceObjectScaleFactor = 0f;
        private int PriceParcelClaim = 0;
        private float PriceParcelClaimFactor = 0f;
        private int PriceParcelRent = 0;
        private int PricePublicObjectDecay = 0;
        private int PricePublicObjectDelete = 0;
        private int PriceRentLight = 0;
        private int PriceUpload = 0;
        private int TeleportMinPrice = 0;
        private float TeleportPriceExponent = 0f;
        private int UserLevelPaysFees = 2;

        protected IDialogModule m_dialogModule;

        //
        // Database
        //
        private MySQLAccountsTableHandler m_AccountsTable;
        private MySQLLedgerTableHandler m_LedgerTable;

        //
        // Event handler
        //
        public event ObjectPaid OnObjectPaid;

        public int UploadCharge
        {
            get { return PriceUpload; }
        }

        public int GroupCreationCharge
        {
            get { return PriceGroupCreate; }
        }

        public void Initialise(IConfigSource config)
        {
            m_Config = config;

            IConfig startupConfig = m_Config.Configs["Startup"];
            IConfig economyConfig = m_Config.Configs["Economy"];

            m_Enabled = (startupConfig.GetString("economymodule",
                    "BetaGridLikeMoneyModule") == "GridMoneyModule");

            if (!m_Enabled)
                return;

            PriceEnergyUnit =
                    economyConfig.GetInt("PriceEnergyUnit", 100);
            PriceObjectClaim =
                    economyConfig.GetInt("PriceObjectClaim", 10);
            PricePublicObjectDecay =
                    economyConfig.GetInt("PricePublicObjectDecay", 4);
            PricePublicObjectDelete =
                    economyConfig.GetInt("PricePublicObjectDelete", 4);
            PriceParcelClaim =
                    economyConfig.GetInt("PriceParcelClaim", 1);
            PriceParcelClaimFactor =
                    economyConfig.GetFloat("PriceParcelClaimFactor", 1f);
            PriceUpload =
                    economyConfig.GetInt("PriceUpload", 0);
            PriceRentLight =
                    economyConfig.GetInt("PriceRentLight", 5);
            TeleportMinPrice =
                    economyConfig.GetInt("TeleportMinPrice", 2);
            TeleportPriceExponent =
                    economyConfig.GetFloat("TeleportPriceExponent", 2f);
            EnergyEfficiency =
                    economyConfig.GetFloat("EnergyEfficiency", 1);
            PriceObjectRent =
                    economyConfig.GetFloat("PriceObjectRent", 1);
            PriceObjectScaleFactor =
                    economyConfig.GetFloat("PriceObjectScaleFactor", 10);
            PriceParcelRent =
                    economyConfig.GetInt("PriceParcelRent", 1);
            PriceGroupCreate =
                    economyConfig.GetInt("PriceGroupCreate", -1);
            string EBA =
                    economyConfig.GetString("EconomyBaseAccount",
                    UUID.Zero.ToString());
            UUID.TryParse(EBA, out EconomyBaseAccount);
            UserLevelPaysFees =
                    economyConfig.GetInt("UserLevelPaysFees", -1);
            m_DatabaseConnect =
                    economyConfig.GetString("DatabaseConnect",String.Empty);
            m_MoneyAddress =
                    economyConfig.GetString("CurrencyServer", String.Empty);

            m_AccountsTable = new MySQLAccountsTableHandler(m_DatabaseConnect, "accounts", String.Empty);
            m_LedgerTable = new MySQLLedgerTableHandler(m_DatabaseConnect, "ledger", String.Empty);

            IHttpServer httpServer = MainServer.Instance;
            httpServer.AddXmlRPCHandler("balanceUpdateRequest",
                    GridMoneyUpdate);
            httpServer.AddXmlRPCHandler("userAlert", UserAlert);
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            lock (m_Scenes)
            {
                // Claim the interface slot
                scene.RegisterModuleInterface<IMoneyModule>(this);

                m_Scenes[scene.RegionInfo.RegionID] = scene;
            }

            m_log.Info("[MONEY] Activated GridMoneyModule");

            // Hook up events
            scene.EventManager.OnNewClient += OnNewClient;
            scene.EventManager.OnMoneyTransfer += MoneyTransferAction;
            scene.EventManager.OnValidateLandBuy += ValidateLandBuy;
            scene.EventManager.OnLandBuy += ProcessLandBuy;
        }

        public void RegionLoaded(Scene scene)
        {
            m_dialogModule = scene.RequestModuleInterface<IDialogModule>();
        }

        public void RemoveRegion(Scene scene)
        {
            m_Scenes.Remove(scene.RegionInfo.RegionID);
        }

        public void ApplyUploadCharge(UUID agentID, int amount, string text)
        {
            Scene scene=GetClientScene(agentID);

            UserAccount account = scene.UserAccountService.GetUserAccount(scene.RegionInfo.ScopeID, agentID);

            if (account != null && ((account.UserFlags & 0xf00) >> 8) > UserLevelPaysFees)
                return;

            DoMoneyTransfer(agentID, EconomyBaseAccount, amount,
                    1101, text, scene);

        }

        public void ApplyCharge(UUID agentID, int amount, string text)
        {
            Scene scene=GetClientScene(agentID);

            UserAccount account = scene.UserAccountService.GetUserAccount(scene.RegionInfo.ScopeID, agentID);

            if (account != null && ((account.UserFlags & 0xf00) >> 8) > UserLevelPaysFees)
                return;

            DoMoneyTransfer(agentID, EconomyBaseAccount, amount,
                    1102, text, scene);

        }

        public bool ObjectGiveMoney(UUID objectID, UUID fromID, UUID toID, int amount)
        {
            Scene scene=GetPrimScene(objectID);

            if(ResolveAgentName(toID) == String.Empty)
            {
                SceneObjectPart part=FindPrim(toID);
                if(part == null)
                    return false;

                UUID ownerID=part.ParentGroup.RootPart.OwnerID;

                // Careminster extension: Object pays object
                return DoMoneyTransfer(fromID, ownerID, amount, 5009,
                        ResolveObjectName(objectID)+"+"+
                        part.ParentGroup.RootPart.Name, scene);
            }

            // Object pays user
            return DoMoneyTransfer(fromID, toID, amount, 5009,
                    ResolveObjectName(objectID), scene);
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public Type ReplacableInterface
        {
            get { return null; }
        }

        public string Name
        {
            get { return "GridMoneyModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        /// New Client Event Handler
        private void OnNewClient(IClientAPI client)
        {
            // Subscribe to Money messages
            client.OnEconomyDataRequest += EconomyDataRequestHandler;
            client.OnMoneyBalanceRequest += SendMoneyBalance;
            client.OnRequestPayPrice += RequestPayPrice;
            client.OnObjectBuy += ObjectBuy;

            ScenePresence agent = null;

            // Get the scene
            Scene s;
            if (client.Scene is Scene)
                s = (Scene)client.Scene;
            else
                return; // Can't process other scene types

            if (s != null)
            {
                agent = s.GetScenePresence(client.AgentId);
                if (!agent.IsChildAgent)
                    SendMoneyBalance(client);
            }
        }

        //
        // Make external XMLRPC request
        //
        private Hashtable GenericCurrencyXMLRPCRequest(Hashtable ReqParams, string method)
        {
            ArrayList SendParams = new ArrayList();
            SendParams.Add(ReqParams);

            // Send Request
            XmlRpcResponse MoneyResp;
            try
            {
                XmlRpcRequest BalanceRequestReq = new XmlRpcRequest(method, SendParams);
                MoneyResp = BalanceRequestReq.Send(m_MoneyAddress, 30000);
            }
            catch (WebException ex)
            {
                m_log.ErrorFormat(
                    "[MONEY]: Unable to connect to Money Server {0}.  Exception {1}",
                    m_MoneyAddress, ex);

                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to manage your money at this time. Purchases may be unavailable";
                ErrorHash["errorURI"] = "";

                return ErrorHash;
            }
            catch (SocketException ex)
            {
                m_log.ErrorFormat(
                    "[MONEY]: Unable to connect to Money Server {0}.  Exception {1}",
                    m_MoneyAddress, ex);

                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to manage your money at this time. Purchases may be unavailable";
                ErrorHash["errorURI"] = "";

                return ErrorHash;
            }
            catch (XmlException ex)
            {
                m_log.ErrorFormat(
                    "[MONEY]: Unable to connect to Money Server {0}.  Exception {1}",
                    m_MoneyAddress, ex);

                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to manage your money at this time. Purchases may be unavailable";
                ErrorHash["errorURI"] = "";

                return ErrorHash;
            }
            if (MoneyResp.IsFault)
            {
                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to manage your money at this time. Purchases may be unavailable";
                ErrorHash["errorURI"] = "";
                return ErrorHash;
            }
            Hashtable MoneyRespData = (Hashtable) MoneyResp.Value;

            return MoneyRespData;
        }

        //
        // Notify a nonlocal agent of a payment
        //
        private void NotifyAgent(UUID agentID, string message)
        {
            Hashtable ht = new Hashtable();
            ht["agentId"] = agentID.ToString();
            ht["description"] = message;

            Hashtable hresult = GenericCurrencyXMLRPCRequest(ht, "notifyAgent");

            if ((bool) hresult["success"] != true)
                m_log.WarnFormat("[MONEY] unable to notify remote agent "+
                        "of payment");
        }

        //
        // Get funds
        //
        private int GetAgentFunds(UUID agentID)
        {
            AccountData[] acc;
            lock (m_AccountsTable)
            {
                acc = m_AccountsTable.Get("a_agent", agentID.ToString());

                if (acc.Length < 1)
                {
                    AccountData a = new AccountData();
                    a.Data = new Dictionary<string,string>();
                    a.a_agent = agentID;
                    a.Data["a_amount"] = "0";
                    a.Data["a_upload"] = "0";

                    m_AccountsTable.Store(a);

                    return 0;
                }
            }

            return Convert.ToInt32(acc[0].Data["a_amount"]);
        }

        //
        // Get funds
        //
        private int GetAvailableAgentFunds(UUID agentID)
        {
            AccountData[] acc;
            lock (m_AccountsTable)
            {
                acc = m_AccountsTable.Get("a_agent", agentID.ToString());
                if (acc.Length < 1)
                {
                    AccountData a = new AccountData();
                    a.Data = new Dictionary<string,string>();
                    a.a_agent = agentID;
                    a.Data["a_amount"] = "0";
                    a.Data["a_upload"] = "0";

                    m_AccountsTable.Store(a);

                    return 0;
                }
            }

            int upload = Convert.ToInt32(acc[0].Data["a_upload"]);
            if (upload < 0)
                upload = 0;
            return Convert.ToInt32(acc[0].Data["a_amount"]) - upload;
        }

        //
        // Transfer money
        //
        private bool DoMoneyTransfer(UUID sender, UUID receiver, int amount,
                int transactiontype, string description, Scene scene)
        {
            string senderText;
            string receiverText;
            string sender_name;
            string receiver_name;

            if (amount >= 0)
            {
                int funds = GetAvailableAgentFunds(sender);
                if (transactiontype == 1101)
                    funds = GetAgentFunds(sender);
                if(funds < amount)
                    return false;

                switch(transactiontype)
                {
                case 1101: // Asset upload
                    // No need to send a text, as the viewer generates one
                    MoveMoney(sender, EconomyBaseAccount, amount,
                            transactiontype, "Asset upload fee", String.Empty,
                            String.Empty, scene);

                    break;
                case 1102: // Group Creation
                    MoveMoney(sender, EconomyBaseAccount, amount,
                            transactiontype, "Group reation fee", String.Empty,
                            String.Empty, scene);

                    break;
                case 5000: // Object bought
                    sender_name=ResolveAgentName(sender);
                    receiver_name=ResolveAgentName(receiver);

                    if(sender_name == String.Empty)
                        sender_name="(hippos)";
                    if(receiver_name == String.Empty)
                        receiver_name="(hippos)";

                    receiverText=String.Format(m_YouGotPaidFor, sender_name, amount,
                            description);

                    senderText=String.Format(m_YouPaidFor, receiver_name, amount,
                            description);

                    MoveMoney(sender, receiver, amount,
                            transactiontype, "Object purchase "+description,
                            senderText, receiverText, scene);

                    break;
                case 5001: // User pays user
                    sender_name=ResolveAgentName(sender);
                    receiver_name=ResolveAgentName(receiver);

                    if(sender_name == String.Empty)
                        sender_name="(hippos)";
                    if(receiver_name == String.Empty)
                        receiver_name="(hippos)";

                    receiverText=String.Format(m_YouGotPaid, sender_name, amount);

                    senderText=String.Format(m_YouPaid, receiver_name, amount);

                    MoveMoney(sender, receiver, amount,
                            transactiontype, "Gift", senderText, receiverText,
                            scene);

                    break;
                case 5008: // User pays object
                    sender_name=ResolveAgentName(sender);
                    receiver_name=ResolveAgentName(receiver);

                    if(sender_name == String.Empty)
                        sender_name="(hippos)";
                    if(receiver_name == String.Empty)
                        receiver_name="(hippos)";

                    senderText=String.Format(m_YouPaidObject, receiver_name, amount,
                            description);

                    receiverText=String.Format(m_YouGotPaidObject, sender_name, amount,
                            description);


                    MoveMoney(sender, receiver, amount,
                            transactiontype, "Paid object "+description,
                            senderText, receiverText, scene);

                    break;
                case 5009: // Object pays user
                    sender_name=ResolveAgentName(sender);
                    receiver_name=ResolveAgentName(receiver);

                    if(sender_name == String.Empty)
                        sender_name="(hippos)";
                    if(receiver_name == String.Empty)
                        receiver_name="(hippos)";

                    senderText=String.Format(m_YourObjectPaid, receiver_name, amount,
                            description);

                    receiverText=String.Format(m_YourObjectGotPaid, sender_name, amount,
                            description);


                    MoveMoney(sender, receiver, amount,
                            transactiontype, "Object "+description+" pays" ,
                            senderText, receiverText, scene);

                    break;
                case 5002: // Land transaction
                    sender_name=ResolveAgentName(sender);
                    receiver_name=ResolveAgentName(receiver);

                    if(sender_name == String.Empty)
                        sender_name="(hippos)";
                    if(receiver_name == String.Empty)
                        receiver_name="(hippos)";

                    senderText=String.Format(m_YouPaidForLand, receiver_name, amount);

                    receiverText=String.Format(m_YouGotPaidForLand, sender_name, amount,
                            description);

                    MoveMoney(sender, receiver, amount,
                            transactiontype, "Land purchase",
                            senderText, receiverText, scene);

                    break;
                }
                    
            }
            return true;
        }

        //
        // Make database ledger entry
        //
        private void MakeLedgerEntry(UUID sender, UUID receiver, int amount,
                int transactiontype, string text, Scene scene)
        {
            LedgerData l = new LedgerData();
            l.Data = new Dictionary<string,string>();
            l.Data["l_from"] = sender.ToString();
            l.Data["l_to"] = receiver.ToString();
            l.Data["l_amount"] = amount.ToString();
            l.Data["l_type"] = transactiontype.ToString();
            l.Data["l_description"] = text;
            l.Data["l_region"] = scene.RegionInfo.RegionName;

            m_LedgerTable.Store(l);
        }

        //
        // Update balance tables
        //
        private void MoveMoney(UUID sender, UUID receiver, int amount,
                int transactiontype, string text, string senderMessage,
                string receiverMessage, Scene scene)
        {
            // This creates the records if they're not there yet
            GetAgentFunds(sender);
            GetAgentFunds(receiver);

            lock (m_AccountsTable)
            {
                m_AccountsTable.Add(sender, -amount);
                if (transactiontype == 1101) // Upload
                    m_AccountsTable.AddUpload(sender, -amount);
                m_AccountsTable.Add(receiver, amount);
                MakeLedgerEntry(sender, receiver, amount,
                        transactiontype, text, scene);
            }

            // If the amount is 0, suppress annoying popups
            if (amount == 0)
            {
                // I believe in SL the payer still sees L$0 messages
                // senderMessage = String.Empty;
                receiverMessage = String.Empty;
            }

            // TODO: Check user prefs regarding payment notifications

            SendMoneyBalance(sender, senderMessage);
            SendMoneyBalance(receiver, receiverMessage);
        }

        //
        // Find the scene for an agent
        //
        private Scene GetClientScene(UUID agentId)
        {
            lock (m_Scenes)
            {
                foreach (Scene scene in m_Scenes.Values)
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

        //
        // Get the client for an agent
        //
        private IClientAPI FindClient(UUID agentID)
        {
            Scene scene=GetClientScene(agentID);
            if(scene == null)
                return null;

            ScenePresence presence=scene.GetScenePresence(agentID);
            if(presence == null)
                return null;

            return presence.ControllingClient;
        }

        //
        // Sends the the stored money balance to the client
        //
        private void SendMoneyBalance(IClientAPI client, UUID agentID,
                UUID sessionID, UUID transactionID)
        {
            SendMoneyBalance(client, String.Empty, transactionID);
        }
        private void SendMoneyBalance(IClientAPI client, string message,
                UUID transactionID)
        {
            int amount = GetAgentFunds(client.AgentId);
            client.SendMoneyBalance(transactionID, true,
                    Util.StringToBytes256(message), amount);
        }

        private void SendMoneyBalance(IClientAPI client)
        {
            SendMoneyBalance(client, String.Empty, UUID.Zero);
        }

        public int GetBalance(UUID agentID)
        {
            return GetAgentFunds(agentID);
        }

        private void SendMoneyBalance(UUID agentID, string text)
        {
            IClientAPI client=FindClient(agentID);
            if(client == null)
            {
                if (agentID != EconomyBaseAccount)
                    NotifyAgent(agentID, text);
                return;
            }

            SendMoneyBalance(client, text.Trim(), UUID.Zero);

//            if (text.Trim() != String.Empty)
//                client.SendBlueBoxMessage(UUID.Zero, "", text);
        }

        //
        // Find a prim in all supported scenes
        //
        private SceneObjectPart FindPrim(UUID objectID)
        {
            lock (m_Scenes)
            {
                foreach (Scene scene in m_Scenes.Values)
                {
                    SceneObjectPart part = scene.GetSceneObjectPart(objectID);
                    if (part != null)
                        return part;
                }
            }
            return null;
        }

        //
        // Find scene a prim is in
        //
        private Scene GetPrimScene(UUID objectID)
        {
            lock (m_Scenes)
            {
                foreach (Scene scene in m_Scenes.Values)
                {
                    SceneObjectPart part = scene.GetSceneObjectPart(objectID);
                    if (part != null)
                        return scene;
                }
            }
            return null;
        }

        //
        // Get the name of an object in the Scene
        //
        private string ResolveObjectName(UUID objectID)
        {
            SceneObjectPart part = FindPrim(objectID);
            if (part != null)
            {
                return part.Name;
            }
            return String.Empty;
        }

        //
        // Get the name of an agent
        //
        private string ResolveAgentName(UUID agentID)
        {
            // Fast way first
            Scene scene=GetClientScene(agentID);
            if(scene != null)
            {
                ScenePresence presence=scene.GetScenePresence(agentID);
                if(presence != null)
                    return presence.ControllingClient.Name;
            }

            // Try avatar username surname
            scene = GetRandomScene();
            UserAccount account = scene.UserAccountService.GetUserAccount(scene.RegionInfo.ScopeID, agentID);
            if (account != null)
                return account.FirstName + " " + account.LastName;

            // Maybe it's not an agent
            IGroupsModule groups = scene.RequestModuleInterface<IGroupsModule>();
            if (groups != null)
            {
                GroupRecord g = groups.GetGroupRecord(agentID);
                if (g != null)
                    return g.GroupName;
            }

            return String.Empty;
        }

        //
        // XmlRPC handler for balance updates from outside
        // This will not actually update the database
        //
        public XmlRpcResponse GridMoneyUpdate(XmlRpcRequest request,
                IPEndPoint endpoint)
        {
            bool success=false;

            Hashtable requestData = (Hashtable) request.Params[0];

            if (requestData.ContainsKey("agentId"))
            {
                UUID agentID;
                UUID.TryParse((string) requestData["agentId"], out agentID);

                string message = String.Empty;

                if(requestData.ContainsKey("message"))
                {
                    message = (string)requestData["message"];
                }

                SendMoneyBalance(agentID, message);
                success=true;
            }

            XmlRpcResponse r = new XmlRpcResponse();
            Hashtable rparms = new Hashtable();
            rparms["success"] = success;

            r.Value = rparms;
            return r;
        }

        // XMLRPC handler to send alert message and sound to client
        public XmlRpcResponse UserAlert(XmlRpcRequest request,
                IPEndPoint endpoint)
        {
            XmlRpcResponse ret = new XmlRpcResponse();
            Hashtable retparam = new Hashtable();
            Hashtable requestData = (Hashtable) request.Params[0];

            UUID agentId = UUID.Zero;
            UUID soundId = UUID.Zero;

            UUID.TryParse((string) requestData["agentId"], out agentId);
            UUID.TryParse((string) requestData["soundId"], out soundId);
            string text = (string) requestData["text"];

            string mode = "bluebox";
            if (requestData["type"] != null)
                mode = (string) requestData["type"];

            IClientAPI client = FindClient(agentId);

            if (client != null)
            {
                if (soundId != UUID.Zero)
                    client.SendPlayAttachedSound(soundId, agentId, agentId, 1.0f, 0);
                if (mode == "alert")
                    client.SendAgentAlertMessage(text, false);
                else if (mode == "modal")
                    client.SendAgentAlertMessage(text, true);
                else
                    client.SendBlueBoxMessage(UUID.Zero, "", text);
                retparam.Add("success", true);
            }
            else
            {
                retparam.Add("success", false);
            }
            ret.Value = retparam;

            return ret;
        }

        //
        // Get a random scene
        //
        public Scene GetRandomScene()
        {
            lock (m_Scenes)
            {
                foreach (Scene rs in m_Scenes.Values)
                    return rs;
            }
            return null;
        }

        //
        // Send the object's pay price
        //
        public void RequestPayPrice(IClientAPI client, UUID objectID)
        {
            Scene scene = GetClientScene(client.AgentId);
            if (scene == null)
                return;

            SceneObjectPart task = scene.GetSceneObjectPart(objectID);
            if (task == null)
                return;
            SceneObjectGroup group = task.ParentGroup;
            SceneObjectPart root = group.RootPart;

            client.SendPayPrice(objectID, root.PayPrice);
        }

        //
        // Send economy data to client
        //
        public void EconomyDataRequestHandler(UUID agentId)
        {
            IClientAPI user = FindClient(agentId);

            if (user != null)
            {
                Scene s = GetClientScene(user.AgentId);
                
                user.SendEconomyData(EnergyEfficiency,
                        s.RegionInfo.ObjectCapacity,
                        ObjectCount, PriceEnergyUnit, PriceGroupCreate,
                        PriceObjectClaim, PriceObjectRent,
                        PriceObjectScaleFactor, PriceParcelClaim,
                        PriceParcelClaimFactor, PriceParcelRent,
                        PricePublicObjectDecay, PricePublicObjectDelete,
                        PriceRentLight, PriceUpload,
                        TeleportMinPrice, TeleportPriceExponent);
            }
        }

        //
        // First pass: validate they have the money to buy
        //
        private void ValidateLandBuy(Object osender, EventManager.LandBuyArgs e)
        {
            if (GetAvailableAgentFunds(e.agentId) >= e.parcelPrice)
            {
                lock (e)
                {
                    e.economyValidated=true;
                }
            }
        }

        //
        // Put the land buy through
        //
        private void ProcessLandBuy(Object osender, EventManager.LandBuyArgs e)
        {
            lock (e)
            {
                Scene scene=GetClientScene(e.agentId);
                if(scene == null)
                    return;

                if (e.economyValidated == true && e.transactionID == 0)
                {
                    e.transactionID = Util.UnixTimeSinceEpoch();

                    if (DoMoneyTransfer(e.agentId, e.parcelOwnerID,
                            e.parcelPrice, 5002, e.parcelArea.ToString(), scene))
                    {
                        e.amountDebited = e.parcelPrice;
                    }
                }
            }
        }

        //
        // Generic money transfer
        // Pay other user or object
        //
        private void MoneyTransferAction(Object osender,
                EventManager.MoneyTransferArgs e)
        {
            IClientAPI sender = null;
            Scene scene;

            if (e.transactiontype == 5008) // Object gets paid
            {
                sender = FindClient(e.sender);
                if (sender != null)
                {
                    scene=GetClientScene(e.sender);

                    SceneObjectPart part = FindPrim(e.receiver);
                    if (part == null)
                        return;

                    // If the sub part doesn't take money, delegate
                    // to root part
                    if ((part.ScriptEvents & scriptEvents.money) == 0)
                        part = part.ParentGroup.RootPart;

                    bool transactionresult = DoMoneyTransfer(e.sender,
                            part.OwnerID, e.amount, e.transactiontype,
                            part.ParentGroup.RootPart.Name, scene);

                    if (transactionresult)
                    {
                        ObjectPaid handlerOnObjectPaid = OnObjectPaid;
                        if (handlerOnObjectPaid != null)
                        {
                            handlerOnObjectPaid(part.UUID, e.sender, e.amount);
                        }
                    }
                }
                return;
            }

            sender = FindClient(e.sender);
            if (sender != null)
            {
                scene=GetClientScene(e.sender);

                DoMoneyTransfer(e.sender, e.receiver,
                        e.amount, e.transactiontype, e.description, scene);
            }
            else
            {
                m_log.Warn("[MONEY]: Potential Fraud Warning, got money "+
                        "transfer request for avatar that isn't in this "+
                        "simulator - Details; Sender:" +
                        e.sender.ToString() + " Receiver: "+
                        e.receiver.ToString() + " Amount: "+
                        e.amount.ToString());
            }
        }

        public bool UploadCovered(IClientAPI client, int amount)
        {
            Scene scene=GetClientScene(client.AgentId);

            UserAccount account = scene.UserAccountService.GetUserAccount(scene.RegionInfo.ScopeID, client.AgentId);

            if (account != null && ((account.UserFlags & 0xf00) >> 8) > UserLevelPaysFees)
                return true;

            if(GetAgentFunds(client.AgentId) < amount)
                return false;
            return true;
        }

        public bool AmountCovered(IClientAPI client, int amount)
        {
            Scene scene=GetClientScene(client.AgentId);

            UserAccount account = scene.UserAccountService.GetUserAccount(scene.RegionInfo.ScopeID, client.AgentId);

            if (account != null && ((account.UserFlags & 0xf00) >> 8) > UserLevelPaysFees)
                return true;

            if(GetAvailableAgentFunds(client.AgentId) < amount)
                return false;
            return true;
        }

        public void ObjectBuy(IClientAPI remoteClient, UUID agentID,
                UUID sessionID, UUID groupID, UUID categoryID,
                uint localID, byte saleType, int salePrice)
        {
            int funds = GetAvailableAgentFunds(remoteClient.AgentId);

            if(salePrice != 0 && funds < salePrice)
            {
                remoteClient.SendAgentAlertMessage("Unable to buy now. You don't have sufficient funds.", false);
                return;
            }

            Scene s = GetClientScene(remoteClient.AgentId);

            SceneObjectPart part = s.GetSceneObjectPart(localID);
            if (part == null)
            {
                remoteClient.SendAgentAlertMessage("Unable to buy now. The object was not found.", false);
                return;
            }

            part = part.ParentGroup.RootPart;

            UUID sellerID = part.OwnerID;

            IBuySellModule module = s.RequestModuleInterface<IBuySellModule>();
            if (module != null)
            {
                if (part.ObjectSaleType != saleType)
                {
                    if (m_dialogModule != null)
                        m_dialogModule.SendAlertToUser(remoteClient, "Cannot buy now. Buy failed.");
                    return;
                }
                if (part.SalePrice != salePrice)
                {
                    if (m_dialogModule != null)
                        m_dialogModule.SendAlertToUser(remoteClient, "Cannot buy at this price. Buy failed.");
                    return;
                }

                if (module.BuyObject(remoteClient, categoryID, localID, saleType, salePrice))
                {
                    bool transactionresult = DoMoneyTransfer(remoteClient.AgentId, sellerID, salePrice, 5000, part.Name, s);
                    if (!transactionresult)
                    {
                        remoteClient.SendAgentAlertMessage("Stale money transfer", false);
                    }
                }
            }
        }
    }

    public class MySQLAccountsTableHandler : MySQLGenericTableHandler<AccountData>
    {
        public MySQLAccountsTableHandler(string conn, string realm, string store)
            : base(conn, realm, store)
        {
        }

        public virtual bool Store(AccountData row)
        {
            using (MySqlCommand cmd = new MySqlCommand())
            {

                string query = "";
                List<String> names = new List<String>();
                List<String> values = new List<String>();

                foreach (FieldInfo fi in m_Fields.Values)
                {
                    names.Add(fi.Name);
                    values.Add("?" + fi.Name);
                    cmd.Parameters.AddWithValue(fi.Name, fi.GetValue(row).ToString());
                }

                if (m_DataField != null)
                {
                    Dictionary<string, string> data =
                        (Dictionary<string, string>)m_DataField.GetValue(row);

                    foreach (KeyValuePair<string, string> kvp in data)
                    {
                        names.Add(kvp.Key);
                        values.Add("?" + kvp.Key);
                        cmd.Parameters.AddWithValue("?" + kvp.Key, kvp.Value);
                    }
                }

                query = String.Format("replace into {0} (`", m_Realm) + String.Join("`,`", names.ToArray()) + "`, `a_date`) values (" + String.Join(",", values.ToArray()) + ", now())";

                cmd.CommandText = query;

                if (ExecuteNonQuery(cmd) > 0)
                    return true;

                return false;
            }
        }

        public void Add(UUID agentID, int amount)
        {
            MySqlCommand cmd = new MySqlCommand();

            cmd.CommandText = String.Format("update {0} set a_amount=a_amount+?a_amount where a_agent=?a_agent", m_Realm);
            cmd.Parameters.AddWithValue("?a_agent", agentID.ToString());
            cmd.Parameters.AddWithValue("?a_amount", amount.ToString());

            ExecuteNonQuery(cmd);
        }

        public void AddUpload(UUID agentID, int amount)
        {
            MySqlCommand cmd = new MySqlCommand();

            cmd.CommandText = String.Format("update {0} set a_upload=a_upload+?a_upload where a_agent=?a_agent and a_upload > 0", m_Realm);
            cmd.Parameters.AddWithValue("?a_agent", agentID.ToString());
            cmd.Parameters.AddWithValue("?a_upload", amount.ToString());

            ExecuteNonQuery(cmd);
        }
    }

    public class MySQLLedgerTableHandler : MySQLGenericTableHandler<LedgerData>
    {
        public MySQLLedgerTableHandler(string conn, string realm, string store)
            : base(conn, realm, store)
        {
        }

        public virtual bool Store(LedgerData row)
        {
            using (MySqlCommand cmd = new MySqlCommand())
            {

                string query = "";
                List<String> names = new List<String>();
                List<String> values = new List<String>();

                foreach (FieldInfo fi in m_Fields.Values)
                {
                    names.Add(fi.Name);
                    values.Add("?" + fi.Name);
                    cmd.Parameters.AddWithValue(fi.Name, fi.GetValue(row).ToString());
                }

                if (m_DataField != null)
                {
                    Dictionary<string, string> data =
                        (Dictionary<string, string>)m_DataField.GetValue(row);

                    foreach (KeyValuePair<string, string> kvp in data)
                    {
                        names.Add(kvp.Key);
                        values.Add("?" + kvp.Key);
                        cmd.Parameters.AddWithValue("?" + kvp.Key, kvp.Value);
                    }
                }

                query = String.Format("insert into {0} (`", m_Realm) + String.Join("`,`", names.ToArray()) + "`, `l_date`) values (" + String.Join(",", values.ToArray()) + ", now())";

                cmd.CommandText = query;

                if (ExecuteNonQuery(cmd) > 0)
                    return true;

                return false;
            }
        }
    }
}
