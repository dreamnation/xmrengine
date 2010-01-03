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
using System.Collections;
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

        private UUID m_CurrentCompileItem;
        private Dictionary<UUID, ArrayList> m_CompilerErrors =
                new Dictionary<UUID, ArrayList>();

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

            DoMaintenance(null, null);

            if (m_Scheduler != null)
            {
                m_Scheduler.Stop();
                WakeUp();
                m_Scheduler.Shutdown();
            }
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
            if (!m_CompilerErrors.ContainsKey(m_CurrentCompileItem))
                m_CompilerErrors[m_CurrentCompileItem] = new ArrayList();
            if (token != null)
            {
                m_CompilerErrors[m_CurrentCompileItem].Add(
                        String.Format("({0},{1}) Error: {2}", token.line,
                        token.posn, message));
            }
            else
            {
                m_CompilerErrors[m_CurrentCompileItem].Add("Error compiling, see exception in log");
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
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.ContainsKey(itemID))
                    return 0;

                instance = m_Instances[itemID];

            }

            return instance.StartParam;
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

            XmlElement scriptStateN = doc.CreateElement("", "ScriptState", "");

            Byte[] data = instance.GetSnapshot();

            string state = Convert.ToBase64String(data);

            XmlElement snapshotN = doc.CreateElement("", "Snapshot", "");
            snapshotN.AppendChild(doc.CreateTextNode(state));

            scriptStateN.AppendChild(snapshotN);

            XmlElement runningN = doc.CreateElement("", "Running", "");
            runningN.AppendChild(doc.CreateTextNode(instance.Running.ToString()));
            scriptStateN.AppendChild(runningN);

            XmlElement startParamN = doc.CreateElement("", "StartParam", "");
            startParamN.AppendChild(doc.CreateTextNode(instance.StartParam.ToString()));

            scriptStateN.AppendChild(startParamN);

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

            Object[] pluginData = AsyncCommandManager.GetSerializationData(this,
                    instance.ItemID);

            XmlNode plugins = doc.CreateElement("", "Plugins", "");
            DumpList(doc, plugins, new LSL_Types.list(pluginData));

                scriptStateN.AppendChild(plugins);

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

            instance.Resume();

            if (scriptStateN == null)
                return String.Empty;

            stateN.AppendChild(scriptStateN);

            SceneObjectPart part = instance.SceneObject;

            UUID assetID = instance.AssetID;

            XmlAttribute assetA = doc.CreateAttribute("", "Asset", "");
            assetA.Value = assetID.ToString();
            stateN.Attributes.Append(assetA);


            string assemblyPath = Path.Combine(m_ScriptBasePath,
                    assetID.ToString() + ".dll");

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
//                    m_log.Debug("[XMREngine]: Unable to open script assembly: " + e.ToString());
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

            XmlAttribute assetA = doc.CreateAttribute("", "Asset", "");
            assetA.Value = stateN.GetAttribute("Asset");
            scriptStateN.Attributes.Append(assetA);

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

