/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using Mono.Tasklets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting;
using System.Text;
using System.Threading;
using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;


namespace OpenSim.Region.ScriptEngine.XMREngine {

	/*
	 * Whenever a script changes state, it calls this method.
	 * scriptWrapper.stateCode is already set to the new state.
	 */
	public delegate void StateChangeDelegate ();

	/*
	 * Entrypoint to script event handlers.
	 */
	public delegate void ScriptEventHandler (ScriptWrapper scriptWrapper);

	/*
	 * All scripts must inherit from this class.
	 */
	public class ScriptWrapper : IDisposable {
		public static UIntPtr stackSize = (UIntPtr)(2*1024*1024);  // microthreads get this stack size
		public static readonly int COMPILED_VERSION_VALUE = 4;     // incrmented when compiler changes for compatibility testing

		public string instanceNo;                 // debugging use only

		public int stateCode = 0;                 // state the script is in (0 = 'default')
		public ScriptEventCode eventCode = ScriptEventCode.None;
		                                          // event handler being executed (None when idle)
		                                          // - code assumes that only the event handling microthread
		                                          //   transitions eventCode from non-idle to idle
		                                          // - idle to non-idle is always Interlocked
		public StateChangeDelegate stateChange;   // called when script changes state
		public ScriptBaseClass beAPI;             // passed as 'this' to methods such as llSay()
		public object[] ehArgs;                   // event handler argument array
		public int memUsage = 0;                  // script's current memory usage
		public int memLimit = 100000;             // CheckRun() throws exception if memUsage > memLimit
		public bool stateChanged = false;         // script sets this if/when it executes a 'state' statement
		public bool doGblInit = true;             // default state_entry() needs to initialize global variables
		public ScriptObjCode objCode;             // the script's object code pointer

		public bool debPrint = false;

		/*
		 * These arrays hold the global variable values for the script instance.
		 * The array lengths are determined by the script compilation.
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
		public bool suspendOnCheckRun;   // false: keep running
		                                 //  true: suspend on next call to CheckRun()
		public bool alwaysSuspend;       // false: normal operation
		                                 //  true: always suspend CheckRun()

		/*
		 * Set to perform migration.
		 */
		public Stream       migrateInStream;   // null: normal start at event handler's entrypoint
		public BinaryReader migrateInReader;   // else: restore position in event handler from stream and resume
		public Stream       migrateOutStream;  // null: continue executing event handler
		public BinaryWriter migrateOutWriter;  // else: write position of event handler to stream and exit
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
		public enum SubsArray {
			SCRIPT = 0,
			BEAPI,
			SIZE
		};
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
		 * Used by script method Dispose() method for debugging.
		 */
		public static bool         disposed_bool     = false;
		public static float        disposed_float    = 69696969.0f;
		public static int          disposed_integer  = 69696969;
		public static LSL_Key      disposed_key      = new LSL_Key ("99999999-6666-9999-6666-999999999999");
		public static LSL_List     disposed_list     = new LSL_List ("DISPOSED SCRIPT");
		public static LSL_Rotation disposed_rotation = new LSL_Rotation (69696969.0f, 69696969.0f, 69696969.0f, 69696969.0f);
		public static string       disposed_string   = "DISPOSED SCRIPT";
		public static LSL_Vector   disposed_vector   = new LSL_Vector (69696969.0f, 69696969.0f, 69696969.0f);

		/*
		 * Makes sure migration data version is same on both ends.
		 */
		public static readonly byte migrationVersion = 3;

		/*
		 * Whether or not we are using the microthread.
		 */
		private int usingMicrothread = 0;

		/*
		 * Open the script DLL and get ready to run event handlers.
		 * Set the initial state to "default".
		 *
		 * Caller should call StartEventHandler() or MigrateInEventHandler() next.
		 * If calling StartEventHandler(), use ScriptEventCode.state_entry with no args.
		 */
		public ScriptWrapper (ScriptObjCode objCode, string descName)
		{
			string envar = Environment.GetEnvironmentVariable ("MMRScriptWrapperDebPrint");
			this.debPrint = ((envar != null) && ((envar[0] & 1) != 0));
			instanceNo = MMRCont.HexString (MMRCont.ObjAddr (this));
			if (instanceNo.Length < 8) {
				instanceNo = "00000000".Substring (0, 8 - instanceNo.Length) + instanceNo;
			}
			DebPrint ("ScriptWrapper*: {0} created", instanceNo);

			this.objCode      = objCode;

			this.gblArrays    = new XMR_Array[objCode.numGblArrays];
			this.gblFloats    = new float[objCode.numGblFloats];
			this.gblIntegers  = new int[objCode.numGblIntegers];
			this.gblLists     = new LSL_List[objCode.numGblLists];
			this.gblRotations = new LSL_Rotation[objCode.numGblRotations];
			this.gblStrings   = new string[objCode.numGblStrings];
			this.gblVectors   = new LSL_Vector[objCode.numGblVectors];

			for (int i = 0; i < objCode.numGblArrays; i ++) {
				this.gblArrays[i]  = new XMR_Array ();
			}
			for (int i = 0; i < objCode.numGblLists; i ++) {
				this.gblLists[i]   = new LSL_List (new object[0]);
			}
			for (int i = 0; i < objCode.numGblStrings; i ++) {
				this.gblStrings[i] = String.Empty;
			}

			/*
			 * Set up debug name string.
			 */
			this.instanceNo += "/" + descName;

			/*
			 * Set up sub-objects and cross-polinate so everything can access everything.
			 */
			this.microthread  = new ScriptUThread (descName);
			this.continuation = new ScriptContinuation ();
			this.microthread.scriptWrapper  = this;
			this.continuation.scriptWrapper = this;

			/*
			 * We do our own object serialization.
			 * It avoids null pointer refs and is much more efficient because we
			 * have a limited number of types to deal with.
			 */
			this.continuation.sendObj = this.SendObjInterceptor;
			this.continuation.recvObj = this.RecvObjInterceptor;

			/*
			 * Constant subsArray values...
			 */
			this.subsArray[(int)SubsArray.SCRIPT] = this;

			/*
			 * All the DLL filenames should be known at this point,
			 * so fill in the entries needed.
			 *
			 * These have to be the exact string returned by mono_image_get_filename().
			 * To find out which DLLs are needed, set envar MMRCONTSAVEDEBUG=1 and observe
			 * debug output to see which DLLs are referenced.
			 */
			this.dllsArray = new string[3];
			this.dllsArray[0] = MMRCont.GetDLLName (typeof (ScriptWrapper));  // ...MMRCont.dll
			this.dllsArray[1] = MMRCont.GetDLLName (typeof (MMRCont));        // ...Mono.Tasklets.dll
			this.dllsArray[2] = MMRCont.GetDLLName (typeof (LSL_Vector));     // ...ScriptEngine.Shared.dll
		}

