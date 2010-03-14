//////////////////////////////////////////////////////////////
//
// Copyright (c) 2009 Careminster Limited and Melanie Thielker
//
// All rights reserved
//

using System;
using System.Threading;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Remoting.Lifetime;
using System.Security.Policy;
using System.IO;
using System.Xml;
using Mono.Tasklets;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenSim.Region.ScriptEngine.Shared;
using OpenSim.Region.ScriptEngine.Shared.Api;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Region.ScriptEngine.XMREngine;
using OpenSim.Region.Framework.Scenes;
using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;
using log4net;

// This class exists in the main app domain
//
namespace OpenSim.Region.ScriptEngine.XMREngine
{
    public class XMRInstance : IDisposable
    {
        public const int MAXEVENTQUEUE = 64;


        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // For a given m_AssetID, do we have the compiled object code and where
        // is it?  m_CompiledScriptRefCount keeps track of how many m_ObjCode
        // pointers are valid.
        public static object m_CompileLock = new object();
        private static Dictionary<UUID, ScriptObjCode> m_CompiledScriptObjCode = new Dictionary<UUID, ScriptObjCode>();
        private static Dictionary<UUID, int> m_CompiledScriptRefCount = new Dictionary<UUID, int>();

        public  SceneObjectPart m_Part = null;
        public  uint m_LocalID = 0;
        public  TaskInventoryItem m_Item = null;
        public  UUID m_ItemID;
        public  UUID m_AssetID;

        private XMREngine m_Engine = null;
        private string m_ScriptBasePath;
        private string m_StateFileName;
        private string m_SourceCode;
        private ScriptObjCode m_ObjCode;
        private bool m_PostOnRez;
        private DetectParams[] m_DetectParams = null;
        private bool m_Reset = false;
        private bool m_Die = false;
        private int m_StartParam = 0;
        private StateSource m_StateSource;
        private string m_DescName;
        private UIntPtr m_StackSize;
        private ArrayList m_CompilerErrors;
        private ScriptWrapper m_Wrapper = null;
        private DateTime m_LastRanAt = DateTime.MinValue;

        // If code needs to have both m_QueueLock and m_RunLock,
        // be sure to lock m_RunLock first then m_QueueLock, as
        // that is the order used in RunOne().
        // These locks are currently separated to allow the script
        // to call API routines that queue events back to the script.
        // If we just had one lock, then the queuing would deadlock.

        // guards m_EventQueue, m_TimerQueued, m_Running
        private Object m_QueueLock = new Object();

        // true iff allowed to accept new events
        private bool m_Running = true;

        // queue of events that haven't been acted upon yet
        private Queue<EventParams> m_EventQueue = new Queue<EventParams>();

        // true iff m_EventQueue contains a timer() event
        private bool m_TimerQueued = false;


        // guards m_IsIdle (locked whilst in ScriptWrapper running the script)
        private Object m_RunLock = new Object();

        // false iff script is running an event handler, ie, from the time its
        // event handler entrypoint is called until its event handler returns
        private bool m_IsIdle = true;

        // script won't step while > 0.  bus-atomic updates only.
        private int m_SuspendCount = 0;

        // don't run any of script until this time
        private DateTime m_SuspendUntil = DateTime.MinValue;


        private Dictionary<string,IScriptApi> m_Apis =
                new Dictionary<string,IScriptApi>();


        public void Construct(uint localID, UUID itemID, string script,
                              int startParam, bool postOnRez, int stateSource,
                              XMREngine engine, SceneObjectPart part, 
                              TaskInventoryItem item, string scriptBasePath,
                              UIntPtr stackSize)
        {

            /*
             * Save all call parameters in instance vars for easy access.
             */
            m_LocalID        = localID;
            m_ItemID         = itemID;
            m_SourceCode     = script;
            m_StartParam     = startParam;
            m_PostOnRez      = postOnRez;
            m_StateSource    = (StateSource)stateSource;
            m_Engine         = engine;
            m_Part           = part;
            m_Item           = item;
            m_AssetID        = item.AssetID;
            m_ScriptBasePath = scriptBasePath;
            m_StackSize      = stackSize;

            m_StateFileName  = Path.Combine(scriptBasePath,
                                            m_ItemID.ToString() + ".state");

            /*
             * Set up a descriptive name string for debug messages.
             */
            m_DescName = MMRCont.HexString(MMRCont.ObjAddr(this));
            if (m_DescName.Length < 8)
            {
                m_DescName = "00000000".Substring(0, 8 - m_DescName.Length);
            }
            m_DescName += " " + part.Name + ":" + item.Name + ":";

            /*
             * Get DLL loaded, compiling script and reading .state file as
             * necessary.
             */
            InstantiateScript();
            m_SourceCode = null;
            if (m_Wrapper == null) throw new ArgumentNullException ("m_Wrapper");
            if (m_Wrapper.objCode == null) throw new ArgumentNullException ("m_Wrapper.objCode");
            if (m_Wrapper.objCode.scriptEventHandlerTable == null) throw new ArgumentNullException ("m_Wrapper.objCode.scriptEventHandlerTable");

            /*
             * Set up list of API calls it has available.
             */
            ApiManager am = new ApiManager();
            foreach (string api in am.GetApis())
            {
                IScriptApi scriptApi;

                if (api != "LSL")
                    scriptApi = am.CreateApi(api);
                else
                    scriptApi = new XMRLSL_Api();

                m_Apis[api] = scriptApi;
                scriptApi.Initialize(m_Engine, m_Part, m_LocalID, m_ItemID);
                m_Wrapper.beAPI.InitApi(api, scriptApi);
            }

            /*
             * Declare which events the script can handle.
             */
            m_Part.SetScriptEvents(m_ItemID, GetStateEventFlags(0));
        }

