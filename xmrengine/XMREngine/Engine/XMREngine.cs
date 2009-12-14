/////////////////////////////////////////////////////////////
//
// Copyright (c)2009 Careminster Limited and Melanie Thielker
//
// All rights reserved
//


using System;
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
        private Dictionary<UUID, int> m_Assemblies =
                new Dictionary<UUID, int>();
        private Dictionary<UUID, AppDomain> m_AppDomains =
                new Dictionary<UUID, AppDomain>();
        private Dictionary<UUID, List<UUID>> m_Objects =
                new Dictionary<UUID, List<UUID>>();
        private Dictionary<UUID, UUID> m_Partmap =
                new Dictionary<UUID, UUID>();

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
            Console.WriteLine ("XMR Initialize*: entry\n");
            m_ConfigSource = config;

            if (config.Configs["XMREngine"] == null)
                m_Config = config.AddConfig("XMREngine");
            else
                m_Config = config.Configs["XMREngine"];

            m_Enabled = m_Config.GetBoolean("Enabled", false);
            Console.WriteLine ("XMR Initialize*: enabled={0}", m_Enabled);

            if (!m_Enabled)
                return;

            m_log.Info("[XMREngine]: Enabled");

            MainConsole.Instance.Commands.AddCommand("xmr", false,
                    "xmr test",
                    "xmr test",
                    "Run current xmr test",
                    RunTest);
        }

        public void AddRegion(Scene scene)
        {
            Console.WriteLine ("XMR AddRegion*: m_Enabled={0}", m_Enabled);
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
            m_log.DebugFormat("[MMR]: ({0},{1}) Error: {2}", token.line,
                    token.posn, message);
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

        public string GetXMLState(UUID itemID)
        {
            return String.Empty;
        }

        public void SetXMLState(UUID itemID, string xml)
        {
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
            Console.WriteLine ("XMR OnRezScript*: script={0}", script);
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

                    ScriptCompile.Compile(script, outputName,
                            UUID.Zero.ToString(), debugFileName, ErrorHandler);

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

                    m_Instances[itemID].PostEvent(new EventParams("state_entry", new Object[0], new DetectParams[0]));

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
                catch
                {
                    m_log.Error("[XMREngine]: Script load failed, restart region");
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

        public void RunOneCycle()
        {
            List<XMRInstance> instances = null;

            lock (m_Instances)
            {
                instances = new List<XMRInstance>(m_Instances.Values);
            }

            foreach (XMRInstance ins in instances)
                if (ins != null)
                    ins.RunOne();
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