		public void DebPrint (string format, object arg0)
		{
			DebPrint (format, arg0, null, null, null);
		}

		public void DebPrint (string format, object arg0, object arg1)
		{
			DebPrint (format, arg0, arg1, null, null);
		}

		public void DebPrint (string format, object arg0, object arg1, object arg2)
		{
			DebPrint (format, arg0, arg1, arg2, null);
		}

		public void DebPrint (string format, object arg0, object arg1, object arg2, object arg3)
		{
			if (this.debPrint) {
				DateTime now = DateTime.Now;
				String nowStr = String.Format ("{0:D2}:{1:D2}:{2:D2} ", now.Hour, now.Minute, now.Second);
				Console.WriteLine (nowStr + format, arg0, arg1, arg2, arg3);
			}
		}

		/*
		 * Called by scripts before and after all beAPI calls if compiled with envar MMRScriptCompileTraceAPICalls=1.
		 *
		 * Input:
		 *   line = .lsl script source line number of call
		 *   signature = signature of function being called: name(argtype,argtype,...)
		 *   args = array of arg values passed to function
		 *   isCall = true: this is before the call
		 *           false: this is after the call (returned)
		 *   retval = null: either this is before the call or function returns void
		 *            else: return value
		 */
		public void TraceAPICall (int line, string signature, object[] args, bool isCall, object retval)
		{
			StringBuilder call = new StringBuilder ();
			call.Append (signature.Substring (0, signature.IndexOf ('(') + 1));
			for (int i = 0; i < args.Length; i ++) {
				if (i > 0) call.Append (',');
				call.Append (args[i].ToString ());
			}
			call.Append (isCall ? ") call" : ") rtn ");
			if (retval != null) call.Append (retval.ToString ());
			DebPrint ("TraceAPICall*: {0} {1} {2}", instanceNo, line, call.ToString ());
		}

		/*
		 * All done with object, it's not usable after this.
		 */
		public virtual void Dispose ()
		{
			DebPrint ("ScriptWrapper*: {0} disposing", instanceNo);
			if (this.microthread != null) {
				this.microthread.Dispose ();
				this.continuation = null;
				this.microthread = null;

				///??? debug ???///
				this.beAPI = null;
				this.stateCode = 12345678;
				this.eventCode = ScriptEventCode.Garbage;
				this.ehArgs  = null;
				this.objCode = null;
				this.migrateInStream  = null;
				this.migrateInReader  = null;
				this.migrateOutStream = null;
				this.migrateOutWriter = null;
				this.subsArray = null;
			}
		}

		/**
		 * @brief Decode state code (int) to state name (string).
		 */
		public string GetStateName (int stateCode)
		{
			if ((objCode.stateNames != null) && (stateCode >= 0) && (stateCode < objCode.stateNames.Length)) {
				return objCode.stateNames[stateCode];
			}
			return stateCode.ToString ();
		}

		/*
		 * Start event handler.
		 * Event handler is put in suspended state at its entrypoint.
		 * This method runs in minimal time.
		 * Call ResumeEventHandler() to keep it running.
		 *
		 * Input:
		 *	eventCode  = code of event to be processed
		 *	ehArgs     = arguments for the event handler
		 *	this.beAPI = 'this' pointer passed to things like llSay()
		 *
		 * Caution:
		 *  It is up to the caller to make sure ehArgs[] is correct for
		 *  the particular event handler being called.  The first thing
		 *  a script event handler method does is to unmarshall the args
		 *  from ehArgs[] and will throw an array bounds or cast exception 
		 *  if it can't.
		 */
		public void StartEventHandler (string eventName, object[] ehArgs)
		{
			StartEventHandler ((ScriptEventCode)Enum.Parse (typeof (ScriptEventCode), eventName), ehArgs);
		}