        /**
         * @brief For a given stateCode, get a mask of the low 32 event codes
         *        that the state has handlers defined for.
         */
        public int GetStateEventFlags(int stateCode)
        {
            if ((stateCode < 0) ||
                (stateCode >= m_Wrapper.objCode.scriptEventHandlerTable.GetLength(0)))
            {
                return 0;
            }

            int code = 0;
            for (int i = 0 ; i < 32; i ++)
            {
                if (m_Wrapper.objCode.scriptEventHandlerTable[stateCode, i] != null)
                {
                    code |= 1 << i;
                }
            }

            return code;
        }

        // In case Dispose() doesn't get called, we want to be sure to clean
        // up.  This makes sure we decrement m_CompiledScriptRefCount.
        ~XMRInstance()
        {
            Dispose();
        }

        // Clean up stuff
        public void Dispose()
        {
            // Don't send us any more events
            if (m_Part != null)
            {
                m_Part.RemoveScriptEvents(m_ItemID);
                AsyncCommandManager.RemoveScript(m_Engine, m_LocalID, m_ItemID);
                m_Part = null;
            }

            // Let script methods get garbage collected if no one else is using
            // them.
            if (m_ObjCode != null)
            {
                lock (m_CompileLock)
                {
                    ScriptObjCode objCode;

                    if (m_CompiledScriptObjCode.TryGetValue(m_AssetID, 
                                                            out objCode) &&
                        (objCode == m_ObjCode) && 
                        (-- m_CompiledScriptRefCount[m_AssetID] == 0))
                    {
                        m_CompiledScriptObjCode.Remove(m_AssetID);
                        m_CompiledScriptRefCount.Remove(m_AssetID);
                    }
                }
                m_ObjCode = null;
            }

            // Unload the script instance struct (ScriptWrapper)
            if (m_Wrapper != null)
            {
                m_Wrapper.Dispose();
                m_Wrapper = null;
            }
        }

        // Called by 'xmr test ls' console command
        // to dump this script's state to console
        public void RunTestLs()
        {
            Console.WriteLine(m_DescName);
            Console.WriteLine("    m_LocalID      = " + m_LocalID);
            Console.WriteLine("    m_ItemID       = " + m_ItemID);
            Console.WriteLine("    m_AssetID      = " + m_AssetID);
            Console.WriteLine("    m_StartParam   = " + m_StartParam);
            Console.WriteLine("    m_PostOnRez    = " + m_PostOnRez);
            Console.WriteLine("    m_StateSource  = " + m_StateSource);
            Console.WriteLine("    m_SuspendCount = " + m_SuspendCount);
            Console.WriteLine("    m_IsIdle       = " + m_IsIdle);
            Console.WriteLine("    m_Reset        = " + m_Reset);
            Console.WriteLine("    m_Die          = " + m_Die);
            string sc = m_Wrapper.GetStateName(m_Wrapper.stateCode);
            Console.WriteLine("    m_StateCode    = " + sc);
            Console.WriteLine("    m_LastRanAt    = " + m_LastRanAt.ToString());
            Console.WriteLine("    heapLeft/Limit = " + m_Wrapper.heapLeft + "/" + m_Wrapper.heapLimit);
            lock (m_QueueLock)
            {
                Console.WriteLine("    m_Running      = " + m_Running);
                foreach (EventParams evt in m_EventQueue)
                {
                    Console.WriteLine("        evt.EventName  = " + evt.EventName);
                }
                Console.WriteLine("    m_TimerQueued  = " + m_TimerQueued);
            }
        }

