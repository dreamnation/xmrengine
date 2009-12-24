/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using Mono.Tasklets;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting;
using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;


namespace MMR {

	/*
	 * Every compiled event handler method has this signature.
	 * It is passed a pointer to the ScriptWrapper struct, where it can access other context.
	 */
	public delegate void ScriptEventHandler (ScriptWrapper __sw);

	/*
	 * All scripts must inherit from this class.
	 */
	public abstract class ScriptWrapper : MarshalByRefObject, IDisposable {

		public int stateCode = 0;                 // state the script is in (0 = 'default')
		public ScriptEventCode eventCode = ScriptEventCode.None;
		                                          // event handler being executed (None when idle)
		                                          // - code assumes that only the event handling microthread
		                                          //   transitions eventCode from non-idle to idle
		                                          // - idle to non-idle is always Interlocked
		public ScriptBaseClass beAPI;             // passed as 'this' to methods such as llSay()
		public ScriptContinuation continuation;   // passed as 'this' to CheckRun()
		public object[] ehArgs;                   // event handler argument array
		public int memUsage = 0;                  // script's current memory usage
		public int memLimit = 100000;             // CheckRun() throws exception if memUsage > memLimit
		public bool stateChanged = false;         // script sets this if/when it executes a 'state' statement

		/*
		 * Info about the script DLL itself as a whole.
		 */
		public string scriptDLLName;

		/*
		 * Every script DLL must define this matrix of event handler delegates.
		 * There is only one of these for the whole script.
		 * The first subscript is the state.
		 * The second subscript is the event.
		 */
		public ScriptEventHandler[,] scriptEventHandlerTable;

		/*
		 * We will use this microthread to run the scripts event handlers.
		 */
		protected ScriptUThread microthread;

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
		public static readonly byte migrationVersion = 2;

		/*
		 * All script classes must define these methods.
		 * They migrate the script's global variables whether it is idle or not.
		 */
		public abstract void MigrateScriptOut (System.IO.Stream stream, Mono.Tasklets.MMRContSendObj sendObj);
		public abstract void MigrateScriptIn (System.IO.Stream stream, Mono.Tasklets.MMRContRecvObj recvObj);