//            m_log.DebugFormat("[XMREngine]: Testing for presence of {0}",
//                    outputName);

            lock (m_CompileLock)
            {
                m_CurrentCompileItem = itemID;

                if (!File.Exists(outputName))
                {
//                    m_log.DebugFormat("[XMREngine]: compiling {0}",
//                            outputName);
                    if (!TryToCompile(script, outputName, item, part))
                    {
                        return;
                    }
                }

                if (!TryToLoad(outputName, item, part, startParam, localID, postOnRez, stateSource))
                {
                    m_log.DebugFormat("[XMREngine]: attempting recompile {0}",
                            outputName);
                    File.Delete(outputName);
                    if (!TryToCompile(script, outputName, item, part))
                    {
                        m_log.DebugFormat("[XMREngine]: recompile failed {0}",
                               outputName);
                        return;
                    }
                    m_log.DebugFormat("[XMREngine]: attempting reload {0}",
                            outputName);
                    if (!TryToLoad(outputName, item, part, startParam, localID, postOnRez, stateSource))
                    {
                        m_log.DebugFormat("[XMREngine]: reload failed {0}",
                                outputName);
                        return;
                    }
                    m_log.DebugFormat("[XMREngine]: reload successful {0}",
                            outputName);
                }
            }
        }

        private bool TryToCompile(string script, 
                                  string outputName, 
                                  TaskInventoryItem item,
                                  SceneObjectPart part)
        {
            if (script == String.Empty)
            {
                m_log.ErrorFormat("[XMREngine]: Compile of asset {0} was requested but source text is not present and no assembly was found", item.AssetID.ToString());
                return false;
            }

//            m_log.DebugFormat("[XMREngine]: Compiling script {0}",
//                    item.AssetID);

            string debugFileName = String.Empty;

            if (m_Config.GetBoolean("WriteScriptSourceToDebugFile", false))
                debugFileName = "/tmp/" + item.AssetID.ToString() + ".lsl";

            try
            {
                ScriptCompile.Compile(script, outputName,
                        item.AssetID.ToString(), debugFileName, ErrorHandler);
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[XMREngine]: Exception compiling script: {0}:{1} ({2}) " + e.ToString(), part.Name, item.Name, item.AssetID);
                File.Delete(outputName);
                return false;
            }

            return File.Exists(outputName);
        }

        //  TryToLoad()
        //      under lock do
        //          if already have an app domain for the script,
        //              increment reference count
        //          else
        //              create new app domain for the script
        //          appDomain = script's app domain
        //      try
        //          create loader instance
        //          create script instance
        //          if state XML file exists for the asset,
        //              extract <ScriptState> element
        //          if <ScriptState.Asset> matches assetID,
        //              restore state from <ScriptState.Snapshot>
        //          ....
        //      catch
        //          output error message
        //          under lock do
        //              decrement refcount
        //              if zero, unload app domain and delete script .DLL
        //
        private bool TryToLoad(string outputName,
                               TaskInventoryItem item,
                               SceneObjectPart part,
                               int startParam,
                               uint localID,
                               bool postOnRez,
                               int stateSource)
        {
            AppDomain appDomain;

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
//                m_log.DebugFormat("[XMREngine]: Created AppDomain, asset count {0}",
//                        m_Assemblies[item.AssetID]);
                appDomain = m_AppDomains[item.AssetID];
            }


            try
            {
                XMRLoader loader =
                        (XMRLoader)appDomain.CreateInstanceAndUnwrap(
                        "OpenSim.Region.ScriptEngine.XMREngine.Loader",
                        "OpenSim.Region.ScriptEngine.XMREngine.Loader.XMRLoader");

//                m_log.DebugFormat("[XMREngine]: Created loader for {0}",
//                        outputName);

                XMRInstance instance = new XMRInstance(loader, this, part,
                        part.LocalId, m_CurrentCompileItem, item.AssetID,
                        outputName);

                loader.StateChange = instance.StateChange;

//                m_log.DebugFormat("[XMREngine]: Loaded assembly {0}",
//                        outputName);

                instance.StartParam = startParam;

                string statePath = Path.Combine(m_ScriptBasePath,
                        m_CurrentCompileItem.ToString() + ".state");

                XmlDocument doc = null;
                XmlElement scriptStateN = null;

                if (File.Exists(statePath))
                {
                    instance.Suspend();
                    
                    FileStream fs = File.Open(statePath, FileMode.Open, FileAccess.Read);
                    StreamReader ss = new StreamReader(fs);
                    string xml = ss.ReadToEnd();
                    ss.Close();
                    fs.Close();

                    doc = new XmlDocument();

                    doc.LoadXml(xml);

                    scriptStateN = (XmlElement)doc.SelectSingleNode("ScriptState");
                }

                if (scriptStateN != null && scriptStateN.GetAttribute("Asset") == item.AssetID.ToString())
                {
                    XmlElement startParamN = (XmlElement)scriptStateN.SelectSingleNode("StartParam");
                    startParam = int.Parse(startParamN.InnerText);
                    instance.StartParam = startParam;

                    XmlElement runningN = (XmlElement)scriptStateN.SelectSingleNode("Running");
                    bool running = bool.Parse(runningN.InnerText);
                    instance.Running = running;

                    XmlElement permissionsN = (XmlElement)scriptStateN.SelectSingleNode("Permissions");
                    item.PermsGranter = new UUID(permissionsN.GetAttribute("granter"));
                    item.PermsMask = Convert.ToInt32(permissionsN.GetAttribute("mask"));
                    part.Inventory.UpdateInventoryItem(item);

                    XmlElement snapshotN = (XmlElement)scriptStateN.SelectSingleNode("Snapshot");
                    Byte[] data = Convert.FromBase64String(snapshotN.InnerText);

//                    ReplaceAssemblyPath(data, outputName);

                    try
                    {
                        instance.RestoreSnapshot(data);
                    }
                    catch (Exception e)
                    {
                        m_log.Error("[XMREngine]: error restoring script state " + item.AssetID);
                        m_log.Error("[XMREngine]: ... " + e.Message);

                        instance.PostEvent(new EventParams("state_entry", new Object[0], new DetectParams[0]));

                        if (postOnRez)
                        {
                            instance.PostEvent(new EventParams("on_rez",
                                    new Object[] {instance.StartParam}, new DetectParams[0]));
                        }
                    }

                    XmlElement detectedN = (XmlElement)scriptStateN.SelectSingleNode("Detect");
                    if (detectedN != null)
                    {
                        List<DetectParams> detected = new List<DetectParams>();

                        XmlNodeList detectL = detectedN.SelectNodes("DetectParams");
                        foreach (XmlNode det in detectL)
                        {
                            string vect =
                                    det.Attributes.GetNamedItem(
                                    "pos").Value;
                            LSL_Types.Vector3 v =
                                    new LSL_Types.Vector3(vect);

                            int d_linkNum=0;
                            UUID d_group = UUID.Zero;
                            string d_name = String.Empty;
                            UUID d_owner = UUID.Zero;
                            LSL_Types.Vector3 d_position =
                                new LSL_Types.Vector3();
                            LSL_Types.Quaternion d_rotation =
                                new LSL_Types.Quaternion();
                            int d_type = 0;
                            LSL_Types.Vector3 d_velocity =
                                new LSL_Types.Vector3();

                            string tmp;

                            tmp = det.Attributes.GetNamedItem(
                                    "linkNum").Value;
                            int.TryParse(tmp, out d_linkNum);

                            tmp = det.Attributes.GetNamedItem(
                                    "group").Value;
                            UUID.TryParse(tmp, out d_group);

                            d_name = det.Attributes.GetNamedItem(
                                    "name").Value;

                            tmp = det.Attributes.GetNamedItem(
                                    "owner").Value;
                            UUID.TryParse(tmp, out d_owner);

                            tmp = det.Attributes.GetNamedItem(
                                    "position").Value;
                            d_position =
                                new LSL_Types.Vector3(tmp);

                            tmp = det.Attributes.GetNamedItem(
                                    "rotation").Value;
                            d_rotation =
                                new LSL_Types.Quaternion(tmp);

                            tmp = det.Attributes.GetNamedItem(
                                    "type").Value;
                            int.TryParse(tmp, out d_type);

                            tmp = det.Attributes.GetNamedItem(
                                    "velocity").Value;
                            d_velocity =
                                new LSL_Types.Vector3(tmp);

                            UUID uuid = new UUID();
                            UUID.TryParse(det.InnerText,
                                    out uuid);

                            DetectParams d = new DetectParams();
                            d.Key = uuid;
                            d.OffsetPos = v;
                            d.LinkNum = d_linkNum;
                            d.Group = d_group;
                            d.Name = d_name;
                            d.Owner = d_owner;
                            d.Position = d_position;
                            d.Rotation = d_rotation;
                            d.Type = d_type;
                            d.Velocity = d_velocity;

                            detected.Add(d);

                        }
                        instance.DetectParams = detected.ToArray();
                    }

                    XmlElement pluginN = (XmlElement)scriptStateN.SelectSingleNode("Plugins");
                    Object[] pluginData = ReadList(pluginN).Data;

                    AsyncCommandManager.CreateFromData(this,
                            localID, m_CurrentCompileItem, part.UUID,
                            pluginData);

                    if (postOnRez)
                    {
                        instance.PostEvent(new EventParams("on_rez",
                                new Object[] {instance.StartParam}, new DetectParams[0]));
                    }

                    if (stateSource == (int)StateSource.AttachedRez)
                    {
                        instance.PostEvent(new EventParams("attach",
                                new object[] { part.AttachedAvatar.ToString() }, new DetectParams[0]));
                    }
                    instance.Resume();
                }
                else
                {
                    WriteStateFile(m_CurrentCompileItem, instance);

                    instance.PostEvent(new EventParams("state_entry", new Object[0], new DetectParams[0]));

                    if (postOnRez)
                    {
                        instance.PostEvent(new EventParams("on_rez",
                                new Object[] {instance.StartParam}, new DetectParams[0]));
                    }

                    if (stateSource == (int)StateSource.AttachedRez)
                    {
                        instance.PostEvent(new EventParams("attach",
                                new object[] { part.AttachedAvatar.ToString() }, new DetectParams[0]));
                    }
                    else if (stateSource == (int)StateSource.NewRez)
                    {
                        instance.PostEvent(new EventParams("changed",
                                new Object[] {256}, new DetectParams[0]));
                    }
                    else if (stateSource == (int)StateSource.PrimCrossing)
                    {
                        instance.PostEvent(new EventParams("changed",
                                new Object[] {512}, new DetectParams[0]));
                    }
                }

                lock (m_Instances)
                {
                    m_Instances[m_CurrentCompileItem] = instance;

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

                    if (!l.Contains(m_CurrentCompileItem))
                        l.Add(m_CurrentCompileItem);

                    m_Partmap[m_CurrentCompileItem] = part.UUID;
                }
            }
            catch (Exception e)
            {
                m_log.Error("[XMREngine]: Script load failed, restart region");
                m_log.Error("[XMREngine]: ... " + e.ToString());
                lock (m_Assemblies)
                {
                    if (!m_Assemblies.ContainsKey(item.AssetID))
                        return false;

                    m_Assemblies[item.AssetID]--;

//                    m_log.DebugFormat("[XMREngine]: Unloading script, asset count now {0}", m_Assemblies[item.AssetID]);
                    if (m_Assemblies[item.AssetID] < 1)
                    {
//                        m_log.Debug("[XMREngine]: Unloading app domain");
                        AppDomain.Unload(m_AppDomains[item.AssetID]);
                        m_AppDomains.Remove(item.AssetID);

                        m_Assemblies.Remove(item.AssetID);

                        File.Delete(outputName);
                        File.Delete(outputName + ".mdb");
                    }
                }
                return false;
            }
            return true;
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

