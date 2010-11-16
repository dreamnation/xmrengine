// ******************************************************************
// Copyright (c) 2008, 2009 Melanie Thielker
//
// All rights reserved
//

using OpenMetaverse;
using Nini.Config;
using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using OpenSim.Framework;
using OpenSim.Framework.Communications;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using Mono.Addins;
using Careminster;

[assembly: Addin("XAtachments.Module", "1.0")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace Careminster.Modules.XAttachments
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "XAttachments")]
    public class XAttachmentsModule : INonSharedRegionModule, IAttachmentsService
    {
        protected Scene m_Scene;
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private UTF8Encoding enc = new UTF8Encoding();
        private string m_URL = String.Empty;

        public void Initialise(IConfigSource configSource)
        {
            IConfig config = configSource.Configs["XAttachments"];
            if (config == null)
                return;

            m_URL = config.GetString("URL", String.Empty);
        }

        public void AddRegion(Scene scene)
        {
            m_log.InfoFormat("[XAttachments]: Enabled for region {0}", scene.RegionInfo.RegionName);
            m_Scene = scene;

            scene.EventManager.OnRemovePresence += OnRemovePresence;

            scene.RegisterModuleInterface<IAttachmentsService>(this);
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public void RemoveRegion(Scene scene)
        {
        }

        public void Close()
        {
        }

        public string Name
        {
            get { return "XAttachmentsModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public string Get(string id)
        {
            if (m_URL == String.Empty)
                return String.Empty;

            RestClient rc = new RestClient(m_URL);
            rc.AddResourcePath("attachments");
            rc.AddResourcePath(id);

            rc.RequestMethod = "GET";

            Stream s = rc.Request();
            StreamReader sr = new StreamReader(s);

            string data = sr.ReadToEnd();
            sr.Close();
            s.Close();

            return data;
        }

        public void Store(string id, string data)
        {
        }

        private void OnRemovePresence(UUID AgentID)
        {
            bool haveAttachments = false;

            if (m_URL == String.Empty)
                return;

            ScenePresence sp;
            if (!m_Scene.TryGetScenePresence(AgentID, out sp))
                return;

            if (sp.IsChildAgent)
                return;

            m_log.InfoFormat("[XAttachments]: Storing attachment script states for {0}", AgentID);

            // Get the old data we need to possibly keep
            string attData = Get(AgentID.ToString());

            // These are the ones actually inworld, from which state
            // can be saved
            List<SceneObjectGroup> attachments = new List<SceneObjectGroup>(sp.Attachments.ToArray());

            // These are the ones we should have. These states need
            // to be preserved
            List<AvatarAttachment> attList = new List<AvatarAttachment>(sp.Appearance.GetAttachments().ToArray());
            List<UUID> validIDs = new List<UUID>();
            foreach (AvatarAttachment att in attList)
            {
                if (!validIDs.Contains(att.ItemID))
                    validIDs.Add(att.ItemID);
            }

            // Contains the actual states
            Dictionary<UUID, string> states = new Dictionary<UUID, string>();

            XmlDocument doc = new XmlDocument();

            // If this is empty or invalid, we don't want to know
            if (attData != String.Empty)
            {
                try
                {
                    doc.LoadXml(attData);
                }
                catch { }
            }

            // This will load all the states we "inherit" on login. This
            // may include states for attachments that haven't rezzed yet,
            // as in the event of a crash or incomplete login
            XmlNodeList nodes = doc.GetElementsByTagName("Attachment");
            if (nodes.Count > 0)
            {
                haveAttachments = true;
                foreach (XmlNode n in nodes)
                {
                    XmlElement elem = (XmlElement)n;
                    UUID itemID = new UUID(elem.GetAttribute("ItemID"));
                    string xml = elem.InnerXml;

                    // Don't save it if we're no longer wearing that attachment
                    if (validIDs.Contains(itemID))
                        states[itemID] = xml;
                }
            }

            // Now, let's add the ones that did rez
            foreach (SceneObjectGroup att in attachments)
            {
                MemoryStream ms = new MemoryStream();
                XmlTextWriter xw = new XmlTextWriter(ms, null);

                // If the group itself has changed, use new IDs because
                // the asset with the new ids is saved later
                if (att.HasGroupChanged)
                    att.SaveScriptedState(xw, false);
                else
                    att.SaveScriptedState(xw, true);
                xw.Flush();
                xw.Close();
                string state = enc.GetString(ms.ToArray());

                if (state != String.Empty)
                {
                    haveAttachments = true;
                    states[att.GetFromItemID()] = state;
                }
            }

            if (!haveAttachments)
                return;

            // Finally, write out new XML
            MemoryStream attStream = new MemoryStream();
            XmlTextWriter attWriter = new XmlTextWriter(attStream, null);

            attWriter.WriteStartElement(String.Empty, "AttachmentStates", String.Empty);
            foreach (KeyValuePair<UUID, string> kvp in states)
            {
                attWriter.WriteStartElement(String.Empty, "Attachment", String.Empty);
                attWriter.WriteAttributeString(String.Empty, "ItemID", String.Empty, kvp.Key.ToString());
                attWriter.WriteRaw(kvp.Value);
                attWriter.WriteEndElement();

            }
            attWriter.WriteEndElement();
            attWriter.Flush();

            RestClient rc = new RestClient(m_URL);
            rc.AddResourcePath("attachments");
            rc.AddResourcePath(AgentID.ToString());

            rc.RequestMethod = "POST";

            MemoryStream reqStream = new MemoryStream(attStream.ToArray());
            Util.FireAndForget(
                delegate
                {
                    rc.Request(reqStream);
                }
            );
        }
    }
}
