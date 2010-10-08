using System;
using System.Collections.Generic;
using System.Reflection;

using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using OpenSim.Server.Base;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Scenes;

using OpenMetaverse;
using log4net;

namespace Careminster.Modules.XEstate
{
    public class EstateConnector
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected XEstateModule m_EstateModule;

        public EstateConnector(XEstateModule module)
        {
            m_EstateModule = module;
        }

        public void SendTeleportHomeOneUser(uint EstateID, UUID PreyID)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "teleport_home_one_user";

            sendData["EstateID"] = EstateID.ToString();
            sendData["PreyID"] = PreyID.ToString();

            SendToEstate(EstateID, sendData);
        }

        public void SendTeleportHomeAllUsers(uint EstateID)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "teleport_home_all_users";

            sendData["EstateID"] = EstateID.ToString();

            SendToEstate(EstateID, sendData);
        }

        public bool SendUpdateCovenant(uint EstateID, UUID CovenantID)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "update_covenant";

            sendData["CovenantID"] = CovenantID.ToString();
            sendData["EstateID"] = EstateID.ToString();

            // Handle local regions locally
            //
            foreach (Scene s in m_EstateModule.Scenes)
            {
                if (s.RegionInfo.EstateSettings.EstateID == EstateID)
                    s.RegionInfo.RegionSettings.Covenant = CovenantID;
//                    s.ReloadEstateData();
            }

            SendToEstate(EstateID, sendData);

            return true;
        }

        public bool SendUpdateEstate(uint EstateID)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "update_estate";

            sendData["EstateID"] = EstateID.ToString();

            // Handle local regions locally
            //
            foreach (Scene s in m_EstateModule.Scenes)
            {
                if (s.RegionInfo.EstateSettings.EstateID == EstateID)
                    s.ReloadEstateData();
            }

            SendToEstate(EstateID, sendData);

            return true;
        }

        public void SendEstateMessage(uint EstateID, UUID FromID, string FromName, string Message)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            sendData["METHOD"] = "estate_message";

            sendData["EstateID"] = EstateID.ToString();
            sendData["FromID"] = FromID.ToString();
            sendData["FromName"] = FromName;
            sendData["Message"] = Message;

            SendToEstate(EstateID, sendData);
        }

        private void SendToEstate(uint EstateID, Dictionary<string, object> sendData)
        {
            List<UUID> regions = m_EstateModule.Scenes[0].GetEstateRegions((int)EstateID);

            UUID ScopeID = UUID.Zero;

            // Handle local regions locally
            //
            foreach (Scene s in m_EstateModule.Scenes)
            {
                if (regions.Contains(s.RegionInfo.RegionID))
                {
                    // All regions in one estate are in the same scope.
                    // Use that scope.
                    //
                    ScopeID = s.RegionInfo.ScopeID;
                    regions.Remove(s.RegionInfo.RegionID);
                }
            }

            // Our own region should always be in the above list.
            // In a standalone this would not be true. But then,
            // Scope ID is not relevat there. Use first scope.
            //
            if (ScopeID == UUID.Zero)
                ScopeID = m_EstateModule.Scenes[0].RegionInfo.ScopeID;

            // Don't send to the same instance twice
            //
            List<string> done = new List<string>();

            // Send to remote regions
            //
            foreach (UUID regionID in regions)
            {
                GridRegion region = m_EstateModule.Scenes[0].GridService.GetRegionByUUID(ScopeID, regionID);
                if (region != null)
                {
                    string url = "http://" + region.ExternalHostName + ":" + region.HttpPort;
                    if (done.Contains(url))
                        continue;

                    Call(region, sendData);
                    done.Add(url);
                }
            }
        }

        private bool Call(GridRegion region, Dictionary<string, object> sendData)
        {
            string reqString = ServerUtils.BuildQueryString(sendData);
            // m_log.DebugFormat("[XESTATE CONNECTOR]: queryString = {0}", reqString);
            try
            {
                string url = "http://" + region.ExternalHostName + ":" + region.HttpPort;
                string reply = SynchronousRestFormsRequester.MakeRequest("POST",
                        url + "/estate",
                        reqString);
                if (reply != string.Empty)
                {
                    Dictionary<string, object> replyData = ServerUtils.ParseXmlResponse(reply);

                    if (replyData.ContainsKey("RESULT"))
                    {
                        if (replyData["RESULT"].ToString().ToLower() == "true")
                            return true;
                        else
                            return false;
                    }
                    else
                        m_log.DebugFormat("[XESTATE CONNECTOR]: reply data does not contain result field");

                }
                else
                    m_log.DebugFormat("[XESTATE CONNECTOR]: received empty reply");
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[XESTATE CONNECTOR]: Exception when contacting remote sim: {0}", e.Message);
            }

            return false;
        }
    }
}