        // Get script DLL loaded in memory and all ready to run,
        // ready to resume it from where the .state file says it was last
        private void InstantiateScript()
        {
            lock (m_CompileLock)
            {
                bool compileFailed = false;
                bool compiledIt = false;
                ScriptObjCode objCode;

                /*
                 * There may already be an ScriptObjCode struct in memory that
                 * we can use.  If not, try to compile it.
                 */
                if (!m_CompiledScriptObjCode.TryGetValue (m_AssetID, 
                                                          out objCode))
                {
                    try {
                        objCode = TryToCompile(false);
                        compiledIt = true;
                    } catch (Exception e) {
                        compileFailed = true;
                    }
                }

                /*
                 * Get a new instance of that script object code loaded in
                 * memory and try to fill in its initial state from the saved
                 * state file.
                 */
                if (!compileFailed && TryToLoad(objCode))
                {
                    m_log.DebugFormat("[XMREngine]: load successful {0}",
                            m_DescName);
                } else if (compiledIt) {
                    throw new Exception("script load failed");
                } else {

                    /*
                     * If it didn't load, maybe it's because of a version
                     * mismatch somewhere.  So try recompiling and reload.
                     */
                    m_log.DebugFormat("[XMREngine]: attempting recompile {0}",
                            m_DescName);
                    objCode = TryToCompile(true);
                    compiledIt = true;
                    m_log.DebugFormat("[XMREngine]: attempting reload {0}",
                            m_DescName);
                    if (!TryToLoad(objCode))
                    {
                        throw new Exception("script reload failed");
                    }
                    m_log.DebugFormat("[XMREngine]: reload successful {0}",
                            m_DescName);
                }

                /*
                 * (Re)loaded successfully, increment reference count.
                 *
                 * If we just compiled it though, reset count to 0 first as
                 * this is the one-and-only existance of this objCode struct,
                 * and we want any old ones for this assetID to be garbage
                 * collected.
                 */
                if (compiledIt) {
                    m_CompiledScriptObjCode[m_AssetID]  = objCode;
                    m_CompiledScriptRefCount[m_AssetID] = 0;
                }
                m_ObjCode = objCode;
                m_CompiledScriptRefCount[m_AssetID] ++;
            }
        }

        // Try to create object code from source code
        // If error, just throw exception
        private ScriptObjCode TryToCompile(bool forceCompile)
        {
            string objName = ScriptCompile.GetObjFileName(m_AssetID.ToString(),
                                                          m_ScriptBasePath);

            m_CompilerErrors = null;

            /*
             * If told to force compilation (presumably because object file 
             * is old version or corrupt), delete the object file which will 
             * make ScriptCompile.Compile() create a new one from the source.
             */
            if (forceCompile) {
                File.Delete(objName);
            }

            /*
             * If we have neither the source nor the object file, not much we
             * can do to create the ScriptObjCode object.
             */
            if ((m_SourceCode == String.Empty) && !File.Exists(objName))
            {
                throw new Exception("Compile of asset " +
                                    m_AssetID.ToString() +
                                    " was requested but source text is not " +
                                    "present and no assembly was found");
            }

            /*
             * If object file exists, create ScriptObjCode directly from that.
             * Otherwise, compile the source to create object file then create
             * ScriptObjCode from that.
             */
            ScriptObjCode objCode = ScriptCompile.Compile(m_SourceCode, 
                                                          m_DescName,
                                                          m_AssetID.ToString(), 
                                                          m_ScriptBasePath, 
                                                          ErrorHandler);
            if (m_CompilerErrors != null)
            {
                throw new Exception ("compilation errors");
            }
            if (objCode == null)
            {
                throw new Exception ("compilation failed");
            }

            return objCode;
        }

        // Output error message when compiling a script
        //
        private void ErrorHandler(Token token, string message)
        {
            if (m_CompilerErrors == null)
            {
                m_CompilerErrors = new ArrayList();
            }
            if (token != null)
            {
                m_CompilerErrors.Add(
                        String.Format("({0},{1}) Error: {2}", token.line,
                                token.posn, message));
            }
            else if (message != null)
            {
                m_CompilerErrors.Add(
                        String.Format("(0,0) Error: {0}", message));
            }
            else
            {
                m_CompilerErrors.Add("Error compiling, see exception in log");
            }
        }

        public ArrayList GetScriptErrors()
        {
            ArrayList errors;
            errors = m_CompilerErrors;
            m_CompilerErrors = null;
            return errors;
        }

