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
using MySql.Data.MySqlClient;
using System.Data;
using Careminster;
using OpenMetaverse;
using System.Security.Cryptography;

namespace Careminster
{
    public class FSAssetConnector : ServiceBase, IAssetService
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
        protected FSAssetConnectorData m_DataConnector = null;
        protected string m_FsckProgram;
        protected IAssetService m_FallbackService;
        protected Thread m_WriterThread;
        protected Thread m_StatsThread;
        protected string m_SpoolDirectory;
        protected object m_readLock = new object();
        protected object m_statsLock = new object();
        protected int m_readCount = 0;
        protected int m_readTicks = 0;
        protected int m_missingAssets = 0;
        protected string m_FSBase;
        protected string m_Realm;

        public FSAssetConnector(IConfigSource config) : base(config)
        {
            m_FsckProgram = string.Empty;

            MainConsole.Instance.Commands.AddCommand("fs", false,
                    "show assets", "show assets", "Show asset stats",
                    HandleShowAssets);
            MainConsole.Instance.Commands.AddCommand("fs", false,
                    "show digest", "show digest <ID>", "Show asset digest",
                    HandleShowDigest);
            MainConsole.Instance.Commands.AddCommand("fs", false,
                    "delete asset", "delete asset <ID>",
                    "Delete asset from database",
                    HandleDeleteAsset);
            MainConsole.Instance.Commands.AddCommand("fs", false,
                    "import", "import <conn> <table> [<start> <count>]",
                    "Import legacy assets",
                    HandleImportAssets);
            MainConsole.Instance.Commands.AddCommand("fs", false,
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

            m_Realm = assetConfig.GetString("Realm", "fsassets");

            m_DataConnector = new FSAssetConnectorData(m_ConnectionString, m_Realm);
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

            m_SpoolDirectory = assetConfig.GetString("SpoolDirectory", "/tmp");

            string spoolTmp = Path.Combine(m_SpoolDirectory, "spool");

            Directory.CreateDirectory(spoolTmp);

            m_FSBase = assetConfig.GetString("BaseDirectory", String.Empty);
            if (m_FSBase == String.Empty)
            {
                m_log.ErrorFormat("[ASSET]: BaseDirectory not specified");
                throw new Exception("Configuration error");
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
            m_log.Info("[ASSET]: FS asset service enabled");

            m_WriterThread = new Thread(Writer);
            m_WriterThread.Start();
            m_StatsThread = new Thread(Stats);
            m_StatsThread.Start();
        }

        private void Stats()
        {
            while (true)
            {
                Thread.Sleep(60000);
                 
                lock (m_statsLock)
                {
                    if (m_readCount > 0)
                    {
                        double avg = (double)m_readTicks / (double)m_readCount;
//                        if (avg > 10000)
//                            Environment.Exit(0);
                        m_log.InfoFormat("[ASSET]: Read stats: {0} files, {1} ticks, avg {2:F2}, missing {3}", m_readCount, m_readTicks, (double)m_readTicks / (double)m_readCount, m_missingAssets);
                    }
                    m_readCount = 0;
                    m_readTicks = 0;
                    m_missingAssets = 0;
                }
            }
        }

        private void Writer()
        {
            m_log.Info("[ASSET]: Writer started");

            while (true)
            {
                string[] files = Directory.GetFiles(m_SpoolDirectory);

                if (files.Length > 0)
                {
                    int tickCount = Environment.TickCount;
                    for (int i = 0 ; i < files.Length ; i++)
                    {
                        string hash = Path.GetFileNameWithoutExtension(files[i]);
                        string s = HashToFile(hash);
                        string diskFile = Path.Combine(m_FSBase, s);

                        Directory.CreateDirectory(Path.GetDirectoryName(diskFile));
                        try
                        {
                            File.Move(files[i], diskFile);
                        }
                        catch(System.IO.IOException e)
                        {
                            if (e.Message.StartsWith("Win32 IO returned ERROR_ALREADY_EXISTS"))
                                File.Delete(files[i]);
                            else
                                throw;
                        }
                    }
                    int totalTicks = System.Environment.TickCount - tickCount;
                    if (totalTicks > 0) // Wrap?
                    {
                        m_log.InfoFormat("[ASSET]: Write cycle complete, {0} files, {1} ticks, avg {2:F2}", files.Length, totalTicks, (double)totalTicks / (double)files.Length);
                    }
                }

                Thread.Sleep(1000);
            }
        }

        string GetSHA1Hash(byte[] data)
        {
            byte[] hash = SHA1.ComputeHash(data);

            return BitConverter.ToString(hash).Replace("-", String.Empty);
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

        public AssetBase Get(string id)
        {
            string hash;

            return Get(id, out hash);
        }

        private AssetBase Get(string id, out string sha)
        {
            string hash = string.Empty;

            int startTime = System.Environment.TickCount;
            AssetMetadata metadata;

            lock (m_readLock)
            {
                metadata = m_DataConnector.Get(id, out hash);
            }

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
//                    m_log.InfoFormat("[ASSET]: Asset {0} not found", id);
                    m_missingAssets++;
                }
                return asset;
            }
            AssetBase newAsset = new AssetBase();
            newAsset.Metadata = metadata;
            try
            {
                newAsset.Data = GetFsData(hash);
                if (newAsset.Data.Length == 0)
                {
                    m_log.InfoFormat("[ASSET]: Asset {0}, hash {1} not found in FS", id, hash);
                }

                lock (m_statsLock)
                {
                    m_readTicks += Environment.TickCount - startTime;
                    m_readCount++;
                }
                return newAsset;
            }
            catch (Exception exception)
            {
                m_log.Error(exception.ToString());
                Thread.Sleep(5000);
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

            return GetFsData(hash);
        }

        public bool Get(string id, Object sender, AssetRetrieved handler)
        {
            AssetBase asset = Get(id);

            handler(id, sender, asset);

            return true;
        }

        public byte[] GetFsData(string hash)
        {
            string spoolFile = Path.Combine(m_SpoolDirectory, hash + ".asset");

            if (File.Exists(spoolFile))
            {
                try
                {
                    byte[] content = File.ReadAllBytes(spoolFile);

                    return content;
                }
                catch
                {
                }
            }

            string file = HashToFile(hash);
            string diskFile = Path.Combine(m_FSBase, file);

            if (File.Exists(diskFile))
            {
                try
                {
                    byte[] content = File.ReadAllBytes(diskFile);

                    return content;
                }
                catch
                {
                }
            }
            return null;

        }

        public string Store(AssetBase asset)
        {
            return Store(asset, false);
        }

        private string Store(AssetBase asset, bool force)
        {
            int tickCount = Environment.TickCount;
            string hash = GetSHA1Hash(asset.Data);

            string tempFile = Path.Combine(Path.Combine(m_SpoolDirectory, "spool"), hash + ".asset");
            string finalFile = Path.Combine(m_SpoolDirectory, hash + ".asset");

            if (!File.Exists(finalFile))
            {
                FileStream fs = File.Create(tempFile);

                fs.Write(asset.Data, 0, asset.Data.Length);

                fs.Close();

                File.Move(tempFile, finalFile);
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
            return false;

//            string oldhash;
//            AssetMetadata meta = m_DataConnector.Get(id, out oldhash);
//
//            if (meta == null)
//                return false;
//
//            AssetBase asset = new AssetBase();
//            asset.Metadata = meta;
//            asset.Data = data;
//
//            Store(asset);
//
//            return true;
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
            MainConsole.Instance.Output(String.Format("FS file: {0}", HashToFile(hash)));

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
