////////////////////////////////////////////////////////////////
//
// (c) 2009, 2010 Careminster Limited and Melanie Thielker
//
// All rights reserved
//
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Reflection;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Server.Base;
using OpenSim.Services.Base;
using OpenSim.Services.Interfaces;
using Nini.Config;
using log4net;
using Careminster;
using OpenMetaverse;

namespace Careminster
{
    public class XAttachments : ServiceBase, IAttachmentsService
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        protected string m_FSBase;

        private System.Text.UTF8Encoding utf8encoding =
                new System.Text.UTF8Encoding();

        public XAttachments(IConfigSource config) : base(config)
        {
            MainConsole.Instance.Commands.AddCommand("fs", false,
                    "delete attachments", "delete attachments <ID>",
                    "Delete agent's attachments from server",
                    HandleDeleteAttachments);

            IConfig assetConfig = config.Configs["AttachmentsService"];
            if (assetConfig == null)
            {
                throw new Exception("No AttachmentsService configuration");
            }

            m_FSBase = assetConfig.GetString("BaseDirectory", String.Empty);
            if (m_FSBase == String.Empty)
            {
                m_log.ErrorFormat("[ATTACHMENTS]: BaseDirectory not specified");
                throw new Exception("Configuration error");
            }

            m_log.Info("[ATTACHMENTS]: XAttachments service enabled");
        }

        public string Get(string id)
        {
            string file = HashToFile(id);
            string diskFile = Path.Combine(m_FSBase, file);

            if (File.Exists(diskFile))
            {
                try
                {
                    byte[] content = File.ReadAllBytes(diskFile);

                    return utf8encoding.GetString(content);
                }
                catch
                {
                }
            }
            return String.Empty;
        }

        public void Store(string id, string sdata)
        {
            string file = HashToFile(id);
            string diskFile = Path.Combine(m_FSBase, file);

            Directory.CreateDirectory(Path.GetDirectoryName(diskFile));

            File.Delete(diskFile);

            byte[] data = utf8encoding.GetBytes(sdata);
            FileStream fs = File.Create(diskFile);

            fs.Write(data, 0, data.Length);

            fs.Close();
        }

        private void HandleDeleteAttachments(string module, string[] args)
        {
            if (args.Length < 3)
            {
                MainConsole.Instance.Output("Syntax: delete attachments <ID>");
                return;
            }

            string file = HashToFile(args[2]);
            string diskFile = Path.Combine(m_FSBase, file);

            if (File.Exists(diskFile))
            {
                File.Delete(diskFile);
                MainConsole.Instance.Output("Attachments deleted");
                return;
            }
            MainConsole.Instance.Output("Attachments not found");
        }

        public string HashToPath(string hash)
        {
            return Path.Combine(hash.Substring(0, 2),
                   Path.Combine(hash.Substring(2, 2),
                   Path.Combine(hash.Substring(4, 2),
                   hash.Substring(6, 4))));
        }

        public string HashToFile(string hash)
        {
            return Path.Combine(HashToPath(hash), hash);
        }
    }
}
