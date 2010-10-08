using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;

using OpenSim.Framework;
using OpenSim.Server.Base;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Interfaces;

using OpenMetaverse;
using log4net;

namespace Careminster.Modules.XEstate
{
    public class EstateRequestHandler : BaseStreamHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected XEstateModule m_EstateModule;
        protected Object m_RequestLock = new Object();

        public EstateRequestHandler(XEstateModule fmodule)
                : base("POST", "/estate")
        {
            m_EstateModule = fmodule;
        }

        public override byte[] Handle(string path, Stream requestData,
        OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            StreamReader sr = new StreamReader(requestData);
            string body = sr.ReadToEnd();
            sr.Close();
            body = body.Trim();

            m_log.DebugFormat("[XESTATE HANDLER]: query String: {0}", body);

            try
            {
                lock (m_RequestLock)
                {
                    Dictionary<string, object> request =
                            ServerUtils.ParseQueryString(body);

                    if (!request.ContainsKey("METHOD"))
                        return FailureResult();

                    string method = request["METHOD"].ToString();
                    request.Remove("METHOD");

                    try
                    {
                        m_EstateModule.InInfoUpdate = false;

                        switch (method)
                        {
                            case "update_covenant":
                                return UpdateCovenant(request);
                            case "update_estate":
                                return UpdateEstate(request);
                            case "estate_message":
                                return EstateMessage(request);
                            case "teleport_home_one_user":
                                return TeleportHomeOneUser(request);
                            case "teleport_home_all_users":
                                return TeleportHomeAllUsers(request);
                        }
                    }
                    finally
                    {
                        m_EstateModule.InInfoUpdate = false;
                    }
                }
            }
            catch (Exception e)
            {
                m_log.Debug("[XESTATE]: Exception {0}" + e.ToString());
            }

            return FailureResult();
        }

        byte[] TeleportHomeAllUsers(Dictionary<string, object> request)
        {
            UUID PreyID = UUID.Zero;
            int EstateID = 0;

            if (!request.ContainsKey("EstateID"))
                return FailureResult();

            if (!Int32.TryParse(request["EstateID"].ToString(), out EstateID))
                return FailureResult();

            foreach (Scene s in m_EstateModule.Scenes)
            {
                if (s.RegionInfo.EstateSettings.EstateID == EstateID)
                {
                    s.ForEachScenePresence(delegate(ScenePresence p) {
                        if (p != null && !p.IsChildAgent)
                        {
                            p.ControllingClient.SendTeleportStart(16);
                            s.TeleportClientHome(p.ControllingClient.AgentId, p.ControllingClient);
                        }
                    });
                }
            }

            return SuccessResult();
        }

        byte[] TeleportHomeOneUser(Dictionary<string, object> request)
        {
            UUID PreyID = UUID.Zero;
            int EstateID = 0;

            if (!request.ContainsKey("PreyID") ||
                !request.ContainsKey("EstateID"))
            {
                return FailureResult();
            }

            if (!UUID.TryParse(request["PreyID"].ToString(), out PreyID))
                return FailureResult();

            if (!Int32.TryParse(request["EstateID"].ToString(), out EstateID))
                return FailureResult();

            foreach (Scene s in m_EstateModule.Scenes)
            {
                if (s.RegionInfo.EstateSettings.EstateID == EstateID)
                {
                    ScenePresence p = s.GetScenePresence(PreyID);
                    if (p != null && !p.IsChildAgent)
                    {
                        p.ControllingClient.SendTeleportStart(16);
                        s.TeleportClientHome(PreyID, p.ControllingClient);
                    }
                }
            }

            return SuccessResult();
        }

        byte[] EstateMessage(Dictionary<string, object> request)
        {
            UUID FromID = UUID.Zero;
            string FromName = String.Empty;
            string Message = String.Empty;
            int EstateID = 0;

            if (!request.ContainsKey("FromID") ||
                !request.ContainsKey("FromName") ||
                !request.ContainsKey("Message") ||
                !request.ContainsKey("EstateID"))
            {
                return FailureResult();
            }

            if (!UUID.TryParse(request["FromID"].ToString(), out FromID))
                return FailureResult();

            if (!Int32.TryParse(request["EstateID"].ToString(), out EstateID))
                return FailureResult();

            FromName = request["FromName"].ToString();
            Message = request["Message"].ToString();

            foreach (Scene s in m_EstateModule.Scenes)
            {
                if (s.RegionInfo.EstateSettings.EstateID == EstateID)
                {
                    IDialogModule dm = s.RequestModuleInterface<IDialogModule>();

                    if (dm != null)
                    {
                        dm.SendNotificationToUsersInRegion(FromID, FromName,
                                Message);
                    }
                }
            }

            return SuccessResult();
        }

        byte[] UpdateCovenant(Dictionary<string, object> request)
        {
            UUID CovenantID = UUID.Zero;
            int EstateID = 0;

            if (!request.ContainsKey("CovenantID") || !request.ContainsKey("EstateID"))
                return FailureResult();

            if (!UUID.TryParse(request["CovenantID"].ToString(), out CovenantID))
                return FailureResult();

            if (!Int32.TryParse(request["EstateID"].ToString(), out EstateID))
                return FailureResult();

            foreach (Scene s in m_EstateModule.Scenes)
            {
                if (s.RegionInfo.EstateSettings.EstateID == (uint)EstateID)
                    s.RegionInfo.RegionSettings.Covenant = CovenantID;
            }

            return SuccessResult();
        }

        byte[] UpdateEstate(Dictionary<string, object> request)
        {
            int EstateID = 0;

            if (!request.ContainsKey("EstateID"))
                return FailureResult();
            if (!Int32.TryParse(request["EstateID"].ToString(), out EstateID))
                return FailureResult();

            foreach (Scene s in m_EstateModule.Scenes)
            {
                if (s.RegionInfo.EstateSettings.EstateID == (uint)EstateID)
                    s.ReloadEstateData();
            }
            return SuccessResult();
        }

        private byte[] FailureResult()
        {
            return BoolResult(false);
        }

        private byte[] SuccessResult()
        {
            return BoolResult(true);
        }

        private byte[] BoolResult(bool value)
        {
            XmlDocument doc = new XmlDocument();

            XmlNode xmlnode = doc.CreateNode(XmlNodeType.XmlDeclaration,
                    "", "");

            doc.AppendChild(xmlnode);

            XmlElement rootElement = doc.CreateElement("", "ServerResponse",
                    "");

            doc.AppendChild(rootElement);

            XmlElement result = doc.CreateElement("", "RESULT", "");
            result.AppendChild(doc.CreateTextNode(value.ToString()));

            rootElement.AppendChild(result);

            return DocToBytes(doc);
        }

        private byte[] DocToBytes(XmlDocument doc)
        {
            MemoryStream ms = new MemoryStream();
            XmlTextWriter xw = new XmlTextWriter(ms, null);
            xw.Formatting = Formatting.Indented;
            doc.WriteTo(xw);
            xw.Flush();

            return ms.ToArray();
        }
    }
}
