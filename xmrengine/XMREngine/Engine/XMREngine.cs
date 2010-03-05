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

        private bool m_TraceCalls;
        private Scene m_Scene;
        private IConfigSource m_ConfigSource;
        private IConfig m_Config;
        private string m_ScriptBasePath;
        private bool m_Enabled = false;
        private XMRSched m_Scheduler = null;
        private XMREvents m_Events = null;
        public  AssemblyResolver m_AssemblyResolver = null;
        private Dictionary<UUID, XMRInstance> m_Instances =
                new Dictionary<UUID, XMRInstance>();
        private Dictionary<UUID, ArrayList> m_ScriptErrors =
                new Dictionary<UUID, ArrayList>();
        private bool m_WakeUpFlag = false;
        private object m_WakeUpLock = new object();
        private Dictionary<UUID, List<UUID>> m_Objects =
                new Dictionary<UUID, List<UUID>>();
        private Dictionary<UUID, UUID> m_Partmap =
                new Dictionary<UUID, UUID>();

        private int m_MaintenanceInterval = 10;
        
        private Timer m_MaintenanceTimer;

        public XMREngine()
        {
            string envar = Environment.GetEnvironmentVariable("XMREngineTraceCalls");
            m_TraceCalls = (envar != null) && ((envar[0] & 1) != 0);
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
                    "xmr test [backup|gc|ls]",
                    "Run current xmr test",
                    RunTest);
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.AddRegion({0})", scene.RegionInfo.RegionName);
            }

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

        /**
         * @brief Called late in shutdown procedure,
         *        after the 'Shutting down..." message.
         */
        public void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.RemoveRegion({0})", scene.RegionInfo.RegionName);
            }

            /*
             * Stop executing script threads and wait for final
             * one to finish (ie, script gets to CheckRun() call).
             */
            if (m_Scheduler != null)
            {
                m_Scheduler.Stop();
                WakeUp();
                m_Scheduler.Shutdown();
            }
            m_Scheduler = null;

            /*
             * Write their state out to .state files so it will be
             * available when the region is restarted.
             */
            DoMaintenance(null, null);

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

            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.RegionLoaded({0})", scene.RegionInfo.RegionName);
            }

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
            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.Close()");
            }
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
            case "ls":
                int numScripts = 0;
                lock (m_Instances)
                {
                    foreach (XMRInstance ins in m_Instances.Values)
                    {
                        ins.RunTestLs();
                        numScripts ++;
                    }
                }
                Console.WriteLine("total of {0} script(s)", numScripts);
                break;
            default:
                Console.WriteLine("xmr test: unknown command " + args[2]);
                break;
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

        // Events targeted at a specific script
        // ... like listen() for an llListen() call
        //
        public bool PostScriptEvent(UUID itemID, EventParams parms)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.TryGetValue(itemID, out instance))
                {
                    return false;
                }
            }

            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.PostScriptEvent({0},{1})", itemID.ToString(), parms.EventName);
            }

            instance.PostEvent(parms);

            return true;
        }

        // Events targeted at an object as a whole
        // ... like change() for an avatar wanting to sit at a table
        //
        public bool PostObjectEvent(uint localID, EventParams parms)
        {
            SceneObjectPart part = m_Scene.GetSceneObjectPart(localID);

            if (part == null)
                return false;

            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.PostObjectEvent({0},{1})", localID.ToString(), parms.EventName);
            }

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
                if (!m_Instances.TryGetValue(item, out instance))
                    return null;
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
                if (!m_Instances.TryGetValue(itemID, out instance)) {
                    return 0;
                }
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
                if (!m_Instances.TryGetValue(itemID, out instance))
                    return;
            }

            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.SetScriptState({0},{1})", itemID.ToString(), state.ToString());
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
                if (!m_Instances.TryGetValue(itemID, out instance))
                    return false;
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
                if (!m_Instances.TryGetValue(itemID, out instance))
                    return;
            }

            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.ApiResetScript({0})", itemID.ToString());
            }

            instance.ApiReset();
        }

        public void ResetScript(UUID itemID)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.TryGetValue(itemID, out instance))
                    return;
            }

            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.ResetScript({0})", itemID.ToString());
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

        // Get script's current state as an XML string
        // - called by "Take", "Take Copy" and when object deleted (ie, moved to Trash)
        // This includes both the .state file and the .DLL file contents
        public string GetXMLState(UUID itemID)
        {
            XMRInstance instance;

            lock (m_Instances)
            {
                if (!m_Instances.TryGetValue(itemID, out instance))
                {
                    return String.Empty;
                }
            }
            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.GetXMLState({0})", itemID.ToString());
            }
            XmlDocument doc = new XmlDocument();
            if (instance.GetXMLState(doc) == null)
            {
                return String.Empty;
            }
            return doc.OuterXml;
        }

        // Set script's current state from an XML string
        // - called just before a script is instantiated
        // So we write the .state file so the .state file  will be seen when 
        // the script is instantiated.  We also write the .DLL file if it
        // doesn't exist to save us from compiling it.  If the .DLL is an old
        // version, the loader will discard it and recreate it.
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
            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.SetXMLState({0})", itemID.ToString());
            }

            // Make sure <State Engine="XMREngine"> so we know it is in our
            // format.
            XmlElement stateN = (XmlElement)doc.SelectSingleNode("State");
            if (stateN == null)
                return false;

            if (stateN.GetAttribute("Engine") != ScriptEngineName)
                return false;

            lock (XMRInstance.m_CompileLock)
            {
                // <ScriptState>...</ScriptState> contains contents of .state file.
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

                // <Assembly>...</Assembly> contains .DLL file contents
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
            }
            return true;
        }

        public bool PostScriptEvent(UUID itemID, string name, Object[] p)
        {
            if (!m_Enabled)
                return false;

            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.PostScriptEvent({0},{1})", itemID.ToString(), name);
            }

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

            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.PostObjectEvent({0},{1})", itemID.ToString(), name);
            }

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

        // Get a script instance loaded
        //
        //  localID     = the object as a whole, may contain many scripts
        //  itemID      = this instance of the script in this object
        //  script      = script source
        //  startParam  = value passed to 'on_rez' event handler
        //  postOnRez   = true to post an 'on_rez' event to script on load
        //  engine      = ??? default script engine ???
        //  stateSource = post this event to script on load

        public void OnRezScript(uint localID, UUID itemID, string script,
                int startParam, bool postOnRez, string engine, int stateSource)
        {
            if (script.StartsWith("//MRM:"))
                return;

            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.OnRezScript({0},{1},{2},{3})", 
                        localID.ToString(), itemID.ToString(), startParam.ToString(), postOnRez.ToString());
            }

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

            m_log.DebugFormat("[XMREngine]: Running script {0}, asset {1}, param {2}",
                    item.Name, item.AssetID, startParam.ToString());

            XMRInstance instance = new XMRInstance();
            try {
                instance.Construct(localID, itemID, script, 
                                   startParam, postOnRez, stateSource, 
                                   this, part, item, m_ScriptBasePath);
            } catch (Exception e) {
                m_log.DebugFormat("[XMREngine]: Error starting script: {0}",
                                  e.Message);
                if (e.Message != "compilation errors") {
                    m_log.DebugFormat("[XMREngine]*:  {0}", e.ToString());
                }
                lock (m_ScriptErrors) {
                    ArrayList errors = instance.GetScriptErrors();
                    if (errors == null) {
                        errors = new ArrayList();
                        errors.Add(e.Message);
                    } else {
                        foreach (Object err in errors) {
                            m_log.DebugFormat("[XMREngine]:   {0}", err.ToString());
                        }
                    }
                    m_ScriptErrors[itemID] = errors;
                }
                return;
            }

            lock (m_Instances)
            {
                // queue it to runnable script instance list
                m_Instances[itemID] = instance;
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
        }

        public void OnRemoveScript(uint localID, UUID itemID)
        {
            SceneObjectPart part =
                    m_Scene.GetSceneObjectPart(localID);
            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.OnRemoveScript({0},{1})", localID.ToString(), itemID.ToString());
            }

            lock (m_Instances)
            {
                XMRInstance instance;

                if (!m_Instances.TryGetValue(itemID, out instance))
                {
                    return;
                }
                instance.Suspend();
                instance.Dispose();
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

            string statePath = Path.Combine(m_ScriptBasePath,
                    itemID + ".state");
            File.Delete(statePath);
        }

        public void OnScriptReset(uint localID, UUID itemID)
        {
            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.OnScriptReset({0},{1})", localID.ToString(), itemID.ToString());
            }
            ResetScript(itemID);
        }

        public void OnStartScript(uint localID, UUID itemID)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.TryGetValue(itemID, out instance))
                    return;
            }
            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.OnStartScript({0},{1})", localID.ToString(), itemID.ToString());
            }

            instance.Running = true;
        }

        public void OnStopScript(uint localID, UUID itemID)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.TryGetValue(itemID, out instance))
                    return;
            }
            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.OnStopScript({0},{1})", localID.ToString(), itemID.ToString());
            }

            instance.Running = false;
        }

        public void OnGetScriptRunning(IClientAPI controllingClient,
                UUID objectID, UUID itemID)
        {
            XMRInstance instance = null;

            lock (m_Instances)
            {
                if (!m_Instances.TryGetValue(itemID, out instance))
                    return;
            }
            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.OnGetScriptRunning({0},{1})", objectID.ToString(), itemID.ToString());
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

        /**
         * @brief Gets called early as part of shutdown,
         *        right after "Persisting changed objects" message.
         */
        public void OnShutdown()
        {
            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.OnShutdown()");
            }
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
                if (!m_Instances.TryGetValue(itemID, out instance))
                    return;
            }
            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.Die({0})", itemID.ToString());
            }
            instance.Die();
        }

        // Called occasionally to write script state to .state file so the
        // script will restart from its last known state if the region crashes
        // and gets restarted.
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
                ins.GetExecutionState(new XmlDocument());
            }
        }

        public ArrayList GetScriptErrors(UUID itemID)
        {
            ArrayList errors;

            lock (m_ScriptErrors)
            {
                if (m_ScriptErrors.TryGetValue(itemID, out errors)) {
                    m_ScriptErrors.Remove(itemID);
                } else {
                    errors = null;
                }
            }
            if (errors == null) {
                errors = new ArrayList();
            }
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