		/*
		 * Open the script DLL and get ready to run event handlers.
		 * Set the initial state to "default".
		 *
		 * Caller should call StartEventHandler() or MigrateInEventHandler() next.
		 * If calling StartEventHandler(), use ScriptEventCode.state_entry with no args.
		 */
		public static ScriptWrapper CreateScriptInstance (string dllName)
		{
			Assembly scriptAssembly;
			ScriptEventHandler[,] scriptEventHandlerTable;

			/*
			 * Get DLL loaded in memory.
			 */
			scriptAssembly = Assembly.LoadFrom (dllName);
			if (scriptAssembly == null) {
				throw new Exception (dllName + " load failed");
			}

			/*
			 * Look for a public class definition called "ScriptModule".
			 */
			Type scriptModule = scriptAssembly.GetType ("ScriptModule");
			if (scriptModule == null) {
				throw new Exception (dllName + " has no ScriptModule class");
			}

			/*
			 * Look for a "public static const int" field called "compiledVersion",
			 * and make sure it matches COMPILED_VERSION so we know we aren't dealing
			 * with an .DLL that we can't support.
			 */
			FieldInfo cvField = scriptModule.GetField (ScriptCodeGen.COMPILED_VERSION_NAME);
			if (cvField == null) {
				throw new Exception (dllName + " has no compiledVersion field");
			}
			int cvValue = (int)cvField.GetValue (null);
			if (cvValue != ScriptCodeGen.COMPILED_VERSION_VALUE) {
				throw new Exception (dllName + " compiled version " + cvValue.ToString () +
				                     ", but require " + ScriptCodeGen.COMPILED_VERSION_VALUE.ToString ());
			}

			/*
			 * Look for a public static method in that class called "GetScriptEventHandlerTable".
			 */
			MethodInfo gseht = scriptModule.GetMethod ("GetScriptEventHandlerTable");
			if (gseht == null) {
				throw new Exception (dllName + " has no GetScriptEventHandlerTable() method");
			}
			if (!gseht.IsStatic) {
				throw new Exception (dllName + " GetScriptEventHandlerTable() is not static");
			}

			/*
			 * Call its GetScriptEventHandlerTable() method.
			 * It should return its scriptEventHandlerTable that contains all its event handler entrypoints.
			 */
			if (gseht.ReturnType != typeof (ScriptEventHandler[,])) {
				throw new Exception (dllName + " GetScriptEventHandlerTable() does not return ScriptEventHandler[,]");
			}
			scriptEventHandlerTable = (ScriptEventHandler[,])gseht.Invoke (null, null);

			/*
			 * Call its default constructor.  It creates the script object which is derived from ScriptWrapper.
			 */
			ScriptWrapper scriptWrapper = (ScriptWrapper)Activator.CreateInstance (scriptModule);

			/*
			 * Save important things we want to remember, like where its event handlers are.
			 */
			scriptWrapper.scriptDLLName           = dllName;
			scriptWrapper.scriptEventHandlerTable = scriptEventHandlerTable;

			/*
			 * Set up sub-objects and cross-polinate so everything can access everything.
			 */
			scriptWrapper.microthread  = new ScriptUThread ();
			scriptWrapper.continuation = new ScriptContinuation ();
			scriptWrapper.microthread.scriptWrapper  = scriptWrapper;
			scriptWrapper.continuation.scriptWrapper = scriptWrapper;

			/*
			 * We do our own object serialization.
			 * It avoids null pointer refs and is much more efficient because we
			 * have a limited number of types to deal with.
			 */
			scriptWrapper.continuation.sendObj = scriptWrapper.SendObjInterceptor;
			scriptWrapper.continuation.recvObj = scriptWrapper.RecvObjInterceptor;

			/*
			 * Constant subsArray values...
			 */
			scriptWrapper.subsArray[(int)SubsArray.SCRIPT] = scriptWrapper;

			/*
			 * All the DLL filenames should be known at this point,
			 * so fill in the entries needed.
			 *
			 * These have to be the exact string returned by mono_image_get_filename().
			 * To find out which DLLs are needed, set envar MMRCONTSAVEDEBUG=1 and observe
			 * debug output to see which DLLs are referenced.
			 */
			scriptWrapper.dllsArray = new string[4];
			scriptWrapper.dllsArray[0] = MMRCont.GetDLLName (scriptModule);            // ...<uuid>.dll
			scriptWrapper.dllsArray[1] = MMRCont.GetDLLName (typeof (ScriptWrapper));  // ...MMRCont.dll
			scriptWrapper.dllsArray[2] = MMRCont.GetDLLName (typeof (MMRCont));        // ...Mono.Tasklets.dll
			scriptWrapper.dllsArray[3] = MMRCont.GetDLLName (typeof (LSL_Vector));     // ...ScriptEngine.Shared.dll

			return scriptWrapper;
		}

		/*
		 * All done with object, it's not usable after this.
		 */
		public virtual void Dispose ()
		{
			if (this.microthread != null) {
				this.microthread.Dispose ();
				this.continuation = null;
				this.microthread = null;

				///??? debug ???///
				this.beAPI = null;
				this.stateCode = 12345678;
				this.eventCode = ScriptEventCode.Garbage;
				this.ehArgs = null;
				this.scriptDLLName = null;
				this.scriptEventHandlerTable = null;
				this.migrateInStream  = null;
				this.migrateInReader  = null;
				this.migrateOutStream = null;
				this.migrateOutWriter = null;
				this.subsArray = null;
			}
		}

		/*
		 * Retrieve name of DLL the script is in.
		 */
		public string ScriptDLLName ()
		{
			return scriptDLLName;
		}

		/*
		 * Overridden by script to decode state code (int) to state name (string).
		 */
		public virtual string GetStateName (int stateCode)
		{
			return stateCode.ToString ();
		}

