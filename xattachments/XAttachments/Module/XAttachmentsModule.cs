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

            List<SceneObjectGroup> attachments = sp.Attachments;

            Dictionary<UUID, string> states = new Dictionary<UUID, string>();

            foreach (SceneObjectGroup att in attachments)
            {
                MemoryStream ms = new MemoryStream();
                XmlTextWriter xw = new XmlTextWriter(ms, null);

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
            rc.Request(reqStream);
        }
    }
}
