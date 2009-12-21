/////////////////////////////////////////////////////////////
//
// Copyright (c)2009 Careminster Limited and Melanie Thielker
//
// All rights reserved
//


using System;
using System.Xml;
using System.Timers;
using System.IO;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Lifetime;
using System.Reflection;
using System.Collections.Generic;
using System.Security.Policy;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenSim.Region.ScriptEngine.Shared;
using OpenSim.Region.ScriptEngine.Shared.Api;
using OpenMetaverse.StructuredData;
using OpenSim.Region.CoreModules.Framework.EventQueue;
using Nini.Config;
using Mono.Addins;
using OpenMetaverse;
using log4net;
using OpenSim.Region.ScriptEngine.XMREngine.Loader;
using MMR;
using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;

[assembly: Addin("XMREngine", "0.1")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace OpenSim.Region.ScriptEngine.XMREngine
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "XMREngine")]
    public class XMREngine : INonSharedRegionModule, IScriptEngine,
            IScriptModule
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Scene m_Scene;
        private IConfigSource m_ConfigSource;
        private IConfig m_Config;
        private string m_ScriptBasePath;
        private bool m_Enabled = false;
        private Object m_CompileLock = new Object();
        private XMRSched m_Scheduler = null;
        private XMREvents m_Events = null;
        private AssemblyResolver m_AssemblyResolver = null;
        private Dictionary<UUID, XMRInstance> m_Instances =
                new Dictionary<UUID, XMRInstance>();
        private bool m_WakeUpFlag = false;
        private object m_WakeUpLock = new object();
        private Dictionary<UUID, int> m_Assemblies =
                new Dictionary<UUID, int>();
        private Dictionary<UUID, AppDomain> m_AppDomains =
                new Dictionary<UUID, AppDomain>();
        private Dictionary<UUID, List<UUID>> m_Objects =
                new Dictionary<UUID, List<UUID>>();
        private Dictionary<UUID, UUID> m_Partmap =
                new Dictionary<UUID, UUID>();

        private int m_MaintenanceInterval = 10;
        
        private Timer m_MaintenanceTimer;

        public XMREngine()
        {
        }

        public string Name
        {
            get { return "XMREngine"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void Initialise(IConfigSource config)
        {
            m_ConfigSource = config;

            if (config.Configs["XMREngine"] == null)
                m_Config = config.AddConfig("XMREngine");
            else
                m_Config = config.Configs["XMREngine"];

            m_Enabled = m_Config.GetBoolean("Enabled", false);

            if (!m_Enabled)
                return;

            m_log.Info("[XMREngine]: Enabled");

            m_MaintenanceInterval = m_Config.GetInt("MaintenanceInterval", 10);

            if (m_MaintenanceInterval > 0)
            {
                m_MaintenanceTimer = new Timer(m_MaintenanceInterval * 60000);
                m_MaintenanceTimer.Elapsed += DoMaintenance;
                m_MaintenanceTimer.Start();
            }

            MainConsole.Instance.Commands.AddCommand("xmr", false,
                    "xmr test",
                    "xmr test",
                    "Run current xmr test",
                    RunTest);
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_Scene = scene;

            m_Scene.RegisterModuleInterface<IScriptModule>(this);

            AppDomain.CurrentDomain.AssemblyResolve +=
                    m_AssemblyResolver.OnAssemblyResolve;

            m_ScriptBasePath = m_Config.GetString("ScriptBasePath",
                    Path.Combine(".", "ScriptData"));
            m_ScriptBasePath = Path.Combine(m_ScriptBasePath,
                    scene.RegionInfo.RegionID.ToString());

            m_AssemblyResolver = new AssemblyResolver(m_ScriptBasePath);

            Directory.CreateDirectory(m_ScriptBasePath);

            m_Scene.EventManager.OnRezScript += OnRezScript;
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            if (m_Scheduler != null)
                m_Scheduler.Stop();
            m_Scheduler = null;

            m_Events = null;

            m_Scene.EventManager.OnRezScript -= OnRezScript;
            m_Scene.EventManager.OnRemoveScript -= OnRemoveScript;
            m_Scene.EventManager.OnScriptReset -= OnScriptReset;
            m_Scene.EventManager.OnStartScript -= OnStartScript;
            m_Scene.EventManager.OnStopScript -= OnStopScript;
            m_Scene.EventManager.OnGetScriptRunning -= OnGetScriptRunning;
            m_Scene.EventManager.OnShutdown -= OnShutdown;

            m_Enabled = false;
            m_Scene = null;
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_Scene.EventManager.OnRemoveScript += OnRemoveScript;
            m_Scene.EventManager.OnScriptReset += OnScriptReset;
            m_Scene.EventManager.OnStartScript += OnStartScript;
            m_Scene.EventManager.OnStopScript += OnStopScript;
            m_Scene.EventManager.OnGetScriptRunning += OnGetScriptRunning;
            m_Scene.EventManager.OnShutdown += OnShutdown;

            m_Scheduler = new XMRSched(this);
            m_Events = new XMREvents(this);

            /////////////// Test
        }

        public void Close()
        {
        }

        private void RunTest(string module, string[] args)
        {
            if (args.Length < 3)
                return;

            switch(args[2])
            {
            case "gc":
                GC.Collect();
                break;
            case "backup":
                foreach (XMRInstance ins in m_Instances.Values)
                {
                    Byte[] data = ins.GetSnapshot();
                    FileStream fs = File.Create("/tmp/test.dump");
                    fs.Write(data, 0, data.Length);
                    fs.Close();
                }
                break;
            }
        }

        private void ErrorHandler(Token token, string message)
        {
            if (token != null)
            {
                m_log.DebugFormat("[MMR]: ({0},{1}) Error: {2}", token.line,
                        token.posn, message);
            }
            else
            {
                m_log.DebugFormat("[MMR]: Error compiling, see exception in log");
            }
        }

        // Not required when not using IScriptInstance
        //
        public IScriptWorkItem QueueEventHandler(object parms)
        {
            return null;
        }

        public Scene World
        {
            get { return m_Scene; }
        }

        public IScriptModule ScriptModule
        {
            get { return this; }
        }

        public event ScriptRemoved OnScriptRemoved;
        public event ObjectRemoved OnObjectRemoved;

        public bool PostScriptEvent(UUID itemID, EventParams parms)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.ContainsKey(itemID))
                    return false;

                instance = m_Instances[itemID];
            }

            instance.PostEvent(parms);

            return true;
        }

        public bool PostObjectEvent(uint localID, EventParams parms)
        {
            SceneObjectPart part = m_Scene.GetSceneObjectPart(localID);

            if (part == null)
                return false;

            if (!m_Objects.ContainsKey(part.UUID))
                return false;

            List<UUID> l = m_Objects[part.UUID];

            bool success = false;

            foreach (UUID id in l)
            {
                if (PostScriptEvent(id, parms))
                    success = true;
            }
                
            return success;
        }

        public DetectParams GetDetectParams(UUID item, int number)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.ContainsKey(item))
                    return null;

                instance = m_Instances[item];
            }

            return instance.GetDetectParams(number);
        }

        public void SetMinEventDelay(UUID itemID, double delay)
        {
        }

        public int GetStartParameter(UUID itemID)
        {
            return 0;
        }

        // This is the "set running" method
        //
        public void SetScriptState(UUID itemID, bool state)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.ContainsKey(itemID))
                    return;

                instance = m_Instances[itemID];

            }

            instance.Running = state;
        }

        // Control display of the "running" checkbox
        //
        public bool GetScriptState(UUID itemID)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.ContainsKey(itemID))
                    return false;

                instance = m_Instances[itemID];

            }

            return instance.Running;
        }

        public void SetState(UUID itemID, string newState)
        {
        }

        public void ApiResetScript(UUID itemID)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.ContainsKey(itemID))
                    return;

                instance = m_Instances[itemID];
            }

            instance.ApiReset();
        }

        public void ResetScript(UUID itemID)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.ContainsKey(itemID))
                    return;

                instance = m_Instances[itemID];
            }

            instance.Suspend();
            instance.Reset();
            instance.Resume();
        }

        public IConfig Config
        {
            get { return m_Config; }
        }

        public IConfigSource ConfigSource
        {
            get { return m_ConfigSource; }
        }

        public string ScriptEngineName
        {
            get { return "XMREngine"; }
        }

        public IScriptApi GetApi(UUID itemID, string name)
        {
            return null;
        }

        private XmlElement GetExecutionState(XMRInstance instance, XmlDocument doc)
        {
            SceneObjectPart part = instance.SceneObject;

            TaskInventoryItem item =
                    part.Inventory.GetInventoryItem(instance.ItemID);

            Byte[] data = instance.GetSnapshot();

            string state = Convert.ToBase64String(data);

            XmlElement scriptStateN = doc.CreateElement("", "ScriptState", "");

            XmlElement runningN = doc.CreateElement("", "Running", "");
            runningN.AppendChild(doc.CreateTextNode(instance.Running.ToString()));

            scriptStateN.AppendChild(runningN);

            DetectParams[] detect = instance.DetectParams;

            if (detect != null)
            {
                XmlElement detectN = doc.CreateElement("", "Detect", "");
                scriptStateN.AppendChild(detectN);

                foreach (DetectParams d in detect)
                {
                    XmlElement detectParamsN = doc.CreateElement("", "DetectParams", "");
                    XmlAttribute pos = doc.CreateAttribute("", "pos", "");
                    pos.Value = d.OffsetPos.ToString();
                    detectParamsN.Attributes.Append(pos);

                    XmlAttribute d_linkNum = doc.CreateAttribute("",
                            "linkNum", "");
                    d_linkNum.Value = d.LinkNum.ToString();
                    detectParamsN.Attributes.Append(d_linkNum);

                    XmlAttribute d_group = doc.CreateAttribute("",
                            "group", "");
                    d_group.Value = d.Group.ToString();
                    detectParamsN.Attributes.Append(d_group);

                    XmlAttribute d_name = doc.CreateAttribute("",
                            "name", "");
                    d_name.Value = d.Name.ToString();
                    detectParamsN.Attributes.Append(d_name);

                    XmlAttribute d_owner = doc.CreateAttribute("",
                            "owner", "");
                    d_owner.Value = d.Owner.ToString();
                    detectParamsN.Attributes.Append(d_owner);

                    XmlAttribute d_position = doc.CreateAttribute("",
                            "position", "");
                    d_position.Value = d.Position.ToString();
                    detectParamsN.Attributes.Append(d_position);

                    XmlAttribute d_rotation = doc.CreateAttribute("",
                            "rotation", "");
                    d_rotation.Value = d.Rotation.ToString();
                    detectParamsN.Attributes.Append(d_rotation);

                    XmlAttribute d_type = doc.CreateAttribute("",
                            "type", "");
                    d_type.Value = d.Type.ToString();
                    detectParamsN.Attributes.Append(d_type);

                    XmlAttribute d_velocity = doc.CreateAttribute("",
                            "velocity", "");
                    d_velocity.Value = d.Velocity.ToString();
                    detectParamsN.Attributes.Append(d_velocity);

                    detectParamsN.AppendChild(
                        doc.CreateTextNode(d.Key.ToString()));

                    detectN.AppendChild(detectParamsN);
                }
            }

            XmlElement snapshotN = doc.CreateElement("", "Snapshot", "");
            snapshotN.AppendChild(doc.CreateTextNode(state));

            scriptStateN.AppendChild(snapshotN);

            // TODO: Plugin data

            if (item != null)
            {
                XmlNode permissionsN = doc.CreateElement("", "Permissions", "");
                scriptStateN.AppendChild(permissionsN);

                XmlAttribute granterA = doc.CreateAttribute("", "granter", "");
                granterA.Value = item.PermsGranter.ToString();
                permissionsN.Attributes.Append(granterA);

                XmlAttribute maskA = doc.CreateAttribute("", "mask", "");
                maskA.Value = item.PermsMask.ToString();
                permissionsN.Attributes.Append(maskA);
            }

            return scriptStateN;
        }

        public string GetXMLState(UUID itemID)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.ContainsKey(itemID))
                    return String.Empty;

                instance = m_Instances[itemID];
            }

            // Script is being migrated out. We will never unsuspend again
            //
            instance.Suspend();

            XmlDocument doc = new XmlDocument();

            XmlElement stateN = doc.CreateElement("", "State", "");

            XmlAttribute uuidA = doc.CreateAttribute("", "UUID", "");
            uuidA.Value = itemID.ToString();
            stateN.Attributes.Append(uuidA);

            XmlAttribute engineA = doc.CreateAttribute("", "Engine", "");
            engineA.Value = ScriptEngineName;
            stateN.Attributes.Append(engineA);

            doc.AppendChild(stateN);

            XmlElement scriptStateN = GetExecutionState(instance, doc);

            if (scriptStateN == null)
                return String.Empty;

            stateN.AppendChild(scriptStateN);

            SceneObjectPart part = instance.SceneObject;

            TaskInventoryItem item =
                    part.Inventory.GetInventoryItem(itemID);

            if (item == null)
                return String.Empty;

            UUID assetID = item.AssetID;

            XmlAttribute assetA = doc.CreateAttribute("", "Asset", "");
            assetA.Value = assetID.ToString();
            stateN.Attributes.Append(assetA);


            string assemblyPath = Path.Combine(m_ScriptBasePath,
                    item.AssetID.ToString() + ".dll");

            if (File.Exists(assemblyPath))
            {
                FileInfo fi = new FileInfo(assemblyPath);
                if (fi == null)
                    return String.Empty;

                Byte[] assemblyData = new Byte[fi.Length];

                try
                {
                    FileStream fs = File.Open(assemblyPath, FileMode.Open, FileAccess.Read);
                    fs.Read(assemblyData, 0, assemblyData.Length);
                    fs.Close();
                }
                catch (Exception e)
                {
                    m_log.Debug("[XMREngine]: Unable to open script assembly: " + e.ToString());
                    return String.Empty;
                }

                XmlElement assemN = doc.CreateElement("", "Assembly", "");
                assemN.AppendChild(doc.CreateTextNode(Convert.ToBase64String(assemblyData)));

                stateN.AppendChild(assemN);
            }

            return doc.OuterXml;
        }

        public bool SetXMLState(UUID itemID, string xml)
        {
            XmlDocument doc = new XmlDocument();

            try
            {
                doc.LoadXml(xml);
            }
            catch
            {
                return false;
            }

            XmlElement stateN = (XmlElement)doc.SelectSingleNode("State");
            if (stateN == null)
                return false;

            if (stateN.GetAttribute("Engine") != ScriptEngineName)
                return false;

            XmlElement scriptStateN = (XmlElement)stateN.SelectSingleNode("ScriptState");
            if (scriptStateN == null)
                return false;

            string statePath = Path.Combine(m_ScriptBasePath,
                    itemID.ToString() + ".state");

            FileStream ss = File.Create(statePath);
            StreamWriter sw = new StreamWriter(ss);
            sw.Write(scriptStateN.OuterXml);
            sw.Close();
            ss.Close();

            UUID assetID = new UUID(stateN.GetAttribute("Asset"));

            string assemblyPath = Path.Combine(m_ScriptBasePath,
                    assetID.ToString() + ".dll");

            if (!File.Exists(assemblyPath))
            {
                XmlElement assemN = (XmlElement)stateN.SelectSingleNode("Assembly");
                if (assemN != null)
                {
                    Byte[] assemData = Convert.FromBase64String(assemN.InnerText);

                    FileStream fs = File.Create(assemblyPath);
                    fs.Write(assemData, 0, assemData.Length);
                    fs.Close();
                }
            }
            return false;
        }

        public bool PostScriptEvent(UUID itemID, string name, Object[] p)
        {
            if (!m_Enabled)
                return false;

            Object[] lsl_p = new Object[p.Length];
            for (int i = 0; i < p.Length ; i++)
            {
                if (p[i] is Vector3)
                    lsl_p[i] = new LSL_Types.Vector3(((Vector3)p[i]).X, ((Vector3)p[i]).Y, ((Vector3)p[i]).Z);
                else if (p[i] is Quaternion)
                    lsl_p[i] = new LSL_Types.Quaternion(((Quaternion)p[i]).X, ((Quaternion)p[i]).Y, ((Quaternion)p[i]).Z, ((Quaternion)p[i]).W);
                else
                    lsl_p[i] = p[i];
            }

            return PostScriptEvent(itemID, new EventParams(name, lsl_p, new DetectParams[0]));
        }

        public bool PostObjectEvent(UUID itemID, string name, Object[] p)
        {
            if (!m_Enabled)
                return false;

            SceneObjectPart part = m_Scene.GetSceneObjectPart(itemID);
            if (part == null)
                return false;

            Object[] lsl_p = new Object[p.Length];
            for (int i = 0; i < p.Length ; i++)
            {
                if (p[i] is Vector3)
                    lsl_p[i] = new LSL_Types.Vector3(((Vector3)p[i]).X, ((Vector3)p[i]).Y, ((Vector3)p[i]).Z);
                else if (p[i] is Quaternion)
                    lsl_p[i] = new LSL_Types.Quaternion(((Quaternion)p[i]).X, ((Quaternion)p[i]).Y, ((Quaternion)p[i]).Z, ((Quaternion)p[i]).W);
                else
                    lsl_p[i] = p[i];
            }

            return PostObjectEvent(part.LocalId, new EventParams(name, lsl_p, new DetectParams[0]));
        }

        public void OnRezScript(uint localID, UUID itemID, string script,
                int startParam, bool postOnRez, string engine, int stateSource)
        {
            if (script.StartsWith("//MRM:"))
                return;

            List<IScriptModule> engines =
                    new List<IScriptModule>(
                    m_Scene.RequestModuleInterfaces<IScriptModule>());

            List<string> names = new List<string>();
            foreach (IScriptModule m in engines)
                names.Add(m.ScriptEngineName);

            SceneObjectPart part =
                    m_Scene.GetSceneObjectPart(localID);

            TaskInventoryItem item =
                    part.Inventory.GetInventoryItem(itemID);

            int lineEnd = script.IndexOf('\n');

            if (lineEnd > 1)
            {
                string firstline = script.Substring(0, lineEnd).Trim();

                int colon = firstline.IndexOf(':');
                if (firstline.Length > 2 && firstline.Substring(0, 2) == "//" &&
                        colon != -1)
                {
                    string engineName = firstline.Substring(2, colon-2);

                    if (names.Contains(engineName))
                    {
                        engine = engineName;
                        script = "//" + script.Substring(script.IndexOf(':')+1);
                    }
                    else
                    {
                        if (engine == ScriptEngineName)
                        {
                            ScenePresence presence =
                                    m_Scene.GetScenePresence(item.OwnerID);

                            if (presence != null)
                            {
                                presence.ControllingClient.SendAgentAlertMessage(
                                        "Selected engine unavailable. "+
                                        "Running script on "+
                                        ScriptEngineName,
                                        false);
                            }
                        }
                    }
                }
            }

            if (engine != ScriptEngineName)
                return;

            m_log.DebugFormat("[XMREngine]: Running script {0}, asset {1}",
                    item.Name, item.AssetID);

            string outputName = Path.Combine(m_ScriptBasePath,
                    item.AssetID.ToString() + ".dll");

            lock (m_CompileLock)
            {
                if (!File.Exists(outputName))
                {
                    if (script == String.Empty)
                    {
                        m_log.ErrorFormat("[XMREngine]: Compile of asset {0} was requested but source text is not present and no assembly was found", item.AssetID.ToString());
                        return;
                    }

                    m_log.DebugFormat("[XMREngine]: Compiling script {0}",
                            item.AssetID);

                    string debugFileName = String.Empty;

                    if (m_Config.GetBoolean("WriteScriptSourceToDebugFile", false))
                        debugFileName = "/tmp/" + item.AssetID.ToString() + ".lsl";

                    try
                    {
                        ScriptCompile.Compile(script, outputName,
                                UUID.Zero.ToString(), debugFileName, ErrorHandler);
                    }
                    catch (Exception e)
                    {
                        m_log.Debug("[XMREngine]: Exception compiling script: " + e.ToString());
                    }

                    if (!File.Exists(outputName))
                        return;
                }
            }

            lock (m_Assemblies)
            {
                if (m_Assemblies.ContainsKey(item.AssetID))
                {
                    m_Assemblies[item.AssetID]++;
                }
                else
                {
                    m_Assemblies[item.AssetID] = 1;

                    AppDomainSetup appSetup = new AppDomainSetup();
                    Evidence baseEvidence = AppDomain.CurrentDomain.Evidence;
                    Evidence evidence = new Evidence(baseEvidence);

                    m_AppDomains[item.AssetID] = AppDomain.CreateDomain(
                            m_Scene.RegionInfo.RegionID.ToString(),
                            evidence, appSetup);
            
                    m_AppDomains[item.AssetID].AssemblyResolve +=
                            m_AssemblyResolver.OnAssemblyResolve;

                }
                m_log.DebugFormat("[XMREngine]: Script loaded, asset count {0}",
                        m_Assemblies[item.AssetID]);
            }

            XMRLoader loader =
                    (XMRLoader)m_AppDomains[item.AssetID].
                    CreateInstanceAndUnwrap(
                    "OpenSim.Region.ScriptEngine.XMREngine.Loader",
                    "OpenSim.Region.ScriptEngine.XMREngine.Loader.XMRLoader");

            lock (m_Instances)
            {
                try
                {
                    m_Instances[itemID] = new XMRInstance(loader, this, part,
                            part.LocalId, itemID, outputName);

                    string statePath = Path.Combine(m_ScriptBasePath,
                            itemID.ToString() + ".state");

                    if (File.Exists(statePath))
                    {
                        m_Instances[itemID].Suspend();
                        
                        FileStream fs = File.Open(statePath, FileMode.Open, FileAccess.Read);
                        StreamReader ss = new StreamReader(fs);
                        string xml = ss.ReadToEnd();
                        ss.Close();
                        fs.Close();

                        XmlDocument doc = new XmlDocument();

                        doc.LoadXml(xml);

                        XmlElement scriptStateN = (XmlElement)doc.SelectSingleNode("ScriptState");
                        if (scriptStateN == null)
                        {
                            m_log.Debug("[XMREngine]: Malformed XML: " + xml);
                            throw new Exception("Malformed XML");
                        }

                        XmlElement runningN = (XmlElement)scriptStateN.SelectSingleNode("Running");
                        bool running = bool.Parse(runningN.InnerText);
                        m_Instances[itemID].Running = running;

                        XmlElement permissionsN = (XmlElement)scriptStateN.SelectSingleNode("Permissions");
                        item.PermsGranter = new UUID(permissionsN.GetAttribute("Granter"));
                        item.PermsMask = Convert.ToInt32(permissionsN.GetAttribute("Mask"));

                        m_Instances[itemID].Resume();

                        m_log.Debug("[XMREngine]: Found state information");
                    }
                    else
                    {
                        WriteStateFile(itemID, m_Instances[itemID]);

                        m_Instances[itemID].PostEvent(new EventParams("state_entry", new Object[0], new DetectParams[0]));
                    }

                    WakeUp();

                    List<UUID> l;

                    if (m_Objects.ContainsKey(part.UUID))
                    {
                        l = m_Objects[part.UUID];
                    }
                    else
                    {
                        l = new List<UUID>();
                        m_Objects[part.UUID] = l;
                    }

                    if (!l.Contains(itemID))
                        l.Add(itemID);

                    m_Partmap[itemID] = part.UUID;
                }
                catch (Exception e)
                {
                    m_log.Error("[XMREngine]: Script load failed, restart region" + e.ToString());
                }
            }
        }

        public void OnRemoveScript(uint localID, UUID itemID)
        {
            SceneObjectPart part =
                    m_Scene.GetSceneObjectPart(localID);

            lock (m_Instances)
            {
                if (m_Instances.ContainsKey(itemID))
                {
                    m_Instances[itemID].Suspend();

                    m_Instances[itemID].Dispose();
                    m_Instances.Remove(itemID);

                    if (m_Objects.ContainsKey(part.UUID))
                    {
                        List<UUID> l = m_Objects[part.UUID];
                        l.Remove(itemID);
                        if (l.Count == 0)
                        {
                            m_Objects.Remove(part.UUID);
                        }
                    }
                    m_Partmap.Remove(itemID);
                }
                else
                {
                    return;
                }
            }

            TaskInventoryItem item =
                    part.Inventory.GetInventoryItem(itemID);

            lock (m_Assemblies)
            {
                if (!m_Assemblies.ContainsKey(item.AssetID))
                    return;

                m_Assemblies[item.AssetID]--;

                m_log.DebugFormat("[XMREngine]: Unloading script, asset count now {0}", m_Assemblies[item.AssetID]);
                if (m_Assemblies[item.AssetID] < 1)
                {
                    m_log.Debug("[XMREngine]: Unloading app domain");
                    AppDomain.Unload(m_AppDomains[item.AssetID]);
                    m_AppDomains.Remove(item.AssetID);

                    m_Assemblies.Remove(item.AssetID);

                    string assemblyPath = Path.Combine(m_ScriptBasePath,
                            item.AssetID + ".dll");

                    File.Delete(assemblyPath);
                    File.Delete(assemblyPath + ".mdb");
                }
            }

            string statePath = Path.Combine(m_ScriptBasePath,
                    itemID + ".state");
            File.Delete(statePath);
        }

        public void OnScriptReset(uint localID, UUID itemID)
        {
            ResetScript(itemID);
        }

        public void OnStartScript(uint localID, UUID itemID)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.ContainsKey(itemID))
                    return;

                instance = m_Instances[itemID];
            }

            instance.Running = true;
        }

        public void OnStopScript(uint localID, UUID itemID)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.ContainsKey(itemID))
                    return;

            }

            instance = m_Instances[itemID];

            instance.Running = false;
        }

        public void OnGetScriptRunning(IClientAPI controllingClient,
                UUID objectID, UUID itemID)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.ContainsKey(itemID))
                    return;

                instance = m_Instances[itemID];

            }

            IEventQueue eq = World.RequestModuleInterface<IEventQueue>();
            if (eq == null)
            {
                controllingClient.SendScriptRunningReply(objectID, itemID,
                        instance.Running);
            }
            else
            {
                eq.Enqueue(EventQueueHelper.ScriptRunningReplyEvent(objectID,
                        itemID, instance.Running, true),
                        controllingClient.AgentId);
            }
        }

        public void OnShutdown()
        {
        }

        public void WakeUp()
        {
            lock (m_WakeUpLock)
            {
                m_WakeUpFlag = true;
                System.Threading.Monitor.PulseAll(m_WakeUpLock);
            }
        }

        // Run all scripts through one cycle.
        // Block until ready to do it all again.
        //
        public void RunOneCycle()
        {
            List<XMRInstance> instances = null;

            m_WakeUpFlag = false;
            lock (m_Instances)
            {
                instances = new List<XMRInstance>(m_Instances.Values);
            }

            DateTime earliest = DateTime.MaxValue;
            foreach (XMRInstance ins in instances)
            {
                if (ins != null)
                {
                    DateTime suspendUntil = ins.RunOne();
                    if (earliest > suspendUntil)
                    {
                        earliest = suspendUntil;
                    }
                }
            }

            DateTime now = DateTime.UtcNow;
            if (earliest > now)
            {
                lock (m_WakeUpLock)
                {
                    if (!m_WakeUpFlag)
                    {
                        TimeSpan deltaTS = earliest - now;
                        int deltaMS = Int32.MaxValue;
                        if (deltaTS.TotalMilliseconds < Int32.MaxValue)
                        {
                            deltaMS = (int)deltaTS.TotalMilliseconds;
                        }
                        System.Threading.Monitor.Wait(m_WakeUpLock, deltaMS);
                    }
                }
            }
        }

        public void Suspend(UUID itemID, int ms)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.ContainsKey(itemID))
                    return;

                instance = m_Instances[itemID];

            }

            instance.Suspend(ms);
        }

        public void Die(UUID itemID)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.ContainsKey(itemID))
                    return;

                instance = m_Instances[itemID];

            }

            instance.Die();
        }

        private void DoMaintenance(object source, ElapsedEventArgs e)
        {
            Dictionary<UUID, XMRInstance> instances;
            lock (m_Instances)
            {
                instances = new Dictionary<UUID, XMRInstance>(m_Instances);
            }

            foreach (KeyValuePair<UUID, XMRInstance> kvp in instances)
            {
                XMRInstance ins = kvp.Value;
                UUID itemID = kvp.Key;

                WriteStateFile(itemID, ins);
            }
        }

        private void WriteStateFile(UUID itemID, XMRInstance ins)
        {
            XmlDocument doc = new XmlDocument();

            XmlElement scriptStateN = GetExecutionState(ins, doc);
            
            if (scriptStateN != null)
            {
                doc.AppendChild(scriptStateN);

                string statepath = Path.Combine(m_ScriptBasePath,
                        itemID.ToString() + ".state");

                FileStream fs = File.Create(statepath);
                StreamWriter sw = new StreamWriter(fs);
                sw.Write(doc.OuterXml);
                sw.Close();
                fs.Close();
            }
        }
    }

    [Serializable]
    public class AssemblyResolver
    {
        private string m_ScriptBasePath;

        public AssemblyResolver(string path)
        {
            m_ScriptBasePath = path;
        }

        public Assembly OnAssemblyResolve(object sender,
                                          ResolveEventArgs args)
        {
            if (!(sender is System.AppDomain))
                return null;

            string[] pathList = new string[] {"bin", m_ScriptBasePath};

            string assemblyName = args.Name;
            if (assemblyName.IndexOf(",") != -1)
                assemblyName = args.Name.Substring(0, args.Name.IndexOf(","));

            foreach (string s in pathList)
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(),
                                           Path.Combine(s, assemblyName))+".dll";

                if (File.Exists(path))
                    return Assembly.LoadFrom(path);
            }

            return null;
        }
    }
}