        //  TryToLoad()
        //      create script instance
        //      if no state XML file exists for the asset,
        //          post initial default state events
        //      else
        //          try to restore from .state file
        //          if unable, delete .state file and retry
        //
        private bool TryToLoad(ScriptObjCode objCode)
        {
            // Create a ScriptWrapper with script loaded.
            // The script is in a "never-ever-has-run-before" state.
            try
            {
                m_Wrapper       = new ScriptWrapper(objCode, 
                                                    m_StackSize, 
                                                    m_DescName);
                m_Wrapper.beAPI = new ScriptBaseClass();
            }
            catch (Exception e)
            {
                m_log.Error("[XMREngine]: error loading script " + 
                        m_DescName + ": " + e.Message);
                m_log.Error("[XMREngine]*: " + e.ToString());
                if (m_Wrapper != null) {
                    m_Wrapper.Dispose();
                    m_Wrapper = null;
                }
                return false;
            }
            // have CheckRun() always return out to scheduler
            m_Wrapper.alwaysSuspend = true;

            // tell script what to call when it does a 'state <newstate>;' stmt
            m_Wrapper.stateChange   = StateChange;

            // If no .state file exists, start from default state
            string envar = Environment.GetEnvironmentVariable("XMREngineIgnoreState");
            if ((envar != null) && ((envar[0] & 1) != 0)) {
                File.Delete(m_StateFileName);
            }
            if (!File.Exists(m_StateFileName))
            {
                m_Running = true;  // event processing is enabled
                m_IsIdle  = true;  // not processing any event handler

                // default state_entry() must initialize global variables
                m_Wrapper.doGblInit = true;
                m_Wrapper.stateCode = 0;
                PostEvent(new EventParams("state_entry", 
                                          new Object[0], 
                                          new DetectParams[0]));

                if (m_PostOnRez)
                {
                    PostEvent(new EventParams("on_rez",
                            new Object[] { m_StartParam }, 
                            new DetectParams[0]));
                }

                if (m_StateSource == StateSource.AttachedRez)
                {
                    PostEvent(new EventParams("attach",
                            new object[] { m_Part.AttachedAvatar.ToString() }, 
                            new DetectParams[0]));
                }
                else if (m_StateSource == StateSource.NewRez)
                {
                    PostEvent(new EventParams("changed",
                            new Object[] { 256 }, 
                            new DetectParams[0]));
                }
                else if (m_StateSource == StateSource.PrimCrossing)
                {
                    PostEvent(new EventParams("changed",
                            new Object[] { 512 }, 
                            new DetectParams[0]));
                }

                return true;
            }

            // Got a .state file, try to read .state file into script instance
            try
            {
                FileStream fs = File.Open(m_StateFileName, 
                                          FileMode.Open, 
                                          FileAccess.Read);
                StreamReader ss = new StreamReader(fs);
                string xml = ss.ReadToEnd();
                ss.Close();
                fs.Close();

                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);
                LoadScriptState(doc);
                return true;
            }
            catch (Exception e)
            {
                m_log.Error("[XMREngine]: error restoring " +
                        m_DescName + ": " + e.Message);

                // Failed to load state, delete bad .state file and reload
                // instance so we get a script at default state.
                File.Delete(m_StateFileName);
                m_Wrapper.Dispose();
                m_Wrapper = null;
                return TryToLoad(objCode);
            }
        }