		public void StartEventHandler (ScriptEventCode eventCode, object[] ehArgs)
		{
			Exception except = null;

			if (debPrint) {
				StringBuilder dumpargs = new StringBuilder ();
				dumpargs.Append (GetStateName (this.stateCode));
				dumpargs.Append (":");
				dumpargs.Append (eventCode.ToString ());
				dumpargs.Append ("(");
				for (int i = 0; i < ehArgs.Length; i ++) {
					if (i > 0) {
						dumpargs.Append (",");
					}
					dumpargs.Append (ehArgs[i].GetType().ToString());
					dumpargs.Append (" ");
					dumpargs.Append (ehArgs[i].ToString());
				}
				dumpargs.Append (")");
				DebPrint ("StartEventHandler*: {0} {1}", this.instanceNo, dumpargs.ToString ());
			}

			/*
			 * We use this.eventCode == ScriptEventCode.None to indicate we are idle.
			 * So trying to execute ScriptEventCode.None might make a mess.
			 */
			if (eventCode == ScriptEventCode.None) {
				throw new Exception ("Can't process ScriptEventCode.None");
			}

			/*
			 * This lock should always be uncontented as we should never try to start
			 * executing one event handler while the previous one is still going.
			 */
			LockMicrothread (1);
			if (this.eventCode != ScriptEventCode.None) {
				except = new Exception ("Event handler already active");
			}

			/*
			 * Silly to even try if there is no handler defined for this event.
			 */
			else if (this.objCode.scriptEventHandlerTable[this.stateCode,(int)eventCode] != null) {

				/*
				 * Save eventCode so we know what event handler to run in the microthread.
				 * And it also marks us busy so we can't be started again and this event lost.
				 */
				this.eventCode = eventCode;
				this.ehArgs    = ehArgs;

				/*
				 * We are starting from beginning of event handler, no migration streams.
				 */
				this.migrateInStream  = null;
				this.migrateInReader  = null;
				this.migrateOutStream = null;
				this.migrateOutWriter = null;

				/*
				 * This calls Main() below directly, and returns when Main() calls Suspend()
				 * or when Main() returns, whichever occurs first.  It should return quickly.
				 */
				DebPrint ("StartEventHandler*: {0} this.eventCode={1}", this.instanceNo, this.eventCode);
				except = microthread.StartEx ();
			}
			UnlkMicrothread (1);

			if (except != null) throw except;
		}

		/*
		 * Migrate an event handler in from somewhere else and suspend it.
		 *
		 * Input:
		 *	stream = as generated by MigrateOutEventHandler()
		 *	this.beAPI = 'this' pointer passed to things like llSay()
		 */
		public void MigrateInEventHandler (Stream stream)
		{

			/*
			 * This lock should always be uncontented as we should never try to start
			 * executing one event handler while the previous one is still going.
			 */
			LockMicrothread (2);
			try {
				if (this.eventCode != ScriptEventCode.None) {
					throw new Exception ("Event handler already active");
				}

				/*
				 * Set up to migrate state in from the network stream.
				 */
				this.migrateInStream  = stream;
				this.migrateInReader  = new BinaryReader (stream);
				this.migrateOutStream = null;
				this.migrateOutWriter = null;

				/*
				 * Read current state code and event code from stream.
				 * And it also marks us busy (by setting this.eventCode) so we can't be
				 * started again and this event lost.
				 */
				int mv = stream.ReadByte ();
				if (mv != migrationVersion) {
					throw new Exception ("incoming migration version " + mv + " but accept only " + migrationVersion);
				}
				this.stateCode = (int)this.continuation.recvObj (stream);
				this.eventCode = (ScriptEventCode)this.continuation.recvObj (stream);
				this.memUsage  = (int)this.continuation.recvObj (stream);
				this.memLimit  = (int)this.continuation.recvObj (stream);
				this.ehArgs    = (object[])this.continuation.recvObj (stream);

				/*
				 * Read script globals in.
				 */
				this.MigrateScriptIn (stream, this.continuation.recvObj);

				/*
				 * If eventCode is None, it means the script was idle when migrated.
				 * So we don't have to read script's stack in.
				 */
				if (this.eventCode != ScriptEventCode.None) {

					/*
					 * This calls Main() below directly, and returns when Main() calls Suspend()
					 * or when Main() returns, whichever occurs first.  It should return quickly.
					 */
					this.migrateComplete = false;
					microthread.Start ();
					if (!this.migrateComplete) throw new Exception ("migrate in did not complete");
				}

				/*
				 * Clear out migration state.
				 */
				//this.migrateInReader.Dispose ();  // moron thing doesn't compile
				this.migrateInStream = null;
				this.migrateInReader = null;
			} finally {
				UnlkMicrothread (2);
			}
		}

		/*
		 * Resume a suspended event handler.
		 * This method is called by a macrothread dedicated to running event handlers,
		 * typically by ringOfActiveScripts.RunForever().
		 * It may run for significant wall-clock time.
		 * It runs until one of the below conditions is satisfied.
		 *
		 * Returns:
		 *
		 *	true:
		 *		script event handler completed or was aborted after
		 *		begin migrated out with MigrateOutEventHandler()
		 *
		 *		the caller can call StartEventHandler() or
		 *		MigrateInEventHandler() to start another event handler
		 *
		 *	false:
		 *		some thread called SuspendEventHandler()
		 *		...and the event handler is now suspended
		 *		this caller should call ResumeEventHandler() when ready
		 */
		public bool ResumeEventHandler ()
		{
			bool idle;
			Exception except;

			/*
			 * Execute script until it gets suspended, migrated out or it completes.
			 */
			LockMicrothread (3);
			except = null;
			ScriptEventCode ec = eventCode;
			if (ec != ScriptEventCode.None) {
				except = microthread.ResumeEx (except);
			}
			idle = (eventCode == ScriptEventCode.None);
			DebPrint ("ResumeEventHandler*: {0} idle={1}", this.instanceNo, idle);
			UnlkMicrothread (3);
			if (except != null) {
				throw except;
			}

			/*
			 * Tell the caller whether or not we have anything more to do.
			 */
			return idle;
		}


		/*
		 * Call on any thread to suspend an event handler.
		 * If event handler is already suspended, the next resume will immediately suspend.
		 */
		public void SuspendEventHandler ()
		{
			this.suspendOnCheckRun = true;
		}

