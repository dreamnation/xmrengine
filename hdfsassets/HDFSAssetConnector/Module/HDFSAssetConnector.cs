using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Reflection;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Server.Base;
using OpenSim.Services.Base;
using OpenSim.Services.Interfaces;
using Nini.Config;
using log4net;
using MySql.Data.MySqlClient;
using System.Data;
using HDFS;
using Careminster;
using OpenMetaverse;
using System.Security.Cryptography;

namespace Careminster
{
    public class HDFSAssetConnector : ServiceBase, IAssetService
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        static System.Text.ASCIIEncoding enc = new System.Text.ASCIIEncoding();
        static SHA1CryptoServiceProvider SHA1 = new SHA1CryptoServiceProvider();

        static byte[] ToCString(string s)
        {
            byte[] ret = enc.GetBytes(s);
            Array.Resize(ref ret, ret.Length + 1);
            ret[ret.Length - 1] = 0;

            return ret;
        }

        protected IAssetLoader m_AssetLoader = null;
        protected string m_ConnectionString;
        protected HDFSAssetConnectorData m_DataConnector = null;
        protected string m_HdfsHost;
        protected int m_HdfsPort;
        protected int m_HdfsReplication = 1;
        protected string m_FsckProgram;
        protected IAssetService m_FallbackService;

        private Object m_hdfsLock = new Object();

        public HDFSAssetConnector(IConfigSource config) : base(config)
        {
            m_FsckProgram = string.Empty;

            MainConsole.Instance.Commands.AddCommand("hdfs", false,
                    "show assets", "show assets", "Show asset stats",
                    HandleShowAssets);
            MainConsole.Instance.Commands.AddCommand("hdfs", false,
                    "show digest", "show digest <ID>", "Show asset digest",
                    HandleShowDigest);
            MainConsole.Instance.Commands.AddCommand("hdfs", false,
                    "delete asset", "delete asset <ID>",
                    "Delete asset from database",
                    HandleDeleteAsset);
            MainConsole.Instance.Commands.AddCommand("hdfs", false,
                    "import", "import <conn> <table> [<start> <count>]",
                    "Import legacy assets",
                    HandleImportAssets);
            MainConsole.Instance.Commands.AddCommand("hdfs", false,
                    "force import", "force import <conn> <table> [<start> <count>]",
                    "Import legacy assets, overwriting current content",
                    HandleImportAssets);

            IConfig assetConfig = config.Configs["AssetService"];
            if (assetConfig == null)
            {
                throw new Exception("No AssetService configuration");
            }

            m_ConnectionString = assetConfig.GetString("ConnectionString", string.Empty);
            if (m_ConnectionString == string.Empty)
            {
                throw new Exception("Missing database connection string");
            }

            m_DataConnector = new HDFSAssetConnectorData(m_ConnectionString);
            m_HdfsHost = assetConfig.GetString("HdfsHost", string.Empty);

            if (m_HdfsHost == string.Empty)
            {
                throw new Exception("Missing HDFS host name");
            }

            m_HdfsPort = assetConfig.GetInt("HdfsPort", 0x4e20);
            m_HdfsReplication = assetConfig.GetInt("HdfsReplication", 0);
            m_FsckProgram = assetConfig.GetString("FsckProgram", string.Empty);

            string str = assetConfig.GetString("FallbackService", string.Empty);
            if (str != string.Empty)
            {
                object[] args = new object[] { config };
                m_FallbackService = LoadPlugin<IAssetService>(str, args);
                if (m_FallbackService != null)
                {
                    m_log.Info("[FALLBACK]: Fallback service loaded");
                }
                else
                {
                    m_log.Error("[FALLBACK]: Failed to load fallback service");
                }
            }

            if (HdfsClient.OpenHdfs(ToCString(m_HdfsHost), m_HdfsPort) < 0)
            {
                throw new Exception("Error connecting to HDFS");
            }

            string loader = assetConfig.GetString("DefaultAssetLoader", string.Empty);
            if (loader != string.Empty)
            {
                m_AssetLoader = LoadPlugin<IAssetLoader>(loader);
                string loaderArgs = assetConfig.GetString("AssetLoaderArgs", string.Empty);
                m_log.InfoFormat("[ASSET]: Loading default asset set from {0}", loaderArgs);
                m_AssetLoader.ForEachDefaultXmlAsset(loaderArgs,
                        delegate(AssetBase a)
                        {
                            Store(a, false);
                        });
            }
            m_log.Info("[ASSET CONNECTOR]: HDFS asset service enabled");
}

