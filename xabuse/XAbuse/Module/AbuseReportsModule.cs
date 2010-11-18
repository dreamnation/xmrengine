/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Net.Mail;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Framework.Client;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Framework.Console;
using OpenSim.Data;
using OpenSim.Data.MySQL;
using Mono.Addins;

[assembly: Addin("XAbuse.Module", "1.0")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace Careminster.Modules.AbuseReport
{
    /// <summary>
    /// Enables the saving of abuse reports to the database
    /// </summary>
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "XAbuse")]
    public class AbuseReportsModule : ISharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private bool m_Enabled = false;
        private string m_ConnectionString;
        private string m_Realm = "AbuseReports";
        private List<Scene> m_SceneList = new List<Scene>();
        private MySQLGenericTableHandler<AbuseReport> m_AbuseTable;
        
        public void Initialise(IConfigSource source)
        {
            IConfig cnf = source.Configs["AbuseReports"];
            if (cnf != null)
            {
                m_Enabled = cnf.GetBoolean("Enabled", true);
                m_ConnectionString = cnf.GetString("DatabaseConnect", String.Empty);
                m_Realm = cnf.GetString("Realm", m_Realm);

                if (m_ConnectionString == String.Empty)
                {
                    m_log.Error("[ABUSE]: No database connect string, not enabling");
                    m_Enabled = false;
                }

                m_AbuseTable = new MySQLGenericTableHandler<AbuseReport>(m_ConnectionString, m_Realm, String.Empty);
            }

            if (m_Enabled == true)
                m_log.Debug("[ABUSE]: Abuse reports enabled");
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            lock (m_SceneList)
            {
                if (!m_SceneList.Contains(scene))
                    m_SceneList.Add(scene);
            }

            scene.EventManager.OnNewClient += OnNewClient;
            //Disabled until complete
            //scene.EventManager.OnRegisterCaps += OnRegisterCaps;
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            lock (m_SceneList)
            {
                if (m_SceneList.Contains(scene))
                    m_SceneList.Remove(scene);
            }
            scene.EventManager.OnNewClient -= OnNewClient;
            //Disabled until complete
            //scene.EventManager.OnRegisterCaps -= OnRegisterCaps;
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public void PostInitialise()
        {
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public string Name
        {
            get { return "AbuseReportsModule"; }
        }

        public bool IsSharedModule
        {
            get { return true; }
        }
        
        public void Close()
        {
        }

        private void OnNewClient(IClientAPI client)
        {
            client.OnUserReport += UserReport;
        }

        /// <summary>
        /// This deals with saving the report into the database.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="regionName"></param>
        /// <param name="abuserID"></param>
        /// <param name="catagory"></param>
        /// <param name="checkflags"></param>
        /// <param name="details"></param>
        /// <param name="objectID"></param>
        /// <param name="position"></param>
        /// <param name="reportType"></param>
        /// <param name="screenshotID"></param>
        /// <param name="summary"></param>
        /// <param name="reporter"></param>
        private void UserReport(IClientAPI client, string regionName,UUID abuserID, byte catagory, byte checkflags, string details, UUID objectID, Vector3 position, byte reportType ,UUID screenshotID, string summary, UUID reporter)
        {
            AbuseReport report = new AbuseReport();
            report.ReportID = UUID.Random();
            report.ObjectUUID = objectID;
            report.ObjectPosition = position.ToString();
            report.Active = true;
            report.Checked = false;
            report.Notes = "";
            report.AssignedTo = "No One";
            report.ScreenshotID = screenshotID;
            if (objectID != UUID.Zero)
            {
                SceneObjectPart Object = ((Scene)client.Scene).GetSceneObjectPart(objectID);
                report.ObjectName = Object.Name;
            }
        	else
                report.ObjectName = "";

        	string [] detailssplit = details.Split('\n');

            string AbuseDetails = detailssplit[4];
            if (detailssplit.Length != 5)
            {
                AbuseDetails = "";
                for(int i = 4; i < detailssplit.Length; i++)
                {
                    AbuseDetails+= detailssplit[i] + "\n";
                }
            }

            report.AbuseDetails = AbuseDetails;

            report.ReporterName = client.Name;

            string[] findRegion = summary.Split('|');
            report.RegionName = findRegion[1];

            string[] findLocation = summary.Split('(');
            string[] findLocationend = findLocation[1].Split(')');
            report.AbuseLocation = findLocationend[0];

            string[] findCategory = summary.Split('[');
            string[] findCategoryend = findCategory[1].Split(']');
            report.Category = findCategoryend[0];

            string[] findAbuserName = summary.Split('{');
            string[] findAbuserNameend = findAbuserName[1].Split('}');
            report.AbuserName = findAbuserNameend[0];

            string[] findSummary = summary.Split('\"');

            string abuseSummary = findSummary[1];
            if (findSummary.Length != 2)
            {
                abuseSummary = "";
                for (int i = 1; i < findSummary.Length; i++)
                {
                    abuseSummary += findSummary[i] + "\n";
                }
            }

            report.AbuseSummary = abuseSummary;


            report.Number = (-1);

            // Estate owner abuse email is incompatible with outbound fitering
            // Also, there is no viewer support
            //
            //EstateSettings ES = client.Scene.RegionInfo.EstateSettings;
            //If the abuse email is set up and the email module is available, send the email
            //if (ES.AbuseEmailToEstateOwner && ES.AbuseEmail != "")
            //{
            //    IEmailModule Email = m_SceneList[0].RequestModuleInterface<IEmailModule>();
            //    if(Email != null)
            //        Email.SendEmail(UUID.Zero, ES.AbuseEmail, "Abuse Report", "This abuse report was submitted by " +
            //            report.ReporterName + " against " + report.AbuserName + " at " + report.AbuseLocation + " in your region " + report.RegionName +
            //            ". Summary: " + report.AbuseSummary + ". Details: " + report.AbuseDetails + ".");
            //}
            //Tell the DB about it
            m_AbuseTable.Store(report);
        }

        #region Disabled CAPS code

        private void OnRegisterCaps(UUID agentID, OpenSim.Framework.Capabilities.Caps caps)
        {
            UUID capuuid = UUID.Random();

            caps.RegisterHandler("SendUserReportWithScreenshot",
                                new RestHTTPHandler("POST", "/CAPS/" + capuuid + "/",
                                                      delegate(Hashtable m_dhttpMethod)
                                                      {
                                                          return ProcessSendUserReportWithScreenshot(m_dhttpMethod, capuuid, agentID);
                                                      }));

            
        }

        private Hashtable ProcessSendUserReportWithScreenshot(Hashtable m_dhttpMethod, UUID capuuid, UUID agentID)
        {
            ScenePresence SP = findScenePresence(agentID);
            string RegionName = (string)m_dhttpMethod["abuse-region-name"];
            UUID AbuserID = UUID.Parse((string)m_dhttpMethod["abuser-id"]);
            byte Category = byte.Parse((string)m_dhttpMethod["category"]);
            byte CheckFlags = byte.Parse((string)m_dhttpMethod["check-flags"]);
            string details = (string)m_dhttpMethod["details"];
            UUID objectID = UUID.Parse((string)m_dhttpMethod["object-id"]);
            Vector3 position = Vector3.Zero;//(string)m_dhttpMethod["position"];
            byte ReportType = byte.Parse((string)m_dhttpMethod["report-type"]);
            UUID ScreenShotID = UUID.Parse((string)m_dhttpMethod["screenshot-id"]);
            string summary = (string)m_dhttpMethod["summary"];
            UserReport(SP.ControllingClient, RegionName, AbuserID, Category, CheckFlags,
                details, objectID, position, ReportType, ScreenShotID, summary, SP.UUID);
            //TODO: Figure this out later
            return new Hashtable();
        }

        #endregion

        #region Helpers

        public ScenePresence findScenePresence(UUID avID)
        {
            foreach (Scene s in m_SceneList)
            {
                ScenePresence SP = s.GetScenePresence(avID);
                if (SP != null)
                {
                    return SP;
                }
            }
            return null;
        }

        #endregion
    }
}
    