		/*
		 * Called by any macrothread to migrate an event handler out to somewhere else.
		 * May take significant wall-clock time to suspend event handler and write the
		 * state to the stream.
		 *
		 * Input:
		 *	stream = stream to write event handler state information to
		 *
		 * Output:
		 *	returns true: script state written to stream, including stack
		 *             false: script was idle, only basic state written
		 */
		public bool MigrateOutEventHandler (Stream stream)
		{
			bool written;

			/*
			 * Lock to keep eventCode stable.
			 * This in essence makes sure the microthread is not executing,
			 * so it is either suspended in CheckRun() or has completed.
			 *
			 * It may take a while for the script to actually suspend.
			 */
			this.suspendOnCheckRun = true;
			LockMicrothread (4);
			try {

				/*
				 * The script microthread should be at its Suspend() call within
				 * CheckRun(), unless it has exited.  Tell CheckRun() that it 
				 * should migrate the script out then suspend.
				 */
				this.migrateOutStream = stream;
				this.migrateOutWriter = new BinaryWriter (stream);
				this.migrateInStream  = null;
				this.migrateInReader  = null;

				/*
				 * Write the basic information out to the stream:
				 *    state, event, eventhandler args, script's globals.
				 */
				stream.WriteByte (migrationVersion);
				this.SendObjInterceptor (stream, this.stateCode);
				this.SendObjInterceptor (stream, this.eventCode);
				this.SendObjInterceptor (stream, this.memUsage);
				this.SendObjInterceptor (stream, this.memLimit);
				this.SendObjInterceptor (stream, this.ehArgs);
				this.MigrateScriptOut (stream, this.SendObjInterceptor);

				/*
				 * Tell caller whether we are actually writing a suspended or idle state out.
				 * SUSPENDED state is with a stack and which eventCode it was processing.
				 * IDLE state is without a stack and ScriptEventCode.None indicating it was idle.
				 */
				ScriptEventCode ec = this.eventCode;
				written = (ec != ScriptEventCode.None);

				/*
				 * Resume the microthread to actually write the network stream.
				 * When it finishes it will suspend, causing the microthreading
				 * to return here.
				 */
				if (written) {
					this.migrateComplete = false;
					Exception except = this.microthread.ResumeEx (null);
					if (except != null) {
						throw except;
					}
					if (!this.migrateComplete) throw new Exception ("migrate out did not complete");
				}

				/*
				 * No longer migrating.
				 */
				//this.migrateOutWriter.Dispose ();  // moron thing doesn't compile
				this.migrateOutStream = null;
				this.migrateOutWriter = null;
			} finally {
				UnlkMicrothread (4);
			}

			return written;
		}

		/*
		 * These routines make sure we aren't trying to use the microthread
		 * more than once at a time.  They are pure debugging and can be
		 * removed if so desired.
		 */
		private void LockMicrothread (int key)
		{
			DebPrint ("LockMicrothread*: {0} locking with {1}", this.instanceNo, key);
			int was = Interlocked.Exchange (ref usingMicrothread, key);
			if (was != 0) {
				throw new Exception ("usingMicrothread lock failed, was " + was.ToString ());
			}
			DebPrint ("LockMicrothread*: {0} locked with {1}", this.instanceNo, key);
		}

		private void UnlkMicrothread (int key)
		{
			DebPrint ("UnlkMicrothread*: {0} unlocking with {1}", this.instanceNo, key);
			int was = Interlocked.Exchange (ref usingMicrothread, 0);
			if (was != key) {
				throw new Exception ("usingMicrothread unlock failed");
			}
			DebPrint ("UnlkMicrothread*: {0} unlocked with {1}", this.instanceNo, key);
		}

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

		/**
		 * @brief called by continuation.Save() for every object to
		 *        be sent over the network.
		 * @param stream = network stream to send it over
		 *                 should be same as this.migrateOutStream
		 * @param graph = object to send
		 */
		private void SendObjInterceptor (Stream stream, object graph)
		{
			if (graph == null) {
				this.migrateOutWriter.Write ((byte)Ser.NULL);
			} else if (graph is ScriptEventCode) {
				this.migrateOutWriter.Write ((byte)Ser.EVENTCODE);
				this.migrateOutWriter.Write ((int)graph);
			} else if (graph is LSL_Float) {
				this.migrateOutWriter.Write ((byte)Ser.LSLFLOAT);
				this.migrateOutWriter.Write ((float)((LSL_Float)graph).value);
			} else if (graph is LSL_Integer) {
				this.migrateOutWriter.Write ((byte)Ser.LSLINT);
				this.migrateOutWriter.Write ((int)((LSL_Integer)graph).value);
			} else if (graph is LSL_Key) {
				this.migrateOutWriter.Write ((byte)Ser.LSLKEY);
				LSL_Key key = (LSL_Key)graph;
				SendObjInterceptor (stream, key.m_string);  // m_string can be null
			} else if (graph is LSL_List) {
				this.migrateOutWriter.Write ((byte)Ser.LSLLIST);
				LSL_List list = (LSL_List)graph;
				SendObjInterceptor (stream, list.Data);
			} else if (graph is LSL_Rotation) {
				this.migrateOutWriter.Write ((byte)Ser.LSLROT);
				this.migrateOutWriter.Write ((double)((LSL_Rotation)graph).x);
				this.migrateOutWriter.Write ((double)((LSL_Rotation)graph).y);
				this.migrateOutWriter.Write ((double)((LSL_Rotation)graph).z);
				this.migrateOutWriter.Write ((double)((LSL_Rotation)graph).s);
			} else if (graph is LSL_String) {
				this.migrateOutWriter.Write ((byte)Ser.LSLSTR);
				LSL_String str = (LSL_String)graph;
				SendObjInterceptor (stream, str.m_string);  // m_string can be null
			} else if (graph is LSL_Vector) {
				this.migrateOutWriter.Write ((byte)Ser.LSLVEC);
				this.migrateOutWriter.Write ((double)((LSL_Vector)graph).x);
				this.migrateOutWriter.Write ((double)((LSL_Vector)graph).y);
				this.migrateOutWriter.Write ((double)((LSL_Vector)graph).z);
			} else if (graph is XMR_Array) {
				this.migrateOutWriter.Write ((byte)Ser.XMRARRAY);
				((XMR_Array)graph).SendArrayObj (this.SendObjInterceptor, stream);
			} else if (graph is object[]) {
				this.migrateOutWriter.Write ((byte)Ser.OBJARRAY);
				object[] array = (object[])graph;
				int len = array.Length;
				this.migrateOutWriter.Write (len);
				for (int i = 0; i < len; i ++) {
					SendObjInterceptor (stream, array[i]);
				}
			} else if (graph is double) {
				this.migrateOutWriter.Write ((byte)Ser.SYSDOUB);
				this.migrateOutWriter.Write ((double)graph);
			} else if (graph is float) {
				this.migrateOutWriter.Write ((byte)Ser.SYSFLOAT);
				this.migrateOutWriter.Write ((float)graph);
			} else if (graph is int) {
				this.migrateOutWriter.Write ((byte)Ser.SYSINT);
				this.migrateOutWriter.Write ((int)graph);
			} else if (graph is string) {
				this.migrateOutWriter.Write ((byte)Ser.SYSSTR);
				this.migrateOutWriter.Write ((string)graph);
			} else {
				throw new Exception ("unhandled class " + graph.GetType().ToString());
			}
		}

