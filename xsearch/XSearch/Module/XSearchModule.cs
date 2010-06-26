///////////////////////////////////////////////////////////////////
//
// (c) 2010 Melanie Thielker and Careminster, Limited
//
// All rights reserved
//
using System;
using System.Timers;
using System.Net;
using System.Net.Sockets;
using System.Xml;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Data;
using OpenSim.Data.MySQL;
using OpenSim.Framework.Communications;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using Mono.Addins;

[assembly: Addin("XSearch.Module", "1.0")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace Careminster.Modules.XSearch
{
    // So we can search classifieds directly
    public class XProfileClassified
    {
        public UUID UserID;
        public UUID ClassifiedID;
        public Dictionary<string, string> Data;
    }

    // Parcels reference table (mostly for the web site)
    public class XSearchParcel
    {
        public UUID RegionID;
        public UUID ParcelID;
        public UUID FakeID;
        public UUID OwnerID;
        public Dictionary<string, string> Data;
    }

    public class XSearchEvent
    {
        public int EventID;
        public Dictionary<string,string> Data;
    }

    [Flags]
    public enum ClassifiedFlags
    {
        None = 1,
        Mature = 2,
        UpdateTime = 16,
        AutoRenew = 32
    }
        
    [Flags]
    public enum ClassifiedQueryFlags
    {
        None = 0,
        FilterMature = 1,
        IncludePG = 4,
        IncludeMature = 8,
        IncludeAdult = 64
    }

    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "XSearch")]
    public class XSearchModule : INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected Scene m_Scene;
        protected bool m_Enabled = false;
        private string m_SearchServer = "";
        private Timer m_LandTimer = new Timer();

        private string m_ConnectionString;
        private MySQLGenericTableHandler<XProfileClassified> m_ClassifiedsTable;
        private MySQLGenericTableHandler<XSearchParcel> m_ParcelsTable;
        private MySQLGenericTableHandler<XSearchEvent> m_EventsTable;
        private bool m_FreshStart = true;
        private IDwellModule m_DwellModule = null;

        public void Initialise(IConfigSource config)
        {
            m_LandTimer.AutoReset = false;
            m_LandTimer.Interval = 20000;
            m_LandTimer.Elapsed +=
                    delegate(object sender, ElapsedEventArgs e)
                    {
                        SendParcelData();
                    };

            IConfig searchConfig = config.Configs["Search"];
            if (searchConfig == null)
                return;

            if (searchConfig.GetString("Module", String.Empty) != "XSearch")
                return;

            m_SearchServer = searchConfig.GetString("SearchServer", "");
            if (m_SearchServer == "")
            {
                m_log.Error("[XSEARCH] No search server, disabling search");
                return;
            }

            m_ConnectionString = searchConfig.GetString("DatabaseConnect",
                    String.Empty);
            if (m_ConnectionString == String.Empty)
            {
                m_log.Error("[XSEARCH]: Module enabled but no DatabaseConnect in [Search]");
                return;
            }

            m_ClassifiedsTable = new MySQLGenericTableHandler<XProfileClassified>(
                    m_ConnectionString, "XProfileClassifieds", String.Empty);
            m_ParcelsTable = new MySQLGenericTableHandler<XSearchParcel>(
                    m_ConnectionString, "XSearchParcels", String.Empty);
            m_EventsTable = new MySQLGenericTableHandler<XSearchEvent>(
                    m_ConnectionString, "XSearchEvents", String.Empty);

            m_Enabled = true;
            m_log.Info("[XSEARCH]: XSearch enabled");
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
            m_Scene = scene;

            scene.EventManager.OnNewClient += OnNewClient;
            scene.EventManager.OnParcelPrimCountUpdate += OnParcelPrimCountUpdate;

            m_log.Debug("[XSEARCH]: Clearing stale parcel data and sending new parcels");
        }

        public void RegionLoaded(Scene scene)
        {
            m_DwellModule = scene.RequestModuleInterface<IDwellModule>();
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
            // Parcel changes will not only reset show in search,
            // but also change parcel IDs. So we need to resend all
            // parcel data.
            //
            client.OnParcelDivideRequest +=
                    delegate(int west, int south, int east, int north,
                    IClientAPI remote_client)
                    {
                        m_LandTimer.Stop();
                        m_LandTimer.Start();
                    };

            client.OnParcelJoinRequest +=
                    delegate(int west, int south, int east, int north,
                    IClientAPI remote_client)
                    {
                        m_LandTimer.Stop();
                        m_LandTimer.Start();
                    };

            // These don't change parcel data, they only change flags.
            // Sale and reclaim simply reset it, while properties update
            // Can do either.
            //
            client.OnParcelBuy +=
                    OnParcelBuy;
            client.OnParcelReclaim +=
                    OnParcelReclaim;
            client.OnParcelPropertiesUpdateRequest +=
                    OnParcelPropertiesUpdateRequest;
            client.OnParcelAbandonRequest +=
                    OnParcelAbandonRequest;
            client.OnParcelGodForceOwner +=
                    OnParcelGodForceOwner;

            // Grab search requests
            client.OnDirPlacesQuery += DirPlacesQuery;
            client.OnDirFindQuery += DirFindQuery;
            client.OnDirLandQuery += DirLandQuery;
            client.OnDirClassifiedQuery += DirClassifiedQuery;
            // Response after Directory Queries
            client.OnEventInfoRequest += EventInfoRequest;
            client.OnClassifiedInfoRequest += ClassifiedInfoRequest;
            client.OnMapItemRequest += HandleMapItemRequest;
        }

        private Hashtable GenericXMLRPCRequest(Hashtable ReqParams, string method)
        {
            ArrayList SendParams = new ArrayList();
            SendParams.Add(ReqParams);

            // Send Request
            XmlRpcResponse Resp;
            try
            {
                XmlRpcRequest Req = new XmlRpcRequest(method, SendParams);
                Resp = Req.Send(m_SearchServer, 30000);
            }
            catch (WebException ex)
            {
                m_log.ErrorFormat("[SEARCH]: Unable to connect to Search " +
                        "Server {0}.  Exception {1}", m_SearchServer, ex);

                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to search at this time. ";
                ErrorHash["errorURI"] = "";

                return ErrorHash;
            }
            catch (SocketException ex)
            {
                m_log.ErrorFormat(
                        "[SEARCH]: Unable to connect to Search Server {0}. " +
                        "Exception {1}", m_SearchServer, ex);

                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to search at this time. ";
                ErrorHash["errorURI"] = "";

                return ErrorHash;
            }
            catch (XmlException ex)
            {
                m_log.ErrorFormat(
                        "[SEARCH]: Unable to connect to Search Server {0}. " +
                        "Exception {1}", m_SearchServer, ex);

                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to search at this time. ";
                ErrorHash["errorURI"] = "";

                return ErrorHash;
            }
            if (Resp.IsFault)
            {
                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to search at this time. ";
                ErrorHash["errorURI"] = "";
                return ErrorHash;
            }
            Hashtable RespData = (Hashtable)Resp.Value;

            return RespData;
        }

        protected void DirPlacesQuery(IClientAPI remoteClient, UUID queryID,
                string queryText, int queryFlags, int category, string simName,
                int queryStart)
        {
            Hashtable ReqHash = new Hashtable();
            ReqHash["Term"] = queryText;
            ReqHash["Flags"] = queryFlags.ToString();
            ReqHash["Category"] = category.ToString();
            ReqHash["ScopeID"] = m_Scene.RegionInfo.ScopeID.ToString();
            ReqHash["Skip"] = queryStart.ToString();

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    "query");

            if (!Convert.ToBoolean(result["success"]))
            {
                remoteClient.SendAgentAlertMessage(
                        result["errorMessage"].ToString(), false);
                return;
            }

            ArrayList dataArray = (ArrayList)result["result"];

            int count = dataArray.Count;

            DirPlacesReplyData[] data = new DirPlacesReplyData[count];

            int i = 0;

            foreach (Object o in dataArray)
            {
                XSearchParcel[] parcels = m_ParcelsTable.Get("ParcelID", o.ToString());
                if (parcels.Length == 0)
                    return;

                XSearchParcel p = parcels[0];

                bool forSale = false;
                if (Convert.ToInt32(p.Data["ForSale"]) > 0)
                    forSale = true;

                data[i] = new DirPlacesReplyData();
                data[i].parcelID = p.FakeID;
                data[i].name = p.Data["Name"];
                data[i].forSale = forSale;
                data[i].auction = false;
                if (m_DwellModule != null)
                    data[i].dwell = m_DwellModule.GetDwell(p.ParcelID);
                else
                    data[i].dwell = 0;

                i++;
            }

            remoteClient.SendDirPlacesReply(queryID, data);
        }

        public void DirLandQuery(IClientAPI remoteClient, UUID queryID,
                uint queryFlags, uint searchType, int price, int area,
                int queryStart)
        {
            List<string> terms = new List<string>();
            string order = String.Empty;

            if ((queryFlags & 0x80000) != 0)
                order = " order by Name";
            if ((queryFlags & 0x10000) != 0)
                order = " order by SalePrice";
            if ((queryFlags & 0x40000) != 0)
                order = " order by Area";
            if ((queryFlags & 0x8000) != 0 && order != String.Empty)
                order += " desc";

            terms.Add("ForSale <> 0");

            if ((queryFlags & 0x100000) != 0)
                terms.Add("SalePrice <= " + price.ToString());
            if ((queryFlags & 0x200000) != 0)
                terms.Add("Area >= " + area.ToString());

            if ((searchType & 26) == 2)
            {
                remoteClient.SendAgentAlertMessage("No auctions listed", false);
                DirLandReplyData[] nodata = new DirLandReplyData[0];
                remoteClient.SendDirLandReply(queryID, nodata);
                return;
            }

            if ((searchType & 24) == 8)
                terms.Add("ParentEstate = 1");
            if ((searchType & 24) == 16)
                terms.Add("ParentEstate <> 1");

            // <= 1.22
            if ((queryFlags & 0x4800) == 0x800)
                terms.Add("AccessLevel < 21");
            if ((queryFlags & 0x4800) == 0x4000)
                terms.Add("AccessLevel > 20 and AccessLevel < 42");

            // >= 1.23
            if ((queryFlags & 0x1000000) == 0)
                terms.Add("AccessLevel <> 13");
            if ((queryFlags & 0x2000000) == 0)
                terms.Add("AccessLevel <> 21");
            if ((queryFlags & 0x4000000) == 0)
                terms.Add("AccessLevel <> 42");

            terms.Add("ScopeID='" + m_Scene.RegionInfo.ScopeID.ToString() + "'");
            string where = String.Join(" and ", terms.ToArray()) + order;
            m_log.Debug(where);
            XSearchParcel[] parcels = m_ParcelsTable.Get(where);
 
            int count = parcels.Length;
            if (count < queryStart)
            {
                DirLandReplyData[] nodata = new DirLandReplyData[0];
                remoteClient.SendDirLandReply(queryID, nodata);
                return;
            }

            count -= queryStart;

            if (count > 100)
                count = 101;

            DirLandReplyData[] data = new DirLandReplyData[count];

            int i = 0;

            for (int idx = queryStart ; idx < queryStart + count ; idx++)
            {
                XSearchParcel p = parcels[idx];

                data[i] = new DirLandReplyData();
                data[i].parcelID = p.FakeID;
                data[i].name = p.Data["Name"];
                data[i].auction = false;
                data[i].forSale = true;
                data[i].salePrice = Convert.ToInt32(p.Data["SalePrice"]);
                data[i].actualArea = Convert.ToInt32(p.Data["Area"]);
                i++;
            }

            remoteClient.SendDirLandReply(queryID, data);
        }

        public void DirFindQuery(IClientAPI remoteClient, UUID queryID,
                string queryText, uint queryFlags, int queryStart)
        {
            if ((queryFlags & 1) != 0)
            {
                DirPeopleQuery(remoteClient, queryID, queryText, queryFlags,
                        queryStart);
                return;
            }
            else if ((queryFlags & 32) != 0)
            {
                DirEventsQuery(remoteClient, queryID, queryText, queryFlags,
                        queryStart);
                return;
            }
        }

        public void DirPeopleQuery(IClientAPI remoteClient, UUID queryID,
                string queryText, uint queryFlags, int queryStart)
        {
            List<UserAccount> accounts = m_Scene.UserAccountService.GetUserAccounts(m_Scene.RegionInfo.ScopeID, queryText);

            DirPeopleReplyData[] data =
                    new DirPeopleReplyData[accounts.Count];

            int i = 0;
            foreach (UserAccount item in accounts)
            {
                data[i] = new DirPeopleReplyData();

                data[i].agentID = item.PrincipalID;
                data[i].firstName = item.FirstName;
                data[i].lastName = item.LastName;
                data[i].group = "";
                data[i].online = false;
                data[i].reputation = 0;
                i++;
            }

            remoteClient.SendDirPeopleReply(queryID, data);
        }

        public void DirEventsQuery(IClientAPI remoteClient, UUID queryID,
                string queryText, uint queryFlags, int queryStart)
        {
            List<string> terms = new List<string>();
            
            if ((queryFlags & 0x1000000) == 0)
                terms.Add("Maturity <> 13");
            if ((queryFlags & 0x2000000) == 0)
                terms.Add("Maturity <> 21");
            if ((queryFlags & 0x4000000) == 0)
                terms.Add("Maturity <> 42");

            string[] args = queryText.Split(new char[] {'|'});
            // TODO: Factor in duration
            if (args[0] == "u")
                terms.Add("date_add(from_unixtime(StartTime), interval Duration minute) >= now()");
            else
                terms.Add(String.Format("date(from_unixtime(StartTime)) = date(date_add(now(), interval {0} day))", args[0]));

            int category = Convert.ToInt32(args[1]);
            if (category > 0)
                terms.Add("Category="+category.ToString());

            if (args[2] != String.Empty)
                terms.Add("match(Name, Description) against ('" + args[2] + "')");
            terms.Add("ScopeID='" + m_Scene.RegionInfo.ScopeID.ToString() + "'");
            string where = String.Join(" and ", terms.ToArray());

            XSearchEvent[] events = m_EventsTable.Get(where);

            if (events.Length < queryStart)
            {
                DirEventsReplyData[] nodata = new DirEventsReplyData[0];
                remoteClient.SendDirEventsReply(queryID, nodata);
                return;
            }

            int count = events.Length;
            count -= queryStart;
            if (count > 100)
                count = 101;

            int i = 0;

            DirEventsReplyData[] data = new DirEventsReplyData[count];
            for (int idx = queryStart ; idx < queryStart + count ; idx++)
            {
                data[i] = new DirEventsReplyData();
                data[i].ownerID = new UUID(events[idx].Data["CreatorID"].ToString());
                data[i].name = events[idx].Data["Name"];
                data[i].eventID = (uint)events[idx].EventID;
                data[i].date = events[idx].Data["EventDate"];
                data[i].unixTime = Convert.ToUInt32(events[idx].Data["StartTime"]);
                data[i].eventFlags = Convert.ToUInt32(events[idx].Data["Flags"]);
                i++;
            }

            remoteClient.SendDirEventsReply(queryID, data);
        }

        public void DirClassifiedQuery(IClientAPI remoteClient, UUID queryID,
                string queryText, uint queryFlags, uint category,
                int queryStart)
        {
            queryText = queryText.Replace("'", "\\'");

            List<string> terms = new List<string>();

            if (queryText != String.Empty)
                terms.Add("match(Name,Description) against ('" + queryText + "')");

            if (category != 0)
                terms.Add("category = '" + category.ToString() + "'");
            queryFlags &= (uint)(ClassifiedQueryFlags.IncludePG |
                                 ClassifiedQueryFlags.IncludeMature |
                                 ClassifiedQueryFlags.IncludeAdult);
            if (queryFlags == 0)
                queryFlags |= (uint)ClassifiedQueryFlags.IncludePG;
            terms.Add("ClassifiedFlags & " + queryFlags.ToString());

            terms.Add("(ScopeID='" + m_Scene.RegionInfo.ScopeID.ToString() +
                    "' or ScopeID='00000000-0000-0000-0000-000000000000')");

            string where = String.Join(" and ", terms.ToArray());
            m_log.Debug(where);

            XProfileClassified[] classifieds = 
                    m_ClassifiedsTable.Get(where); 

            int count = classifieds.Length;
            if (queryStart >= count)
            {
                DirClassifiedReplyData[] blankReply =
                        new DirClassifiedReplyData[0];
                remoteClient.SendDirClassifiedReply(queryID, blankReply);
                return;
            }

            count -= queryStart;

            // Use 101 to make the "> 100 results" and the "more >>" button
            // appear.
            if (count > 101)
                count = 101;

            DirClassifiedReplyData[] data = new DirClassifiedReplyData[count];

            int i = 0;

            for (int idx = queryStart ; idx < queryStart + count ; idx++)
            {
                data[i] = new DirClassifiedReplyData();
                data[i].classifiedID = classifieds[idx].ClassifiedID;
                data[i].name = classifieds[idx].Data["Name"].ToString();
                data[i].classifiedFlags = Convert.ToByte(classifieds[idx].Data["ClassifiedFlags"]);
                data[i].creationDate = Convert.ToUInt32(classifieds[idx].Data["CreationDate"]);
                data[i].expirationDate = Convert.ToUInt32(classifieds[idx].Data["ExpirationDate"]);
                data[i].price = Convert.ToInt32(classifieds[idx].Data["Price"]);
                i++;
            }

            remoteClient.SendDirClassifiedReply(queryID, data);
        }

        public void EventInfoRequest(IClientAPI remoteClient, uint queryEventID)
        {
            XSearchEvent[] events = m_EventsTable.Get("EventID", queryEventID.ToString());
            if (events.Length != 1)
            {
                return;
            }

            XSearchEvent e = events[0];

            EventData data = new EventData();
            data.eventID = (uint)e.EventID;
            data.creator = e.Data["CreatorID"];
            data.name = e.Data["Name"];
            data.category = e.Data["CategoryText"];
            data.description = e.Data["Description"];
            data.date = e.Data["EventDate"];
            data.dateUTC = Convert.ToUInt32(e.Data["StartTime"]);
            data.duration = Convert.ToUInt32(e.Data["Duration"]);
            data.cover = Convert.ToUInt32(e.Data["Cover"]);
            data.amount = Convert.ToUInt32(e.Data["CoverAmount"]);
            data.simName = e.Data["RegionName"];
            Vector3.TryParse(e.Data["GlobalPosition"].ToString(), out data.globalPos);
            data.eventFlags = Convert.ToUInt32(e.Data["Flags"]);

            remoteClient.SendEventInfoReply(data);
        }

        public void ClassifiedInfoRequest(UUID queryClassifiedID, IClientAPI remoteClient)
        {
            XProfileClassified[] classifieds = 
                    m_ClassifiedsTable.Get("ClassifiedID",
                    queryClassifiedID.ToString()); 

            if (classifieds.Length == 0)
            {
                remoteClient.SendAgentAlertMessage("Couldn't find classified.",
                        false);
                return;
            }

            Vector3 globalPos;
            Vector3.TryParse(classifieds[0].Data["GlobalPosition"].ToString(), out globalPos);

            remoteClient.SendClassifiedInfoReply(
                    classifieds[0].ClassifiedID,
                    classifieds[0].UserID,
                    Convert.ToUInt32(classifieds[0].Data["CreationDate"]),
                    Convert.ToUInt32(classifieds[0].Data["ExpirationDate"]),
                    Convert.ToUInt32(classifieds[0].Data["Category"]),
                    classifieds[0].Data["Name"].ToString(),
                    classifieds[0].Data["Description"].ToString(),
                    new UUID(classifieds[0].Data["ParcelID"].ToString()),
                    Convert.ToUInt32(classifieds[0].Data["ParentEstate"]),
                    new UUID(classifieds[0].Data["SnapshotID"].ToString()),
                    classifieds[0].Data["RegionName"].ToString(),
                    globalPos,
                    classifieds[0].Data["ParcelName"].ToString(),
                    Convert.ToByte(classifieds[0].Data["ClassifiedFlags"]),
                    Convert.ToInt32(classifieds[0].Data["Price"]));
        }

        public void HandleMapItemRequest(IClientAPI remoteClient, uint flags,
                 uint EstateID, bool godlike, uint itemtype, ulong regionhandle)
        {
            // This lets the map show the yellow "sale" overlays
            if (itemtype == 7) //(land sales)
            {
                List<string> terms = new List<string>();

                // This is 0 from the viewer. Override it
                regionhandle = m_Scene.RegionInfo.RegionHandle;

                terms.Add("RegionHandle=" + regionhandle.ToString());
                terms.Add("ForSale <> 0");

                string where = String.Join(" and ", terms.ToArray());

                m_log.DebugFormat("[XSEARCH]: Where is {0}", where);
                XSearchParcel[] parcels = m_ParcelsTable.Get(where);

                int i = 0;
                List<mapItemReply> mapitems = new List<mapItemReply>();
                int regionX = (int)(regionhandle >> 32);
                int regionY = (int)(regionhandle & 0xffffffff);

                foreach (XSearchParcel p in parcels)
                {
                    Vector3 landingPoint = Vector3.Parse(p.Data["LandingPoint"]);
                    mapItemReply mapitem = new mapItemReply();
                    mapitem.x = (uint)(regionX + landingPoint.X);
                    mapitem.y = (uint)(regionY + landingPoint.Y);
                    mapitem.id = new UUID(p.FakeID);
                    mapitem.name = p.Data["Name"];
                    mapitem.Extra = Convert.ToInt32(p.Data["Area"]);
                    mapitem.Extra2 = Convert.ToInt32(p.Data["SalePrice"]);
                    mapitems.Add(mapitem);
                    i++;
                }
                m_log.DebugFormat("[XSEARCH]: Sending {0} map items", mapitems.Count);
                remoteClient.SendMapItemReply(mapitems.ToArray(), itemtype, flags);
            }
        }

        private void OnParcelBuy(UUID agentId, UUID groupId, bool final,
                    bool groupOwned, bool removeContribution,
                    int parcelLocalID, int parcelArea, int parcelPrice,
                    bool authenticated)
        {
            m_LandTimer.Stop();
            m_LandTimer.Start();
        }

        private void OnParcelReclaim(int local_id, IClientAPI remote_client)
        {
            m_LandTimer.Stop();
            m_LandTimer.Start();
        }

        private void OnParcelPropertiesUpdateRequest(LandUpdateArgs args,
                int local_id, IClientAPI remote_client)
        {
            m_LandTimer.Stop();
            m_LandTimer.Start();
        }

        private void OnParcelAbandonRequest(int local_id, IClientAPI
                remote_client)
        {
            m_LandTimer.Stop();
            m_LandTimer.Start();
        }

        private void OnParcelGodForceOwner(int local_id, UUID ownerID,
                IClientAPI remote_client)
        {
            m_LandTimer.Stop();
            m_LandTimer.Start();
        }

        private void CreateTextElem(XmlDocument doc, XmlNode parent, string name, string content)
        {
            XmlElement elem = doc.CreateElement("", name, "");
            parent.AppendChild(elem);

            XmlNode tn = doc.CreateTextNode(content);
            elem.AppendChild(tn);
        }

        private void SendParcelData()
        {
            XmlDocument doc = new XmlDocument();
            XmlNode xmlnode = doc.CreateNode(XmlNodeType.XmlDeclaration,
                    "", "");
            doc.AppendChild(xmlnode);

            XmlElement root = doc.CreateElement("", "Parcels", "");
            doc.AppendChild(root);

            m_log.Debug("[XSEARCH]: Refreshing server parcels list");

            m_ParcelsTable.Delete("RegionID", m_Scene.RegionInfo.RegionID.ToString());
            ILandChannel landChannel = m_Scene.LandChannel;
            List<ILandObject> parcels = landChannel.AllParcels();
            if (parcels == null)
                return;
            m_log.DebugFormat("[XSEARCH]: Updating {0} parcels", parcels.Count);

            foreach (ILandObject land in parcels)
            {
                XSearchParcel parcel = new XSearchParcel();

                Vector3 userLocation = land.LandData.UserLocation;

                uint x = 0, y = 0;
                // If no tp point is set, find the parcel's geometric center.
                // This point MAY not be on the actual parcel!
                if (userLocation != Vector3.Zero)
                {
                    x = (uint)userLocation.X;
                    y = (uint)userLocation.Y;
                }
                else
                {
                    uint sx = (uint)(land.LandData.AABBMax.X - land.LandData.AABBMin.X);
                    uint sy = (uint)(land.LandData.AABBMax.Y - land.LandData.AABBMin.Y);
                    userLocation = new Vector3(land.LandData.AABBMin.X + sx / 2,
                                               land.LandData.AABBMin.Y + sy / 2,
                                               0);
                    findPointInParcel(land, ref x, ref y); // find a suitable spot
                }

                parcel.FakeID = Util.BuildFakeParcelID(
                        m_Scene.RegionInfo.RegionHandle, x, y);

                parcel.RegionID = m_Scene.RegionInfo.RegionID;
                parcel.ParcelID = land.LandData.GlobalID;
                parcel.OwnerID = land.LandData.OwnerID;

                parcel.Data = new Dictionary<string, string>();
                parcel.Data["LandingPoint"] = userLocation.ToString();
                parcel.Data["ImageID"] = land.LandData.SnapshotID.ToString();
                parcel.Data["GroupID"] = land.LandData.GroupID.ToString();
                parcel.Data["Name"] = land.LandData.Name;
                parcel.Data["Description"] = land.LandData.Description;

                parcel.Data["ForSale"] = "0";
                if ((land.LandData.Flags & (uint)ParcelFlags.ForSale) != 0)
                    parcel.Data["ForSale"] = "1";

                parcel.Data["Flags"] = land.LandData.Flags.ToString();
                parcel.Data["SalePrice"] = land.LandData.SalePrice.ToString();
                parcel.Data["AuthBuyerID"] = land.LandData.AuthBuyerID.ToString();
                parcel.Data["ScopeID"] = m_Scene.RegionInfo.ScopeID.ToString();
                parcel.Data["RegionHandle"] = m_Scene.RegionInfo.RegionHandle.ToString();
                parcel.Data["Area"] = land.LandData.Area.ToString();
                parcel.Data["ParentEstate"] = m_Scene.RegionInfo.EstateSettings.EstateID.ToString();
                parcel.Data["AccessLevel"] = m_Scene.RegionInfo.AccessLevel.ToString();

                m_ParcelsTable.Store(parcel);

                CreateTextElem(doc, root, "RegionID", m_Scene.RegionInfo.RegionID.ToString());

                if ((land.LandData.Flags & (uint)ParcelFlags.ShowDirectory) != 0)
                {
                    XmlElement parcelElem = doc.CreateElement("", "Parcel", "");
                    root.AppendChild(parcelElem);

                    CreateTextElem(doc, parcelElem, "FakeID", Util.BuildFakeParcelID(
                            m_Scene.RegionInfo.RegionHandle, x, y).ToString());

                    CreateTextElem(doc, parcelElem, "RegionID", m_Scene.RegionInfo.RegionID.ToString());
                    CreateTextElem(doc, parcelElem, "ParcelID", land.LandData.GlobalID.ToString());
                    CreateTextElem(doc, parcelElem, "OwnerID", land.LandData.OwnerID.ToString());

                    CreateTextElem(doc, parcelElem, "LandingPoint", userLocation.ToString());
                    CreateTextElem(doc, parcelElem, "ImageID", land.LandData.SnapshotID.ToString());
                    CreateTextElem(doc, parcelElem, "GroupID", land.LandData.GroupID.ToString());
                    CreateTextElem(doc, parcelElem, "Name", land.LandData.Name);
                    CreateTextElem(doc, parcelElem, "Description", land.LandData.Description);

                    string forsale = "0";
                    if ((land.LandData.Flags & (uint)ParcelFlags.ForSale) != 0)
                        forsale = "1";
                    CreateTextElem(doc, parcelElem, "ForSale", forsale);

                    CreateTextElem(doc, parcelElem, "Flags", land.LandData.Flags.ToString());
                    CreateTextElem(doc, parcelElem, "SalePrice", land.LandData.SalePrice.ToString());
                    CreateTextElem(doc, parcelElem, "AuthBuyerID", land.LandData.AuthBuyerID.ToString());
                    CreateTextElem(doc, parcelElem, "ScopeID", m_Scene.RegionInfo.ScopeID.ToString());
                    CreateTextElem(doc, parcelElem, "RegionHandle", m_Scene.RegionInfo.RegionHandle.ToString());
                    CreateTextElem(doc, parcelElem, "Area", land.LandData.Area.ToString());
                    CreateTextElem(doc, parcelElem, "ParentEstate", m_Scene.RegionInfo.EstateSettings.EstateID.ToString());
                    CreateTextElem(doc, parcelElem, "AccessLevel", m_Scene.RegionInfo.AccessLevel.ToString());
                    CreateTextElem(doc, parcelElem, "Category", ((sbyte)land.LandData.Category).ToString());
                }
            }
            
            Hashtable args = new Hashtable();
            args["Regions"] = doc.InnerXml;

            GenericXMLRPCRequest(args, "set_parcels");
        }

        private void findPointInParcel(ILandObject land, ref uint refX, ref uint refY)
        {
            // the point we started with already is in the parcel
            if (land.ContainsPoint((int)refX, (int)refY)) return;

            // ... otherwise, we have to search for a point within the parcel
            uint startX = (uint)land.LandData.AABBMin.X;
            uint startY = (uint)land.LandData.AABBMin.Y;
            uint endX = (uint)land.LandData.AABBMax.X;
            uint endY = (uint)land.LandData.AABBMax.Y;

            // default: center of the parcel
            refX = (startX + endX) / 2;
            refY = (startY + endY) / 2;
            // If the center point is within the parcel, take that one
            if (land.ContainsPoint((int)refX, (int)refY)) return;

            // otherwise, go the long way.
            for (uint y = startY; y <= endY; ++y)
            {
                for (uint x = startX; x <= endX; ++x)
                {
                    if (land.ContainsPoint((int)x, (int)y))
                    {
                        // found a point
                        refX = x;
                        refY = y;
                        return;
                    }
                }
            }
        }

        private void OnParcelPrimCountUpdate()
        {
            if (m_FreshStart)
            {
                m_FreshStart = false;
                SendParcelData();
            }
        }
    }
}
