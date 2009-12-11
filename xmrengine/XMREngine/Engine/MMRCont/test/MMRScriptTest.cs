/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using System;
using System.IO;

using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;

/**
 * @brief test program for script engine.
 *
 * Run it giving it the name of a .lsl script file, eg:
 *
 *   WRITEMLTFILEDIR=mltfiles ../monosrc/mono/mono/mini/mono --debug tokentest.exe flightassist
 */

//
// Script file must be annotated with test data, bounded by markers /**TEST  ...  TEST**/
//
// It has the form:
//
//    eventname(args,...) {			// sends this event to script in the state it currently is in
//        llcall(args,...) [returnvalue];	// script must call each 'llcall()' with the arg values given
//						// 'returnvalue' is passed back to script unless function is void
//        llcall(args,...) [returnvalue];
//    } [:state]				// script must end up at this state (don't care if :state omitted)
//
//    eventname(args...) {
//        llcall(args,...) [returnvalue];
//        llcall(args,...) [returnvalue];
//    } [:state]
//

namespace MMR {

	public class ScriptTest {

		private static readonly log4net.ILog m_log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		public delegate ScriptWrapper Stepper (ScriptWrapper scriptWrapper);

		public static System.Collections.Generic.Dictionary<string, TokenDeclFunc> eventFunctions;

		public static void Main (string[] args)
		{
			Console.WriteLine ("ScriptTest: {0}", System.Diagnostics.Process.GetCurrentProcess ().MainModule.FileName);
			log4net.Config.BasicConfigurator.Configure();
			m_log.DebugFormat("[MMR]: log4net sux");

			if (args.Length != 1) {
				Console.WriteLine ("ScriptTest: must have one command line arg");
				goto alldone;
			}
			string scriptname = args[0];
			string sourceName = scriptname + ".lsl";
			string cSharpName = scriptname + "_script_temp.cs";
			string binaryName = scriptname + "_script_temp.dll";

			FileStream sourceFile = File.OpenRead (sourceName);
			StreamReader sourceReader = new StreamReader (sourceFile);
			string source = sourceReader.ReadToEnd ();
			sourceReader.Close ();

			Console.WriteLine ("compiling " + sourceName);
			if (!MMR.ScriptCompile.Compile (source, binaryName, scriptname, cSharpName, ErrorMessage)) goto alldone;
			Console.WriteLine ("compilation successful");

			//
			// Extract section starting with /**TEST and ending with "TEST**/
			// and parse it into tokens.
			//
			int begIndex = source.IndexOf ("/**TEST");
			int endIndex = source.IndexOf ("TEST**/");
			if ((begIndex < 0) || (endIndex <= begIndex)) {
				Console.WriteLine ("can't find /**TEST ... TEST**/ in " + sourceName);
				goto alldone;
			}

			begIndex += 7;
			TokenBegin testTokenBegin = TokenBegin.Construct (ErrorMessage, source.Substring (begIndex, endIndex - begIndex));
			if (testTokenBegin == null) {
				Console.WriteLine ("error parsing script test section tokens in " + sourceName);
				goto alldone;
			}

			/*
			 * Build a dictionary of event handlers so we can parse them from the test tokens.
			 * This translates eventhandlername -> a function declaration
			 */
			eventFunctions = new InternalFuncDict (typeof (IEventHandlers));

			/*
			 * Create a script object.  This loads the .DLL into memory
			 * and gets its ScriptEventHandler[,] table, which contains
			 * a delegate for each event handler in the script.
			 * 
			 * The TestLSLAPI() object contains all the ll...() functions,
			 * but they simply compare their call against what is expected
			 * in the script, and throw an exception if there is a mismatch.
			 */
			OpenSim.Region.ScriptEngine.Shared.ScriptBase.ScriptBaseClass beAPI = 
					new OpenSim.Region.ScriptEngine.Shared.ScriptBase.ScriptBaseClass ();
			TestLSLAPI testLSLAPI = new TestLSLAPI ();
			beAPI.m_LSL_Functions = testLSLAPI;

			/*
			 * First just run the script with no interruptions.
			 */
			Console.WriteLine ("RUNNING TEST WITH NO SUSPENDS...");
			ScriptWrapper sw = ScriptWrapper.CreateScriptInstance (binaryName);
			sw.beAPI         = beAPI;
			sw.alwaysSuspend = false;
			testLSLAPI.scriptWrapper = sw;
			if (!ProcessTestScript (sw, testLSLAPI, testTokenBegin.nextToken, NormalStepper)) goto alldone;

			/*
			 * Next run the script with a microthread switch at each CheckRun() point.
			 */
			Console.WriteLine ("RUNNING TEST WITH SIMPLE SUSPENDS...");
			sw = ScriptWrapper.CreateScriptInstance (binaryName);
			sw.beAPI         = beAPI;
			sw.alwaysSuspend = true;
			testLSLAPI.scriptWrapper = sw;
			if (!ProcessTestScript (sw, testLSLAPI, testTokenBegin.nextToken, NormalStepper)) goto alldone;

			/*
			 * Finally run the script with a checkpoint/restart at each CheckRun() point.
			 */
			Console.WriteLine ("RUNNING TEST WITH CONTINUATIONS...");
			sw = ScriptWrapper.CreateScriptInstance (binaryName);
			sw.beAPI         = beAPI;
			sw.alwaysSuspend = true;
			testLSLAPI.scriptWrapper = sw;
			if (!ProcessTestScript (sw, testLSLAPI, testTokenBegin.nextToken, CheckpointStepper)) goto alldone;

			Console.WriteLine ("TESTS COMPLETED SUCCESSFULLY");

		alldone:
			Console.WriteLine ("");
			Console.WriteLine ("ALL DONE!!!");
		}