		private object RecvObjInterceptor (Stream stream)
		{
			Ser code = (Ser)this.migrateInReader.ReadByte ();
			switch (code) {
				case Ser.NULL: {
					return null;
				}
				case Ser.EVENTCODE: {
					return (ScriptEventCode)this.migrateInReader.ReadInt32 ();
				}
				case Ser.LSLFLOAT: {
					return new LSL_Float (this.migrateInReader.ReadSingle ());
				}
				case Ser.LSLINT: {
					return new LSL_Integer (this.migrateInReader.ReadInt32 ());
				}
				case Ser.LSLKEY: {
					return new LSL_Key ((string)RecvObjInterceptor (stream));
				}
				case Ser.LSLLIST: {
					object[] array = (object[])RecvObjInterceptor (stream);
					return new LSL_List (array);
				}
				case Ser.LSLROT: {
					double x = this.migrateInReader.ReadDouble ();
					double y = this.migrateInReader.ReadDouble ();
					double z = this.migrateInReader.ReadDouble ();
					double s = this.migrateInReader.ReadDouble ();
					return new LSL_Rotation (x, y, z, s);
				}
				case Ser.LSLSTR: {
					return new LSL_String ((string)RecvObjInterceptor (stream));
				}
				case Ser.LSLVEC: {
					double x = this.migrateInReader.ReadDouble ();
					double y = this.migrateInReader.ReadDouble ();
					double z = this.migrateInReader.ReadDouble ();
					return new LSL_Vector (x, y, z);
				}
				case Ser.OBJARRAY: {
					int len = this.migrateInReader.ReadInt32 ();
					object[] array = new object[len];
					for (int i = 0; i < len; i ++) {
						array[i] = RecvObjInterceptor (stream);
					}
					return array;
				}
				case Ser.SYSDOUB: {
					return this.migrateInReader.ReadDouble ();
				}
				case Ser.SYSFLOAT: {
					return this.migrateInReader.ReadSingle ();
				}
				case Ser.SYSINT: {
					return this.migrateInReader.ReadInt32 ();
				}
				case Ser.SYSSTR: {
					return this.migrateInReader.ReadString ();
				}
				case Ser.XMRARRAY: {
					XMR_Array array = new XMR_Array ();
					array.RecvArrayObj (this.RecvObjInterceptor, stream);
					return array;
				}
				default: throw new Exception ("bad stream code " + code.ToString ());
			}
		}


		/*****************************************************************\
		 *  Wrapper around continuation to enclose it in a microthread.  *
		\*****************************************************************/

		private class ScriptUThread : MMRUThread {

			public ScriptWrapper scriptWrapper;  // script wrapper we belong to

			public ScriptUThread (string descName) : base (ScriptWrapper.stackSize, descName) { }

			/*
			 * Called on the microthread stack as part of Start().
			 * Start() returns when this method calls Suspend() or
			 * when this method returns (whichever happens first).
			 */
			public override void Main ()
			{
				Exception except;

				/*
				 * The normal case is this script event handler is just being
				 * called directly at its entrypoint.  The RunItEx() method
				 * calls RunCont() below.  Any exceptions thrown by RunCont()
				 * are returned by RunItEx().
				 */
				if (scriptWrapper.migrateInStream == null) {
					except = scriptWrapper.continuation.RunItEx ();
				} else {

					/*
					 * The other case is that we want to resume execution of
					 * a script from its migration data.  So this reads the
					 * data from the stream to recreate wherever RunCont()
					 * called Save() from, then it jumps to that point.
					 *
					 * In our case, that point is always within our CheckRun()
					 * method, which immediately suspends when the restore is
					 * complete, which causes LoadEx() to return at that time.
					 */
					scriptWrapper.subsArray[(int)ScriptWrapper.SubsArray.BEAPI] = scriptWrapper.beAPI;
					except = scriptWrapper.continuation.LoadEx (scriptWrapper.migrateInStream, 
					                                            scriptWrapper.subsArray,
					                                            scriptWrapper.dllsArray);
				}

				if (except != null) throw except;
			}
		}

		/**
		 * @brief Write script global variables to the output stream.
		 */
		public void MigrateScriptOut (System.IO.Stream stream, MMRContSendObj sendObj)
		{
			SendGblArray (stream, sendObj, gblArrays);
			SendGblArray (stream, sendObj, gblFloats);
			SendGblArray (stream, sendObj, gblIntegers);
			SendGblArray (stream, sendObj, gblLists);
			SendGblArray (stream, sendObj, gblRotations);
			SendGblArray (stream, sendObj, gblStrings);
			SendGblArray (stream, sendObj, gblVectors);
		}

