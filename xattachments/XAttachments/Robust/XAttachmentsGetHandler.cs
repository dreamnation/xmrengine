////////////////////////////////////////////////////////////////
//
// (c) 2009, 2010 Careminster Limited and Melanie Thielker
//
// All rights reserved
//
using Nini.Config;
using log4net;
using System;
using System.IO;
using System.Reflection;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;

namespace Careminster
{
    public class AttachmentsServerGetHandler : BaseStreamHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IAttachmentsService m_AttachmentsService;
        private System.Text.UTF8Encoding utf8 =
                new System.Text.UTF8Encoding();

        public AttachmentsServerGetHandler(IAttachmentsService service) :
                base("GET", "/attachments")
        {
            m_AttachmentsService = service;
        }

        public override byte[] Handle(string path, Stream request,
                OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            string[] p = SplitParams(path);

            if (p.Length == 0)
                return new byte[0];

            return utf8.GetBytes(m_AttachmentsService.Get(p[0]));
        }
    }
}