		/*
		 * @brief Run script through the test procedure.
		 * @param scriptWrapper = script to test
		 * @param firstToken = test to run on it (points to first eventname token)
		 * @param stepper = steps the script through microthread suspends
		 * @returns true: successful
		 *         false: failed
		 * scriptWrapper state and variables altered
		 */
		public static bool ProcessTestScript (ScriptWrapper scriptWrapper, TestLSLAPI testLSLAPI, Token firstToken, Stepper stepper)
		{
			/*
			 * Process test script
			 */
			for (Token token = firstToken; !(token is TokenEnd); token = token.nextToken) {

				/*
				 * Get event name from the test script tokens.
				 * Leave token pointer just past the open parenthesis.
				 */
				if (!(token is TokenName)) {
					token.ErrorMsg ("expected event name");
					return false;
				}
				TokenName eventNameToken = (TokenName)token;
				string eventName = eventNameToken.val;
				if (!eventFunctions.ContainsKey (eventName)) {
					token.ErrorMsg ("unknown event name " + eventName);
					return false;
				}
				TokenDeclFunc eventFunc = eventFunctions[eventName];
				token = token.nextToken;
				if (!(token is TokenKwParOpen)) {
					token.ErrorMsg ("expected open parenthesis");
					return false;
				}
				token = token.nextToken;

				/*
				 * Get event argument list, putting a pointer to the value in
				 * our ehArgs[] array.  Value objects get boxed.
				 */
				object[] ehArgs = new object[eventFunc.argDecl.types.Length];
				for (int i = 0; i < eventFunc.argDecl.types.Length; i++) {

					/*
					 * Get parameter value into array.
					 */
					TokenType argType = eventFunc.argDecl.types[i];
					ehArgs[i] = TestLSLAPI.GetTokenVal (ref token);
					if (ehArgs[i] == null) return false;

					/*
					 * Some values require explicit conversions.
					 */
					if ((argType is TokenTypeKey) && (ehArgs[i] is string)) {
						ehArgs[i] = new LSL_Key ((string)ehArgs[i]);
					}

					/*
					 * If prototype expects more arguments, check for comma.
					 */
					if (i + 1 < eventFunc.argDecl.types.Length) {
						if (!(token is TokenKwComma)) {
							token.ErrorMsg ("expected comma");
							return false;
						}
						token = token.nextToken;
					}
				}

				/*
				 * Check for the closing parenthesis and there should be an open
				 * brace that begins the expected calls section.
				 * 
				 * This leaves the token pointer pointed at the name of the first
				 * expected ll...() call.
				 */
				if (!(token is TokenKwParClose)) {
					token.ErrorMsg ("expected close parenthesis");
					return false;
				}
				token = token.nextToken;
				testLSLAPI.doCompares = false;
				if (!(token is TokenKwMul)) {
					if (!(token is TokenKwBrcOpen)) {
						token.ErrorMsg ("expected asterisk or open brace");
						return false;
					}
					testLSLAPI.doCompares = true;
					token = token.nextToken;
				}

				/*
				 * Pass the event to script's event handler and see what ll... functions it calls.
				 * Each time a function in TestLSLAPI is called, it compares itself with the tokens
				 * in testLSLAPI.token and throws an exception if there is a mismatch.  If no mismatch,
				 * it advances testLSLAPI.token to the next call.
				 */
				Console.WriteLine ("{0}.{1}: Sending event {2}", eventNameToken.line, eventNameToken.posn, eventName);
				testLSLAPI.token = token;
				ScriptEventCode sec = (ScriptEventCode)Enum.Parse (typeof (ScriptEventCode), eventName);
				try {
					scriptWrapper.StartEventHandler (sec, ehArgs);
					scriptWrapper = stepper (scriptWrapper);
				}
				catch (Exception e) {
					Console.WriteLine ("exception: " + e.ToString ());
					return false;
				}

				/*
				 * Fetch testLSLAPI out of the script again because maybe it got migrated.
				 */
				testLSLAPI = (TestLSLAPI)scriptWrapper.beAPI.m_LSL_Functions;

				/*
				 * It should be pointed at a closing brace meaning we expect no further calls.
				 */
				token = testLSLAPI.token;
				if (testLSLAPI.doCompares && !(token is TokenKwBrcClose)) {
					token.ErrorMsg ("expected close brace");
					return false;
				}

				/*
				 * Print current state.
				 */
				Console.WriteLine ("{0}.{1}: State = {2} {3}", token.line, token.posn, 
						scriptWrapper.stateCode, scriptWrapper.GetStateName (scriptWrapper.stateCode));

				/*
				 * Maybe script wants to verify state.
				 */
				if (token.nextToken is TokenKwColon) {
					token = token.nextToken.nextToken;
					string expected = "default";
					if (token is TokenName) {
						expected = ((TokenName)token).val;
					} else if (!(token is TokenKwDefault)) {
						token.ErrorMsg ("expected a state name");
						return false;
					}
					if (expected != scriptWrapper.GetStateName (scriptWrapper.stateCode)) {
						token.ErrorMsg ("expected state " + expected);
						return false;
					}
				}
			}
			return true;
		}