		private void SendGblArray (System.IO.Stream stream, MMRContSendObj sendObj, Array array)
		{
			sendObj (stream, (object)(int)array.Length);
			for (int i = 0; i < array.Length; i ++) {
				sendObj (stream, array.GetValue (i));
			}
		}

		/**
		 * @brief Read script global variables from the input stream.
		 */
		public void MigrateScriptIn (System.IO.Stream stream, MMRContRecvObj recvObj)
		{
			gblArrays    = (XMR_Array[])   RecvGblArray (stream, recvObj, typeof (XMR_Array));
			gblFloats    = (float[])       RecvGblArray (stream, recvObj, typeof (float));
			gblIntegers  = (int[])         RecvGblArray (stream, recvObj, typeof (int));
			gblLists     = (LSL_List[])    RecvGblArray (stream, recvObj, typeof (LSL_List));
			gblRotations = (LSL_Rotation[])RecvGblArray (stream, recvObj, typeof (LSL_Rotation));
			gblStrings   = (string[])      RecvGblArray (stream, recvObj, typeof (string));
			gblVectors   = (LSL_Vector[])  RecvGblArray (stream, recvObj, typeof (LSL_Vector));
		}

		private Array RecvGblArray (System.IO.Stream stream, MMRContRecvObj recvObj, Type eleType)
		{
			int length = (int)recvObj (stream);
			Array array = Array.CreateInstance (eleType, length);
			for (int i = 0; i < length; i ++) {
				array.SetValue (recvObj (stream), i);
			}
			return array;
		}
	}


	/****************************************************************\
	 *  Wrapper around script event handler so it can be migrated.  *
	\****************************************************************/

	// needs to be public so it will be seen by mmrcont_load()
	public class ScriptContinuation : MMRCont {

		public ScriptWrapper scriptWrapper;  // script wrapper we belong to

		/*
		 * Called by RunItEx() to start the event handler at its entrypoint.
		 */
		[MMRContableAttribute ()]
		public override Exception RunContEx ()
		{
			Exception except  = null;
			int oldStateCode  = -1;
			ScriptWrapper sw  = scriptWrapper;

			try {
				int newStateCode;
				ScriptEventHandler seh;

				/*
				 * Immediately suspend so the StartEventHandler() method
				 * returns with minimal delay.  We will resume from this
				 * point when microthread.Resume() is called.
				 */
				sw.suspendOnCheckRun = true;
				this.CheckRun ();

				/*
				 * Process event given by 'stateCode' and 'eventCode'.
				 * The event handler should call CheckRun() as often as convenient.
				 *
				 * We do not have to check for null 'seh' here because
				 * StartEventHandler() already checked the table entry.
				 */
				sw.stateChanged = false;
				oldStateCode = sw.stateCode;
				sw.DebPrint ("RunContEx*: {0} {1}:{2} begin", 
						sw.instanceNo, 
						sw.GetStateName (oldStateCode), 
						sw.eventCode);
				seh = sw.objCode.scriptEventHandlerTable[oldStateCode,(int)sw.eventCode];
				seh (sw);

				sw.ehArgs = null;  // we are done with them and no args for
				                   // exit_state()/enter_state() anyway

				/*
				 * Note that 'seh' is now invalid, as the continuation restore cannot restore it.
				 * But mono should see that 'seh' is no longer needed and so Save() shouldn't try
				 * to save it, theoretically.  Likewise for the other uses of 'seh' below.
				 */

				/*
				 * If event handler changed state, call exit_state() on the old state,
				 * change the state, then call enter_state() on the new state.
				 */
				while (sw.stateChanged) {

					/*
					 * Get what state they transitioned to.
					 */
					newStateCode = sw.stateCode;

					/*
					 * Maybe print out what happened.
					 */
					sw.DebPrint ("RunContEx*: {0} {1}:{2} -> {3}", 
							sw.instanceNo, sw.GetStateName (oldStateCode), 
							sw.eventCode, sw.GetStateName (newStateCode));

					/*
					 * Restore to old state and call its state_exit() handler.
					 */
					sw.stateChanged = false;
					sw.eventCode = ScriptEventCode.state_exit;
					sw.stateCode = oldStateCode;
					seh = sw.objCode.scriptEventHandlerTable[oldStateCode,(int)ScriptEventCode.state_exit];
					if (seh != null) seh (sw);

					/*
					 * Ignore any state change by state_exit() handlers.
					 */
					if (sw.stateChanged) {
						sw.DebPrint ("RunContEx*: {0} ignoring {1}:state_exit() state change -> {2}", 
								sw.instanceNo, sw.GetStateName (oldStateCode), 
								sw.GetStateName (sw.stateCode));
					}

					/*
					 * Now that the old state can't possibly start any more activity,
					 * cancel any listening handlers, etc, of the old state.
					 */
					sw.stateCode = newStateCode;
					sw.stateChange ();

					/*
					 * Now the new state becomes the old state in case the new state_entry() 
					 * changes state again.
					 */
					oldStateCode = newStateCode;

					/*
					 * Call the new state's state_entry() handler.
					 * I've seen scripts that change state in the state_entry() handler, 
					 * so allow for that by looping back to check sw.stateChanged again.
					 */
					sw.stateChanged = false;
					sw.eventCode = ScriptEventCode.state_entry;
					seh = sw.objCode.scriptEventHandlerTable[newStateCode,(int)ScriptEventCode.state_entry];
					if (seh != null) seh (sw);
				}
			} catch (Exception e) {
				except = e;
			}

			/*
			 * The event handler has run to completion.
			 */
			sw.DebPrint ("RunContEx*: {0} {1}:{2} done", 
					sw.instanceNo, sw.GetStateName (oldStateCode), sw.eventCode);
			sw.eventCode = ScriptEventCode.None;
			return except;
		}