        string GetSHA1Hash(byte[] data)
        {
            byte[] hash = SHA1.ComputeHash(data);

            return BitConverter.ToString(hash).Replace("-", String.Empty);
        }

        public string HashToPath(string hash)
        {
            return new String(new char[] {Path.DirectorySeparatorChar}) + 
                   Path.Combine(hash.Substring(0, 2),
                   Path.Combine(hash.Substring(2, 2),
                   Path.Combine(hash.Substring(4, 2),
                   hash.Substring(6, 4))));
        }

        public string HashToFile(string hash)
        {
            return Path.Combine(HashToPath(hash), hash);
        }

        public AssetBase Get(string id)
        {
            string hash;

            return Get(id, out hash);
        }

        private AssetBase Get(string id, out string sha)
        {
            string hash = string.Empty;

            m_log.DebugFormat("[DEBUG]: Asset request for {0}", id);
            int startTime = System.Environment.TickCount;
            AssetMetadata metadata = m_DataConnector.Get(id, out hash);

            sha = hash;

            if (metadata == null)
            {
                AssetBase asset = null;
                if (m_FallbackService != null)
                {
                    asset = m_FallbackService.Get(id);
                    if (asset != null)
                    {
                        asset.Metadata.ContentType =
                                ServerUtils.SLAssetTypeToContentType((int)asset.Type);
                        sha = GetSHA1Hash(asset.Data);
                        m_log.InfoFormat("[FALLBACK]: Added asset {0} from fallback to local store", id);
                        Store(asset);
                    }
                }
                if (asset == null)
                {
                    m_log.InfoFormat("[ASSET]: Asset {0} not found", id);
                }
                return asset;
            }
            AssetBase newAsset = new AssetBase();
            newAsset.Metadata = metadata;
            try
            {
                newAsset.Data = GetHdfsData(hash);
                if (newAsset.Data.Length == 0)
                {
                    m_log.InfoFormat("[ASSET]: Asset {0}, hash {1} not found in HDFS", id, hash);
                }
                else
                {
                    m_log.DebugFormat("[DEBUG]: Asset {0} retrieved in {1}s", id, (float)(System.Environment.TickCount - startTime) / 1000.0);
                }
                return newAsset;
            }
            catch (Exception exception)
            {
                m_log.Error(exception.ToString());
                Environment.Exit(1);
                return null;
            }
        }

        public AssetMetadata GetMetadata(string id)
        {
            string hash;
            return m_DataConnector.Get(id, out hash);
        }

        public byte[] GetData(string id)
        {
            string hash;
            if (m_DataConnector.Get(id, out hash) == null)
                return null;

            return GetHdfsData(hash);
        }

        public bool Get(string id, Object sender, AssetRetrieved handler)
        {
            AssetBase asset = Get(id);

            handler(id, sender, asset);

            return true;
        }

        public byte[] GetHdfsData(string hash)
        {
            string file = HashToFile(hash);
            int size = HdfsClient.Size(ToCString(file));
            if (size < 0)
                return new byte[0];

            byte[] buf = new byte[size];

            int fd = HdfsClient.Open(ToCString(HashToFile(hash)), 0, 3);
            if (fd == 0) // Not there
            {
                throw new Exception("Error opening HDFS file");
            }

            HdfsClient.Read(fd, buf, size);
            HdfsClient.Close(fd);

            return buf;
        }

        public string Store(AssetBase asset)
        {
            return Store(asset, false);
        }

        private string Store(AssetBase asset, bool force)
        {
            int tickCount = Environment.TickCount;
            string hash = GetSHA1Hash(asset.Data);
            string s = HashToFile(hash);
            int hdfsFile = 0;

            lock (m_hdfsLock)
            {
                if (!force)
                {
                    hdfsFile = HdfsClient.Open(ToCString(s), 0, m_HdfsReplication);
                }
                if (hdfsFile == 0)
                {
                    hdfsFile = HdfsClient.Open(ToCString(s), 0x41, m_HdfsReplication);
                    if (hdfsFile == 0)
                    {
                        throw new Exception("Error opening HDFS file");
                    }

                    int length = asset.Data.Length;
                    byte[] destinationArray = new byte[0x4000];
                    int sourceIndex = 0;

                    while (length > 0)
                    {
                        int len = length;
                        if (len > 0x4000)
                        {
                            len = 0x4000;
                        }
                        Array.Copy(asset.Data, sourceIndex, destinationArray, 0, len);
                        int bytes = HdfsClient.Write(hdfsFile, destinationArray, len);
                        if (bytes <= 0)
                        {
                            break;
                        }
                        sourceIndex += bytes;
                        length -= bytes;
                    }
                }

                HdfsClient.Close(hdfsFile);
            }

            if (asset.ID == string.Empty)
            {
                if (asset.FullID == UUID.Zero)
                {
                    asset.FullID = UUID.Random();
                }
                asset.ID = asset.FullID.ToString();
            }
            else if (asset.FullID == UUID.Zero)
            {
                UUID uuid = UUID.Zero;
                if (UUID.TryParse(asset.ID, out uuid))
                {
                    asset.FullID = uuid;
                }
                else
                {
                    asset.FullID = UUID.Random();
                }
            }
            m_DataConnector.Store(asset.Metadata, hash);
            return asset.ID;
        }

