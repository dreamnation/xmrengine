/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using Mono.Tasklets;
using System;
using System.IO;
using System.Reflection;
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
	public abstract class ScriptWrapper : IDisposable {

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

		/*
		 * Info about the script DLL itself as a whole.
		 */
		public string scriptName;
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
		public Stream migrateInStream;   // null: normal start at event handler's entrypoint
		                                 // else: restore position in event handler from stream and resume
		public Stream migrateOutStream;  // null: continue executing event handler
		                                 // else: write position of event handler to stream and exit

		public MMRContSendObj originalSendObj;
		public MMRContRecvObj originalRecvObj;

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
		public static readonly byte migrationVersion = 1;

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
		public static ScriptWrapper CreateScriptInstance (string scriptname, string dllName)
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
			 * Look for a public class definition called "ScriptModule_<scriptname>".
			 */
			Type scriptModule = scriptAssembly.GetType ("ScriptModule_" + scriptname);
			if (scriptModule == null) {
				throw new Exception (dllName + " has no ScriptModule_" + scriptname + " class");
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
			scriptWrapper.scriptName              = scriptname;
			scriptWrapper.scriptDLLName           = dllName;
			scriptWrapper.scriptEventHandlerTable = scriptEventHandlerTable;

			/*
			 * Set up sub-objects and cross-polinate so everything can access everything.
			 */
			scriptWrapper.microthread = new ScriptUThread ();
			scriptWrapper.continuation = new ScriptContinuation ();
			scriptWrapper.microthread.scriptWrapper = scriptWrapper;
			scriptWrapper.continuation.scriptWrapper = scriptWrapper;

			/*
			 * We need to intervene to serialize null pointers.
			 */
			scriptWrapper.originalSendObj = scriptWrapper.continuation.sendObj;
			scriptWrapper.continuation.sendObj = scriptWrapper.SendObjInterceptor;
			scriptWrapper.originalRecvObj = scriptWrapper.continuation.recvObj;
			scriptWrapper.continuation.recvObj = scriptWrapper.RecvObjInterceptor;

			/*
			 * Constant subsArray values...
			 */
			scriptWrapper.subsArray[(int)SubsArray.SCRIPT] = scriptWrapper;

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
				this.scriptName = null;
				this.scriptDLLName = null;
				this.scriptEventHandlerTable = null;
				this.migrateInStream = null;
				this.migrateOutStream = null;
				this.originalSendObj = null;
				this.originalRecvObj = null;
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
					this.migrateOutStream = null;

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
					 * We aren't starting the event handler from the beginning,
					 * we are going to read where we left off from the stream.
					 * And we aren't doing any outbound migration on it.
					 */
					this.migrateInStream  = stream;
					this.migrateOutStream = null;

					/*
					 * This calls Main() below directly, and returns when Main() calls Suspend()
					 * or when Main() returns, whichever occurs first.  It should return quickly.
					 */
					microthread.Start ();
				}
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
			lock (microthread) {

				/*
				 * Write the basic information out to the stream:
				 *    state, event, eventhandler args, script's globals.
				 */
				stream.WriteByte (migrationVersion);
				this.continuation.sendObj (stream, this.stateCode);
				this.continuation.sendObj (stream, this.eventCode);
				this.continuation.sendObj (stream, this.memUsage);
				this.continuation.sendObj (stream, this.memLimit);
				this.continuation.sendObj (stream, this.ehArgs);
				this.MigrateScriptOut (stream, this.continuation.sendObj);

				/*
				 * The script microthread should be at its Suspend() call within
				 * CheckRun(), unless it has exited.  Tell CheckRun() that it 
				 * should migrate the script out then unwind.
				 */
				this.migrateOutStream = stream;

				/*
				 * Resume the microthread to actually write the network stream.
				 * When it finishes it will unwind its stack and return here.
				 */
				while (eventCode != ScriptEventCode.None) {
					microthread.Resume ();
				}

				/*
				 * If it actually wrote instead of simply exiting, it will have
				 * cleared this.migrateOutStream.
				 */
				written = (this.migrateOutStream == null);
				this.migrateOutStream = null;
			}

			return written;
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
				stream.WriteByte (1);
			} else {
				stream.WriteByte (2);
				this.originalSendObj (stream, graph);
			}
		}

		private object RecvObjInterceptor (Stream stream)
		{
			int code = stream.ReadByte ();
			switch (code) {
				case 1: {
					return null;
				}
				case 2: {
					object graph = this.originalRecvObj (stream);
					return graph;
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
				                                            scriptWrapper.subsArray);
			}

			/*
			 * If CheckRun() sees that MigrateOutEventHandler() has been called,
			 * it writes the stack contents to the stream then throws a
			 * GetMeOutOfThisScript() which unwinds us to this point.
			 */
			if ((except != null) && !(except is GetMeOutOfThisScript)) {
				throw except;
			}
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
			oldStateCode = scriptWrapper.stateCode;
			seh = scriptWrapper.scriptEventHandlerTable[oldStateCode,(int)scriptWrapper.eventCode];
			seh (scriptWrapper);
			newStateCode = scriptWrapper.stateCode;

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
			 *
			 * Note that we ignore any state transition requested by exit_state()/enter_state().
			 * ??? should we throw an exception if they change in scriptWrapper.stateCode ???
			 */
			if (newStateCode != oldStateCode) {
				scriptWrapper.stateCode = oldStateCode;
				seh = scriptWrapper.scriptEventHandlerTable[oldStateCode,(int)ScriptEventCode.state_exit];
				if (seh != null) seh (scriptWrapper);
				if (scriptWrapper.stateCode != oldStateCode) throw new Exception ("state_exit() transitioned state");
				scriptWrapper.stateCode = newStateCode;
				seh = scriptWrapper.scriptEventHandlerTable[newStateCode,(int)ScriptEventCode.state_entry];
				if (seh != null) seh (scriptWrapper);
				if (scriptWrapper.stateCode != newStateCode) throw new Exception ("state_entry() transitioned state");
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
			 * Make sure script isn't hogging too much memory.
			 */
			if (scriptWrapper.memUsage > scriptWrapper.memLimit) {
				throw new Exception ("memory usage " + scriptWrapper.memUsage.ToString () + 
				                     " exceeds limit " + scriptWrapper.memLimit.ToString ());
			}

			while (scriptWrapper.suspendOnCheckRun || (scriptWrapper.migrateOutStream != null)) {

				/*
				 * See if MigrateOutEventHandler() has been called.
				 * If so, dump our stack to the stream and unwind via exception.
				 * If, on return from Save(), migrateOutStream is null, it means
				 * this is the restore code on the receiving end, in which case we
				 * want to simply return to resume where we left off.
				 */
				if (scriptWrapper.migrateOutStream != null) {

					/*
					 * Puque our stack to the output stream.
					 */
					scriptWrapper.subsArray[(int)ScriptWrapper.SubsArray.BEAPI] = scriptWrapper.beAPI;
					this.Save (scriptWrapper.migrateOutStream, scriptWrapper.subsArray);

					/*
					 * See if this is during MigrateOutEventHandler() or
					 * during MigrateInEventHandler().
					 */
					if (scriptWrapper.migrateOutStream != null) {

						/*
						 * Migrating out, unwind stack to Main().
						 * We also say script has completed because we 
						 * don't want this microthread to run again as 
						 * it will be all unwound.
						 */
						scriptWrapper.eventCode = ScriptEventCode.None;
						scriptWrapper.migrateOutStream = null;
						throw new GetMeOutOfThisScript ();
					}

					/*
					 * This is on the receiving end from MigrateInEventHandler().
					 * Suspend right away so MigrateInEventHandler() will return 
					 * quickly.
					 */
					scriptWrapper.migrateInStream   = null;
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
