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
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using DotNetOpenMail;
using DotNetOpenMail.SmtpAuth;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using Mono.Addins;
using OpenSim.Data;
using OpenSim.Data.MySQL;
using OpenSim.Services.Interfaces;

[assembly: Addin("XEmail.Module", "1.0")]
[assembly: AddinDependency("OpenSim", "0.5")]
namespace Careminster.Modules.XEmail
{
    public class XEmailMessage
    {
        public UUID ObjectID;
        public Dictionary<string,string> Data;
    }

    public class XEmailObject
    {
        public UUID ObjectID;
        public UUID RegionID;
        public Dictionary<string,string> Data;
    }
    
    public class XEmailWhitelist
    {
        public string Email;
        public Dictionary<string,string> Data;
    }

    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "XEmail")]
    public class XEmailModule : ISharedRegionModule, IEmailModule
    {
        //
        // Log
        //
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        //
        // Module vars
        //
        private IConfigSource m_Config;
        private string m_HostName = string.Empty;
        private string m_SmtpServer = string.Empty;
        private int m_SmtpPort = 25;
        private string m_SmtpUser = string.Empty;
        private string m_SmtpPassword = string.Empty;
        private string m_DatabaseConnect = string.Empty;
        private bool m_AllowAnyAddress = false;
        private bool m_AllowExternal = true;

        private int m_MaxQueueSize = 50; // Max size of local queue
        private Dictionary<UUID, List<Email>> m_MailQueues = new Dictionary<UUID, List<Email>>();
        private Dictionary<UUID, DateTime> m_LastGetEmailCall = new Dictionary<UUID, DateTime>();
        private Dictionary<UUID, DateTime> m_LastPoll = new Dictionary<UUID, DateTime>();
        private TimeSpan m_QueueTimeout = new TimeSpan(2, 0, 0); // 2 hours without llGetNextEmail drops the queue
        private TimeSpan m_PollDelay = new TimeSpan(0, 0, 5); // 10 seconds poll delay
        private MySQLGenericTableHandler<XEmailObject> m_ObjectsTable;
        private MySQLGenericTableHandler<XEmailMessage> m_MessagesTable;
        private MySQLGenericTableHandler<XEmailWhitelist> m_WhitelistTable;

        // Scenes by Region Handle
        private Dictionary<UUID, Scene> m_Scenes =
            new Dictionary<UUID, Scene>();

        private bool m_Enabled = false;

        public void InsertEmail(UUID to, Email email)
        {
            // It's tempting to create the queue here.  Don't; objects which have
            // not yet called GetNextEmail should have no queue, and emails to them
            // should be silently dropped.

            lock (m_MailQueues)
            {
                if (m_MailQueues.ContainsKey(to))
                {
                    if (m_MailQueues[to].Count >= m_MaxQueueSize)
                    {
                        // fail silently
                        return;
                    }

                    lock (m_MailQueues[to])
                    {
                        m_MailQueues[to].Add(email);
                    }
                }
            }
        }

        public void Initialise(IConfigSource config)
        {
            m_Config = config;
            IConfig SMTPConfig;

            IConfig startupConfig = m_Config.Configs["Startup"];

            m_Enabled = (startupConfig.GetString("emailmodule", "DefaultEmailModule") == "XEmail");
            if (!m_Enabled)
                return;

            //Load SMTP SERVER config
            try
            {
                if ((SMTPConfig = m_Config.Configs["SMTP"]) == null)
                {
                    m_log.InfoFormat("[XEmail] SMTP section not configured");
                    m_Enabled = false;
                    return;
                }

                if (!SMTPConfig.GetBoolean("enabled", false))
                {
                    m_log.InfoFormat("[XEmail] module disabled in configuration");
                    m_Enabled = false;
                    return;
                }

                m_HostName = SMTPConfig.GetString("EmailAddressSuffix", m_HostName);
                m_SmtpServer = SMTPConfig.GetString("SmtpServer", m_SmtpServer);
                m_SmtpPort = SMTPConfig.GetInt("SmtpPort", m_SmtpPort);
                m_SmtpUser = SMTPConfig.GetString("SmtpUser", m_SmtpUser);
                m_SmtpPassword = SMTPConfig.GetString("SmtpPassword", m_SmtpPassword);
                m_AllowAnyAddress = SMTPConfig.GetBoolean("AllowAnyAddress", m_AllowAnyAddress);
                m_AllowExternal = SMTPConfig.GetBoolean("AllowExternal", m_AllowExternal);
                m_DatabaseConnect = SMTPConfig.GetString("DatabaseConnect", m_SmtpPassword);
            }
            catch (Exception e)
            {
                m_log.Error("[XEmail] XEmail not configured: "+ e.Message);
                m_Enabled = false;
                return;
            }

            m_ObjectsTable = new MySQLGenericTableHandler<XEmailObject>(
                    m_DatabaseConnect, "XEmailObjects", String.Empty);
            m_MessagesTable = new MySQLGenericTableHandler<XEmailMessage>(
                    m_DatabaseConnect, "XEmailMessages", String.Empty);
            m_WhitelistTable = new MySQLGenericTableHandler<XEmailWhitelist>(
                    m_DatabaseConnect, "XEmailWhitelist", String.Empty);

            m_log.Info("[XEmail] Activated XEmail");
        }

        public void AddRegion(Scene scene)
        {
            // It's a go!
            if (m_Enabled)
            {
                lock (m_Scenes)
                {
                    // Claim the interface slot
                    scene.RegisterModuleInterface<IEmailModule>(this);
                    m_log.InfoFormat("[XEmail]: Adding module to region {0}", scene.RegionInfo.RegionName);

                    m_Scenes[scene.RegionInfo.RegionID] = scene;
                }
            }
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public void RemoveRegion(Scene scene)
        {
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public string Name
        {
            get { return "XEmail"; }
        }

        public Type ReplaceableInterface
        {
            get
            {
                return null;
            }
        }

        private bool IsLocal(UUID objectID)
        {
            string unused;
            UUID regionID;
            return (findPrim(objectID, out unused, out regionID) != null);
        }

        private SceneObjectPart findPrim(UUID objectID, out string ObjectRegionName, out UUID regionID)
        {
            lock (m_Scenes)
            {
                foreach (Scene s in m_Scenes.Values)
                {
                    SceneObjectPart part = s.GetSceneObjectPart(objectID);
                    if (part != null)
                    {
                        ObjectRegionName = s.RegionInfo.RegionName;
                        uint localX = (s.RegionInfo.RegionLocX * (int)Constants.RegionSize);
                        uint localY = (s.RegionInfo.RegionLocY * (int)Constants.RegionSize);
                        ObjectRegionName = ObjectRegionName + " (" + localX + ", " + localY + ")";
                        regionID = s.RegionInfo.RegionID;
                        return part;
                    }
                }
            }
            ObjectRegionName = string.Empty;
            regionID = UUID.Zero;
            return null;
        }

        private void resolveNamePositionRegionName(UUID objectID, out string ObjectName, out string ObjectAbsolutePosition, out string ObjectRegionName)
        {
            string m_ObjectRegionName;
            UUID regionID;
            int objectLocX;
            int objectLocY;
            int objectLocZ;
            SceneObjectPart part = findPrim(objectID, out m_ObjectRegionName, out regionID);
            if (part != null)
            {
                objectLocX = (int)part.AbsolutePosition.X;
                objectLocY = (int)part.AbsolutePosition.Y;
                objectLocZ = (int)part.AbsolutePosition.Z;
                ObjectAbsolutePosition = "(" + objectLocX + ", " + objectLocY + ", " + objectLocZ + ")";
                ObjectName = part.Name;
                ObjectRegionName = m_ObjectRegionName;
                return;
            }
            objectLocX = (int)part.AbsolutePosition.X;
            objectLocY = (int)part.AbsolutePosition.Y;
            objectLocZ = (int)part.AbsolutePosition.Z;
            ObjectAbsolutePosition = "(" + objectLocX + ", " + objectLocY + ", " + objectLocZ + ")";
            ObjectName = part.Name;
            ObjectRegionName = m_ObjectRegionName;
            return;
        }

        /// <summary>
        /// SendMail function utilized by llEMail
        /// </summary>
        /// <param name="objectID"></param>
        /// <param name="address"></param>
        /// <param name="subject"></param>
        /// <param name="body"></param>
        public void SendEmail(UUID objectID, string address, string subject, string body)
        {
            //Check if address is empty
            if (address == string.Empty)
                return;

            //FIXED:Check the email is correct form in REGEX
            string EMailpatternStrict = @"^(([^<>()[\]\\.,;:\s@\""]+"
                + @"(\.[^<>()[\]\\.,;:\s@\""]+)*)|(\"".+\""))@"
                + @"((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}"
                + @"\.[0-9]{1,3}\])|(([a-zA-Z\-0-9]+\.)+"
                + @"[a-zA-Z]{2,}))$";
            Regex EMailreStrict = new Regex(EMailpatternStrict);
            bool isEMailStrictMatch = EMailreStrict.IsMatch(address);
            if (!isEMailStrictMatch)
            {
                //m_log.Error("[XEmail] REGEX Problem in EMail Address: "+address);
                return;
            }
            //FIXME:Check if subject + body = 4096 Byte
            if ((subject.Length + body.Length) > 1024)
            {
                //m_log.Error("[XEmail] subject + body > 1024 Byte");
                return;
            }

            string LastObjectName = string.Empty;
            string LastObjectPosition = string.Empty;
            string LastObjectRegionName = string.Empty;

            resolveNamePositionRegionName(objectID, out LastObjectName, out LastObjectPosition, out LastObjectRegionName);

            string guid = address.Substring(0, address.IndexOf("@"));
            if (guid.Length > 36)
                guid = guid.Substring(0, 36);
            UUID toID = UUID.Zero;

            if (!UUID.TryParse(guid, out toID) || !address.EndsWith(m_HostName) || !IsLocal(toID))
            {
                if (toID != UUID.Zero && address.EndsWith(m_HostName)) // Prim
                {
                    XEmailObject[] prims = m_ObjectsTable.Get("ObjectID", toID.ToString());
                    if (prims.Length == 0)
                    {
                        return;
                    }
                    else
                    {
                        string message = "From: " + objectID.ToString() + m_HostName + "\n";
                        message += "Subject: " + subject + "\n";
                        message += "Date: " + ((int)((DateTime.UtcNow - new DateTime(1970,1,1,0,0,0)).TotalSeconds)).ToString() + "\n\n";

                        int headerlen = message.Length;
                        message = String.Format("X-XEmail-Internal-length: {0}", headerlen) + "\n" + message;

                        message += "Object-Name: " + LastObjectName +
                              "\nRegion: " + LastObjectRegionName + "\nLocal-Position: " +
                              LastObjectPosition + "\n\n" + body;

                        XEmailMessage m = new XEmailMessage();
                        m.Data = new Dictionary<string, string>();
                        m.ObjectID = toID;
                        m.Data["Message"] = message;
                        m_MessagesTable.Store(m);

                        return;
                    }
                }

                bool addressOK = m_AllowAnyAddress && m_AllowExternal;

                if (!addressOK && m_AllowExternal)
                {
                    string dummy1;
                    UUID regionID;

                    SceneObjectPart part = findPrim(objectID, out dummy1, out regionID);
                    Scene s;

                    if (part != null && m_Scenes.TryGetValue(regionID, out s))
                    {
                        UserAccount account = s.UserAccountService.GetUserAccount(s.RegionInfo.ScopeID, part.OwnerID);

                        if (address == account.Email)
                            addressOK = true;
                    }
                }

                if (!addressOK && m_AllowExternal)
                {
                    XEmailWhitelist[] wl = m_WhitelistTable.Get("Email", address);
                    if (wl.Length > 0)
                        addressOK = true;
                }

                if (!addressOK)
                {
                    m_log.InfoFormat("[XEmail]: message from prim {0} to {1} rejected", objectID, address);
                    return;
                }

                // regular email, send it out
                try
                {
                    //Creation EmailMessage
                    EmailMessage emailMessage = new EmailMessage();
                    //From
                    emailMessage.FromAddress = new EmailAddress(objectID.ToString() + m_HostName);
                    //To - Only One
                    emailMessage.AddToAddress(new EmailAddress(address));
                    //Subject
                    emailMessage.Subject = subject;
                    //TEXT Body
                    resolveNamePositionRegionName(objectID, out LastObjectName, out LastObjectPosition, out LastObjectRegionName);
                    emailMessage.BodyText = "Object-Name: " + LastObjectName +
                              "\nRegion: " + LastObjectRegionName + "\nLocal-Position: " +
                              LastObjectPosition + "\n\n" + body;

                    if (emailMessage.BodyText.Length > 2048)
                        emailMessage.BodyText = emailMessage.BodyText.Substring(0, 2048);
                    //Config SMTP Server
                    //Set SMTP SERVER config
                    SmtpServer smtpServer=new SmtpServer(m_SmtpServer,m_SmtpPort);
                    // Add authentication only when requested
                    //
                    if (m_SmtpUser != String.Empty && m_SmtpPassword != String.Empty)
                    {
                        //Authentication
                        smtpServer.SmtpAuthToken=new SmtpAuthToken(m_SmtpUser, m_SmtpPassword);
                    }
                    //Send Email Message
                    emailMessage.Send(smtpServer);

                    //Log
                    //m_log.Info("[XEmail] EMail sent to: " + address + " from object: " + objectID.ToString() + m_HostName);
                }
                catch (Exception e)
                {
                    m_log.Error("[XEmail] SMTP Exception: " + e.Message);
                }
            }
            else
            {
                // inter object email, keep it in the family
                Email email = new Email();
                email.time = ((int)((DateTime.UtcNow - new DateTime(1970,1,1,0,0,0)).TotalSeconds)).ToString();
                email.subject = subject;
                email.sender = objectID.ToString() + m_HostName;
                email.message = "Object-Name: " + LastObjectName +
                              "\nRegion: " + LastObjectRegionName + "\nLocal-Position: " +
                              LastObjectPosition + "\n\n" + body;

                InsertEmail(toID, email);
            }

        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="objectID"></param>
        /// <param name="sender"></param>
        /// <param name="subject"></param>
        /// <returns></returns>
        public Email GetNextEmail(UUID objectID, string sender, string subject)
        {
            List<Email> queue = null;

            lock (m_LastGetEmailCall)
            {
                m_LastGetEmailCall[objectID] = DateTime.Now;

                // Hopefully this isn't too time consuming.  If it is, we can always push it into a worker thread.
                DateTime now = DateTime.Now;
                List<UUID> removal = new List<UUID>();
                foreach (UUID uuid in m_LastGetEmailCall.Keys)
                {
                    if ((now - m_LastGetEmailCall[uuid]) > m_QueueTimeout)
                    {
                        removal.Add(uuid);
                    }
                }

                foreach (UUID remove in removal)
                {
                    m_LastGetEmailCall.Remove(remove);
                    m_LastPoll.Remove(remove);
                    lock (m_MailQueues)
                    {
                        m_MailQueues.Remove(remove);
                    }
                }
            }

            lock (m_MailQueues)
            {
                if (m_MailQueues.ContainsKey(objectID))
                {
                    queue = m_MailQueues[objectID];
                }
                else
                {
                    queue = new List<Email>();
                    m_MailQueues.Add(objectID, queue);
                }
            }

            if (queue != null)
            {
                if ((m_LastPoll.ContainsKey(objectID) && (DateTime.Now - m_LastPoll[objectID]) >= m_PollDelay) || queue.Count == 0)
                {
                    m_LastPoll[objectID] = DateTime.Now;

                    string regionName;
                    UUID regionID;
                    if (findPrim(objectID, out regionName, out regionID) != null)
                    {
                        XEmailObject obj = new XEmailObject();
                        obj.Data = new Dictionary<string,string>();
                        obj.ObjectID = objectID;
                        obj.RegionID = regionID;
                        m_ObjectsTable.Store(obj);
                    }

                    if (queue.Count < 3) // Never let it become 0 before poll
                    {
                        string where = String.Format("ObjectID='{0}' order by id asc limit 10", objectID);

                        XEmailMessage[] messages = m_MessagesTable.Get(where);

                        foreach (XEmailMessage message in messages)
                        {
                            m_MessagesTable.Delete("id", message.Data["id"]);

                            // As the PHP script writes this, line endings
                            // are sane (LF), not RFC 2822
                            string[] lines = message.Data["Message"].Split(new char[] {'\n'});
                            if (lines.Length == 0)
                                continue;

                            Email msg = new Email();

                            if (lines[0].StartsWith("From "))
                                msg.sender = lines[0].Substring(5);
                            bool intersim = false;
                            bool inMessage = false;
                            string body = String.Empty;
                            foreach (string line in lines)
                            {
                                if (line.StartsWith("X-XEmail-Internal-length: "))
                                {
                                    int off = line.Length;
                                    int len = Convert.ToInt32(line.Substring(26));

                                    body = message.Data["Message"].Substring(len + off + 1);
                                    intersim = true;
                                }

                                if (line.StartsWith("Date: "))
                                {
                                    string timestr = line.Substring(6);
                                    DateTime tx = DateTime.Now;
                                    try
                                    {
                                        tx = Convert.ToDateTime(timestr);
                                    }
                                    catch { }

                                    msg.time = ((int)((tx - new DateTime(1970,1,1,0,0,0)).TotalSeconds)).ToString();
                                }
                                if (line.StartsWith("From: "))
                                    msg.sender = line.Substring(6);
                                if (line.StartsWith("Subject: "))
                                    msg.subject = line.Substring(9);
                                if (line == String.Empty && inMessage == false)
                                {
                                    if (intersim)
                                        break;
                                    inMessage = true;
                                    continue;
                                }
                                if (inMessage)
                                    body += line + "\r\n";
                            }

                            msg.message = body;
                            queue.Add(msg);
                        }
                    }
                }

                lock (queue)
                {
                    if (queue.Count > 0)
                    {
                        int i;

                        for (i = 0; i < queue.Count; i++)
                        {
                            if ((sender == null || sender.Equals("") || sender.Equals(queue[i].sender)) &&
                                (subject == null || subject.Equals("") || subject.Equals(queue[i].subject)))
                            {
                                break;
                            }
                        }

                        if (i != queue.Count)
                        {
                            Email ret = queue[i];
                            queue.Remove(ret);
                            ret.numLeft = queue.Count;
                            return ret;
                        }
                    }
                }
            }

            return null;
        }
    }
}