        public bool UpdateContent(string id, byte[] data)
        {
            string oldhash;
            AssetMetadata meta = m_DataConnector.Get(id, out oldhash);

            if (meta == null)
                return false;

            AssetBase asset = new AssetBase();
            asset.Metadata = meta;
            asset.Data = data;

            Store(asset);

            return true;
        }

        public bool Delete(string id)
        {
            m_DataConnector.Delete(id);

            return true;
        }

        private void HandleShowAssets(string module, string[] args)
        {
            int num = m_DataConnector.Count();
            MainConsole.Instance.Output(string.Format("Total asset count: {0}", num));
            if (m_FsckProgram != string.Empty)
            {
                Process process = new Process();
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.FileName = "/bin/bash";
                process.StartInfo.Arguments = string.Format("{0} fsck / -blocks", m_FsckProgram);
                process.Start();
                while (process.StandardOutput.Peek() >= 0)
                {
                    string str = process.StandardOutput.ReadLine();
                    if (str.StartsWith(" ") && !str.Contains("Under replicated"))
                    {
                        MainConsole.Instance.Output(str.Trim());
                    }
                }
                process.WaitForExit();
            }
        }

        private void HandleShowDigest(string module, string[] args)
        {
            if (args.Length < 3)
            {
                MainConsole.Instance.Output("Syntax: show digest <ID>");
                return;
            }

            string hash;
            AssetBase asset = Get(args[2], out hash);

            if (asset == null || asset.Data.Length == 0)
            {   
                MainConsole.Instance.Output("Asset not found");
                return;
            }

            int i;

            MainConsole.Instance.Output(String.Format("Name: {0}", asset.Name));
            MainConsole.Instance.Output(String.Format("Description: {0}", asset.Description));
            MainConsole.Instance.Output(String.Format("Type: {0}", asset.Type));
            MainConsole.Instance.Output(String.Format("Content-type: {0}", asset.Metadata.ContentType));
            MainConsole.Instance.Output(String.Format("HDFS file: {0}", HashToFile(hash)));

            for (i = 0 ; i < 5 ; i++)
            {
                int off = i * 16;
                if (asset.Data.Length <= off)
                    break;
                int len = 16;
                if (asset.Data.Length < off + len)
                    len = asset.Data.Length - off;

                byte[] line = new byte[len];
                Array.Copy(asset.Data, off, line, 0, len);

                string text = BitConverter.ToString(line);
                MainConsole.Instance.Output(String.Format("{0:x4}: {1}", off, text));
            }
        }

        private void HandleDeleteAsset(string module, string[] args)
        {
            if (args.Length < 3)
            {
                MainConsole.Instance.Output("Syntax: delete asset <ID>");
                return;
            }

            AssetBase asset = Get(args[2]);

            if (asset == null || asset.Data.Length == 0)
            {   
                MainConsole.Instance.Output("Asset not found");
                return;
            }

            m_DataConnector.Delete(args[2]);

            MainConsole.Instance.Output("Asset deleted");
        }

        private void HandleImportAssets(string module, string[] args)
        {
            bool force = false;
            if (args[0] == "force")
            {
                force = true;
                List<string> list = new List<string>(args);
                list.RemoveAt(0);
                args = list.ToArray();
            }
            if (args.Length < 3)
            {
                MainConsole.Instance.Output("Syntax: import <conn> <table> [<start> <count>]");
            }
            else
            {
                string conn = args[1];
                string table = args[2];
                int start = 0;
                int count = -1;
                if (args.Length > 3)
                {
                    start = Convert.ToInt32(args[3]);
                }
                if (args.Length > 4)
                {
                    count = Convert.ToInt32(args[4]);
                }
                m_DataConnector.Import(conn, table, start, count, force, new StoreDelegate(Store));
            }
        }
    }
}
