// ******************************************************************
// Copyright (c) 2008, 2009 Melanie Thielker
//
// All rights reserved
//

//#define SECURE
//#define VEC
#define ENGLISH

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

[assembly: Addin("Economy.Modules", "1.8")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace Careminster.Modules.Currency
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule")]
    public class GridMoneyModule : ISharedRegionModule, IMoneyModule
    {
        //
        // Log module
        //
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);


#if (!VEC)
#if (ENGLISH)
        private const string m_UploadPaid = "You paid L$ {0} for an upload";

        private const string m_YouGotPaidFor = "{0} paid you L$ {1} for {2}";
        private const string m_YouPaidFor = "You paid {0} L$ {1} for {2}";

        private const string m_YouGotPaid = "{0} paid you L$ {1}";
        private const string m_YouPaid = "You paid {0} L$ {1}";

        private const string m_YouPaidObject = "You paid {0} L$ {1} through "+
                "object {2}";

        private const string m_YouGotPaidObject = "{0} paid you L$ {1} "+
                "through your object {2}";

        private const string m_YourObjectPaid = "Your object {2} paid {0} L$ "+
                "{1}";
        private const string m_YourObjectGotPaid = "{0} paid you L$ {1} via "+
                "object {2}";

        private const string m_YouPaidForLand = "You paid {0} L$ {1} for a "+
                "parcel of land";

        private const string m_YouGotPaidForLand = "{0} paid you L$ {1} for "+
                "a parcel of {2} sq m";
#else
        private const string m_UploadPaid = "Fuer diesen Upload wurden L$ {0} "
                + "berechnet";

        private const string m_YouGotPaidFor = "{0} hat dir {1} L$ fuer {2} "+
                "gezahlt";
        private const string m_YouPaidFor = "Du hast {0} {1} L$ fuer {2} "+
                "gezahlt";

        private const string m_YouGotPaid = "{0} hat dir {1} L$ gezahlt";
        private const string m_YouPaid = "Du hast {0} {1} L$ gezahlt";

        private const string m_YouPaidObject = "Du hast {0} {1} L$ ueber "+
                "Objekt {2} gezahlt";

        private const string m_YouGotPaidObject = "{0} hat dir {1} L$ ueber "+
                "dein Objekt {2} gezahlt";

        private const string m_YourObjectPaid = "Dein Objekt {2} hat {0} {1} "+
                "L$ gezahlt";
        private const string m_YourObjectGotPaid = "{0} hat dir {1} L$ ueber "+
                "das Objekt {2} gezahlt";

        private const string m_YouPaidForLand = "Du hast {0} {1} L$ fuer ein "+
                "Stueck land gezahlt";

        private const string m_YouGotPaidForLand = "{0} hat dir {1} L$ fuer "+
                "ein Stueck Land von {2} qm gezahlt";
#endif
#else
        private const string m_UploadPaid = "You paid VEC {0} for an upload";

        private const string m_YouGotPaidFor = "{0} paid you VEC {1} for {2}";
        private const string m_YouPaidFor = "You paid {0} VEC {1} for {2}";

        private const string m_YouGotPaid = "{0} paid you VEC {1}";
        private const string m_YouPaid = "You paid {0} VEC {1}";

        private const string m_YouPaidObject = "You paid {0} VEC {1} through "+
                "object {2}";

        private const string m_YouGotPaidObject = "{0} paid you VEC {1} "+
                "through your object {2}";

        private const string m_YourObjectPaid = "Your object {2} paid {0} VEC "+
                "{1}";
        private const string m_YourObjectGotPaid = "{0} paid you VEC {1} via "+
                "object {2}";

        private const string m_YouPaidForLand = "You paid {0} VEC {1} for a "+
                "parcel of land";

        private const string m_YouGotPaidForLand = "{0} paid you VEC {1} for "+
                "a parcel of {2} sq m";
#endif

        //
        // Module vars
        //
        private IConfigSource m_gConfig;

        // Funds
        private Dictionary<UUID, int> m_KnownClientFunds =
                new Dictionary<UUID, int>();

        // Region UUIDS indexed by AgentID
        private Dictionary<UUID, UUID> m_rootAgents =
                new Dictionary<UUID, UUID>();

        // Scenes by Region Handle
        private Dictionary<ulong, Scene> m_scenel =
                new Dictionary<ulong, Scene>();

        //
        // Economy config params
        //
        private UUID EconomyBaseAccount = UUID.Zero;
        private float EnergyEfficiency = 0f;
        private bool m_enabled = true;
        private string m_DatabaseConnect = String.Empty;
        private string m_MoneyAddress = String.Empty;
        private int ObjectCapacity = 45000;
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
        private long m_WaitTimeout = 0;
        private long m_WaitTimeoutLeeway = 60 * TimeSpan.TicksPerSecond;
        private long m_LastUsed = 0;

        //
        // Database
        //
        private MySqlConnection m_Database;

        //
        // Event handler
        //
        public event ObjectPaid OnObjectPaid;

        public void Initialise(IConfigSource config)
        {
            m_gConfig = config;

            IConfig startupConfig = m_gConfig.Configs["Startup"];
            IConfig economyConfig = m_gConfig.Configs["Economy"];

            ReadConfigAndPopulate(startupConfig, "Startup");
            ReadConfigAndPopulate(economyConfig, "Economy");

#if SECURE
            string licenseData = String.Empty;

            if (economyConfig != null)
                licenseData = economyConfig.GetString("license_data", "");

            Protect p = new Protect();

            Byte[] hash = p.GetHash();
            hash = p.Revolve(hash, "CentralGrid");
            hash = p.Revolve(hash, "CentralGrid");

            ulong left = System.BitConverter.ToUInt64(hash, 0);
            ulong right = System.BitConverter.ToUInt64(hash, 8);

            string s1 = left.ToString();
            string s2 = right.ToString();

            string code = s1 + "M" + s2;

            List<string> parts = new List<string>();

            while (code != String.Empty)
            {
                if (code.Length < 5)
                {
                    parts.Add(code);
                    break;
                }
                parts.Add(code.Substring(0, 5));
                code = code.Substring(5);
            }

            string serial = String.Join("-", parts.ToArray());

            Byte[] raw = Convert.FromBase64String(licenseData);
            if (raw.Length != 24)
            {
                m_log.ErrorFormat("[MONEY] Bad license key. License code: {0}", serial);
                m_log.Error("[MONEY] module disabled");
                throw new Exception("Bad License");
                return;
            }

            long ticks = System.BitConverter.ToInt64(raw, 16);

            Byte[] lic = new Byte[16];
            Array.Copy(raw, 0, lic, 0,16);

            licenseData = System.Convert.ToBase64String(lic);

            string b64 = System.Convert.ToBase64String(p.GetHash());

            if (b64 != licenseData)
            {
                m_log.ErrorFormat("[MONEY] Bad license key. License code: {0}", serial);
                m_log.Error("[MONEY] module disabled");
                throw new Exception("Bad License");
                return;
            }

            DateTime cutoff = new DateTime(ticks);
            if(DateTime.Now > cutoff)
            {
                m_log.ErrorFormat("[MONEY] License expired on {0}", cutoff.ToString());
                m_log.Error("[MONEY] module disabled");
                throw new Exception("Bad License");
                return;
            }

            m_log.InfoFormat("[MONEY] Licensed until {0}", cutoff.ToString());
#endif
            if (m_enabled)
            {
                try
                {
                    m_Database = new MySqlConnection(m_DatabaseConnect);
                    m_Database.Open();
                    m_log.Info("[MONEY]: Connected to database");
                }
                catch (Exception e)
                {
                    m_log.Error("[MONEY] Database connection error\n"+e.ToString());
                    m_enabled=false;
                    return;
                }
            }
        }

        private IDataReader ExecuteReader(MySqlCommand c)
        {
            IDataReader r = null;
            bool errorSeen = false;

            while (true)
            {
                try
                {
                    r = c.ExecuteReader();
                }
                catch (MySqlException)
                {
                    System.Threading.Thread.Sleep(500);

                    m_Database.Close();
                    m_Database = (MySqlConnection) ((ICloneable)m_Database).Clone();
                    m_Database.Open();
                    c.Connection = m_Database;

                    if (!errorSeen)
                    {
                        errorSeen = true;
                        continue;
                    }
                    throw;
                }

                break;
            }

            return r;
        }

        private void ExecuteNonQuery(MySqlCommand c)
        {
            bool errorSeen = false;

            while (true)
            {
                try
                {
                    c.ExecuteNonQuery();
                }
                catch (MySqlException e)
                {
                    System.Threading.Thread.Sleep(500);

                    m_Database.Close();
                    m_Database = (MySqlConnection) ((ICloneable)m_Database).Clone();
                    m_Database.Open();
                    c.Connection = m_Database;

                    if (!errorSeen)
                    {
                        errorSeen = true;
                        continue;
                    }
                    m_log.ErrorFormat("[MONEY] MySQL command: {0}", c.CommandText);
                    m_log.Error(e.ToString());

                    throw;
                }

                break;
            }
        }

        public void AddRegion(Scene scene)
        {
            if (m_enabled)
            {
                lock (m_scenel)
                {
                    // Claim the interface slot
                    scene.RegisterModuleInterface<IMoneyModule>(this);

                    // First scene registration
                    if (m_scenel.Count == 0)
                    {
                        IHttpServer httpServer = MainServer.Instance;
                        httpServer.AddXmlRPCHandler("balanceUpdateRequest",
                                GridMoneyUpdate);
                        httpServer.AddXmlRPCHandler("userAlert", UserAlert);
                    }

                    // Add to scene list
                    if (m_scenel.ContainsKey(scene.RegionInfo.RegionHandle))
                    {
                        m_scenel[scene.RegionInfo.RegionHandle] = scene;
                    }
                    else
                    {
                        m_scenel.Add(scene.RegionInfo.RegionHandle, scene);
                    }
                }

                m_log.Info("[MONEY] Activated GridMoneyModule");

                // Hook up events
                scene.EventManager.OnNewClient += OnNewClient;
                scene.EventManager.OnMoneyTransfer += MoneyTransferAction;
                scene.EventManager.OnClientClosed += ClientClosed;
                scene.EventManager.OnAvatarEnteringNewParcel +=
                        AvatarEnteringParcel;
                scene.EventManager.OnMakeChildAgent += MakeChildAgent;
                scene.EventManager.OnValidateLandBuy += ValidateLandBuy;
                scene.EventManager.OnLandBuy += ProcessLandBuy;

                scene.SetObjectCapacity(ObjectCapacity);
            }
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public void RemoveRegion(Scene scene)
        {
            m_scenel.Remove(scene.RegionInfo.RegionHandle);
        }

        public void ApplyCharge(UUID agentID, int amount, string text)
        {
            Scene scene=LocateSceneClientIn(agentID);

            UserAccount account = scene.UserAccountService.GetUserAccount(scene.RegionInfo.ScopeID, agentID);

            if (account != null && ((account.UserFlags & 0xf00) >> 8) > UserLevelPaysFees)
                return;

            MoveMoney(agentID, EconomyBaseAccount, amount);

            LocalTransaction(agentID, -amount, String.Empty);

            MakeLedgerEntry(agentID, EconomyBaseAccount, amount,
                    1101, text, scene);

        }

        public void ApplyUploadCharge(UUID agentID)
        {
            ApplyCharge(agentID, PriceUpload, "Asset upload fee");
            DeductUpload(agentID, PriceUpload);
        }

        public void ApplyGroupCreationCharge(UUID agentID)
        {
            ApplyCharge(agentID, PriceGroupCreate, "Group creation fee");
        }

        public bool ObjectGiveMoney(UUID objectID, UUID fromID, UUID toID, int amount)
        {
            Scene scene=FindPrimScene(objectID);

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

        /// Parse Configuration
        private void ReadConfigAndPopulate(IConfig startupConfig,
                string config)
        {
            if (config == "Startup" && startupConfig != null)
            {
                m_enabled = (startupConfig.GetString("economymodule",
                        "BetaGridLikeMoneyModule") == "GridMoneyModule");
            }

            if (config == "Economy" && startupConfig != null)
            {
                ObjectCapacity =
                        startupConfig.GetInt("ObjectCapacity", 45000);
                PriceEnergyUnit =
                        startupConfig.GetInt("PriceEnergyUnit", 100);
                PriceObjectClaim =
                        startupConfig.GetInt("PriceObjectClaim", 10);
                PricePublicObjectDecay =
                        startupConfig.GetInt("PricePublicObjectDecay", 4);
                PricePublicObjectDelete =
                        startupConfig.GetInt("PricePublicObjectDelete", 4);
                PriceParcelClaim =
                        startupConfig.GetInt("PriceParcelClaim", 1);
                PriceParcelClaimFactor =
                        startupConfig.GetFloat("PriceParcelClaimFactor", 1f);
                PriceUpload =
                        startupConfig.GetInt("PriceUpload", 0);
                PriceRentLight =
                        startupConfig.GetInt("PriceRentLight", 5);
                TeleportMinPrice =
                        startupConfig.GetInt("TeleportMinPrice", 2);
                TeleportPriceExponent =
                        startupConfig.GetFloat("TeleportPriceExponent", 2f);
                EnergyEfficiency =
                        startupConfig.GetFloat("EnergyEfficiency", 1);
                PriceObjectRent =
                        startupConfig.GetFloat("PriceObjectRent", 1);
                PriceObjectScaleFactor =
                        startupConfig.GetFloat("PriceObjectScaleFactor", 10);
                PriceParcelRent =
                        startupConfig.GetInt("PriceParcelRent", 1);
                PriceGroupCreate =
                        startupConfig.GetInt("PriceGroupCreate", -1);
                string EBA =
                        startupConfig.GetString("EconomyBaseAccount",
                        UUID.Zero.ToString());
                UUID.TryParse(EBA, out EconomyBaseAccount);
                UserLevelPaysFees =
                        startupConfig.GetInt("UserLevelPaysFees", -1);
                m_DatabaseConnect =
                        startupConfig.GetString("DatabaseConnect",String.Empty);
                m_MoneyAddress =
                        startupConfig.GetString("CurrencyServer", String.Empty);
            }

        }

        /// New Client Event Handler
        private void OnNewClient(IClientAPI client)
        {
            bool childYN = true;
            ScenePresence agent = null;

            // Get the scene
            Scene s;
            if (client.Scene is Scene)
                s = (Scene)client.Scene;
            else
                return; // Can't process other scene types

            if (s != null)
            {
                // Try to find out agent status
                agent = s.GetScenePresence(client.AgentId);
                if (agent != null)
                {
                    childYN = agent.IsChildAgent;
                    if (childYN == false)
                    {
                        int funds=GetAgentFunds(client.AgentId);
                        m_KnownClientFunds[client.AgentId] = funds;
                        SendMoneyBalance(client, UUID.Zero);
                    }
                }
            }

            // Subscribe to Money messages
            client.OnEconomyDataRequest += EconomyDataRequestHandler;
            client.OnMoneyBalanceRequest += SendMoneyBalance;
            client.OnRequestPayPrice += RequestPayPrice;
            client.OnObjectBuy += ObjectBuy;
            client.OnLogout += ClientClosed;
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
            int funds=0;

            lock(m_Database)
            {
                MySqlCommand fundsCommand=(MySqlCommand)m_Database.CreateCommand();
                fundsCommand.CommandText="select a_amount from accounts where a_agent = ?agent";
                fundsCommand.Parameters.AddWithValue("?agent", agentID.ToString());

                IDataReader r = ExecuteReader(fundsCommand);
                try
                {
                    if(r.Read())
                    {
                        funds=Convert.ToInt32(r["a_amount"]);
                        r.Close();
                    }
                    else
                    {
                        r.Close();
                        fundsCommand.CommandText = "insert into accounts (a_agent, a_date, a_amount) values ( ?agent, now(), 0)";
                        ExecuteNonQuery(fundsCommand);
                    }
                }
                catch (Exception e)
                {
                    r.Close();
                }
            }

            m_KnownClientFunds[agentID]=funds;

            return funds;
        }

        //
        // Get funds
        //
        private int GetAvailableAgentFunds(UUID agentID)
        {
            int funds=0;

            lock(m_Database)
            {
                MySqlCommand fundsCommand=(MySqlCommand)m_Database.CreateCommand();
                fundsCommand.CommandText="select a_amount - a_upload as a_amount from accounts where a_agent = ?agent";
                fundsCommand.Parameters.AddWithValue("?agent", agentID.ToString());

                IDataReader r = ExecuteReader(fundsCommand);
                try
                {
                    if(r.Read())
                    {
                        funds=Convert.ToInt32(r["a_amount"]);
                        r.Close();
                    }
                    else
                    {
                        r.Close();
                        fundsCommand.CommandText = "insert into accounts (a_agent, a_date, a_amount) values ( ?agent, now(), 0)";
                        ExecuteNonQuery(fundsCommand);
                    }
                }
                catch (Exception)
                {
                    r.Close();
                }
            }

            m_KnownClientFunds[agentID]=funds;

            return funds;
        }

        //
        // Transfer money
        //
        private bool DoMoneyTransfer(UUID sender, UUID receiver, int amount,
                int transactiontype, string description, Scene scene)
        {
            string text;
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
                    DeductUpload(sender, amount);
                    MoveMoney(sender, EconomyBaseAccount, amount);

                    text=String.Format(m_UploadPaid, amount);
                    LocalTransaction(sender, -amount, text);

                    MakeLedgerEntry(sender, EconomyBaseAccount, amount,
                            transactiontype, "Asset upload fee", scene);

                    break;
                case 5000: // Object bought
                    sender_name=ResolveAgentName(sender);
                    receiver_name=ResolveAgentName(receiver);

                    if(sender_name == String.Empty)
                        sender_name="(hippos)";
                    if(receiver_name == String.Empty)
                        receiver_name="(hippos)";

                    MoveMoney(sender, receiver, amount);

                    text=String.Format(m_YouGotPaidFor, sender_name, amount,
                            description);
                    LocalTransaction(receiver, amount, text);

                    text=String.Format(m_YouPaidFor, receiver_name, amount,
                            description);
                    LocalTransaction(sender, -amount, text);

                    MakeLedgerEntry(sender, receiver, amount,
                            transactiontype, "Object purchase", scene);

                    break;
                case 5001: // User pays user
                    sender_name=ResolveAgentName(sender);
                    receiver_name=ResolveAgentName(receiver);

                    if(sender_name == String.Empty)
                        sender_name="(hippos)";
                    if(receiver_name == String.Empty)
                        receiver_name="(hippos)";

                    MoveMoney(sender, receiver, amount);

                    text=String.Format(m_YouGotPaid, sender_name, amount);
                    LocalTransaction(receiver, amount, text);

                    text=String.Format(m_YouPaid, receiver_name, amount);
                    LocalTransaction(sender, -amount, text);

                    MakeLedgerEntry(sender, receiver, amount,
                            transactiontype, "Gift", scene);

                    break;
                case 5008: // User pays object
                    sender_name=ResolveAgentName(sender);
                    receiver_name=ResolveAgentName(receiver);

                    if(sender_name == String.Empty)
                        sender_name="(hippos)";
                    if(receiver_name == String.Empty)
                        receiver_name="(hippos)";

                    MoveMoney(sender, receiver, amount);

                    text=String.Format(m_YouPaidObject, receiver_name, amount,
                            description);
                    LocalTransaction(sender, -amount, text);

                    text=String.Format(m_YouGotPaidObject, sender_name, amount,
                            description);

                    LocalTransaction(receiver, amount, text);

                    MakeLedgerEntry(sender, receiver, amount,
                            transactiontype, "Paid object", scene);

                    break;
                case 5009: // Object pays user
                    sender_name=ResolveAgentName(sender);
                    receiver_name=ResolveAgentName(receiver);

                    if(sender_name == String.Empty)
                        sender_name="(hippos)";
                    if(receiver_name == String.Empty)
                        receiver_name="(hippos)";

                    MoveMoney(sender, receiver, amount);

                    text=String.Format(m_YourObjectPaid, receiver_name, amount,
                            description);
                    LocalTransaction(sender, -amount, text);

                    text=String.Format(m_YourObjectGotPaid, sender_name, amount,
                            description);

                    LocalTransaction(receiver, amount, text);

                    MakeLedgerEntry(sender, receiver, amount,
                            transactiontype, "Object pays", scene);

                    break;
                case 5002: // Land transaction
                    sender_name=ResolveAgentName(sender);
                    receiver_name=ResolveAgentName(receiver);

                    if(sender_name == String.Empty)
                        sender_name="(hippos)";
                    if(receiver_name == String.Empty)
                        receiver_name="(hippos)";

                    MoveMoney(sender, receiver, amount);

                    text=String.Format(m_YouPaidForLand, receiver_name, amount);
                    LocalTransaction(sender, -amount, text);

                    text=String.Format(m_YouGotPaidForLand, sender_name, amount,
                            description);
                    LocalTransaction(receiver, amount, text);

                    MakeLedgerEntry(sender, receiver, amount,
                            transactiontype, "Land purchase", scene);

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
            lock(m_Database)
            {
                MySqlCommand ledgerCmd=(MySqlCommand)m_Database.CreateCommand();
                ledgerCmd.CommandText="insert into ledger (l_date, l_from, l_to, l_amount, l_type, l_region, l_description) values (now(), ?from, ?to, ?amount, ?type, ?region, ?description)";
                ledgerCmd.Parameters.AddWithValue("?from", sender.ToString());
                ledgerCmd.Parameters.AddWithValue("?to", receiver.ToString());
                ledgerCmd.Parameters.AddWithValue("?amount", amount);
                ledgerCmd.Parameters.AddWithValue("?type", transactiontype);
                if(scene != null)
                    ledgerCmd.Parameters.AddWithValue("?region", scene.RegionInfo.RegionName);
                else
                    ledgerCmd.Parameters.AddWithValue("?region", String.Empty);
                ledgerCmd.Parameters.AddWithValue("?description", text);

                ExecuteNonQuery(ledgerCmd);
            }
        }

        //
        // Perform the local part of the transaction and send notifies
        //
        private void LocalTransaction(UUID agentID, int amount, string text)
        {
            lock(m_KnownClientFunds)
            {
                if (m_KnownClientFunds.ContainsKey(agentID))
                {
                    m_KnownClientFunds[agentID]+=amount;

                    IClientAPI client=LocateClientObject(agentID);
                    if(client != null)
                    {
                        SendMoneyBalance(client, UUID.Zero);
                        if(text != String.Empty)
                            client.SendBlueBoxMessage(UUID.Zero,
                                    "", text);
                    }
                    else
                    {
                        NotifyAgent(agentID, text);
                    }
                }
                else
                {
                    NotifyAgent(agentID, text);
                }
            }
        }

        //
        // Update balance tables
        //
        private void MoveMoney(UUID sender, UUID receiver, int amount)
        {
            lock (m_Database)
            {
                MySqlCommand lockCmd=(MySqlCommand)m_Database.CreateCommand();
                lockCmd.CommandText="select get_lock('movemoney', 10)";

                ExecuteNonQuery(lockCmd);

                int from_funds=GetAgentFunds(sender);
                int to_funds=GetAgentFunds(receiver);

                if(sender != receiver)
                {
                    from_funds-=amount;
                    to_funds+=amount;
                }

                MySqlCommand fromCmd=(MySqlCommand)m_Database.CreateCommand();
                fromCmd.CommandText = "update accounts set a_date=now(), "+
                        "a_amount = ?amount where a_agent = ?agent";
//                fromCmd.CommandText="replace into accounts (a_date, a_agent, "+
//                        "a_amount) values(now(), ?agent, ?amount)";

                fromCmd.Parameters.AddWithValue("?agent", sender.ToString());
                fromCmd.Parameters.AddWithValue("?amount", from_funds);
                
                ExecuteNonQuery(fromCmd);

                MySqlCommand toCmd=(MySqlCommand)m_Database.CreateCommand();
                toCmd.CommandText = "update accounts set a_date=now(), "+
                        "a_amount = ?amount where a_agent = ?agent";
//                toCmd.CommandText="replace into accounts (a_date, a_agent, "+
//                        "a_amount) values(now(), ?agent, ?amount)";

                toCmd.Parameters.AddWithValue("?agent", receiver.ToString());
                toCmd.Parameters.AddWithValue("?amount", to_funds);
                
                ExecuteNonQuery(toCmd);

                MySqlCommand unlockCmd=(MySqlCommand)m_Database.CreateCommand();
                unlockCmd.CommandText="select release_lock('movemoney')";

                ExecuteNonQuery(unlockCmd);
            }
        }

        //
        // Find the scene for an agent
        //
        private Scene LocateSceneClientIn(UUID agentId)
        {
            lock (m_scenel)
            {
                foreach (Scene scene in m_scenel.Values)
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
        // Find the client for a ID
        //
        private IClientAPI LocateClientObject(UUID agentID)
        {
            Scene scene=LocateSceneClientIn(agentID);
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
            SendMoneyBalance(client, transactionID);
        }

        private void SendMoneyBalance(IClientAPI client, UUID transactionID)
        {
            if(client == null)
                return;
            if(client.AgentId == UUID.Zero)
                return;

            if(!m_KnownClientFunds.ContainsKey(client.AgentId))
                return;

            int returnfunds=m_KnownClientFunds[client.AgentId];

            client.SendMoneyBalance(transactionID, true,
                    new byte[0], returnfunds);
        }

        private void SendMoneyBalance(UUID agentID, UUID transactionID)
        {
            IClientAPI client=LocateClientObject(agentID);
            if(client == null)
                return;

            SendMoneyBalance(client, transactionID);
        }

        //
        // Find a prim in all supported scenes
        //
        private SceneObjectPart FindPrim(UUID objectID)
        {
            lock (m_scenel)
            {
                foreach (Scene scene in m_scenel.Values)
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
        private Scene FindPrimScene(UUID objectID)
        {
            lock (m_scenel)
            {
                foreach (Scene scene in m_scenel.Values)
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
            Scene scene=LocateSceneClientIn(agentID);
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

            return String.Empty;
        }

        //
        // XmlRPC handler for balance updates from outside
        // This will not actually update the database
        //
        public XmlRpcResponse GridMoneyUpdate(XmlRpcRequest request)
        {
            return GridMoneyUpdate(request, null);
        }

        public XmlRpcResponse GridMoneyUpdate(XmlRpcRequest request,
                IPEndPoint endpoint)
        {
            bool success=false;

            Hashtable requestData = (Hashtable) request.Params[0];

            if (requestData.ContainsKey("agentId"))
            {
                UUID agentID;
                UUID.TryParse((string) requestData["agentId"], out agentID);

                if(requestData.ContainsKey("amount"))
                {
                    int amount=0;
                    try
                    {
                        amount=int.Parse((string)requestData["amount"]);
                    }
                    catch(System.Exception)
                    {
                    }

                    lock (m_KnownClientFunds)
                    {
                        m_KnownClientFunds[agentID]=amount;
                        SendMoneyBalance(agentID, UUID.Zero);
                    }
                    success=true;
                }
                if(requestData.ContainsKey("message"))
                {
                    IClientAPI client=LocateClientObject(agentID);
                    if(client != null)
                        client.SendBlueBoxMessage(UUID.Zero,
                                "", (string)requestData["message"]);
                }
            }

            XmlRpcResponse r = new XmlRpcResponse();
            Hashtable rparms = new Hashtable();
            rparms["success"] = success;

            r.Value = rparms;
            return r;
        }

        // XMLRPC handler to send alert message and sound to client
        public XmlRpcResponse UserAlert(XmlRpcRequest request)
        {
            return UserAlert(request, null);
        }

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

            IClientAPI client = LocateClientObject(agentId);

            if (client != null)
            {
                if (soundId != UUID.Zero)
                    client.SendPlayAttachedSound(soundId, UUID.Zero, UUID.Zero, 1.0f, 0);
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
            lock (m_scenel)
            {
                foreach (Scene rs in m_scenel.Values)
                    return rs;
            }
            return null;
        }

        //
        // Get a acene by region ID
        //
        public Scene GetSceneByUUID(UUID RegionID)
        {
            lock (m_scenel)
            {
                foreach (Scene rs in m_scenel.Values)
                {
                    if (rs.RegionInfo.originRegionID == RegionID)
                        return rs;
                }
            }
            return null;
        }

        //
        // Send the object's pay price
        //
        public void RequestPayPrice(IClientAPI client, UUID objectID)
        {
            Scene scene = LocateSceneClientIn(client.AgentId);
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
        // Handle logout
        //
        public void ClientClosed(UUID AgentID, Scene scene)
        {
            lock (m_rootAgents)
            {
                if (m_rootAgents.ContainsKey(AgentID))
                {
                    m_rootAgents.Remove(AgentID);
                }
            }

            lock (m_KnownClientFunds)
            {
                m_KnownClientFunds.Remove(AgentID);
            }
        }

        //
        // Send economy data to client
        //
        public void EconomyDataRequestHandler(UUID agentId)
        {
            IClientAPI user = LocateClientObject(agentId);

            if (user != null)
            {
                user.SendEconomyData(EnergyEfficiency, ObjectCapacity,
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
                Scene scene=LocateSceneClientIn(e.agentId);
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
                sender = LocateClientObject(e.sender);
                if (sender != null)
                {
                    scene=LocateSceneClientIn(e.sender);

                    SceneObjectPart part = FindPrim(e.receiver);
                    if (part == null)
                        return;

                    bool transactionresult = DoMoneyTransfer(e.sender,
                            part.OwnerID, e.amount, e.transactiontype,
                            part.Name, scene);

                    if (transactionresult)
                    {
                        ObjectPaid handlerOnObjectPaid = OnObjectPaid;
                        if (handlerOnObjectPaid != null)
                        {
                            handlerOnObjectPaid(e.receiver, e.sender, e.amount);
                        }
                    }
                }
                return;
            }

            sender = LocateClientObject(e.sender);
            if (sender != null)
            {
                scene=LocateSceneClientIn(e.sender);

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

        //
        // Agent becomes child
        //
        private void MakeChildAgent(ScenePresence avatar)
        {
            lock (m_rootAgents)
            {
                if (m_rootAgents.ContainsKey(avatar.UUID))
                {
                    if (m_rootAgents[avatar.UUID] ==
                            avatar.Scene.RegionInfo.originRegionID)
                        m_rootAgents.Remove(avatar.UUID);
                }
            }
        }

        //
        // Call this when the client disconnects.
        //
        public void ClientClosed(IClientAPI client)
        {
            Scene s = null;
            if (client.Scene is Scene)
                s = (Scene)client.Scene;

            ClientClosed(client.AgentId, s);
        }

        //
        // Event Handler for when an Avatar enters one of the parcels
        // in the simulator.
        //
        private void AvatarEnteringParcel(ScenePresence avatar,
                int localLandID, UUID regionID)
        {
            GetAgentFunds(avatar.ControllingClient.AgentId); // For side effects
            SendMoneyBalance(avatar.ControllingClient.AgentId, UUID.Zero);

            lock (m_rootAgents)
            {
                if (m_rootAgents.ContainsKey(avatar.UUID))
                {
                    if (avatar.Scene.RegionInfo.originRegionID !=
                            m_rootAgents[avatar.UUID])
                    {
                        m_rootAgents[avatar.UUID]=
                                avatar.Scene.RegionInfo.originRegionID;
                    }
                }
                else
                {
                    lock (m_rootAgents)
                    {
                        m_rootAgents.Add(avatar.UUID,
                                avatar.Scene.RegionInfo.originRegionID);
                    }
                }
            }
        }

        public int GetBalance(IClientAPI client)
        {
            return GetAgentFunds(client.AgentId);
        }

        public bool AmountCovered(IClientAPI client, int amount)
        {
            Scene scene=LocateSceneClientIn(client.AgentId);

            UserAccount account = scene.UserAccountService.GetUserAccount(scene.RegionInfo.ScopeID, client.AgentId);

            if (account != null && ((account.UserFlags & 0xf00) >> 8) > UserLevelPaysFees)
                return true;

            if(GetAvailableAgentFunds(client.AgentId) < amount)
                return false;
            return true;
        }

        public bool UploadCovered(IClientAPI client)
        {
            Scene scene=LocateSceneClientIn(client.AgentId);

            UserAccount account = scene.UserAccountService.GetUserAccount(scene.RegionInfo.ScopeID, client.AgentId);

            if (account != null && ((account.UserFlags & 0xf00) >> 8) > UserLevelPaysFees)
                return true;

            if(GetAgentFunds(client.AgentId) < PriceUpload)
                return false;

            return true;
        }

        public bool GroupCreationCovered(IClientAPI client)
        {
            return AmountCovered(client, PriceGroupCreate);
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

            Scene s = LocateSceneClientIn(remoteClient.AgentId);

            SceneObjectPart part = s.GetSceneObjectPart(localID);
            if (part == null)
            {
                remoteClient.SendAgentAlertMessage("Unable to buy now. The object was not found.", false);
                return;
            }

            UUID sellerID = part.OwnerID;

            if (s.PerformObjectBuy(remoteClient, categoryID, localID, saleType))
            {
                bool transactionresult = DoMoneyTransfer(remoteClient.AgentId, sellerID, salePrice, 5000, part.Name, s);
                if (!transactionresult)
                {
                    remoteClient.SendAgentAlertMessage("Stale money transfer", false);
                }
            }
        }

        private void DeductUpload(UUID sender, int amount)
        {
            lock (m_Database)
            {
                MySqlCommand upd = (MySqlCommand)m_Database.CreateCommand();
                upd.CommandText = "update accounts set " +
                        "a_upload = case when a_upload > 0 then " +
                        "a_upload - ?amount else 0 end where a_agent = ?agent";

                upd.Parameters.AddWithValue("agent", sender.ToString());
                upd.Parameters.AddWithValue("amount", amount);

                ExecuteNonQuery(upd);
            }
        }

        public EconomyData GetEconomyData()
        {
            EconomyData edata = new EconomyData();
            edata.ObjectCapacity = ObjectCapacity;
            edata.ObjectCount = ObjectCount;
            edata.PriceEnergyUnit = PriceEnergyUnit;
            edata.PriceGroupCreate = PriceGroupCreate;
            edata.PriceObjectClaim = PriceObjectClaim;
            edata.PriceObjectRent = PriceObjectRent;
            edata.PriceObjectScaleFactor = PriceObjectScaleFactor;
            edata.PriceParcelClaim = PriceParcelClaim;
            edata.PriceParcelClaimFactor = PriceParcelClaimFactor;
            edata.PriceParcelRent = PriceParcelRent;
            edata.PricePublicObjectDecay = PricePublicObjectDecay;
            edata.PricePublicObjectDelete = PricePublicObjectDelete;
            edata.PriceRentLight = PriceRentLight;
            edata.PriceUpload = PriceUpload;
            edata.TeleportMinPrice = TeleportMinPrice;
            return edata;
        }
    }
}