		/*
		 * The script code should call this routine whenever it is
		 * convenient to perform a migation or switch microthreads.
		 *
		 * Note:  I tried moving this to ScriptWrapper itself but Save() chokes on it because it gets wrapped
		 *        by a marshalling wrapper, presumably because ScriptWrapper is tagged with MarshalByRefObj.
		 */
		[MMRContableAttribute ()]
		public void CheckRun ()
		{
			scriptWrapper.DebPrint ("CheckRun*: {0} {1}:{2} entry", 
					scriptWrapper.instanceNo, scriptWrapper.GetStateName (scriptWrapper.stateCode), scriptWrapper.eventCode);

			scriptWrapper.suspendOnCheckRun |= scriptWrapper.alwaysSuspend;

			/*
			 * We should never try to stop with stateChanged as once stateChanged is set to true,
			 * the compiled script functions all return directly out without calling CheckRun().
			 *
			 * Thus any checkpoint/restart save/resume code can assume stateChanged = false.
			 */
			if (scriptWrapper.stateChanged) throw new Exception ("CheckRun() called with stateChanged set");

			/*
			 * Make sure script isn't hogging too much memory.
			 */
			int mu = scriptWrapper.memUsage;
			int ml = scriptWrapper.memLimit;
			if (mu > ml) {
				throw new Exception ("memory usage " + mu + " exceeds limit " + ml);
			}

			while (scriptWrapper.suspendOnCheckRun || (scriptWrapper.migrateOutStream != null)) {

				/*
				 * See if MigrateOutEventHandler() has been called.
				 * If so, dump our stack to the stream then suspend.
				 */
				if (scriptWrapper.migrateOutStream != null) {

					/*
					 * Puque our stack to the output stream.
					 * But otherwise, our state remains intact.
					 */
					scriptWrapper.subsArray[(int)ScriptWrapper.SubsArray.BEAPI] = scriptWrapper.beAPI;
					this.Save (scriptWrapper.migrateOutStream, scriptWrapper.subsArray, scriptWrapper.dllsArray);

					/*
					 * We return here under two circumstances:
					 *  1) the script state has been written out to the migrateOutStream
					 *  2) the script state has been read in from the migrateOutStream
					 */
					scriptWrapper.migrateComplete = true;

					/*
					 * Suspend immediately.
					 * If we were migrating out, the MMRUThread.Suspend() call below will return
					 *    to the microthread.Resume() call within MigrateOutEventHandler().
					 * If we were migrating in, the MMRUThread.Suspend() call below will return
					 *    to the microthread.Start() call within MigrateInEventHandler().
					 */
					scriptWrapper.suspendOnCheckRun = true;
				}

				/*
				 * Maybe SuspendEventHandler() has been called.
				 */
				if (scriptWrapper.suspendOnCheckRun) {
					scriptWrapper.suspendOnCheckRun = false;
					MMRUThread.Suspend ();
				}
			}

			scriptWrapper.DebPrint ("CheckRun*: {0} {1}:{2} exit", 
					scriptWrapper.instanceNo, scriptWrapper.GetStateName (scriptWrapper.stateCode), scriptWrapper.eventCode);
		}

		/**
		 * @brief Called by internals of Load() to see if we know where the new method is
		 *        for a given old method.
		 * @param methName = method name, eg, "Save"
		 * @param sigDesc = signature, eg, "object,System.IO.BinaryWriter"
		 * @param className = class, eg, "Continuation"
		 * @param classNameSpace = name space, eg, "Mono.Tasklets"
		 * @param imageName = image name, eg, ".../gac/Mono.Tasklets/2.0.0.0__0738eb9f132ed756/Mono.Tasklets.dll"
		 * @returns 0: not known, go find it in DLL somewhere
		 *       else: methodInfo.MethodHandle.Value of corresponding method = MonoMethod struct pointer
		 */
		public IntPtr LoadFindMethod (string methName, string sigDesc, string className, string classNameSpace, string imageName)
		{
			MethodInfo methodInfo;

			/*
			 * All our names are superfunky with $MMRContableAttribute$ and the asset ID, so
			 * all we do is catalog them by function name which is always going to be unique.
			 */
			if (scriptWrapper.objCode.dynamicMethods.TryGetValue (methName, out methodInfo)) {
				return methodInfo.MethodHandle.Value;
			}
			return (IntPtr)0;
		}
	}

	/**
	 * @brief Array objects.
	 */
	public class XMR_Array {
		private bool enumrValid;                              // true: enumr set to return array[arrayValid]
		                                                      // false: array[0..arrayValid-1] is all there is
		private Dictionary<object, object> dnary = new Dictionary<object, object> ();
		private Dictionary<object, object>.Enumerator enumr;  // enumerator used to fill 'array' past arrayValid to end of dictionary
		private int arrayValid;                               // number of elements in 'array' that have been filled in
		private KeyValuePair<object, object>[] array;         // list of kvp's that have been returned by ForEach() since last modification

		/**
		 * @brief Handle 'array[index]' syntax to get or set an element of the dictionary.
		 * Get returns null if element not defined, script sees type 'undef'.
		 * Setting an element to null removes it.
		 */
		public object this[object key]
		{
			get {
				object val;
				if (!dnary.TryGetValue (key, out val)) val = null;
				return val;
			}
			set {
				/*
				 * Save new value in array, replacing one of same key if there.
				 * null means remove the value, ie, script did array[key] = undef.
				 */
				if (value != null) {
					dnary[key] = value;
				} else {
					dnary.Remove (key);

					/*
					 * Shrink the enumeration array, but always leave at least one element.
					 */
					if ((array != null) && (dnary.Count < array.Length / 2)) {
						Array.Resize<KeyValuePair<object, object>> (ref array, array.Length / 2);
					}
				}

				/*
				 * The enumeration array is invalid because the dictionary has been modified.
				 * Next time a ForEach() call happens, it will repopulate 'array' as elements are retrieved.
				 */
				arrayValid = 0;
			}
		}