//                m_log.DebugFormat("[XMREngine]: Unloading script, asset count now {0}", m_Assemblies[item.AssetID]);
                if (m_Assemblies[item.AssetID] < 1)
                {
//                    m_log.Debug("[XMREngine]: Unloading app domain");
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
                scriptStateN.SetAttribute("Asset", ins.AssetID.ToString());

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

        private static void DumpList(XmlDocument doc, XmlNode parent,
                LSL_Types.list l)
        {
            foreach (Object o in l.Data)
                WriteTypedValue(doc, parent, "ListItem", "", o);
        }

        private static LSL_Types.list ReadList(XmlNode parent)
        {
            List<Object> olist = new List<Object>();

            XmlNodeList itemL = parent.ChildNodes;
            foreach (XmlNode item in itemL)
                olist.Add(ReadTypedValue(item));

            return new LSL_Types.list(olist.ToArray());
        }

        private static void WriteTypedValue(XmlDocument doc, XmlNode parent,
                string tag, string name, object value)
        {
            Type t=value.GetType();
            XmlAttribute typ = doc.CreateAttribute("", "type", "");
            XmlNode n = doc.CreateElement("", tag, "");

            if (value is LSL_Types.list)
            {
                typ.Value = "list";
                n.Attributes.Append(typ);

                DumpList(doc, n, (LSL_Types.list) value);

                if (name != String.Empty)
                {
                    XmlAttribute nam = doc.CreateAttribute("", "name", "");
                    nam.Value = name;
                    n.Attributes.Append(nam);
                }

                parent.AppendChild(n);
                return;
            }

            n.AppendChild(doc.CreateTextNode(value.ToString()));

            typ.Value = t.ToString();
            n.Attributes.Append(typ);
            if (name != String.Empty)
            {
                XmlAttribute nam = doc.CreateAttribute("", "name", "");
                nam.Value = name;
                n.Attributes.Append(nam);
            }

            parent.AppendChild(n);
        }

        private static object ReadTypedValue(XmlNode tag, out string name)
        {
            name = tag.Attributes.GetNamedItem("name").Value;

            return ReadTypedValue(tag);
        }

        private static object ReadTypedValue(XmlNode tag)
        {
            Object varValue;
            string assembly;

            string itemType = tag.Attributes.GetNamedItem("type").Value;

            if (itemType == "list")
                return ReadList(tag);

            if (itemType == "OpenMetaverse.UUID")
            {
                UUID val = new UUID();
                UUID.TryParse(tag.InnerText, out val);

                return val;
            }

            Type itemT = Type.GetType(itemType);
            if (itemT == null)
            {
                Object[] args =
                    new Object[] { tag.InnerText };

                assembly = itemType+", OpenSim.Region.ScriptEngine.Shared";
                itemT = Type.GetType(assembly);
                if (itemT == null)
                    return null;

                varValue = Activator.CreateInstance(itemT, args);

                if (varValue == null)
                    return null;
            }
            else
            {
                varValue = Convert.ChangeType(tag.InnerText, itemT);
            }
            return varValue;
        }

        public ArrayList GetScriptErrors(UUID itemID)
        {
            ArrayList errors;

            if (m_CompilerErrors.ContainsKey(itemID))
                errors = m_CompilerErrors[itemID];
            else
                return new ArrayList();

            m_CompilerErrors.Remove(itemID);
            return errors;
        }

//        private void ReplaceAssemblyPath(Byte[] data, string path)
//        {
//            string[] elems = path.Split(new char[] {Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar}, StringSplitOptions.RemoveEmptyEntries);
//            if (elems.Length < 2)
//                return;
//
//            string filename = elems[elems.Length - 1];
//            if (!filename.EndsWith(".dll"))
//                return;
//
//            Byte[] newDir = Util.UTF8.GetBytes(elems[elems.Length - 2]);
//
//            int index = FindInArray(data, Util.UTF8.GetBytes(filename));
//            if (index == -1)
//                return;
//
//            index -= 37;
//
//            if (index < 0)
//                return;
//
//            Array.Copy(newDir, 0, data, index, newDir.Length);
//        }
//
//        private int FindInArray(Byte[] data, Byte[] search)
//        {
//            for (int i = 0 ; i < data.Length - search.Length ; i++)
//                if (ArrayCompare(data, i, search, 0, search.Length))
//                    return i;
//
//            return -1;
//        }
//
//        private bool ArrayCompare(Byte[] data, int s_off, Byte[] search, int off, int len)
//        {
//            for (int i = 0 ; i < len ; i++)
//                if (data[s_off + i] != search[off + i])
//                    return false;
//
//            return true;
//        }
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