		/**
		 * @brief Step the microthread through until the script completes.
		 * With sw.alwaysSuspend = false, it is just one big step
		 * With sw.alwaysSuspend = true, it steps microthread at every CheckRun()
		 */
		public static ScriptWrapper NormalStepper (ScriptWrapper scriptWrapper)
		{
			while (!scriptWrapper.ResumeEventHandler ()) { }
			return scriptWrapper;
		}

		/**
		 * @brief Step the microthread through until the script completes.
		 * Always runs with sw.alwaysSuspend = true, so it checkpoints at
		 * every CheckRun() call.
		 */
		public static ScriptWrapper CheckpointStepper (ScriptWrapper scriptWrapper)
		{
			int cycleNo = 0;

			do scriptWrapper = MassMigration (scriptWrapper, ref cycleNo);
			while (!scriptWrapper.ResumeEventHandler ());
			Console.WriteLine ("CheckpointStepper: event handler complete");
			scriptWrapper = MassMigration (scriptWrapper, ref cycleNo);
			return scriptWrapper;
		}

		/**
		 * @brief migrate the script out then back in
		 * @param oldSW = original script state
		 * @param cycleNo = increments for each migration to give unique temp file
		 * @returns cloned script state, orignal state Dispose()'d
		 */
		public static ScriptWrapper MassMigration (ScriptWrapper oldSW, ref int cycleNo)
		{
			byte[] stopOnADime = new byte[] { (byte)'D', (byte)'i', (byte)'m', (byte)'e' };

			Console.WriteLine ("CheckpointStepper: mem usage/limit {0}/{1}", oldSW.memUsage, oldSW.memLimit);

			/*
			 * Write script state out to a temporary file.
			 */
			string fileName = String.Format ("migration_{0}.tmp", ++ cycleNo);
			if (File.Exists (fileName)) File.Delete (fileName);
			Stream outStream = File.Create (fileName);
			Console.WriteLine ("CheckpointStepper: migrating out to file");
			if (!oldSW.MigrateOutEventHandler (outStream)) {
				Console.WriteLine ("CheckpointStepper: (null migration)");
			}
			outStream.Write (stopOnADime, 0, 4);
			outStream.Close ();
			Console.WriteLine ("CheckpointStepper: migrate out complete");

			/*
			 * Create a new script instance and migrate in.
			 * Dispose the old script instance so any dangling refs to it will be discovered.
			 * Likewise, clone the lslApi object and dispose of the old one.
			 */
			string dllName           = oldSW.scriptDLLName;
			OpenSim.Region.ScriptEngine.Shared.ScriptBase.ScriptBaseClass beAPI = oldSW.beAPI;
			TestLSLAPI testLSLAPI    = (TestLSLAPI)beAPI.m_LSL_Functions;
			ScriptWrapper newSW      = ScriptWrapper.CreateScriptInstance (dllName);
			testLSLAPI.scriptWrapper = newSW;
			newSW.beAPI              = beAPI;
			newSW.alwaysSuspend      = true;
			oldSW.Dispose ();
			Console.WriteLine ("CheckpointStepper: migrating in from file");
			Stream inStream = File.OpenRead (fileName);
			newSW.MigrateInEventHandler (inStream);
			int rc = inStream.Read (stopOnADime, 0, 4);
			if ((rc != 4) || (stopOnADime[0] != (byte)'D') || (stopOnADime[1] != (byte)'i') || 
			                 (stopOnADime[2] != (byte)'m') || (stopOnADime[3] != (byte)'e')) {
				throw new Exception ("didnt stop on a dime");
			}
			inStream.Close ();
			Console.WriteLine ("CheckpointStepper: migrate in complete");

			return newSW;
		}

		public static void ErrorMessage (Token token, string message)
		{
			Console.WriteLine ("Error at {0}.{1}: {2}", token.line, token.posn, message);
		}
	}
}