		/**
		 * @brief Converts an 'object' type to array, key, list, string, but disallows null,
		 *        as our language doesn't allow types other than 'object' to be null.
		 *        Value types (float, rotation, etc) don't need explicit check for null as
		 *        the C# runtime can't convert a null to a value type, and throws an exception.
		 *        But for any reference type (array, key, etc) we must manually check for null.
		 */
		public static XMR_Array Obj2Array (object obj)
		{
			if (obj == null) throw new NullReferenceException ();
			return (XMR_Array)obj;
		}
		public static LSL_Key Obj2Key (object obj)
		{
			if (obj == null) throw new NullReferenceException ();
			return (LSL_Key)obj;
		}
		public static LSL_List Obj2List (object obj)
		{
			if (obj == null) throw new NullReferenceException ();
			return (LSL_List)obj;
		}
		public static LSL_String Obj2String (object obj)
		{
			if (obj == null) throw new NullReferenceException ();
			return obj.ToString ();
		}

		/**
		 * @brief return number of elements in the array.
		 */
		public int __pub_count {
			get { return dnary.Count; }
		}

		/**
		 * @brief Retrieve index (key) of an arbitrary element.
		 * @param number = number of the element (0 based)
		 * @returns null: array doesn't have that many elements
		 *          else: index (key) for that element
		 */
		public object __pub_index (int number)
		{
			object key = null;
			object val = null;
			ForEach (number, ref key, ref val);
			return key;
		}


		/**
		 * @brief Retrieve value of an arbitrary element.
		 * @param number = number of the element (0 based)
		 * @returns null: array doesn't have that many elements
		 *          else: value for that element
		 */
		public object __pub_value (int number)
		{
			object key = null;
			object val = null;
			ForEach (number, ref key, ref val);
			return val;
		}

		/**
		 * @brief Called in each iteration of a 'foreach' statement.
		 * @param number = index of element to retrieve (0 = first one)
		 * @returns false: element does not exist
		 *           true: element exists:
		 *                 key = key of retrieved element
		 *                 val = value of retrieved element
		 */
		public bool ForEach (int number, ref object key, ref object val)
		{

			/*
			 * If we don't have any array, we can't have ever done
			 * any calls here before, so allocate an array big enough
			 * and set everything else to the beginning.
			 */
			if (array == null) {
				array = new KeyValuePair<object, object>[dnary.Count];
				arrayValid = 0;
			}

			/*
			 * If dictionary modified since last enumeration, get a new enumerator.
			 */
			if (arrayValid == 0) {
				enumr = dnary.GetEnumerator ();
				enumrValid = true;
			}

			/*
			 * Make sure we have filled the array up enough for requested element.
			 */
			while ((arrayValid <= number) && enumrValid && enumr.MoveNext ()) {
				if (arrayValid >= array.Length) {
					Array.Resize<KeyValuePair<object, object>> (ref array, dnary.Count);
				}
				array[arrayValid++] = enumr.Current;
			}

			/*
			 * If we don't have that many elements, return end-of-array status.
			 */
			if (arrayValid <= number) return false;

			/*
			 * Return the element values.
			 */
			key = array[number].Key;
			val = array[number].Value;
			return true;
		}

		/**
		 * @brief Transmit array out in such a way that it can be reconstructed,
		 *        including any in-progress ForEach() enumerations.
		 */
		public void SendArrayObj (Mono.Tasklets.MMRContSendObj sendObj, Stream stream)
		{
			int index = arrayValid;
			object key = null;
			object val = null;

			/*
			 * Completely fill the array from where it is already filled to the end.
			 * Any elements before arrayValid remain as the current enumerator has
			 * seen them, and any after arrayValid will be filled by that same
			 * enumerator.  The array will then be used on the receiving end to iterate
			 * in the same exact order, because a new enumerator on the receiving end
			 * probably wouldn't see them in the same order.
			 */
			while (ForEach (index ++, ref key, ref val)) { }

			/*
			 * Set the count then the elements themselves.
			 */
			sendObj (stream, (object)arrayValid);
			for (index = 0; index < arrayValid; index ++) {
				sendObj (stream, array[index].Key);
				sendObj (stream, array[index].Value);
			}
		}

		/**
		 * @brief Receive array in.  Any previous contents are erased.
		 *        Set up such that any enumeration in progress will resume
		 *        at the exact spot and in the exact same order as they
		 *        were in on the sending side.
		 */
		public void RecvArrayObj (Mono.Tasklets.MMRContRecvObj recvObj, Stream stream)
		{
			int index;

			/*
			 * Empty the dictionary.
			 */
			dnary.Clear ();

			/*
			 * Any enumerator in progress is now invalid, and all elements
			 * for enumeration must come from the array, so they will be in
			 * the same order they were in on the sending side.
			 */
			enumrValid = false;

			/*
			 * Get number of elements we will receive and set up an
			 * array to receive them into.  The array will be used
			 * for any enumerations in progress, and will have elements
			 * in order as given by previous calls to those enumerations.
			 */
			arrayValid = (int)recvObj (stream);
			array = new KeyValuePair<object, object>[arrayValid];

			/*
			 * Fill the array and dictionary.
			 * Any enumerations will use the array so they will be in the
			 * same order as on the sending side (until the dictionary is
			 * modified).
			 */
			for (index = 0; index < arrayValid; index ++) {
				object key = recvObj (stream);
				object val = recvObj (stream);
				array[index] = new KeyValuePair<object, object> (key, val);
				dnary.Add (key, val);
			}
		}
	}
}