        // Load state from the given XML into the script object
        private void LoadScriptState(XmlDocument doc)
        {
            DetectParams[] detParams;

            // Everything we know is enclosed in <ScriptState>...</ScriptState>
            XmlElement scriptStateN = (XmlElement)doc.SelectSingleNode("ScriptState");
            if (scriptStateN == null)
            {
                throw new Exception("no <ScriptState> tag");
            }

            // AssetID is unique for the script source text so make sure the
            // state file was written for that source file
            string assetID = scriptStateN.GetAttribute("Asset");
            if (assetID != m_Item.AssetID.ToString())
            {
                throw new Exception("assetID mismatch");
            }

            // Get various attributes
            XmlElement runningN = (XmlElement)scriptStateN.SelectSingleNode("Running");
            m_Running = bool.Parse(runningN.InnerText);

            XmlElement doGblInitN = (XmlElement)scriptStateN.SelectSingleNode("DoGblInit");
            m_Wrapper.doGblInit = bool.Parse(doGblInitN.InnerText);

            XmlElement permissionsN = (XmlElement)scriptStateN.SelectSingleNode("Permissions");
            m_Item.PermsGranter = new UUID(permissionsN.GetAttribute("granter"));
            m_Item.PermsMask = Convert.ToInt32(permissionsN.GetAttribute("mask"));
            m_Part.Inventory.UpdateInventoryItem(m_Item);

            // get values used by stuff like llDetectedGrab, etc.
            detParams = RestoreDetectParams(scriptStateN);

            // Restore timers and listeners
            XmlElement pluginN = (XmlElement)scriptStateN.SelectSingleNode("Plugins");
            Object[] pluginData = ReadList(pluginN).Data;

            // See if we are supposed to send an 'on_rez' event
            if (m_PostOnRez)
            {
                PostEvent(new EventParams("on_rez",
                        new Object[] { m_StartParam }, new DetectParams[0]));
            }

            // Maybe an 'attach' event too
            if (m_StateSource == StateSource.AttachedRez)
            {
                PostEvent(new EventParams("attach",
                        new object[] { m_Part.AttachedAvatar.ToString() }, 
                        new DetectParams[0]));
            }

            // Script's global variables and stack contents
            XmlElement snapshotN = 
                    (XmlElement)scriptStateN.SelectSingleNode("Snapshot");

            Byte[] data = Convert.FromBase64String(snapshotN.InnerText);
            MemoryStream ms = new MemoryStream();
            ms.Write(data, 0, data.Length);
            ms.Seek(0, SeekOrigin.Begin);
            m_Wrapper.MigrateInEventHandler(ms);
            m_IsIdle = ms.ReadByte() == 0;
            ms.Close();

            // Now that we can't throw an exception, do final updates
            AsyncCommandManager.CreateFromData(m_Engine,
                    m_LocalID, m_ItemID, m_Part.UUID,
                    pluginData);
            m_DetectParams = detParams;
        }

        /**
         * @brief Read llDetectedGrab, etc, values from XML
         */
        private DetectParams[] RestoreDetectParams(XmlElement scriptStateN)
        {
            XmlElement detectedN = 
                    (XmlElement)scriptStateN.SelectSingleNode("Detect");
            if (detectedN == null)
            {
                return null;
            }

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

                tmp = det.Attributes.GetNamedItem("linkNum").Value;
                int.TryParse(tmp, out d_linkNum);

                tmp = det.Attributes.GetNamedItem("group").Value;
                UUID.TryParse(tmp, out d_group);

                d_name = det.Attributes.GetNamedItem("name").Value;

                tmp = det.Attributes.GetNamedItem("owner").Value;
                UUID.TryParse(tmp, out d_owner);

                tmp = det.Attributes.GetNamedItem("position").Value;
                d_position = new LSL_Types.Vector3(tmp);

                tmp = det.Attributes.GetNamedItem("rotation").Value;
                d_rotation = new LSL_Types.Quaternion(tmp);

                tmp = det.Attributes.GetNamedItem("type").Value;
                int.TryParse(tmp, out d_type);

                tmp = det.Attributes.GetNamedItem("velocity").Value;
                d_velocity = new LSL_Types.Vector3(tmp);

                UUID uuid = new UUID();
                UUID.TryParse(det.InnerText, out uuid);

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
            return detected.ToArray();
        }

