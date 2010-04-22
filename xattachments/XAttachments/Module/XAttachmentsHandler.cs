////////////////////////////////////////////////////////////////
//
// (c) 2009, 2010 Careminster Limited and Melanie Thielker
//
// All rights reserved
//
using System;
using Nini.Config;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Handlers.Base;

namespace Careminster
{
    public class XAttachmentsConnector : ServiceConnector
    {
        private IAttachmentsService m_AttachmentsService;
        private string m_ConfigName = "AttachmentsService";

        public XAttachmentsConnector(IConfigSource config, IHttpServer server, string configName) :
                base(config, server, configName)
        {
            if (configName != String.Empty)
                m_ConfigName = configName;

            IConfig serverConfig = config.Configs[m_ConfigName];
            if (serverConfig == null)
                throw new Exception(String.Format("No section '{0}' in config file", m_ConfigName));

            string assetService = serverConfig.GetString("LocalServiceModule",
                    String.Empty);

            if (assetService == String.Empty)
                throw new Exception("No AttachmentsService in config file");

            Object[] args = new Object[] { config };
            m_AttachmentsService =
                    ServerUtils.LoadPlugin<IAttachmentsService>(assetService, args);

            server.AddStreamHandler(new AttachmentsServerGetHandler(m_AttachmentsService));
            server.AddStreamHandler(new AttachmentsServerPostHandler(m_AttachmentsService));
        }
    }
}
