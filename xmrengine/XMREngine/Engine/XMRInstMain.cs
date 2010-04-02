//////////////////////////////////////////////////////////////
//
// Copyright (c) 2009 Careminster Limited and Melanie Thielker
// Copyright (c) 2010 Mike Rieker, Beverly, MA, USA
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
using System.Text;
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
    public delegate void ScriptEventHandler (XMRInstance instance);

    public enum SubsArray {
        SCRIPT = 0,
        BEAPI,
        SIZE
    };

    /**
     * @brief Which queue it is in as far as running is concerned,
     *        ie, m_StartQueue, m_YieldQueue, m_SleepQueue, etc.
     * Allowed transitions:
     *   Starts in CONSTRUCT when constructed
     *   CONSTRUCT->ONSTARTQ          : only by thread that constructed it
     *   IDLE->ONSTARTQ,RESETTING     : by any thread but must have m_QueueLock when transitioning
     *   ONSTARTQ->RUNNING,RESETTING  : only by thread that removed it from m_StartQueue
     *   ONYIELDQ->RUNNING,RESETTING  : only by thread that removed it from m_YieldQueue
     *   ONSLEEPQ->ONYIELDQ,RESETTING : only by thread that removed it from m_SleepQueue
     *   RUNNING->whatever1           : only by thread that transitioned it to RUNNING
     *                                  whatever1 = IDLE,ONSLEEPQ,ONYIELDQ,ONSTARTQ,SUSPENDED,FINISHED
     *   FINSHED->whatever2           : only by thread that transitioned it to FINISHED
     *                                  whatever2 = IDLE,ONSTARTQ,DISPOSED
     *   SUSPENDED->ONSTARTQ          : by any thread (NOT YET IMPLEMENTED, should be under some kind of lock?)
     *   RESETTING->ONSTARTQ          : only by the thread that transitioned it to RESETTING
     */
    public enum XMRInstState {
        CONSTRUCT,  // it is being constructed
        IDLE,       // nothing happening (finished last event and m_EventQueue is empty)
        ONSTARTQ,   // inserted on m_Engine.m_StartQueue
        RUNNING,    // currently being executed by RunOne()
        ONSLEEPQ,   // inserted on m_Engine.m_SleepQueue
        ONYIELDQ,   // inserted on m_Engine.m_YieldQueue
        FINISHED,   // just finished handling an event
        SUSPENDED,  // m_SuspendCount > 0
        RESETTING,  // being reset via external call
        DISPOSED    // has been disposed
    }

    public partial class XMRInstance : IDisposable
    {
        /******************************************************************\
         *  This module contains the instance variables for XMRInstance.  *
        \******************************************************************/

        public const int MAXEVENTQUEUE = 64;

        public static readonly DetectParams[] zeroDetectParams = new DetectParams[0];
        public static readonly object[] zeroObjectArray = new object[0];

        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // For a given m_AssetID, do we have the compiled object code and where
        // is it?  m_CompiledScriptRefCount keeps track of how many m_ObjCode
        // pointers are valid.
        public static object m_CompileLock = new object();
        private static Dictionary<UUID, ScriptObjCode> m_CompiledScriptObjCode = new Dictionary<UUID, ScriptObjCode>();
        private static Dictionary<UUID, int> m_CompiledScriptRefCount = new Dictionary<UUID, int>();

        public  XMRInstance  m_NextInst;  // used by XMRInstQueue
        public  XMRInstance  m_PrevInst;
        public  XMRInstState m_IState;

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
        private bool m_Die = false;
        private int m_StartParam = 0;
        private StateSource m_StateSource;
        public  string m_DescName;
        private bool m_DebugFlag = false;
        private UIntPtr m_StackSize;
        private ArrayList m_CompilerErrors;
        private DateTime m_LastRanAt = DateTime.MinValue;
        private string m_RunOnePhase = "hasn't run";
        private string m_CheckRunPhase = "hasn't checked";
        private int m_CheckRunLine = 0;
        public  int m_InstEHEvent  = 0;  // number of events dequeued (StartEventHandler called)
        public  int m_InstEHSlice  = 0;  // number of times handler timesliced (ResumeEx called)

        // If code needs to have both m_QueueLock and m_RunLock,
        // be sure to lock m_RunLock first then m_QueueLock, as
        // that is the order used in RunOne().
        // These locks are currently separated to allow the script
        // to call API routines that queue events back to the script.
        // If we just had one lock, then the queuing would deadlock.

        // guards m_EventQueue, m_TimerQueued, m_Running, m_LostEvents
        public Object m_QueueLock = new Object();

        // true iff allowed to accept new events
        private bool m_Running = true;

        // queue of events that haven't been acted upon yet
        public Queue<EventParams> m_EventQueue = new Queue<EventParams>();
        public int m_LostEvents;

        // true iff m_EventQueue contains a timer() event
        private bool m_TimerQueued = false;


        // locked whilst running on the microthread stack (or about to run on it or just ran on it)
        private Object m_RunLock = new Object();

        // script won't step while > 0.  bus-atomic updates only.
        private int m_SuspendCount = 0;

        // don't run any of script until this time
        public DateTime m_SleepUntil = DateTime.MinValue;

        private Dictionary<string,IScriptApi> m_Apis =
                new Dictionary<string,IScriptApi>();

        public int stateCode = 0;                 // state the script is in (0 = 'default')
        public ScriptEventCode eventCode = ScriptEventCode.None;
                                                  // event handler being executed (None when idle)
                                                  // - code assumes that only the event handling microthread
                                                  //   transitions eventCode from non-idle to idle
                                                  // - idle to non-idle is always Interlocked
        public ScriptBaseClass beAPI;             // passed as 'this' to methods such as llSay()
        public object[] ehArgs;                   // event handler argument array
        public bool stateChanged = false;         // script sets this if/when it executes a 'state' statement
        public bool doGblInit = true;             // default state_entry() needs to initialize global variables
        public ScriptObjCode objCode;             // the script's object code pointer
        public uint stackLimit;                   // CheckRun() must always see this much stack available
        public int heapLimit;                     // let script use this many bytes of heap maximum
                                                  // includes global vars, local vars, that reference heap
                                                  // does not include value-type vars, that is part of stackLimit
        public int heapLeft;                      // how much of heapLimit remains available

        /*
         * These arrays hold the global variable values for the script instance.
         * The array lengths are determined by the script compilation,
         * and are found in ScriptObjCode.numGblArrays, .numGblFloats, etc.
         */
        public XMR_Array[]    gblArrays;
        public float[]        gblFloats;
        public int[]          gblIntegers;
        public LSL_List[]     gblLists;
        public LSL_Rotation[] gblRotations;
        public string[]       gblStrings;
        public LSL_Vector[]   gblVectors;

        /*
         * We will use this microthread to run the scripts event handlers.
         */
        private ScriptUThread microthread;

        /*
         * Continuation layer to serialize/deserialize script stack.
         */
        public ScriptContinuation continuation;

        /*
         * Set to suspend microthread at next CheckRun() call.
         */
        public bool suspendOnCheckRunHold;
        public bool suspendOnCheckRunTemp;   // false: keep running
                                             //  true: suspend on next call to CheckRun()

        /*
         * Set to perform migration.
         */
        public BinaryReader migrateInReader;   // used to read script state from a file
        public Stream       migrateInStream;
        public BinaryWriter migrateOutWriter;  // used to write script state to a file
        public Stream       migrateOutStream;
        public bool         migrateComplete;   // false: migration in progress; true: migration complete

        /*
         * The subsArray[] is used by continuations to say which object references
         * it must translate between sender and receiver.
         *
         * Save()/Load() are 'smart' enough to translate any references it finds on
         * the stack to these objects on the receiving end without being explicitly 
         * told:
         *
         *   1) continuation object itself
         *   2) writer stream object references are translated to null
         *   3) subsArray object itself
         *   4) dllsArray object itself
         *
         * So any additional object references on the stack we want translated must 
         * be placed in subsArray[].
         *
         * Finally, any objects not in the list of (3) or in subsArray are passed to
         * the general serialize/deserialize routines and thus get reconstructed by
         * value.  This is how things like vectors and strings get sent.
         *
         * Bottom line is this array contains references to objects that can be active
         * when Save() is called, other than objects that are to be reconstructed by
         * serialization/deserialization.
         */
        public object[] subsArray = new object[(int)SubsArray.SIZE];

        /*
         * The dllsArray[] tells the continuations what DLL filename translation must
         * be done between the sender and receiver.
         *
         * Basically, unless the DLL has the same exact path on sending and receiving
         * systems, this must include all DLLs that have MMRContableAttribute() routines
         * in them.
         */
        public string[] dllsArray;

        /*
         * Makes sure migration data version is same on both ends.
         */
        public static readonly byte migrationVersion = 4;

        /**
         * @brief types of data we serialize
         */
        private enum Ser : byte {
            NULL,
            EVENTCODE,
            LSLFLOAT,
            LSLINT,
            LSLKEY,
            LSLLIST,
            LSLROT,
            LSLSTR,
            LSLVEC,
            OBJARRAY,
            SYSDOUB,
            SYSFLOAT,
            SYSINT,
            SYSSTR,
            XMRARRAY
        }
    }
}