        private static LSL_List ReadList(XmlNode parent)
        {
            List<Object> olist = new List<Object>();

            XmlNodeList itemL = parent.ChildNodes;
            foreach (XmlNode item in itemL)
                olist.Add(ReadTypedValue(item));

            return new LSL_List(olist.ToArray());
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

        // Create an XML element that gives the current state of the script.
        // <State Engine="XMREngine" UUID=m_ItemID Asset=m_AssetID>
        //   <ScriptState> as given by GetExecutionState </ScriptState>
        //   <Assembly>DLLimage</Assembly>
        // </State>
        public XmlElement GetXMLState(XmlDocument doc)
        {
            Suspend();

            XmlElement stateN = doc.CreateElement("", "State", "");

            XmlAttribute uuidA = doc.CreateAttribute("", "UUID", "");
            uuidA.Value = m_ItemID.ToString();
            stateN.Attributes.Append(uuidA);

            XmlAttribute engineA = doc.CreateAttribute("", "Engine", "");
            engineA.Value = m_Engine.ScriptEngineName;
            stateN.Attributes.Append(engineA);

            doc.AppendChild(stateN);

            XmlElement scriptStateN = GetExecutionState(doc);

            Resume();

            if (scriptStateN == null)
                return null;

            stateN.AppendChild(scriptStateN);

            XmlAttribute assetA = doc.CreateAttribute("", "Asset", "");
            assetA.Value = m_AssetID.ToString();
            stateN.Attributes.Append(assetA);

            if (File.Exists(m_DescName))
            {
                FileInfo fi = new FileInfo(m_DescName);
                if (fi == null)
                    return null;

                Byte[] assemblyData = new Byte[fi.Length];

                try
                {
                    FileStream fs = File.Open(m_DescName, FileMode.Open,
                                              FileAccess.Read);
                    fs.Read(assemblyData, 0, assemblyData.Length);
                    fs.Close();
                }
                catch (Exception e)
                {
                    m_log.Debug("[XMREngine]: Unable to open script assembly: " + 
                            e.ToString());
                    return null;
                }

                XmlElement assemN = doc.CreateElement("", "Assembly", "");
                assemN.AppendChild(doc.CreateTextNode(Convert.ToBase64String(assemblyData)));

                stateN.AppendChild(assemN);
            }

            return stateN;
        }

        // Create an XML element that gives the current state of the script.
        //   <ScriptState Asset=m_AssetID>
        //     <Snapshot>stackdump</Snapshot>
        //     <Running>m_Running</Running>
        //     <Detect ...
        //     <Permissions ...
        //     <Plugins />
        //   </ScriptState>
        // Updates the .state file while we're at it.
        public XmlElement GetExecutionState(XmlDocument doc)
        {
            XmlElement scriptStateN = doc.CreateElement("", "ScriptState", "");
            scriptStateN.SetAttribute("Asset", m_AssetID.ToString());

            Byte[] data = GetSnapshot();

            string state = Convert.ToBase64String(data);

            XmlElement snapshotN = doc.CreateElement("", "Snapshot", "");
            snapshotN.AppendChild(doc.CreateTextNode(state));

            scriptStateN.AppendChild(snapshotN);

            XmlElement runningN = doc.CreateElement("", "Running", "");
            runningN.AppendChild(doc.CreateTextNode(m_Running.ToString()));
            scriptStateN.AppendChild(runningN);

            XmlElement doGblInitN = doc.CreateElement("", "DoGblInit", "");
            doGblInitN.AppendChild(doc.CreateTextNode(m_Wrapper.doGblInit.ToString()));
            scriptStateN.AppendChild(doGblInitN);

            DetectParams[] detect = m_DetectParams;

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

            XmlNode permissionsN = doc.CreateElement("", "Permissions", "");
            scriptStateN.AppendChild(permissionsN);

            XmlAttribute granterA = doc.CreateAttribute("", "granter", "");
            granterA.Value = m_Item.PermsGranter.ToString();
            permissionsN.Attributes.Append(granterA);

            XmlAttribute maskA = doc.CreateAttribute("", "mask", "");
            maskA.Value = m_Item.PermsMask.ToString();
            permissionsN.Attributes.Append(maskA);

            Object[] pluginData = 
                    AsyncCommandManager.GetSerializationData(m_Engine,
                            m_ItemID);

            XmlNode plugins = doc.CreateElement("", "Plugins", "");
            DumpList(doc, plugins, new LSL_List(pluginData));

            scriptStateN.AppendChild(plugins);

            // scriptStateN represents the contents of the .state file so
            // write the .state file while we are here.
            FileStream fs = File.Create(m_StateFileName);
            StreamWriter sw = new StreamWriter(fs);
            sw.Write(scriptStateN.OuterXml);
            sw.Close();
            fs.Close();

            return scriptStateN;
        }

        private static void DumpList(XmlDocument doc, XmlNode parent,
                LSL_List l)
        {
            foreach (Object o in l.Data)
                WriteTypedValue(doc, parent, "ListItem", "", o);
        }

        private static void WriteTypedValue(XmlDocument doc, XmlNode parent,
                string tag, string name, object value)
        {
            Type t=value.GetType();
            XmlAttribute typ = doc.CreateAttribute("", "type", "");
            XmlNode n = doc.CreateElement("", tag, "");

            if (value is LSL_List)
            {
                typ.Value = "list";
                n.Attributes.Append(typ);

                DumpList(doc, n, (LSL_List) value);

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

        public int StartParam
        {
            get { return m_StartParam; }
            set { m_StartParam = value; }
        }

        public SceneObjectPart SceneObject
        {
            get { return m_Part; }
        }

        public DetectParams[] DetectParams
        {
            get { return m_DetectParams; }
            set { m_DetectParams = value; }
        }

        public UUID ItemID
        {
            get { return m_ItemID; }
        }

        public UUID AssetID
        {
            get { return m_AssetID; }
        }

        public void Suspend()
        {
            Interlocked.Increment(ref m_SuspendCount);
        }

        public void Resume()
        {
            int nowIs = Interlocked.Decrement(ref m_SuspendCount);
            if (nowIs < 0)
            {
                throw new Exception("m_SuspendCount negative");
            }
            if (nowIs == 0)
            {
                KickScheduler();
            }
        }

        public bool Running
        {
            get
            {
                return m_Running;
            }

            set
            {
                lock (m_QueueLock)
                {
                    m_Running = value;
                    if (!value)
                    {
                        m_EventQueue.Clear();
                        m_TimerQueued = false;
                    }
                }
            }
        }

        /*
         * Kick the scheduler to call our RunOne() if it is asleep.
         */
        private void KickScheduler()
        {
            ((XMREngine)m_Engine).WakeUp();
        }

        public void PostEvent(EventParams evt)
        {
            for (int i = 0 ; i < evt.Params.Length ; i++)
            {
                if (evt.Params[i] is LSL_Integer)
                    evt.Params[i] = (int)((LSL_Integer)evt.Params[i]);
                else if (evt.Params[i] is LSL_Float)
                    evt.Params[i] = (float)((LSL_Float)evt.Params[i]);
                else if (evt.Params[i] is LSL_String)
                    evt.Params[i] = (string)((LSL_String)evt.Params[i]);
                else if (evt.Params[i] is LSL_Key)
                    evt.Params[i] = (string)((LSL_Key)evt.Params[i]);
            }

            lock (m_QueueLock)
            {
                if (!m_Running)
                    return;

                if (m_EventQueue.Count >= MAXEVENTQUEUE)
                {
                    m_log.DebugFormat("[XMREngine]: event queue overflow, {0} -> {1}:{2}\n", 
                            evt.EventName, m_Part.Name, 
                            m_Part.Inventory.GetInventoryItem(m_ItemID).Name);
                    return;
                }

                if (evt.EventName == "timer")
                {
                    if (m_TimerQueued)
                        return;
                    m_TimerQueued = true;
                }

                m_EventQueue.Enqueue(evt);
            }
            KickScheduler();
        }

        /*
         * This is called in the script thread to step script until it calls
         * CheckRun().
         */
        public DateTime RunOne()
        {
            /*
             * If script has called llSleep(), don't do any more until time is
             * up.
             */
            if (m_SuspendUntil > DateTime.UtcNow)
            {
                return m_SuspendUntil;
            }

            /*
             * Also, someone may have called Suspend().
             */
            if (m_SuspendCount > 0) {
                return DateTime.MaxValue;
            }

            /*
             * Make sure we aren't being migrated in or out and prevent that 
             * whilst we are in here.
             */
            lock (m_RunLock)
            {

                /*
                 * Maybe we can dequeue a new event and start processing it.
                 */
                if (m_IsIdle)
                {
                    EventParams evt = null;
                    lock (m_QueueLock)
                    {
                        if (m_EventQueue.Count > 0)
                        {
                            evt = m_EventQueue.Dequeue();
                            if (evt.EventName == "timer")
                            {
                                m_TimerQueued = false;
                            }
                        }
                    }

                    if (evt == null)
                    {
                        return DateTime.MaxValue;
                    }

                    m_IsIdle = false;
                    m_DetectParams = evt.DetectParams;

                    try
                    {
                        m_LastRanAt = DateTime.UtcNow;
                        m_Wrapper.StartEventHandler(evt.EventName, evt.Params);
                    }
                    catch (Exception e)
                    {
                        m_log.Error("[XMREngine]: Exception while starting script event. Disabling script.\n" + e.ToString());
                        Interlocked.Increment(ref m_SuspendCount);
                        return DateTime.MaxValue;
                    }
                }

                /*
                 * New or old event, step script until it calls CheckRun().
                 */
                try
                {
                    m_LastRanAt = DateTime.UtcNow;
                    m_IsIdle = m_Wrapper.ResumeEventHandler();
                }
                catch (Exception e)
                {
                    m_log.Error("[XMREngine]: Exception while running script event. Disabling script.\n" + e.ToString());
                    Interlocked.Increment(ref m_SuspendCount);
                }

                /*
                 * If event handler completed, get rid of detect params.
                 */
                if (m_IsIdle)
                {
                    m_DetectParams = null;
                }

                /*
                 * Maybe script called llResetScript().
                 * If so, reset script to initial state.
                 */
                if (m_Reset)
                {
                    m_Reset = false;
                    Reset();
                }

                /*
                 * Maybe script called llDie().
                 * If so, perform deletion and get out.
                 */
                if (m_Die)
                {
                    m_Engine.World.DeleteSceneObject(m_Part.ParentGroup, false);
                    return DateTime.MinValue;
                }
            }

            /*
             * Call this one again asap.
             */
            return DateTime.MinValue;
        }

        public DetectParams GetDetectParams(int number)
        {
            if (m_DetectParams == null)
                return null;

            if (number < 0 || number >= m_DetectParams.Length)
                return null;

            return m_DetectParams[number];
        }

        public void Suspend(int ms)
        {
            m_SuspendUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(ms);
            ((XMREngine)m_Engine).WakeUp();
        }

        /**
         * @brief The script is calling llResetReset().
         *        We want to set a flag and exit out of the script immediately.
         *        The script will exit immediately as we compile in a call to
         *        CheckRun() immediately following the llResetScript() api call.
         */
        public void ApiReset()
        {
            // tell RunOne() that script called llResetScript()
            m_Reset = true;

            // tell CheckRun() to suspend microthread so RunOne() will check
            // m_Reset
            m_Wrapper.suspendOnCheckRun = true;
        }

        /**
         * @brief The script called llResetScript() while it was running and
         *        has suspended.  We want to reset the script to a never-has-
         *        ever-run-before state.
         */
        public void Reset()
        {
            ReleaseControls();

            m_Part.Inventory.GetInventoryItem(m_ItemID).PermsMask = 0;
            m_Part.Inventory.GetInventoryItem(m_ItemID).PermsGranter = UUID.Zero;
            AsyncCommandManager.RemoveScript(m_Engine, m_LocalID, m_ItemID);

            lock (m_QueueLock)
            {
                m_EventQueue.Clear();
                m_TimerQueued = false;
            }
            m_DetectParams = null;

            /*
             * Tell next call do 'default state_entry()' to reset all global
             * vars to their initial values.
             */
            m_Wrapper.doGblInit = true;

            /*
             * Set script to 'default' state and queue call to its 
             * 'state_entry()' event handler.
             */
            m_Wrapper.stateCode = 0;
            m_Part.SetScriptEvents(m_ItemID, GetStateEventFlags(0));
            PostEvent(new EventParams("state_entry", 
                                      new Object[0], 
                                      new DetectParams[0]));
        }

        public void Die()
        {
            m_Die = true;
        }

        private void ReleaseControls()
        {
            if (m_Part != null)
            {
                int permsMask;
                UUID permsGranter;
                m_Part.TaskInventory.LockItemsForRead(true);
                if (!m_Part.TaskInventory.ContainsKey(m_ItemID))
                {
                    m_Part.TaskInventory.LockItemsForRead(false);
                    return;
                }
                permsGranter = m_Part.TaskInventory[m_ItemID].PermsGranter;
                permsMask = m_Part.TaskInventory[m_ItemID].PermsMask;
                m_Part.TaskInventory.LockItemsForRead(false);

                if ((permsMask & ScriptBaseClass.PERMISSION_TAKE_CONTROLS) != 0)
                {
                    ScenePresence presence = m_Engine.World.GetScenePresence(permsGranter);
                    if (presence != null)
                        presence.UnRegisterControlEventsToScript(m_LocalID, m_ItemID);
                }
            }
        }

        public Byte[] GetSnapshot()
        {
            Byte[] snapshot;

            /*
             * Make sure we aren't executing part of the script so it stays 
             * stable.
             */
            lock (m_RunLock)
            {
                MemoryStream ms = new MemoryStream();
                bool suspended = m_Wrapper.MigrateOutEventHandler(ms);
                ms.WriteByte((byte)(suspended ? 1 : 0));
                snapshot = ms.ToArray();
                ms.Close();
            }
            return snapshot;
        }

        /*
         * The script is executing a 'state <newState>;' command.
         * Tell outer layers to cancel any event triggers, like llListen().
         */
        public void StateChange()
        {
            AsyncCommandManager.RemoveScript(m_Engine, m_LocalID, m_ItemID);
            m_Part.SetScriptEvents(m_ItemID, 
                                   GetStateEventFlags(m_Wrapper.stateCode));
        }
    }

    public class XMRLSL_Api : LSL_Api
    {
        protected override void ScriptSleep(int delay)
        {
            if (m_ScriptEngine is XMREngine)
            {
                XMREngine e = (XMREngine)m_ScriptEngine;

                e.Suspend(m_itemID, delay);
            }
            else
            {
                base.ScriptSleep(delay);
            }
        }

        public override void llSleep(double sec)
        {
            if (m_ScriptEngine is XMREngine)
            {
                XMREngine e = (XMREngine)m_ScriptEngine;

                e.Suspend(m_itemID, (int)(sec * 1000.0));
            }
            else
            {
                base.llSleep(sec);
            }
        }

        public override void llDie()
        {
            if (m_ScriptEngine is XMREngine)
            {
                XMREngine e = (XMREngine)m_ScriptEngine;

                e.Die(m_itemID);
            }
            else
            {
                base.llDie();
            }
        }
    }
}