		/**
		 * @brief translate line number as reported in script DLL exception message
		 *        to line number in script source file
		 * This method is overridden by scripts to perform translation using their numbers.
		 */
		public virtual int DLLToSrcLineNo (int dllLineNo)
		{
			return dllLineNo;
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
			lock (microthread) {
				if (this.eventCode != ScriptEventCode.None) {
					throw new Exception ("Event handler already active");
				}

				/*
				 * Silly to even try if there is no handler defined for this event.
				 */
				if (this.scriptEventHandlerTable[this.stateCode,(int)eventCode] != null) {

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
					microthread.Start ();
				}
			}
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
			lock (microthread) {
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

			/*
			 * Execute script until it gets suspended, migrated out or it completes.
			 */
			lock (microthread) {
				if (eventCode != ScriptEventCode.None) {
					microthread.Resume ();
				}
				idle = eventCode == ScriptEventCode.None;
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
			lock (this.microthread) {

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
				written = (this.eventCode != ScriptEventCode.None);

				/*
				 * Resume the microthread to actually write the network stream.
				 * When it finishes it will suspend, causing the microthreading
				 * to return here.
				 */
				if (written) {
					this.migrateComplete = false;
					this.microthread.Resume ();
					if (!this.migrateComplete) throw new Exception ("migrate out did not complete");
				}

				/*
				 * No longer migrating.
				 */
				//this.migrateOutWriter.Dispose ();  // moron thing doesn't compile
				this.migrateOutStream = null;
				this.migrateOutWriter = null;
			}

			return written;
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
				this.migrateOutWriter.Write ((float)graph);
			} else if (graph is LSL_Integer) {
				this.migrateOutWriter.Write ((byte)Ser.LSLINT);
				this.migrateOutWriter.Write ((int)graph);
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
	}


	/*****************************************************************\
	 *  Wrapper around continuation to enclose it in a microthread.  *
	\*****************************************************************/

	public class ScriptUThread : MMRUThread {

		public ScriptWrapper scriptWrapper;  // script wrapper we belong to

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


	/****************************************************************\
	 *  Wrapper around script event handler so it can be migrated.  *
	\****************************************************************/

	public class ScriptContinuation : MMRCont {

		public ScriptWrapper scriptWrapper;  // script wrapper we belong to

		/*
		 * Called by RunItEx() to start the event handler at its entrypoint.
		 */
		[MMRContableAttribute ()]
		public override void RunCont ()
		{
			int newStateCode, oldStateCode;
			ScriptEventHandler seh;

			/*
			 * Immediately suspend so the StartEventHandler() method
			 * returns with minimal delay.  We will resume from this
			 * point when microthread.Resume() is called.
			 */
			scriptWrapper.suspendOnCheckRun = true;
			this.CheckRun ();

			/*
			 * Process event given by 'stateCode' and 'eventCode'.
			 * The event handler should call CheckRun() as often as convenient.
			 *
			 * We do not have to check for null 'seh' here because
			 * StartEventHandler() already checked the table entry.
			 */
			scriptWrapper.stateChanged = false;
			oldStateCode = scriptWrapper.stateCode;
			seh = scriptWrapper.scriptEventHandlerTable[oldStateCode,(int)scriptWrapper.eventCode];
			seh (scriptWrapper);

			scriptWrapper.ehArgs = null;  // we are done with them and no args for
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
			if (scriptWrapper.stateChanged) {
				scriptWrapper.stateChanged = false;
				newStateCode = scriptWrapper.stateCode;

				scriptWrapper.stateCode = oldStateCode;
				seh = scriptWrapper.scriptEventHandlerTable[oldStateCode,(int)ScriptEventCode.state_exit];
				if (seh != null) seh (scriptWrapper);
				if (scriptWrapper.stateChanged) throw new Exception ("state_exit() transitioned state");

				scriptWrapper.stateCode = newStateCode;
				seh = scriptWrapper.scriptEventHandlerTable[newStateCode,(int)ScriptEventCode.state_entry];
				if (seh != null) seh (scriptWrapper);
				if (scriptWrapper.stateChanged) throw new Exception ("state_entry() transitioned state");
			}

			/*
			 * The event handler has run to completion.
			 */
			scriptWrapper.eventCode = ScriptEventCode.None;
		}

		/*
		 * The script code should call this routine whenever it is
		 * convenient to perform a migation or switch microthreads.
		 */
		[MMRContableAttribute ()]
		public void CheckRun ()
		{
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
		}
	}


	/**
	 * @brief This exception is thrown by CheckRun() when it has completed
	 *        writing the script state out to the network for a MigrateOut
	 *        operation, to unwind the stack.
	 */
	public class GetMeOutOfThisScript : Exception { }
}
