/////////////////////////////////////////////////////////////
//
// Copyright (c)2009 Careminster Limited and Melanie Thielker
// Copyright (c) 2010 Mike Rieker, Beverly, MA, USA
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
using System.Threading;
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
using Mono.Tasklets;
using OpenMetaverse;
using log4net;
using OpenSim.Region.ScriptEngine.XMREngine;
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
        public static readonly DetectParams[] zeroDetectParams = new DetectParams[0];
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private bool m_TraceCalls;
        private Scene m_Scene;
        private IConfigSource m_ConfigSource;
        private IConfig m_Config;
        private string m_ScriptBasePath;
        private bool m_Enabled = false;
        private XMREvents m_Events = null;
        public  AssemblyResolver m_AssemblyResolver = null;
        private Dictionary<UUID, ArrayList> m_ScriptErrors =
                new Dictionary<UUID, ArrayList>();
        private bool m_WakeUpFlag = false;
        private object m_WakeUpLock = new object();
        private Dictionary<UUID, List<UUID>> m_Objects =
                new Dictionary<UUID, List<UUID>>();
        private Dictionary<UUID, UUID> m_Partmap =
                new Dictionary<UUID, UUID>();
        private UIntPtr m_StackSize;
        private object m_SuspendScriptThreadLock = new object();
        private bool m_SuspendScriptThreadFlag = false;
        public  XMRInstance m_RunInstance = null;
        private DateTime m_SleepUntil = DateTime.MinValue;
        public  DateTime m_LastRanAt = DateTime.MinValue;
        private Thread m_ScriptThread = null;
        public  int m_ScriptThreadTID = 0;
        private Thread m_SliceThread = null;
        private bool m_Exiting = false;

        private int m_MaintenanceInterval = 10;
        
        private System.Timers.Timer m_MaintenanceTimer;

        /*
         * Various instance lists:
         *   m_InstancesDict = all known instances
         *                     find an instance given its itemID
         *   m_StartQueue = instances that have just had event queued to them
         *   m_YieldQueue = instances that are ready to run right now
         *   m_SleepQueue = instances that have m_SleepUntil valid
         *                  sorted by ascending m_SleepUntil
         */
        private Dictionary<UUID, XMRInstance> m_InstancesDict =
                new Dictionary<UUID, XMRInstance>();
        public  XMRInstQueue m_StartQueue = new XMRInstQueue();
        public  XMRInstQueue m_YieldQueue = new XMRInstQueue();
        public  XMRInstQueue m_SleepQueue = new XMRInstQueue();

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

            m_StackSize = (UIntPtr)m_Config.GetInt("ScriptStackSize", 
                                                   2*1024*1024);

            m_log.InfoFormat("[XMREngine]: Enabled, {0}.{1} Meg (0x{2}) stacks",
                    (m_StackSize.ToUInt64() >> 20).ToString (),
                    (((m_StackSize.ToUInt64() % 0x100000) * 1000) 
                            >> 20).ToString ("D3"),
                    m_StackSize.ToUInt64().ToString ("X"));

            m_MaintenanceInterval = m_Config.GetInt("MaintenanceInterval", 10);

            if (m_MaintenanceInterval > 0)
            {
                m_MaintenanceTimer = new System.Timers.Timer(m_MaintenanceInterval * 60000);
                m_MaintenanceTimer.Elapsed += DoMaintenance;
                m_MaintenanceTimer.Start();
            }

            MainConsole.Instance.Commands.AddCommand("xmr", false,
                    "xmr test",
                    "xmr test [backup|gc|ls|resume|suspend|top]",
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
             * Write script states out to .state files so it will be
             * available when the region is restarted.
             */
            DoMaintenance(null, null);

            /*
             * Stop executing script threads and wait for final
             * one to finish (ie, script gets to CheckRun() call).
             */
            m_Exiting = true;
            if (m_ScriptThread != null)
            {
                WakeUpScriptThread();
                m_ScriptThread.Join();
                m_ScriptThread = null;
            }
            if (m_SliceThread != null)
            {
                m_SliceThread.Join();
                m_SliceThread = null;
            }

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

            m_Events = new XMREvents(this);

            m_ScriptThread = new Thread(RunScriptThread);
            m_SliceThread  = new Thread(RunSliceThread);
            m_ScriptThread.Priority = ThreadPriority.BelowNormal;
            m_ScriptThread.Start();
            m_SliceThread.Start();

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
            case "ls":
                int numScripts = 0;
                lock (m_InstancesDict)
                {
                    foreach (XMRInstance ins in m_InstancesDict.Values)
                    {
                        if (InstanceMatchesArgs(ins, args)) {
                            ins.RunTestLs();
                            numScripts ++;
                        }
                    }
                }
                Console.WriteLine("total of {0} script(s)", numScripts);
                XMRInstance rins = m_RunInstance;
                if (rins != null) {
                    Console.WriteLine("running {0} {1}",
                            rins.m_ItemID.ToString(),
                            rins.m_DescName);
                }
                DateTime suntil = m_SleepUntil;
                if (suntil > DateTime.MinValue) {
                    Console.WriteLine("sleeping until {0}", suntil.ToString());
                }
                Console.WriteLine("last ran at {0}", m_LastRanAt.ToString());
                LsQueue("start", m_StartQueue, args);
                LsQueue("sleep", m_SleepQueue, args);
                LsQueue("yield", m_YieldQueue, args);
                break;
            case "resume":
                m_log.Debug("[XMREngine]: resuming scripts");
                m_SuspendScriptThreadFlag = false;
                Monitor.Enter (m_SuspendScriptThreadLock);
                Monitor.PulseAll (m_SuspendScriptThreadLock);
                Monitor.Exit (m_SuspendScriptThreadLock);
                break;
            case "suspend":
                m_log.Debug("[XMREngine]: suspending scripts");
                m_SuspendScriptThreadFlag = true;
                Monitor.Enter (m_WakeUpLock);
                Monitor.PulseAll (m_WakeUpLock);
                Monitor.Exit (m_WakeUpLock);
                break;
            case "top":
                lock (m_InstancesDict)
                {
                    foreach (XMRInstance ins2 in m_InstancesDict.Values)
                    {
                        if (InstanceMatchesArgs(ins2, args)) {
                            ins2.RunTestTop();
                        }
                    }
                }
                break;
            default:
                Console.WriteLine("xmr test: unknown command " + args[2]);
                break;
            }
        }

        private void LsQueue(string name, XMRInstQueue queue, string[] args)
        {
            Console.WriteLine("Queue " + name + ":");
            lock (queue) {
                for (XMRInstance inst = queue.PeekHead(); inst != null; inst = inst.m_NextInst) {
                    try {

                        /*
                         * Try to print instance name.
                         */
                        if (InstanceMatchesArgs(inst, args)) {
                            Console.WriteLine("   " + inst.m_ItemID.ToString() + " " + inst.m_DescName);
                        }
                    } catch (Exception e) {

                        /*
                         * Sometimes there are instances in the queue that are disposed.
                         */
                        Console.WriteLine("   " + inst.m_ItemID.ToString() + " " + inst.m_DescName + ": " + e.Message);
                    }
                }
            }
        }

        private bool InstanceMatchesArgs(XMRInstance ins, string[] args)
        {
            if (args.Length < 4) return true;
            for (int i = 3; i < args.Length; i ++)
            {
                if (ins.m_Part.Name.Contains(args[i])) return true;
                if (ins.m_Item.Name.Contains(args[i])) return true;
                if (ins.m_ItemID.ToString().Contains(args[i])) return true;
                if (ins.m_AssetID.ToString().Contains(args[i])) return true;
            }
            return false;
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

            lock (m_InstancesDict)
            {
                if (!m_InstancesDict.TryGetValue(itemID, out instance))
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

            lock (m_InstancesDict)
            {
                if (!m_InstancesDict.TryGetValue(item, out instance))
                {
                    return null;
                }
            }

            return instance.GetDetectParams(number);
        }

        public void SetMinEventDelay(UUID itemID, double delay)
        {
        }

        public int GetStartParameter(UUID itemID)
        {
            XMRInstance instance = null;

            lock (m_InstancesDict)
            {
                if (!m_InstancesDict.TryGetValue(itemID, out instance))
                {
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

            lock (m_InstancesDict)
            {
                if (!m_InstancesDict.TryGetValue(itemID, out instance))
                {
                    return;
                }
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

            lock (m_InstancesDict)
            {
                if (!m_InstancesDict.TryGetValue(itemID, out instance))
                {
                    return false;
                }
            }

            return instance.Running;
        }

        public void SetState(UUID itemID, string newState)
        {
        }

        public void ApiResetScript(UUID itemID)
        {
            XMRInstance instance = null;

            lock (m_InstancesDict)
            {
                if (!m_InstancesDict.TryGetValue(itemID, out instance))
                {
                    return;
                }
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

            lock (m_InstancesDict)
            {
                if (!m_InstancesDict.TryGetValue(itemID, out instance))
                {
                    return;
                }
            }

            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.ResetScript({0})", itemID.ToString());
            }

            instance.Reset();
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

        /**
         * @brief Get script's current state as an XML string
         *        - called by "Take", "Take Copy" and when object deleted (ie, moved to Trash)
         *        This includes both the .state file and the .xmrobj file contents
         */
        public string GetXMLState(UUID itemID)
        {
            XMRInstance instance;

            lock (m_InstancesDict)
            {
                if (!m_InstancesDict.TryGetValue(itemID, out instance))
                {
                    return String.Empty;
                }
            }
            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.GetXMLState({0})", itemID.ToString());
            }
            XmlDocument doc = new XmlDocument();

            /*
             * Set up <State Engine="XMREngine" UUID="itemID" Asset="assetID"> tag.
             */
            XmlElement stateN = doc.CreateElement("", "State", "");
            doc.AppendChild(stateN);

            XmlAttribute engineA = doc.CreateAttribute("", "Engine", "");
            engineA.Value = ScriptEngineName;
            stateN.Attributes.Append(engineA);

            XmlAttribute uuidA = doc.CreateAttribute("", "UUID", "");
            uuidA.Value = itemID.ToString();
            stateN.Attributes.Append(uuidA);

            XmlAttribute assetA = doc.CreateAttribute("", "Asset", "");
            string assetID = instance.m_AssetID.ToString();
            assetA.Value = assetID;
            stateN.Attributes.Append(assetA);

            /*
             * Get <ScriptState>...</ScriptState> item that hold's script's state.
             * This suspends the script if necessary then takes a snapshot.
             */
            XmlElement scriptStateN = instance.GetExecutionState(doc);
            stateN.AppendChild(scriptStateN);

            /*
             * Set up <Assembly>...</Assembly> item that has script's object code.
             */
            string assemblyPath = ScriptCompile.GetObjFileName(assetID, m_ScriptBasePath);
            if (File.Exists(assemblyPath))
            {
                FileInfo fi = new FileInfo(assemblyPath);
                if (fi != null)
                {
                    Byte[] assemblyData = new Byte[fi.Length];
                    try
                    {
                        FileStream fs = File.Open(assemblyPath, FileMode.Open,
                                                  FileAccess.Read);
                        fs.Read(assemblyData, 0, assemblyData.Length);
                        fs.Close();
                        XmlElement assemN = doc.CreateElement("", "Assembly", "");
                        assemN.AppendChild(doc.CreateTextNode(Convert.ToBase64String(assemblyData)));
                        stateN.AppendChild(assemN);
                    }
                    catch (Exception e)
                    {
                        m_log.Debug("[XMREngine]: Unable to open script assembly: " + 
                                e.ToString());
                    }
                }
            }

            return doc.OuterXml;
        }

        // Set script's current state from an XML string
        // - called just before a script is instantiated
        // So we write the .state file so the .state file  will be seen when 
        // the script is instantiated.  We also write the .xmrobj file if it
        // doesn't exist to save us from compiling it.  If the .xmrobj is an old
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

            /*
             * Lock to prevent XMRInstance from trying to compile the same script
             * at the same time we are writing the .xmrobj file (in case there are
             * more than one instance referencing the same script).
             */
            lock (XMRInstance.m_CompileLock)
            {
                // <ScriptState>...</ScriptState> contains contents of .state file.
                XmlElement scriptStateN = (XmlElement)stateN.SelectSingleNode("ScriptState");
                if (scriptStateN == null)
                    return false;

                XmlAttribute assetA = doc.CreateAttribute("", "Asset", "");
                assetA.Value = stateN.GetAttribute("Asset");
                scriptStateN.Attributes.Append(assetA);

                string statePath = XMRInstance.GetStateFileName(m_ScriptBasePath, itemID);
                FileStream ss = File.Create(statePath);
                StreamWriter sw = new StreamWriter(ss);
                sw.Write(scriptStateN.OuterXml);
                sw.Close();
                ss.Close();

                // <Assembly>...</Assembly> contains .xmrobj file contents
                UUID assetID = new UUID(stateN.GetAttribute("Asset"));

                string assemblyPath = ScriptCompile.GetObjFileName(assetID.ToString(), m_ScriptBasePath);

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

            return PostScriptEvent(itemID, new EventParams(name, lsl_p, zeroDetectParams));
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

            return PostObjectEvent(part.LocalId, new EventParams(name, lsl_p, zeroDetectParams));
        }

        // Get a script instance loaded, compiling it if necessary
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

            /*
             * These errors really should be cleared by same thread that calls 
             * GetScriptErrors() BEFORE it could possibly create or trigger the 
             * thread that calls OnRezScript().
             */
            lock (m_ScriptErrors) {
                m_ScriptErrors.Remove(itemID);
            }

            /*
             * Compile and load the script in memory.
             */
            XMRInstance instance;
            ArrayList errors = new ArrayList();
            try {
                instance = new XMRInstance(localID, itemID, script, 
                                           startParam, postOnRez, stateSource, 
                                           this, part, item, m_ScriptBasePath,
                                           m_StackSize, errors);
            } catch (Exception e) {
                m_log.DebugFormat("[XMREngine]: Error starting script {0}: {1}",
                                  itemID.ToString(), e.Message);
                if (e.Message != "compilation errors") {
                    m_log.DebugFormat("[XMREngine]:   exception:\n{0}", e.ToString());
                }

                /*
                 * Post errors to GetScriptErrors() and wake it.
                 */
                lock (m_ScriptErrors) {
                    if (errors.Count == 0) {
                        errors.Add(e.Message);
                    } else {
                        foreach (Object err in errors) {
                            m_log.DebugFormat("[XMREngine]:   {0}", err.ToString());
                        }
                    }
                    m_ScriptErrors[itemID] = errors;
                    Monitor.PulseAll(m_ScriptErrors);
                }
                return;
            }

            /*
             * Tell GetScriptErrors() that we have finished compiling/loading
             * successfully (by posting a 0 element array).
             */
            lock (m_ScriptErrors) {
                errors.Clear();
                m_ScriptErrors[itemID] = errors;
                Monitor.PulseAll(m_ScriptErrors);
            }

            /*
             * Let other parts of OpenSim see the instance.
             */
            lock (m_InstancesDict)
            {
                // Insert on known scripts list
                m_InstancesDict[itemID] = instance;

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

            /*
             * Transition from CONSTRUCT->ONSTARTQ and give to RunScriptThread().
             * Put it on the start queue so it will run any queued event handlers,
             * such as state_entry() or on_rez().  If there aren't any queued, it
             * will just go back to idle state when RunOne() tries to dequeue an
             * event.
             */
            if (instance.m_IState != XMRInstState.CONSTRUCT) throw new Exception("bad state");
            instance.m_IState = XMRInstState.ONSTARTQ;
            QueueToStart(instance);
        }

        public void OnRemoveScript(uint localID, UUID itemID)
        {
            SceneObjectPart part =
                    m_Scene.GetSceneObjectPart(localID);

            lock (m_InstancesDict)
            {
                XMRInstance instance;

                /*
                 * Tell the instance to free off everything it can.
                 */
                if (!m_InstancesDict.TryGetValue(itemID, out instance))
                {
                    return;
                }
                instance.Dispose();

                /*
                 * Remove it from our list of known script instances.
                 */
                m_InstancesDict.Remove(itemID);

                /*
                 * Delete the .state file as any needed contents were fetched with GetXMLState()
                 * and stored on the database server.
                 */
                string stateFileName = XMRInstance.GetStateFileName(m_ScriptBasePath, itemID);
                File.Delete(stateFileName);

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

            lock (m_InstancesDict)
            {
                if (!m_InstancesDict.TryGetValue(itemID, out instance))
                {
                    return;
                }
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

            lock (m_InstancesDict)
            {
                if (!m_InstancesDict.TryGetValue(itemID, out instance))
                {
                    return;
                }
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

            lock (m_InstancesDict)
            {
                if (!m_InstancesDict.TryGetValue(itemID, out instance))
                {
                    return;
                }
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

        /**
         * @brief Queue an instance to the StartQueue so it will run.
         *        This queue is used for instances that have just had
         *        an event queued to them when they were previously
         *        idle.  It must only be called by the thread that
         *        transitioned the thread to XMRInstState.ONSTARTQ so
         *        we don't get two threads trying to queue the same
         *        instance to the m_StartQueue at the same time.
         */
        public void QueueToStart(XMRInstance inst)
        {
            lock (m_StartQueue) {
                if (inst.m_IState != XMRInstState.ONSTARTQ) throw new Exception("bad state");
                m_StartQueue.InsertTail(inst);
            }
            WakeUpScruptThread();
        }

        /**
         * @brief Wake up RunScriptThread() as there is more work for it to do now.
         */
        private void WakeUpScriptThread()
        {
            lock (m_WakeUpLock)
            {
                m_WakeUpFlag = true;
                System.Threading.Monitor.PulseAll(m_WakeUpLock);
            }
        }

        /**
         * @brief Thread that runs the scripts.
         */
        private void RunScriptThread()
        {
            DateTime now, sleepUntil;
            XMRInstance inst;
            XMRInstState newIState;

            m_ScriptThreadTID = MMRUThread.gettid ();
            m_log.DebugFormat("[XMREngine]: RunScriptThread TID {0}", m_ScriptThreadTID);

            while (!m_Exiting)
            {
                /*
                 * Handle 'xmr test resume/suspend' commands.
                 */
                if (m_SuspendScriptThreadFlag) {
                    m_log.Debug ("[XMREngine]: scripts suspended");
                    Monitor.Enter (m_SuspendScriptThreadLock);
                    while (m_SuspendScriptThreadFlag) {
                        Monitor.Wait (m_SuspendScriptThreadLock);
                    }
                    Monitor.Exit (m_SuspendScriptThreadLock);
                    m_log.Debug ("[XMREngine]: scripts resumed");
                }

                /*
                 * Anything that changes any of the conditions
                 * below should set m_WakeUpFlag and pulse anything
                 * sleeping on m_WakeUpLock().
                 */
                m_WakeUpFlag = false;

                /*
                 * If event just queued to any idle scripts
                 * start them right away.  But only start so
                 * many so we can make some progress on sleep
                 * and yield queues.
                 */
                int numStarts;
                for (numStarts = 5; -- numStarts >= 0;) {
                    lock (m_StartQueue) {
                        inst = m_StartQueue.RemoveHead();
                    }
                    if (inst == null) break;
                    if (inst.m_IState != XMRInstState.ONSTARTQ) throw new Exception("bad state");
                    inst.m_IState = XMRInstState.RUNNING;
                    m_RunInstance = inst;
                    newIState = inst.RunOne();
                    m_RunInstance = null;
                    HandleNewIState(inst, newIState);
                }

                /*
                 * Move any expired timers to the end of the
                 * yield queue so they will run.
                 */
                m_LastRanAt = now = DateTime.UtcNow;
                while (true) {
                    sleepUntil = DateTime.MaxValue;
                    lock (m_SleepQueue) {
                        inst = m_SleepQueue.PeekHead();
                        if (inst == null) break;
                        if (inst.m_IState != XMRInstState.ONSLEEPQ) throw new Exception("bad state");
                        sleepUntil = inst.m_SleepUntil;
                        if (sleepUntil > now) break;
                        m_SleepQueue.RemoveHead();
                    }
                    lock (m_YieldQueue) {
                        inst.m_IState = XMRInstState.ONYIELDQ;
                        m_YieldQueue.InsertTail(inst);
                    }
                }

                /*
                 * If there is something to run, run it
                 * then rescan from the beginning in case
                 * a lot of things have changed meanwhile.
                 *
                 * These are considered lower priority than
                 * m_StartQueue as they have been taking at
                 * least one quantum of CPU time and event
                 * handlers are supposed to be quick.
                 */
                lock (m_YieldQueue) {
                    inst = m_YieldQueue.RemoveHead();
                }
                if (inst != null) {
                    if (inst.m_IState != XMRInstState.ONYIELDQ) throw new Exception("bad state");
                    inst.m_IState = XMRInstState.RUNNING;
                    m_RunInstance = inst;
                    newIState = inst.RunOne();
                    m_RunInstance = null;
                    HandleNewIState(inst, newIState);
                    continue;
                }

                /*
                 * If we left something dangling in the m_StartQueue, go back to check it.
                 */
                if (numStarts < 0) continue;

                /*
                 * Nothing to do, sleep.
                 * Note that at this point, we know sleepUntil > now.
                 */
                lock (m_WakeUpLock) {
                    if (!m_WakeUpFlag) {
                        TimeSpan deltaTS = sleepUntil - now;
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

        /**
         * @brief An instance has just finished running for now,
         *        figure out what to do with it next.
         * @param inst = instance in question, not on any queue at the moment
         * @param newIState = its new state
         * @returns with instance inserted onto proper queue (if any)
         */
        private void HandleNewIState(XMRInstance inst, XMRInstState newIState)
        {
            /*
             * RunOne() should have left the instance in RUNNING state.
             */
            if (inst.m_IState != XMRInstState.RUNNING) throw new Exception("bad state");

            /*
             * Now see what RunOne() wants us to do with the instance next.
             */
            switch (newIState) {

                /*
                 * Instance has set m_SleepUntil to when it wants to sleep until.
                 * So insert instance in sleep queue by ascending wake time.
                 */
                case XMRInstState.ONSLEEPQ: {
                    lock (m_SleepQueue) {
                        XMRInstance after;

                        inst.m_IState = XMRInstState.ONSLEEPQ;
                        for (after = m_SleepQueue.PeekHead(); after != null; after = after.m_NextInst) {
                            if (after.m_SleepUntil > inst.m_SleepUntil) break;
                        }
                        m_SleepQueue.InsertBefore(inst, after);
                    }
                    break;
                }

                /*
                 * Instance just took a long time to run and got wacked by the
                 * slicer.  So put on end of yield queue to let someone else
                 * run.  If there is no one else, it will run again right away.
                 */
                case XMRInstState.ONYIELDQ: {
                    lock (m_YieldQueue) {
                        inst.m_IState = XMRInstState.ONYIELDQ;
                        m_YieldQueue.InsertTail(inst);
                    }
                    break;
                }

                /*
                 * Instance finished executing an event handler.  So if there is
                 * another event queued for it, put it on the start queue so it
                 * will process the new event.  Otherwise, mark it idle and the
                 * next event to queue to it will start it up.
                 */
                case XMRInstState.FINISHED: {
                    Monitor.Enter(inst.m_QueueLock);
                    if (inst.m_EventQueue.Count > 0) {
                        Monitor.Exit(inst.m_QueueLock);
                        lock (m_StartQueue) {
                            inst.m_IState = XMRInstState.ONSTARTQ;
                            m_StartQueue.InsertTail (inst);
                        }
                    } else {
                        inst.m_IState = XMRInstState.IDLE;
                        Monitor.Exit(inst.m_QueueLock);
                    }
                    break;
                }

                /*
                 * Its m_SuspendCount > 0.
                 * Don't put it on any queue and it won't run.
                 * Since it's not IDLE, even queuing an event won't start it.
                 */
                case XMRInstState.SUSPENDED: {
                    inst.m_IState = XMRInstState.SUSPENDED;
                    break;
                }

                /*
                 * It has been disposed of.
                 * Just set the new state and all refs should theoretically drop off
                 * as the instance is no longer in any list.
                 */
                case XMRInstState.DISPOSED: {
                    inst.m_IState = XMRInstState.DISPOSED;
                    break;
                }

                /*
                 * RunOne returned something bad.
                 */
                default: throw new Exception("bad new state");
            }
        }

        /**
         * @brief Thread that runs a time slicer.
         */
        private void RunSliceThread()
        {
            while (!m_Exiting)
            {
                /*
                 * Let script run for a little bit.
                 */
                System.Threading.Thread.Sleep(50);

                /*
                 * If some script is running, flag it to suspend
                 * next time it calls CheckRun().
                 */
                XMRInstance instance = m_RunInstance;
                if (instance != null)
                {
                    instance.suspendOnCheckRunTemp = true;
                }
            }
        }

        public void Suspend(UUID itemID, int ms)
        {
            XMRInstance instance = null;

            lock (m_InstancesDict)
            {
                if (!m_InstancesDict.ContainsKey(itemID))
                    return;

                instance = m_InstancesDict[itemID];

            }

            instance.Sleep(ms);
        }

        public void Die(UUID itemID)
        {
            XMRInstance instance = null;

            lock (m_InstancesDict)
            {
                if (!m_InstancesDict.TryGetValue(itemID, out instance))
                    return;
            }
            if (m_TraceCalls)
            {
                m_log.DebugFormat("[XMREngine]: XMREngine.Die({0})", itemID.ToString());
            }
            instance.Die();
        }

        public XMRInstance GetInstance(UUID itemID)
        {
            lock (m_InstancesDict)
            {
                return m_InstancesDict[itemID];
            }
        }

        // Called occasionally to write script state to .state file so the
        // script will restart from its last known state if the region crashes
        // and gets restarted.
        private void DoMaintenance(object source, ElapsedEventArgs e)
        {
            XMRInstance[] instanceArray;

            lock (m_InstancesDict) {
                instanceArray = System.Linq.Enumerable.ToArray(m_InstancesDict.Values);
            }
            foreach (XMRInstance ins in instanceArray)
            {
                ins.GetExecutionState(new XmlDocument());
            }
        }

        /**
         * @brief Retrieve errors generated by a previous call to OnRezScript().
         *        It's possible that OnRezScript() hasn't been called yet but
         *        will be very soon in another thread.  In that case, we must
         *        wait for that other thread then retrieve the errors.
         */
        public ArrayList GetScriptErrors(UUID itemID)
        {
            ArrayList errors;

            lock (m_ScriptErrors)
            {
                if (!m_ScriptErrors.TryGetValue(itemID, out errors)) {
                    m_log.DebugFormat("[XMREngine]: waiting for {0} errors", itemID.ToString());
                    do Monitor.Wait(m_ScriptErrors);
                    while (!m_ScriptErrors.TryGetValue(itemID, out errors));
                    m_log.DebugFormat("[XMREngine]: retrieved {0} errors", itemID.ToString());
                }
                m_ScriptErrors.Remove(itemID);
            }
            if (errors.Count == 0) {
                m_log.DebugFormat("[XMREngine]: {0} successful", itemID.ToString());
            } else {
                m_log.DebugFormat("[XMREngine]: {0} has {1} error(s)", itemID.ToString(), errors.Count.ToString());
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
//            if (!filename.EndsWith(".xmrobj"))
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
