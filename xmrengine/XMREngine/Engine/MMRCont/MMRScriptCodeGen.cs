/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Reflection;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;
using log4net;
using Microsoft.CSharp;
using System.CodeDom.Compiler;


/**
 * @brief translate a reduced script token into corresponding CIL code.
 * The single script token contains a tokenized and textured version of the whole script file.
 */

namespace MMR
{

	public class ScriptCodeGen
	{
		private static readonly ILog m_log =
			LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		public static readonly string COMPILED_VERSION_NAME = "XMRCompiledVersion";
		public static readonly int COMPILED_VERSION_VALUE = 1;

		public static readonly int CALL_FRAME_MEMUSE = 64;
		public static readonly int STRING_LEN_TO_MEMUSE = 2;

		private static CSharpCodeProvider CSCodeProvider = new CSharpCodeProvider();

		/*
		 * Static tables that there only needs to be one copy of for all.
		 */
		public static Dictionary<string, TokenDeclFunc> beAPIFunctions = null;

		private static Dictionary<string, BinOpStr> binOpStrings = null;
		private static TokenTypeBool tokenTypeBool = new TokenTypeBool (null);
		private static Dictionary<string, string> legalTypeCasts = null;

		public static bool CodeGen (TokenScript tokenScript, string binaryName,
				string debugFileName)
		{

			/*
			 * This is a table of methods that take two operands and an operator
			 * and generate the code to process them.
			 */
			if (binOpStrings == null) {
				Dictionary<string, BinOpStr> bos = DefineBinOps ();
				//MB()
				binOpStrings = bos;
			}
			//MB()

			if (beAPIFunctions == null) {

				/*
				 * Look up an BackEnd Api function by name and get its prototype.
				 * We do this by looking at the ScriptBaseClass interface definition.
				 */
				InternalFuncDict beapif = new InternalFuncDict (typeof (ScriptBaseClass), true);

				//MB()

				/*
				 * Now that the dictionary is complete, let other threads see it.
				 */
				beAPIFunctions = beapif;
			}
			//MB()

			if (legalTypeCasts == null) {
				Dictionary<string, string> ltc = CreateLegalTypeCasts ();
				//MB()
				legalTypeCasts = ltc;
			}
			//MB()

			/*
			 * Run compiler such that it has a 'this' context for convenience.
			 */
			ScriptCodeGen sc = new ScriptCodeGen (tokenScript, binaryName,
					debugFileName);
			return sc.exitCode == 0;
		}

		/*
		 * There is one set of these variables for each script being compiled.
		 */
		private bool youveAnError = false;
		private MemoryStream objectFile;
		private TextWriter objectWriter;
		private int exitCode = 0;
		private int nStates = 0;
		private string smClassName = null;
		private TokenDeclFunc curDeclFunc = null;
		private TokenStmtBlock curStmtBlock = null;

		private Dictionary<string, TokenDeclFunc> scriptFunctions = null;
		private Dictionary<string, int> stateIndices = null;
		private Stack<Dictionary<string, TokenType>> scriptVariablesStack = null;
		private Dictionary<string, TokenType> scriptInstanceVariables = null;

		private ScriptCodeGen (TokenScript tokenScript, string binaryName, string debugFileName)
		{
			/*
			 * Set up dictionary to translate function names to their declaration.
			 * We only do top-level functions so this doesn't need to be a stack.
			 */
			scriptFunctions = new Dictionary<string, TokenDeclFunc> ();

			/*
			 * Set up dictionary to translate state names to their index number.
			 */
			stateIndices = new Dictionary<string, int> ();

			/*
			 * Assign each state its own unique index.
			 * The default state gets 0.
			 */
			nStates = 0;
			tokenScript.defaultState.body.index = nStates ++;
			foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclState> kvp in tokenScript.states) {
				TokenDeclState declState = kvp.Value;
				declState.body.index = nStates ++;
				stateIndices.Add (declState.name.val, declState.body.index);
			}

			/*
			 * Set up stack of dictionaries to translate variable names to their declaration.
			 * Then push the first element on the stack that will get any global variables.
			 */
			scriptVariablesStack = new Stack<Dictionary<string, TokenType>> ();
			scriptInstanceVariables = PushVarDefnBlock ();

			objectFile   = new MemoryStream();
			objectWriter = new StreamWriter (objectFile);

			/*
			 * Output script class statement.
			 * Note we don't put any 'using' statements to avoid nasty tricks,
			 * so we must use complete names when referencing classes.
			 */
			smClassName  = "ScriptModule";
			WriteOutput (0, "public class " + smClassName + " : MMR.ScriptWrapper {");

			/*
			 * Write out COMPILED_VERSION.  This is used by ScriptWrapper to determine if the
			 * compilation is suitable for it to use.  If it sees the wrong version, it will
			 * recompile the script so it has the correct version.
			 */
			WriteOutput (0, "public static readonly int " + COMPILED_VERSION_NAME + " = " + COMPILED_VERSION_VALUE.ToString () + ";");

			/*
			 * Put script defined functions in 'scriptFunctions' dictionary so any calls
			 * made by functions or event handlers will be seen, in case of forward
			 * references.
			 *
			 * Prefix the names with __fun_ to keep them separate from any ScriptWrapper functions.
			 */
			foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclFunc> kvp in tokenScript.funcs) {
				TokenDeclFunc declFunc = kvp.Value;
				scriptFunctions.Add (declFunc.funcName.val, declFunc);
			}

			/*
			 * Translate all the global variable declarations to private instance variables.
			 * Prefix them with __gbl_ so scripts can't directly access any vars defined in ScriptWrapper,
			 * as all script-generated references are done with __sm.__gbl_<name>.
			 */
			int nGlobals = 0;
			foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclVar> kvp in tokenScript.vars) {
				TokenDeclVar declVar = kvp.Value;
				AddVarDefinition (declVar.type, declVar.name.val);
				WriteOutput (declVar, "private " + TypeName (declVar.type) + " __gbl_" + declVar.name.val + ";");
				nGlobals ++;
			}

			/*
			 * We only need a default constructor with no arguments.
			 * All it does is fill in the instance variable declarations initial values.
			 * We set up __sm in case any of the GenerateFromRVal()s references it.
			 * Then we use __sm to reference the instance vars so __sm doesn't go unreferenced.
			 */
			if (nGlobals > 0) {
				WriteOutput (0, "public " + smClassName + " () {");
				WriteOutput (0, smClassName + " __sm = this;");
				WriteOutput (0, TypeName (typeof (ScriptContinuation)) + " __sc = this.continuation;");

				/*
				 * Now fill in field initialization values.
				 */
				int initialSize = 0;
				foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclVar> kvp in tokenScript.vars) {
					TokenDeclVar declVar = kvp.Value;
					initialSize += TokenType.StaticSize (declVar.type.typ);
					if (declVar.init != null) {
						CompRVal rVal = GenerateFromRVal (null, declVar.init);
						WriteOutput (declVar, "__sm.__gbl_" + declVar.name.val + " = " + StringWithCast (declVar.type, rVal) + ";");
						if (declVar.type is TokenTypeList) {
							WriteOutput (declVar, "__sm.memUsage += __sm.__gbl_" + declVar.name.val + ".Size;");
						}
						if (declVar.type is TokenTypeStr) {
							WriteOutput (declVar, "__sm.memUsage += __sm.__gbl_" + declVar.name.val + ".Length * " + STRING_LEN_TO_MEMUSE + ";");
						}
					}
				}
				WriteOutput (0, "__sm.memUsage += " + initialSize.ToString () + ";");

				/*
				 * That's all for the constructor.
				 */
				WriteOutput (0, "}");
			}

			/*
			 * Output each global function as a private method.  We dont need to add them to function
			 * table because that was done above.
			 *
			 * Prefix the names with __fun_ to keep them separate from any ScriptWrapper functions.
			 */
			foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclFunc> kvp in tokenScript.funcs) {
				TokenDeclFunc declFunc = kvp.Value;
				WriteOutput (declFunc, "[Mono.Tasklets.MMRContableAttribute ()]\n");
				WriteOutput (declFunc, "private static ");
				GenerateMethod (declFunc);
			}

			/*
			 * Output default state.
			 * Each event handler is a private static method named __seh_default_<eventname>
			 */
			GenerateStateHandler ("default", tokenScript.defaultState.body);

			/*
			 * Output script-defined states.
			 * Each event handler is a private static method named __seh_<statename>_<eventname>
			 */
			foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclState> kvp in tokenScript.states) {
				TokenDeclState declState = kvp.Value;
				GenerateStateHandler (declState.name.val, declState.body);
			}

			/*
			 * Create a static variable to hold the ScriptEventHandlerTable.
			 */
			WriteOutput (0, "private static MMR.ScriptEventHandler[,] myScriptEventHandlerTable;");

			/*
			 * Create a method that builds and retrieves a pointer to the matrix:
			 *
			 * public static ScriptEventHandler[,] GetScriptEventHandlerTable ()
			 * {
			 *    if (scriptEventHandlerTable == null) {
			 *       ScriptEventHandler[,] seht = new ScriptEventHandler[,];
			 *       seht[stateIndex,eventIndex] = entrypoint;
			 *       ...
			 *       //MB()
			 *       scriptEventHandlerTable = seht;
			 *    }
			 *    //MB()
			 *    return scriptEventHandlerTable;
			 * }
			 * 
			 * The returned table is what the script engine uses to access the event handlers.
			 * The table is indexed by the current state and an event code (from enum ScriptEventCode):
			 *    ScriptEventHandler seh = scriptEventHandlerTable[stateIndex,eventCode];
			 * ...and each entry is called:
			 *    object[] args = new object[] { event handler argument list... };
			 *    seh(this,args);
			 */
			WriteOutput (0, "public static MMR.ScriptEventHandler[,] GetScriptEventHandlerTable () {");
			WriteOutput (0, "if (myScriptEventHandlerTable == null) {");
			WriteOutput (0, "MMR.ScriptEventHandler[,] seht = new MMR.ScriptEventHandler[" + nStates + "," + (int)ScriptEventCode.Size + "];");
			GenerateSEHTFill ("default", tokenScript.defaultState.body);
			foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclState> kvp in tokenScript.states) {
				TokenDeclState declState = kvp.Value;
				GenerateSEHTFill (declState.name.val, declState.body);
			}
			WriteOutput (0, "//MB();");
			WriteOutput (0, "myScriptEventHandlerTable = seht;");
			WriteOutput (0, "}");
			WriteOutput (0, "//MB();");
			WriteOutput (0, "return myScriptEventHandlerTable;");
			WriteOutput (0, "}");

			/*
			 * Output a function to convert a stateCode (integer) to a state name (string).
			 */
			WriteOutput (0, "private static string[] stateNames = new string[] { \"default\"");
			int i = 0;
			foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclState> kvp in tokenScript.states) {
				TokenDeclState declState = kvp.Value;
				if (declState.body.index != ++ i) {
					throw new Exception ("state index mismatch");
				}
				WriteOutput (0, ", \"" + declState.name.val + "\"");
			}
			WriteOutput (0, "};");
			WriteOutput (0, "public override string GetStateName (int stateCode) {");
			WriteOutput (0, "if ((stateCode < 0) || (stateCode >= stateNames.Length)) return null;");
			WriteOutput (0, "return stateNames[stateCode];");
			WriteOutput (0, "}");

			/*
			 * Generate a method to migrate the script out, ie, serialize it.
			 */
			WriteOutput (0, "public override void MigrateScriptOut (System.IO.Stream stream, Mono.Tasklets.MMRContSendObj sendObj) {");
			foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclVar> kvp in tokenScript.vars) {
				TokenDeclVar declVar = kvp.Value;
				WriteOutput (0, "sendObj (stream, (object)__gbl_" + declVar.name.val + ");");
			}
			WriteOutput (0, "}");

			/*
			 * Generate a method to migrate the script in, ie, deserialize it.
			 */
			WriteOutput (0, "public override void MigrateScriptIn (System.IO.Stream stream, Mono.Tasklets.MMRContRecvObj recvObj) {");
			foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclVar> kvp in tokenScript.vars) {
				TokenDeclVar declVar = kvp.Value;
				WriteOutput (0, "__gbl_" + declVar.name.val + " = (" + TypeName (declVar.type) + ")recvObj (stream);");
			}
			WriteOutput (0, "}");

			/*
			 * Generate a method to dispose the script.
			 * It writes garbage values to the variables, used for debugging migration.
			 */
			WriteOutput (0, "public override void Dispose () {");
			foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclVar> kvp in tokenScript.vars) {
				TokenDeclVar declVar = kvp.Value;
				WriteOutput (0, "__gbl_" + declVar.name.val + " = MMR.ScriptWrapper.disposed_" + declVar.type.ToString () + ";");
			}
			WriteOutput (0, "base.Dispose ();");
			WriteOutput (0, "}");

			/*
			 * Finally a function to translate a C# line number to an LSL line number, for runtime exception reporting.
			 */
			WriteOutput (0, "private static int[] lineNoTrans = new int[] {0");
			i = 0;
			int[] lineNoArray = System.Linq.Enumerable.ToArray (lineNoTrans);
			foreach (int srcLineNo in lineNoArray) {
				WriteOutput (0, "," + srcLineNo.ToString ());
				if (++ i == 10) {
					WriteOutput (0, "\n");
					i = 0;
				}
			}
			WriteOutput (0, " };");
			WriteOutput (0, "public override int DLLToSrcLineNo (int dllLineNo) {");
			WriteOutput (0, "if ((dllLineNo < 0) || (dllLineNo >= lineNoTrans.Length)) return -1;");
			WriteOutput (0, "return lineNoTrans[dllLineNo];");
			WriteOutput (0, "}");

			/*
			 * End of the ScriptModule class definition.
			 */
			WriteOutput (0, "}");
			objectWriter.Flush();
			objectWriter.Close();

			/*
			 * Check for error and abort if so.
			 */
			if (youveAnError) {
				exitCode = -1;
				return;
			}

			/*
			 * Convert C# to .DLL by sending to comipler.
			 * Theoretically, we shouldn't get any errors from the compilation.
			 */
			UTF8Encoding encoding = new UTF8Encoding();
			string text = encoding.GetString(objectFile.ToArray());

			if (debugFileName != String.Empty)
			{
				FileStream dfs = File.Create(debugFileName);
				StreamWriter dsw = new StreamWriter(dfs);

				dsw.Write(text);

				dsw.Close();
				dfs.Close();
			}

			CompilerParameters parameters = new CompilerParameters();

			parameters.IncludeDebugInformation = true;

			string rootPath =
				Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory);
			
			parameters.ReferencedAssemblies.Add(Path.Combine(rootPath,
				"OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll"));
			parameters.ReferencedAssemblies.Add(Path.Combine(rootPath,
				"OpenSim.Region.ScriptEngine.Shared.dll"));
			parameters.ReferencedAssemblies.Add(Path.Combine(rootPath,
				"OpenSim.Region.ScriptEngine.XMREngine.Engine.MMRCont.dll"));

			parameters.ReferencedAssemblies.Add("Mono.Tasklets.dll");

			parameters.GenerateExecutable = false;
			parameters.OutputAssembly = binaryName;

			CompilerResults results;

			try
			{
				lock(CSCodeProvider)
				{
					results = CSCodeProvider.CompileAssemblyFromSource(parameters, text);
				}

				if (results.Errors.Count > 0)
				{
					foreach (CompilerError CompErr in results.Errors)
					{
						if (CompErr.IsWarning)
							continue;

						m_log.DebugFormat("[MMR]: ({0},{1}]) Error: {2}",
								CompErr.Line, CompErr.Column,
								CompErr.ErrorText);

						exitCode = 1;
					}
				}
			}
			catch
			{
				exitCode = 1;
			}
		}

		/**
		 * @brief generate event handler code
		 * Writes out a function definition for each state handler
		 * named __seh_<statename>_<eventname>
		 *
		 * However, each has just 'ScriptWrapper __sw' as its single argument
		 * and each of its user-visible argments is extracted from __sm.ehArgs[].
		 *
		 * So we end up generating something like this:
		 *
		 *   private static void __seh_<statename_<eventname>(ScriptWrapper __sw)
		 *   {
		 *      <smClassName> __sm = (ScriptWrapper)__sw;
		 *      ScriptContinuation __sc = __sm.continuation;
		 *      <typeArg0> <namearg0> = (<typeArg0>)__sw.ehArgs[0];
		 *      <typeArg1> <nameArg1> = (<typeArg1>)__sw.ehArgs[1];
		 *
		 *      ... script code ...
		 *   }
		 *
		 * The continuations code assumes there will be no references to ehArgs[]
		 * after the first call to CheckRun() as CheckRun() makes no attempt to
		 * serialize the ehArgs[] array, as doing so would be redundant.  Any values
		 * from ehArgs[] that are being used will be in local stack variables and
		 * thus preserved that way.
		 */
		private void GenerateStateHandler (string statename, TokenStateBody body)
		{
			for (Token t = body.eventFuncs; t != null; t = t.nextToken) {
				GenerateEventHandler (statename, (TokenDeclFunc)t);
			}
		}

		private void GenerateEventHandler (string statename, TokenDeclFunc declFunc)
		{
			string eventname = declFunc.funcName.val;
			TokenArgDecl argDecl = declFunc.argDecl;

			/*
			 * Push current function being processed.
			 */
			TokenDeclFunc oldDeclFunc = curDeclFunc;
			curDeclFunc = declFunc;

			/*
			 * Any vars defined by function, including its args, go in their own var block.
			 */
			PushVarDefnBlock ();

			/*
			 * Output function header.
			 */
			WriteOutput (declFunc, "[Mono.Tasklets.MMRContableAttribute ()]\n");
			WriteOutput (declFunc, "private static void __seh_");
			WriteOutput (declFunc, statename);
			WriteOutput (declFunc, "_");
			WriteOutput (declFunc, eventname);
			WriteOutput (declFunc, "(MMR.ScriptWrapper __sw) {");

			/*
			 * We use a __sm variable to access the script's user-defined fields and methods.
			 * And __sc is a cache for __sm.continuation for calling CheckRun().
			 */
			WriteOutput (declFunc, smClassName + " __sm = (" + smClassName + ")__sw;");
			WriteOutput (declFunc, TypeName (typeof (ScriptContinuation)) + " __sc = __sw.continuation;");

			/*
			 * Output args as variable definitions and initialize each from __sw.ehArgs[].
			 * If the script writer goofed, the typecast will complain.
			 * ??? add arg count and static type checking to match our type using reflection
			 */
			if (argDecl.types.Length > 0) {
				// warning CS0219: The variable `__lcl_change' is assigned but its value is never used
				WriteOutput (0, "#pragma warning disable 219\n");
				for (int i = 0; i < argDecl.types.Length; i ++) {
					WriteOutput (argDecl, TypeName (argDecl.types[i]) + " __lcl_" + argDecl.names[i].val + " = (" +
							TypeName (argDecl.types[i]) + ")__sw.ehArgs[" + i + "];");
					AddVarDefinition (argDecl.types[i], argDecl.names[i].val);
				}
				WriteOutput (0, "#pragma warning restore 219\n");
			}

			/*
			 * Output code for the statements and clean up.
			 */
			GenMemUseBlockProlog (declFunc.body, true);
			GenerateFuncBody (declFunc);
			curDeclFunc = oldDeclFunc;
		}

		/**
		 * @brief generate code for an arbitrary script-defined function.
		 * @param name = name of the function
		 * @param argDecl = argument declarations
		 * @param body = function's code body
		 */
		private void GenerateMethod (TokenDeclFunc declFunc)
		{
			string name = declFunc.funcName.val;
			TokenArgDecl argDecl = declFunc.argDecl;

			/*
			 * Push current function being processed.
			 */
			TokenDeclFunc oldDeclFunc = curDeclFunc;
			curDeclFunc = declFunc;

			/*
			 * The function's vars, including its arguments, start in an empty definition frame.
			 */
			PushVarDefnBlock ();

			/*
			 * Output function header.
			 * We splice in <smClassName> __sm as the first argument as all our functions are static.
			 */
			if (declFunc.retType is TokenTypeVoid) {
				WriteOutput (declFunc, "void");
			} else {
				WriteOutput (declFunc, TypeName (declFunc.retType));
			}
			WriteOutput (declFunc, " __fun_");
			WriteOutput (declFunc, name);
			WriteOutput (declFunc, "(" + smClassName + " __sm");
			if (argDecl.types.Length > 0) {
				for (int i = 0; i < argDecl.types.Length; i ++) {
					WriteOutput (argDecl, "," + TypeName (argDecl.types[i]) + " __lcl_" + argDecl.names[i].val);
					AddVarDefinition (argDecl.types[i], argDecl.names[i].val);
				}
			}
			WriteOutput (declFunc, ") {");

			/*
			 * Account for the call frame and all top-level variables.
			 */
			GenMemUseBlockProlog (declFunc.body, true);

			/*
			 * See if time to suspend in case they are doing a loop with recursion.
			 */
			WriteOutput (declFunc.body, TypeName (typeof (ScriptContinuation)) + " __sc = __sm.continuation;");
			WriteOutput (declFunc.body, "__sc.CheckRun();");

			/*
			 * Output code for the statements and clean up.
			 */
			GenerateFuncBody (declFunc);
			curDeclFunc = oldDeclFunc;
		}

		/**
		 * @brief output function body.
		 *        the open brace and GenMemUseBlockProlog() has already been output.
		 */
		private void GenerateFuncBody (TokenDeclFunc declFunc)
		{

			/*
			 * Set up a local variable to capture return value.
			 */
			if (!(declFunc.retType is TokenTypeVoid)) {
				WriteOutput (declFunc, TypeName (declFunc.retType) + " __retval = " + DefaultValue (declFunc.retType) + ";");
			}

			/*
			 * Output code body.
			 */
			GenerateStmtBlock (declFunc.body, true);

			/*
			 * All return statements 'goto __Return' so we can do mem use epilog.
			 */
			WriteOutput (declFunc.body.closebr, "__Return:;");
			GenMemUseBlockEpilog (declFunc.body, true);
			if (declFunc.retType is TokenTypeVoid) {
				// I had an example where the moron gmcs didn't generate the 'ret' CIL opcode...
				WriteOutput (declFunc.body.closebr, "return;");
			} else {
				WriteOutput (declFunc.body.closebr, "return __retval;");
			}
			WriteOutput (declFunc.body.closebr, "}");

			/*
			 * All done, clean up.
			 */
			PopVarDefnBlock ();
		}

		/**
		 * @brief generate code for an arbitrary statement.
		 */
		private void GenerateStmt (TokenStmt stmt)
		{
			if (stmt is TokenStmtBlock)   { GenerateStmtBlock   ((TokenStmtBlock)stmt, false);       return; }
			if (stmt is TokenStmtDo)      { GenerateStmtDo      ((TokenStmtDo)stmt);                 return; }
			if (stmt is TokenStmtFor)     { GenerateStmtFor     ((TokenStmtFor)stmt);                return; }
			if (stmt is TokenStmtForEach) { GenerateStmtForEach ((TokenStmtForEach)stmt);            return; }
			if (stmt is TokenStmtIf)      { GenerateStmtIf      ((TokenStmtIf)stmt);                 return; }
			if (stmt is TokenStmtJump)    { GenerateStmtJump    ((TokenStmtJump)stmt);               return; }
			if (stmt is TokenStmtLabel)   { GenerateStmtLabel   ((TokenStmtLabel)stmt);              return; }
			if (stmt is TokenStmtRet)     { GenerateStmtRet     ((TokenStmtRet)stmt);                return; }
			if (stmt is TokenStmtRVal)    { GenerateStmtRVal    ((TokenStmtRVal)stmt);               return; }
			if (stmt is TokenStmtState)   { GenerateStmtState   ((TokenStmtState)stmt);              return; }
			if (stmt is TokenStmtWhile)   { GenerateStmtWhile   ((TokenStmtWhile)stmt);              return; }
			throw new Exception ("unknown TokenStmt type " + stmt.GetType ().ToString ());
		}

		/**
		 * @brief generate statement block (ie, with braces)
		 */
		private void GenerateStmtBlock (TokenStmtBlock stmtBlock, bool fromFunc)
		{

			/*
			 * If this is an inner statement block, generate the { and all that goes with it.
			 * Don't bother with the actual { if there is exactly one statement in the block.
			 *
			 * If this is a top-level statement block, the caller must do this as necessary.
			 */
			if (!fromFunc) {
				PushVarDefnBlock ();
				if ((stmtBlock.statements == null) ||
				    (stmtBlock.statements is TokenDeclVar) ||
				    (stmtBlock.statements.nextToken != null)) {
					WriteOutput (stmtBlock, "{");
				}
				GenMemUseBlockProlog (stmtBlock, false);
			}

			/*
			 * Push new current statement block pointer for anyone who cares.
			 */
			TokenStmtBlock oldStmtBlock = curStmtBlock;
			curStmtBlock = stmtBlock;

			/*
			 * Declare any variables at the very top of the block in case there
			 * is a jump or return statement that exits the block, so we can
			 * account for the used memory.
			 *
			 * We will just set them to their default value though as we don't
			 * want to execute any initialization code out of order.  Then the
			 * actual initialization will turn into an assignment statement.
			 *
			 * Do not put in codegen's list of defined variables for the block
			 * yet though so we will give proper undefined variable messages.
			 */
			bool seenStatements = false;
			for (Token t = stmtBlock.statements; t != null; t = t.nextToken) {
				if (!(t is TokenDeclVar)) {
					seenStatements = true;
				} else if (seenStatements) {
					TokenDeclVar declVar = (TokenDeclVar)t;
					declVar.preDefd = true;
					WriteOutput (declVar, TypeName (declVar.type) + " __lcl_" + declVar.name.val + " = " + DefaultValue (declVar.type) + ";");
				}
			}

			/*
			 * Output the statements that make up the block.
			 */
			for (Token t = stmtBlock.statements; t != null; t = t.nextToken) {
				if (t is TokenStmt) {
					GenerateStmt ((TokenStmt)t);
				} else {
					GenerateDeclVar ((TokenDeclVar)t);
				}
			}

			/*
			 * Pop the current statement block.
			 */
			curStmtBlock = oldStmtBlock;

			/*
			 * If this is an inner statement block, generate the } and all that goes with it.
			 * Don't bother with the actual } if there is exactly one statement in the block.
			 *
			 * If this is a top-level statement block, the caller must do this as necessary.
			 */
			if (!fromFunc) {
				GenMemUseBlockEpilog (stmtBlock, false);
				if ((stmtBlock.statements == null) ||
				    (stmtBlock.statements is TokenDeclVar) ||
				    (stmtBlock.statements.nextToken != null)) {
					WriteOutput (stmtBlock.closebr, "}");
				}
				PopVarDefnBlock ();
			}
		}

		/**
		 * @brief output code for a 'do' statement
		 * Must use labels and if/goto's instead of braces as the 'while' clause may generate temp 
		 * assignment statements and so the result may not be in scope outside the closing brace.
		 */
		private void GenerateStmtDo (TokenStmtDo doStmt)
		{
			string lbl = GetTempNo ();

			string loopLabel = "__DoLoop_" + lbl;
			WriteOutput (doStmt, loopLabel + ":;");
			GenerateStmt (doStmt.bodyStmt);
			WriteOutput (doStmt, "__sc.CheckRun();");
			CompRVal testRVal = GenerateFromRVal (null, doStmt.testRVal);
			WriteOutput (doStmt.testRVal, "if (");
			OutputWithCastToBool (testRVal);
			WriteOutput (doStmt.testRVal, ") goto " + loopLabel + ";");
		}

		/**
		 * @brief output code for a 'for' statement
		 * Must use labels and if/goto's instead of braces as the test expression may generate temp 
		 * assignment statements and then we can't cram all the temp assignment statments in a real
		 * for statement.
		 */
		private void GenerateStmtFor (TokenStmtFor forStmt)
		{
			string lbl = GetTempNo ();

			string doneLabel = "__ForDone_" + lbl;
			string loopLabel = "__ForLoop_" + lbl;

			if (forStmt.initStmt != null) {
				GenerateStmt (forStmt.initStmt);
			}
			WriteOutput (forStmt, loopLabel + ":;");
			WriteOutput (forStmt, "__sc.CheckRun();");
			if (forStmt.testRVal != null) {
				CompRVal testRVal = GenerateFromRVal (null, forStmt.testRVal);
				WriteOutput (forStmt.testRVal, "if (!");
				OutputWithCastToBool (testRVal);
				WriteOutput (forStmt.testRVal, ") goto " + doneLabel + ";");
			}
			GenerateStmt (forStmt.bodyStmt);
			if (forStmt.incrRVal != null) {
				GenerateFromRVal (new CompRVal (new TokenTypeVoid (forStmt), "forIncr"), forStmt.incrRVal);
			}
			WriteOutput (forStmt, "goto " + loopLabel + ";");
			WriteOutput (forStmt, doneLabel + ":;");
		}

		private void GenerateStmtForEach (TokenStmtForEach forEachStmt)
		{
			string lbl = GetTempNo ();
			CompLVal keyLVal   = null;
			CompLVal valLVal   = null;
			CompLVal arrayLVal = GenerateFromLVal (forEachStmt.arrayLVal);

			if (forEachStmt.keyLVal != null) {
				keyLVal = GenerateFromLVal (forEachStmt.keyLVal);
				if (!(keyLVal.type is TokenTypeObject)) {
					ErrorMsg (forEachStmt.arrayLVal, "must be object");
				}
			}
			if (forEachStmt.valLVal != null) {
				valLVal = GenerateFromLVal (forEachStmt.valLVal);
				if (!(valLVal.type is TokenTypeObject)) {
					ErrorMsg (forEachStmt.arrayLVal, "must be object");
				}
			}
			if (!(arrayLVal.type is TokenTypeArray)) {
				ErrorMsg (forEachStmt.arrayLVal, "must be an array");
			}

			string doneLabel = "__ForBrk_" + lbl;
			string loopLabel = "__ForTop_" + lbl;
			string indexVar  = "__ForIdx_" + lbl;
			string objectVar = "__ForObj_" + lbl;

			WriteOutput (forEachStmt, "int " + indexVar + " = 0;");
			if ((keyLVal == null) || (valLVal == null)) {
				WriteOutput (forEachStmt, "object " + indexVar + ";");
			}
			WriteOutput (forEachStmt, loopLabel + ":;");
			WriteOutput (forEachStmt, "if (!" + arrayLVal.locstr + ".ForEach(" + indexVar + "++, ref ");
			WriteOutput (forEachStmt, (keyLVal == null) ? objectVar : keyLVal.locstr);
			WriteOutput (forEachStmt, ", ref ");
			WriteOutput (forEachStmt, (valLVal == null) ? objectVar : valLVal.locstr);
			WriteOutput (forEachStmt, ")) goto " + doneLabel + ";");
			WriteOutput (forEachStmt, "__sc.CheckRun();");
			GenerateStmt (forEachStmt.bodyStmt);
			WriteOutput (forEachStmt, "goto " + loopLabel + ";");
			WriteOutput (forEachStmt, doneLabel + ":;");
		}

		/**
		 * @brief output code for an 'if' statement
		 * Braces are necessary because what may be one statement for trueStmt or elseStmt in
		 * the script may translate to more than one statement in the resultant C# code.
		 */
		private void GenerateStmtIf (TokenStmtIf ifStmt)
		{
			CompRVal testRVal = GenerateFromRVal (null, ifStmt.testRVal);
			WriteOutput (ifStmt, "if (");
			OutputWithCastToBool (testRVal);
			WriteOutput (ifStmt, ") {");
			GenerateStmt (ifStmt.trueStmt);
			WriteOutput (ifStmt, "}");
			if (ifStmt.elseStmt != null) {
				WriteOutput (ifStmt.elseStmt, "else {");
				GenerateStmt (ifStmt.elseStmt);
				WriteOutput (ifStmt.elseStmt, "}");
			}
		}

		/**
		 * @brief output code for a 'jump' statement
		 */
		private void GenerateStmtJump (TokenStmtJump jumpStmt)
		{
			/*
			 * Make sure the target label is defined somewhere in the function.
			 */
			if (!curDeclFunc.labels.ContainsKey (jumpStmt.label.val)) {
				ErrorMsg (jumpStmt, "undefined label " + jumpStmt.label.val);
				return;
			}
			TokenStmtLabel stmtLabel = curDeclFunc.labels[jumpStmt.label.val];

			/*
			 * Find which block the target label is in.  Must be in this or an outer block,
			 * no laterals allowed or it would make memory usage accounting very difficult.
			 */
			TokenStmtBlock stmtBlock;
			for (stmtBlock = curStmtBlock; stmtBlock != null; stmtBlock = stmtBlock.outerStmtBlock) {
				if (stmtBlock == stmtLabel.block) break;
			}
			if (stmtBlock == null) {
				ErrorMsg (jumpStmt, "no lateral jumps allowed");
				return;
			}

			/*
			 * If we are jumping to an outer block, decrement memory usage by whatever variables we leave behind.
			 */
			for (stmtBlock = curStmtBlock; stmtBlock != null; stmtBlock = stmtBlock.outerStmtBlock) {
				if (stmtBlock == stmtLabel.block) break;
				GenMemUseBlockEpilog (stmtBlock, false);
			}

			/*
			 * Finally output the equivalent 'goto' statement.
			 */
			WriteOutput (jumpStmt, "goto __Jump_" + jumpStmt.label.val + ";");
		}

		/**
		 * @brief output code for a jump target label statement.
		 */
		private void GenerateStmtLabel (TokenStmtLabel labelStmt)
		{
			WriteOutput (labelStmt, "__Jump_" + labelStmt.name.val + ":;");
			if (labelStmt.hasBkwdRefs) {
				WriteOutput (labelStmt, " __sc.CheckRun();");
			}
		}

		/**
		 * @brief output code for a return statement.
		 * @param retStmt = return statement token, including return value if any
		 */
		private void GenerateStmtRet (TokenStmtRet retStmt)
		{
			/*
			 * Set return value variable (if not void).
			 */
			if (retStmt.rVal != null) {
				CompRVal retVal = new CompRVal (curDeclFunc.retType, "__retval");
				CompRVal rVal = GenerateFromRVal (retVal, retStmt.rVal);
				if (rVal != retVal) {
					WriteOutput (retStmt, "__retval = " + StringWithCast (curDeclFunc.retType, rVal) + ";");
				}
			} else if (!(curDeclFunc.retType is TokenTypeVoid)) {
				ErrorMsg (retStmt, "function requires return value type " + curDeclFunc.retType.ToString ());
			}

			/*
			 * Goto function epilog.
			 */
			OutputGotoReturn (retStmt);
		}

		/**
		 * @brief the statement is just an expression, most likely an assignment or a ++ or -- thing.
		 */
		private void GenerateStmtRVal (TokenStmtRVal rValStmt)
		{
			/*
			 * Tell the expression code generator that the result is going to be
			 * thrown away by giving it a location of type 'void'.  This will tell
			 * things like 'i ++' or calls to not bother saving the result in a temp.
			 */
			CompRVal rVal = new CompRVal (new TokenTypeVoid (rValStmt), "rValStmt");
			GenerateFromRVal (rVal, rValStmt.rVal);
		}

		/**
		 * @brief generate code for a 'state' statement that transitions state.
		 * It sets the new state then returns.
		 */
		private void GenerateStmtState (TokenStmtState stateStmt)
		{

			/*
			 * Set new state value and set the global 'stateChanged' flag.
			 */
			if (stateStmt.state == null) {
				WriteOutput (stateStmt, "__sm.stateCode = 0;");  // default state
			} else if (!stateIndices.ContainsKey (stateStmt.state.val)) {
				ErrorMsg (stateStmt, "undefined state " + stateStmt.state.val);
			} else {
				int index = stateIndices[stateStmt.state.val];
				WriteOutput (stateStmt, "__sm.stateCode = " + index + ";");
			}
			WriteOutput (stateStmt, "__sm.stateChanged = true;");

			/*
			 * Goto function epilog.
			 */
			OutputGotoReturn (stateStmt);
		}

		/**
		 * @brief generate code for a 'while' statement including the loop body.
		 */
		private void GenerateStmtWhile (TokenStmtWhile whileStmt)
		{
			string lbl = GetTempNo ();

			string breakLabel = "__WhileBot_" + lbl;
			string contLabel = "__WhileTop_" + lbl;

			WriteOutput (whileStmt, contLabel + ":;");
			WriteOutput (whileStmt, "__sc.CheckRun();");
			CompRVal testRVal = GenerateFromRVal (null, whileStmt.testRVal);
			WriteOutput (whileStmt.testRVal, "if (!");
			OutputWithCastToBool (testRVal);
			WriteOutput (whileStmt.testRVal, ") goto " + breakLabel + ";");
			GenerateStmt (whileStmt.bodyStmt);
			WriteOutput (whileStmt, "goto " + contLabel + ";");
			WriteOutput (whileStmt, breakLabel + ":;");
		}

		/**
		 * @brief process a variable declaration statement, possibly with initialization expression.
		 */
		private void GenerateDeclVar (TokenDeclVar declVar)
		{
			if (declVar.init != null) {

				/*
				 * Script gave us an initialization value, so just use it.
				 */
				CompRVal var;
				if (declVar.preDefd) {
					var = new CompRVal (declVar.type, "__lcl_" + declVar.name.val);
				} else {
					var = new CompRVal (declVar.type, TypeName (declVar.type) + " __lcl_" + declVar.name.val);
				}
				CompRVal rVal = GenerateFromRVal (var, declVar.init);
				if (rVal != var) {
					WriteOutput (declVar, var.locstr + " = " + StringWithCast (var.type, rVal) + ";");
				}
				GenMemUseIncrement ("__lcl_" + declVar.name.val, declVar.type);
			} else if (!declVar.preDefd) {

				/*
				 * Scripts have paths that don't initialize variables.
				 * So initialize them with something so C# compiler doesn't complain.
				 */
				WriteOutput (declVar, TypeName (declVar.type) + " __lcl_" + declVar.name.val + " = " + DefaultValue (declVar.type) + ";");
			}

			/*
			 * Now it's ok for subsequent expressions in the block to reference the variable.
			 */
			AddVarDefinition (declVar.type, declVar.name.val);
		}

		/**
		 * @brief Get the type and location of an L-value (eg, variable)
		 */
		private CompLVal GenerateFromLVal (TokenLVal lVal)
		{
			if (lVal is TokenLValArEle) return GenerateFromLValArEle ((TokenLValArEle)lVal);
			if (lVal is TokenLValField) return GenerateFromLValField ((TokenLValField)lVal);
			if (lVal is TokenLValName)  return GenerateFromLValName  ((TokenLValName)lVal);
			throw new Exception ("bad lval class");
		}

		/**
		 * @brief we have an L-value token that is an element within an array.
		 * @returns a CompLVal giving the type and location of the element of the array.
		 */
		private CompLVal GenerateFromLValArEle (TokenLValArEle lVal)
		{
			/*
			 * Compute subscript before rest of lVal in case of multiple subscripts.
			 */
			CompRVal subRVal = GenerateFromRVal (null, lVal.subRVal);

			/*
			 * Compute location of array itself.
			 */
			CompLVal baseLVal = GenerateFromLVal (lVal.baseLVal);

			/*
			 * It better be an array!
			 */
			if (!(baseLVal.type is TokenTypeArray)) {
				ErrorMsg (lVal, "taking subscript of non-array");
				return baseLVal;
			}

			/*
			 * Ok, generate reference.
			 */
			return new CompLVal (new TokenTypeObject (lVal), "((" + baseLVal.locstr + ")[" + subRVal.locstr + "])");
		}

		/**
		 * @brief we have an L-value token that is a field within a struct.
		 * @returns a CompLVal giving the type and location of the field in the struct.
		 */
		private CompLVal GenerateFromLValField (TokenLValField lVal)
		{
			CompLVal baseLVal = GenerateFromLVal (lVal.baseLVal);
			string fieldName = lVal.field.val;

			/*
			 * Since we only have a few types with fields, just pound them out.
			 * To expand, we can make a table, possibly using reflection to look up the baseLVal.type definition.
			 */
			if (baseLVal.type is TokenTypeArray) {
				if (fieldName == "count") {
					return new CompLVal (new TokenTypeInt (lVal), "(" + baseLVal.locstr + ").__pub_" + fieldName);
				}
				if ((fieldName == "index") || (fieldName == "value")) {
					TokenTypeMeth ttm             = new TokenTypeMeth (lVal);
					ttm.funcs                     = new TokenDeclFunc[1];
					ttm.funcs[0]                  = new TokenDeclFunc (lVal);
					ttm.funcs[0].retType          = new TokenTypeObject (lVal);
					ttm.funcs[0].funcName         = new TokenName (lVal, "array." + fieldName);
					ttm.funcs[0].argDecl          = new TokenArgDecl (lVal);
					ttm.funcs[0].argDecl.types    = new TokenType[1];
					ttm.funcs[0].argDecl.types[0] = new TokenTypeInt (lVal);
					ttm.funcs[0].argDecl.names    = new TokenName[1];
					ttm.funcs[0].argDecl.names[0] = new TokenName (lVal, "number");
					return new CompLVal (ttm, "(" + baseLVal.locstr + ").__pub_" + fieldName);
				}
			}
			if (baseLVal.type is TokenTypeRot) {
				if ((fieldName.Length == 1) && (((fieldName[0] >= 'x') && (fieldName[0] <= 'z')) || (fieldName[0] == 's'))) {
					return new CompLVal (new TokenTypeFloat (lVal), "(" + baseLVal.locstr + ")." + fieldName);
				}
			}
			if (baseLVal.type is TokenTypeVec) {
				if ((fieldName.Length == 1) && (fieldName[0] >= 'x') && (fieldName[0] <= 'z')) {
					return new CompLVal (new TokenTypeFloat (lVal), "(" + baseLVal.locstr + ")." + fieldName);
				}
			}

			ErrorMsg (lVal, "type " + baseLVal.type + " does not define field " + fieldName);
			return baseLVal;
		}

		/**
		 * @brief we have an L-value token that is a variable name.
		 * @returns a CompLVal giving the type and location of the variable.
		 */
		private CompLVal GenerateFromLValName (TokenLValName lVal)
		{
			string name = lVal.name.val;

			foreach (Dictionary<string, TokenType> vars in scriptVariablesStack) {
				if (vars.ContainsKey (name)) {
					TokenType type = vars[name];
					if (vars == scriptInstanceVariables) name = "__sm.__gbl_" + name;
					else name = "__lcl_" + name;
					return new CompLVal (type, name);
				}
			}

			ErrorMsg (lVal, "undefined variable " + name);
			return new CompLVal (new TokenTypeVoid (lVal), name);
		}

		/**
		 * @brief generate code from an RVal expression and return its type and where the result is stored.
		 * For anything that has side-effects, statements are generated that perform the computation then
		 * the result it put in a temp var and the temp var name is returned.
		 * For anything without side-effects, they are returned as an equivalent string.
		 * Things like rotations and vectors are returned as a "new <objecttype>(<parameters>)" string.
		 * @param result = null: result can go anywhere
		 *                 else: try to put result here rather than create a temp if possible
		 *                       but if result.type is TokenTypeVoid, throw the result value away
		 * @param rVal = rVal token to be evaluated
		 * @returns resultant type and location (= result param if result param is used)
		 */
		private CompRVal GenerateFromRVal (CompRVal result, TokenRVal rVal)
		{
			if (rVal is TokenRValAsnPost)  return GenerateFromRValAsnPost (result, (TokenRValAsnPost)rVal);
			if (rVal is TokenRValAsnPre)   return GenerateFromRValAsnPre  (result, (TokenRValAsnPre)rVal);
			if (rVal is TokenRValCall)     return GenerateFromRValCall    (result, (TokenRValCall)rVal);
			if (rVal is TokenRValCast)     return GenerateFromRValCast    ((TokenRValCast)rVal);
			if (rVal is TokenRValFloat)    return GenerateFromRValFloat   ((TokenRValFloat)rVal);
			if (rVal is TokenRValInt)      return GenerateFromRValInt     ((TokenRValInt)rVal);
			if (rVal is TokenRValIsType)   return GenerateFromRValIsType  ((TokenRValIsType)rVal);
			if (rVal is TokenRValList)     return GenerateFromRValList    ((TokenRValList)rVal);
			if (rVal is TokenRValConst)    return ((TokenRValConst)rVal).val.rVal;
			if (rVal is TokenRValLVal)     return GenerateFromRValLVal    ((TokenRValLVal)rVal);
			if (rVal is TokenRValOpBin)    return GenerateFromRValOpBin   (result, (TokenRValOpBin)rVal);
			if (rVal is TokenRValOpUn)     return GenerateFromRValOpUn    ((TokenRValOpUn)rVal);
			if (rVal is TokenRValParen)    return GenerateFromRValParen   ((TokenRValParen)rVal);
			if (rVal is TokenRValRot)      return GenerateFromRValRot     ((TokenRValRot)rVal);
			if (rVal is TokenRValStr)      return GenerateFromRValStr     ((TokenRValStr)rVal);
			if (rVal is TokenRValUndef)    return GenerateFromRValUndef   ((TokenRValUndef)rVal);
			if (rVal is TokenRValVec)      return GenerateFromRValVec     ((TokenRValVec)rVal);
			throw new Exception ("bad rval class " + rVal.GetType ().ToString ());
		}

		/**
		 * @brief compute the result of a binary operator
		 * @param token = binary operator token, includes the left and right operands
		 * @returns where the resultant R-value is as something that doesn't have side effects
		 */
		private CompRVal GenerateFromRValOpBin (CompRVal result, TokenRValOpBin token)
		{
			CompLVal leftLVal = null;
			CompRVal left = null;
			CompRVal right;

			/*
			 * If left operand is an L-value, create an leftLVal location marker for it.
			 * In either case, create a R-value location marker for it.
			 */
			if (token.rValLeft is TokenRValLVal) {
				leftLVal = GenerateFromLVal (((TokenRValLVal)token.rValLeft).lvToken);
				left = new CompRVal (leftLVal.type, leftLVal.locstr);
			}

			/*
			 * Simple overwriting assignments are their own special case,
			 * as we want to cast the R-value to the type of the L-value.
			 * StringWithCast() is what determines if it is legal or not.
			 * And we might also be able to optimize out a temp by having
			 * the result put directly in the L-value variable.
			 */
			string opcodeIndex = token.opcode.ToString ();
			if (opcodeIndex == "=") {
				if (left == null) {
					ErrorMsg (token, "invalid L-value");
					left = GenerateFromRVal (null, token.rValLeft);
				} else {
					GenMemUseDecrement (leftLVal.locstr, leftLVal.type);
					right = GenerateFromRVal (left, token.rValRight);
					if (right != left) {
						WriteOutput (token, leftLVal.locstr + " = " + StringWithCast (leftLVal.type, right) + ";");
					}
					GenMemUseIncrement (leftLVal.locstr, leftLVal.type);
				}
				return left;
			}

			/*
			 * Comma operators are also special, as they say to compute the left-hand value
			 * and discard it, then compute the right-hand argument and that is the result.
			 */
			if (opcodeIndex == ",") {

				/*
				 * Compute left-hand operand but throw away result (because we say to store in a 'void').
				 */
				if (left == null) {
					CompRVal leftRVal = new CompRVal (new TokenTypeVoid (token.opcode), "comma");
					GenerateFromRVal (leftRVal, token.rValLeft);
				}

				/*
				 * Compute right-hand operand and that is the value of the expression.
				 */
				return GenerateFromRVal (result, token.rValRight);
			}

			/*
			 * Computation of some sort, compute right-hand operand value then left-hand value
			 * because LSL is supposed to be right-to-left evaluation.
			 *
			 * If left-hand operand has side effects, force right-hand operand into a temp so
			 * it will get computed first, and not just stacked for later evaluation.
			 */
			right = GenerateFromRVal (null, token.rValRight);
			if (token.rValLeft.sideEffects && !right.isFinal) {
				CompRVal rightTemp = new CompRVal (right.type);
				WriteOutput (token.rValRight, TypeName (rightTemp.type) + " " + rightTemp.locstr + " = ");
				WriteOutput (token.rValRight, StringWithCast (rightTemp.type, right) + ";");
				right = rightTemp;
			}
			left = GenerateFromRVal (null, token.rValLeft);

			/*
			 * Formulate key string for binOpStrings = (lefttype)(operator)(righttype)
			 */
			string leftIndex = left.type.ToString ();
			string rightIndex = right.type.ToString ();
			string key = leftIndex + opcodeIndex + rightIndex;

			/*
			 * If that key exists in table, then the operation is defined between those types
			 * ... and it produces an R-value of type as given in the table.
			 */
			if (binOpStrings.ContainsKey (key)) {
				BinOpStr binOpStr = binOpStrings[key];

				/*
				 * If table contained an explicit assignment type like +=, output the statement without
				 * casting the L-value, then return the L-value as the resultant value.
				 *
				 * Make sure we don't include such things as ==, >=, etc.
				 * Nothing like +=, -=, %=, etc, generate a boolean, only the comparisons.
				 */
				if ((binOpStr.outtype != typeof (bool)) && opcodeIndex.EndsWith ("=")) {
					WriteOutput (token, String.Format (binOpStr.format, left.locstr, right.locstr));
					WriteOutput (token, ";");
					return left;
				}

				/*
				 * It's the form left binop right.
				 * If either the original left or right had side effects, they should have been evaluated
				 * and put in temps already, so what we have for left and right don't have side effects.
				 * So we can simply return (outtype)(left binop right) as the location of the result.
				 */
				string fmt = binOpStr.format;
				int whack = fmt.IndexOf ('#');
				if (whack >= 0) {
					fmt = fmt.Substring (0, whack) + fmt.Substring (whack + 1);
				}
				StringBuilder retval = new StringBuilder ("((");
				retval.Append (TypeName (binOpStr.outtype));
				retval.Append (")(");
				retval.Append (String.Format (fmt, left.locstr, right.locstr));
				retval.Append ("))");
				CompRVal retRVal = new CompRVal (TokenType.FromSysType (token.opcode, binOpStr.outtype), retval.ToString ());
				retRVal.isFinal = left.isFinal && right.isFinal;
				return retRVal;
			}

			/*
			 * If the opcode ends with "=", it may be something like "+=".
			 * So look up the key as if we didn't have the "=" to tell us if the operation is legal.
			 * Also, the binary operation's output type must be the same as the L-value type.
			 * Likewise, integer += float not allowed because result is float, but float += integer is ok.
			 */
			if (opcodeIndex.EndsWith ("=")) {
				key = leftIndex + opcodeIndex.Substring (0, opcodeIndex.Length - 1) + rightIndex;
				if (binOpStrings.ContainsKey (key)) {

					/*
					 * Now we know for something like %= that left%right is legal for the types given.
					 * We can only actually process it if the resultant type is of the left type.
					 * So for example, we can't do float += list, as float + list gives a list.
					 */
					BinOpStr binOpStr = binOpStrings[key];
					if (binOpStr.outtype == left.type.typ) {

						/*
						 * Types are ok, see if the '=' form is allowed...
						 */
						string fmt = binOpStr.format;
						int whack = fmt.IndexOf ('#');
						if (whack >= 0) {
							if (leftLVal == null) {
								ErrorMsg (token, "invalid L-value");
							} else {
								GenMemUseDecrement (leftLVal.locstr, leftLVal.type);
								fmt = fmt.Substring (0, whack) + '=' + fmt.Substring (whack + 1);
								WriteOutput (token, String.Format (fmt, leftLVal.locstr, right.locstr));
								WriteOutput (token, ";");
								GenMemUseIncrement (leftLVal.locstr, leftLVal.type);
							}
							return left;
						}
					}
				}
			}

			/*
			 * Can't find it, oh well.
			 */
			ErrorMsg (token, "op not defined: " + leftIndex + " " + opcodeIndex + " " + rightIndex);
			return new CompRVal (new TokenTypeVoid (token.opcode), "undefOp");
		}

		/**
		 * @brief compute the result of an unary operator
		 * @param token = unary operator token, includes the operand
		 * @returns where the resultant R-value is
		 */
		private CompRVal GenerateFromRValOpUn (TokenRValOpUn token)
		{
			CompRVal inRVal = GenerateFromRVal (null, token.rVal);
			return UnOpGenerate (inRVal, token.opcode);
		}

		/**
		 * @brief postfix operator -- this returns the type and location of the resultant value
		 */
		private CompRVal GenerateFromRValAsnPost (CompRVal result, TokenRValAsnPost asnPost)
		{
			CompLVal lVal = GenerateFromLVal (asnPost.lVal);
			if (result == null) {

				/*
				 * Caller says they want the resultant value put in a temp.
				 */
				result = new CompRVal (lVal.type);
				WriteOutput (asnPost, TypeName (lVal.type) + " " + result.locstr + " = " + lVal.locstr + " " + asnPost.postfix.ToString () + ";");
			} else if (result.type is TokenTypeVoid) {

				/*
				 * Caller says they are going to throw the value away so don't bother putting it anywhere.
				 */
				WriteOutput (asnPost, lVal.locstr + " " + asnPost.postfix.ToString () + ";");
			} else {

				/*
				 * Caller would like us to put the value in a specific place.
				 */
				CompRVal rVal = new CompRVal (lVal.type, "(" + lVal.locstr + " " + asnPost.postfix.ToString () + ")");
				WriteOutput (asnPost, result.locstr + " = " + StringWithCast (result.type, rVal) + ";");
			}
			return result;
		}

		/**
		 * @brief prefix operator -- this returns the type and location of the resultant value
		 */
		private CompRVal GenerateFromRValAsnPre (CompRVal result, TokenRValAsnPre asnPre)
		{
			CompLVal lVal = GenerateFromLVal (asnPre.lVal);
			if (result == null) {

				/*
				 * Caller says they want the resultant value put in a temp.
				 */
				result = new CompRVal (lVal.type);
				WriteOutput (asnPre, TypeName (lVal.type) + " " + result.locstr + " = " + asnPre.prefix.ToString () + " " + lVal.locstr + ";");
			} else if (result.type is TokenTypeVoid) {

				/*
				 * Caller says they are going to throw the value away so don't bother putting it anywhere.
				 */
				WriteOutput (asnPre, asnPre.prefix.ToString () + " " + lVal.locstr + ";");
			} else {

				/*
				 * Caller would like us to put the value in a specific place.
				 */
				CompRVal rVal = new CompRVal (lVal.type, "(" + asnPre.prefix.ToString () + " " + lVal.locstr + ")");
				WriteOutput (asnPre, result.locstr + " = " + StringWithCast (result.type, rVal) + ";");
			}
			return result;
		}

		/**
		 * @brief Generate code that calls a function or object's method.
		 * @returns where the call's return value is stored (a TokenTypeVoid if void)
		 */
		private CompRVal GenerateFromRValCall (CompRVal result, TokenRValCall call)
		{
			if (call.meth is TokenLValField) return GenerateFromRValCallField (result, call);
			if (call.meth is TokenLValName)  return GenerateFromRValCallName  (result, call);
			throw new Exception ("unknown call type");
		}

		/**
		 * @brief Generate code that calls a method of an object.
		 * @returns where the call's return value is stored (a TokenTypeVoid if void)
		 */
		private CompRVal GenerateFromRValCallField (CompRVal result, TokenRValCall call)
		{
			CompRVal[] argRVals = null;
			int i, nargs;
			string name;
			TokenDeclFunc declFunc;

			/*
			 * Compute the values of all the function's call arguments.
			 * Save where the computation results are in the argRVals[] array.
			 * We can't generate these inline with the call itself as GenerateFromRVal() may
			 * output complete statements to compute some particular argument.
			 */
			nargs = call.nArgs;
			if (nargs > 0) {
				argRVals = new CompRVal[nargs];
				i = 0;
				for (TokenRVal arg = call.args; arg != null; arg = (TokenRVal)arg.nextToken) {
					argRVals[i] = GenerateFromRVal (null, arg);
					i ++;
				}
			}

			/*
			 * Get method's entrypoint and signature.
			 */
			CompLVal method = GenerateFromLVal (call.meth);
			name = method.locstr;
			if (((TokenTypeMeth)method.type).funcs.Length != 1) throw new Exception ("tu stOOpid");
			declFunc = ((TokenTypeMeth)method.type).funcs[0];

			/*
			 * Number of arguments passed should match number of params the function was declared with.
			 */
			if (nargs != declFunc.argDecl.types.Length) {
				ErrorMsg (call, name + " has " + declFunc.argDecl.types.Length.ToString () + " param(s), but call has " + nargs.ToString ());
				if (nargs > declFunc.argDecl.types.Length) nargs = declFunc.argDecl.types.Length;
			}

			/*
			 * Generate call.
			 */
			StringBuilder callString = new StringBuilder ();
			if (declFunc.retType.lslBoxing == typeof (LSL_Float)) {
				callString.Append ("(float)");  // because LSL_Float.value is a 'double'
			}
			callString.Append (name + "(");
			for (i = 0; i < nargs; i ++) {
				if (i > 0) callString.Append (",");
				callString.Append (StringWithCast (declFunc.argDecl.types[i], argRVals[i]));
			}
			callString.Append (")");

			return OutputCallStatement (result, call, declFunc, callString);
		}

		/**
		 * @brief Generate code that calls a function.
		 * @returns where the call's return value is stored (a TokenTypeVoid if void)
		 */
		private CompRVal GenerateFromRValCallName (CompRVal result, TokenRValCall call)
		{
			bool isBEAPIFunc;
			CompRVal[] argRVals = null;
			int i, nargs;
			string name;
			StringBuilder signature;
			TokenDeclFunc declFunc;

			if (!(call.meth is TokenLValName)) {
				ErrorMsg (call, "cannot call a field");
				return GenerateFromRValLVal (new TokenRValLVal (call.meth));
			}
			name = ((TokenLValName)call.meth).name.val;

			/*
			 * Compute the values of all the function's call arguments.
			 * Save where the computation results are in the argRVals[] array.
			 * Might as well build the signature from the argument types, too.
			 */
			nargs = call.nArgs;
			signature = new StringBuilder (name);
			signature.Append ("(");
			if (nargs > 0) {
				argRVals = new CompRVal[nargs];
				i = 0;
				for (TokenRVal arg = call.args; arg != null; arg = (TokenRVal)arg.nextToken) {
					argRVals[i] = GenerateFromRVal (null, arg);
					if (i > 0) signature.Append (",");
					signature.Append (argRVals[i].type.ToString ());
					i ++;
				}
			}
			signature.Append (")");

			/*
			 * Look the function up.
			 */
			string sig = signature.ToString ();
			isBEAPIFunc = false;
			if (scriptFunctions.ContainsKey (name)) {
				declFunc = scriptFunctions[name];
			} else if (beAPIFunctions.ContainsKey (sig)) {
				declFunc = beAPIFunctions[sig];
				isBEAPIFunc = true;
			} else {
				ErrorMsg (call, "undefined function " + sig);
				sig = name + "(";
				foreach (KeyValuePair<string, TokenDeclFunc> kvp in beAPIFunctions) {
					if (kvp.Key.StartsWith (sig)) ErrorMsg (call, "  have " + kvp.Key);
				}
				declFunc = new TokenDeclFunc (call);
				declFunc.retType  = new TokenTypeObject (call);
				declFunc.funcName = new TokenName (call, name);
				declFunc.argDecl  = new TokenArgDecl (call);
			}

			/*
			 * Number of arguments passed should match number of params the function was declared with.
			 * (The only time declFunc.argDecl.types is null is when the function was detected as undefined above).
			 */
			if ((declFunc.argDecl.types != null) && (nargs != declFunc.argDecl.types.Length)) {
				ErrorMsg (call, name + " has " + declFunc.argDecl.types.Length.ToString () + " param(s), but call has " + nargs.ToString ());
				if (nargs > declFunc.argDecl.types.Length) nargs = declFunc.argDecl.types.Length;
			}

			/*
			 * If calling a backend api function, prefix with beapi object reference.
			 * If calling a script-defined function, first arg is __sm to pass context along as it is static.
			 */
			StringBuilder callString = new StringBuilder ();
			if (isBEAPIFunc) {
				if (declFunc.retType.lslBoxing == typeof (LSL_Float)) {
					callString.Append ("(float)");  // because LSL_Float.value is a 'double'
				}
				callString.Append ("__sm.beAPI." + name + "(");
				if (nargs > 0) {
					if (declFunc.argDecl.types == null) callString.Append (argRVals[0].locstr);
					else callString.Append (StringWithCast (declFunc.argDecl.types[0], argRVals[0]));
					for (i = 0; ++ i < nargs;) {
						callString.Append (",");
						if (declFunc.argDecl.types == null) callString.Append (argRVals[i].locstr);
						else callString.Append (StringWithCast (declFunc.argDecl.types[i], argRVals[i]));
					}
				}
				callString.Append (")");

				result = OutputCallStatement (result, call, declFunc, callString);

				/*
				 * Also, we want to call CheckRun() after every backend call as
				 * the backend call may have set flags for CheckRun() to process.
				 */
				WriteOutput (call, "__sc.CheckRun();");
			} else {
				callString.Append ("__fun_" + name + "(__sm");
				for (i = 0; i < nargs; i ++) {
					callString.Append (",");
					if (declFunc.argDecl.types == null) callString.Append (argRVals[i].locstr);
					else callString.Append (StringWithCast (declFunc.argDecl.types[i], argRVals[i]));
				}
				callString.Append (")");
				result = OutputCallStatement (result, call, declFunc, callString);

				/*
				 * Also, unwind out if the inner function changed state.
				 */
				WriteOutput (call, "if (__sm.stateChanged) {");
				OutputGotoReturn (call);
				WriteOutput (call, "}");
			}
			return result;
		}

		/**
		 * @brief Output the C# call statement itself
		 * @param result = suggested place of where to put result
		 * @param call = the call statement token
		 * @param declFunc = call function declaration
		 * @param callString = string giving the C# equivalent call, up to and including the ")"
		 * @returns where the result is (a TokenTypeVoid if void)
		 */
		private CompRVal OutputCallStatement (CompRVal result, TokenRValCall call, TokenDeclFunc declFunc, StringBuilder callString)
		{

			/*
			 * Maybe we have to unbox the LSL-style boxed return value.
			 * This converts things like LSL_Integer madness to int.
			 */
			if (declFunc.retType.lslBoxing != null) {
				if ((result == null) || !(result.type is TokenTypeVoid)) {
					callString.Append (".value");
				}
			}
			callString.Append (";");

			/*
			 * If return is void, just output the call by itself.
			 */
			if (declFunc.retType is TokenTypeVoid) {
				WriteOutput (call, callString.ToString ());
				if (result == null) {
					result = new CompRVal (declFunc.retType, "voidCall");
				} else if (!(result.type is TokenTypeVoid)) {
					ErrorMsg (call, "function doesn't return a value");
				}
				return result;
			}

			/*
			 * Otherwise, put the result somewhere, either in given result location or a temp.
			 */
			if (result == null) {
				result = new CompRVal (declFunc.retType);
				WriteOutput (call, TypeName (declFunc.retType) + " " + result.locstr + " = ");
			} else if (!(result.type is TokenTypeVoid)) {
				WriteOutput (call, result.locstr + " = ");
			}
			WriteOutput (call, callString.ToString ());
			return result;
		}

		/**
		 * @brief Generate code that casts a value to a particular type.
		 * @returns where the result of the conversion is stored.
		 */
		private CompRVal GenerateFromRValCast (TokenRValCast cast)
		{
			CompRVal inRVal = GenerateFromRVal (null, cast.rVal);
			TokenType outType = cast.castTo;

			if (inRVal.type == outType) return inRVal;

			CompRVal outRVal = new CompRVal (outType, "(" + StringWithCast (outType, inRVal, true) + ")");
			outRVal.isFinal = inRVal.isFinal;
			return outRVal;
		}

		/**
		 * @brief floating-point constant.
		 */
		private CompRVal GenerateFromRValFloat (TokenRValFloat rValFloat)
		{
			CompRVal rVal = new CompRVal (new TokenTypeFloat (rValFloat), rValFloat.flToken.ToString ());
			rVal.isFinal = true;
			return rVal;
		}

		/**
		 * @brief integer constant.
		 */
		private CompRVal GenerateFromRValInt (TokenRValInt rValInt)
		{
			CompRVal rVal = new CompRVal (new TokenTypeInt (rValInt), rValInt.inToken.ToString ());
			rVal.isFinal = true;
			return rVal;
		}

		/**
		 * @brief generate a new list object
		 * @param rValList = an rVal to create it from
		 */
		private CompRVal GenerateFromRValList (TokenRValList rValList)
		{
			string arrayLocstr = "__tmp_" + ScriptCodeGen.GetTempNo ();
			WriteOutput (rValList, "object[] " + arrayLocstr + " = new object[" + rValList.nItems + "];");
			int i = 0;
			TokenType tto = new TokenTypeObject (rValList);
			for (TokenRVal val = rValList.rVal; val != null; val = (TokenRVal)val.nextToken) {
				CompRVal tRVal = new CompRVal (tto, arrayLocstr + "[" + i + "]");
				CompRVal eRVal = GenerateFromRVal (tRVal, val);
				if (eRVal != tRVal) {
					WriteOutput (val, tRVal.locstr + " = (object)" + eRVal.locstr + ";");
				}
				i ++;
			}
			return new CompRVal (new TokenTypeList (rValList.rVal),
			                     "(new " + TypeName (typeof (LSL_List)) + "(" + arrayLocstr + "))");
		}

		/**
		 * @brief get the R-value from an L-value
		 *        this happens when doing things like reading a variable
		 */
		private CompRVal GenerateFromRValLVal (TokenRValLVal rValLVal)
		{
			if (!(rValLVal.lvToken is TokenLValName)) {
				CompLVal compLVal = GenerateFromLVal (rValLVal.lvToken);
				return new CompRVal (compLVal.type, compLVal.locstr);
			}

			string name = ((TokenLValName)rValLVal.lvToken).name.val;

			foreach (Dictionary<string, TokenType> vars in scriptVariablesStack) {
				if (vars.ContainsKey (name)) {
					TokenType type = vars[name];
					if (vars == scriptInstanceVariables) name = "__sm.__gbl_" + name;
					else name = "__lcl_" + name;
					return new CompRVal (type, name);
				}
			}

			ErrorMsg (rValLVal, "undefined variable " + name);
			return new CompRVal (new TokenTypeVoid (rValLVal), name);
		}

		/**
		 * @brief parenthesized expression
		 * @returns type and location of the result of the computation.
		 */
		private CompRVal GenerateFromRValParen (TokenRValParen rValParen)
		{
			return GenerateFromRVal (null, rValParen.rVal);
		}

		/**
		 * @brief create a rotation object from the x,y,z,w value expressions.
		 */
		private CompRVal GenerateFromRValRot (TokenRValRot rValRot)
		{
			CompRVal xRVal, yRVal, zRVal, wRVal;
			TokenTypeFloat flToken = new TokenTypeFloat (rValRot);

			xRVal = GenerateFromRVal (null, rValRot.xRVal);
			yRVal = GenerateFromRVal (null, rValRot.yRVal);
			zRVal = GenerateFromRVal (null, rValRot.zRVal);
			wRVal = GenerateFromRVal (null, rValRot.wRVal);
			return new CompRVal (new TokenTypeRot (rValRot),
			                     "(new " + TypeName (typeof (LSL_Rotation)) + "(" + StringWithCast (flToken, xRVal) + "," + 
			                     StringWithCast (flToken, yRVal) + "," +
					     StringWithCast (flToken, zRVal) + "," + StringWithCast (flToken, wRVal) + "))");
		}

		/**
		 * @brief string constant.
		 */
		private CompRVal GenerateFromRValStr (TokenRValStr rValStr)
		{
			CompRVal rVal = new CompRVal (new TokenTypeStr (rValStr), rValStr.strToken.ToString ());
			rVal.isFinal = true;
			return rVal;
		}

		/**
		 * @brief 'undefined' constant.
		 *        If this constant gets written to an array element, it will delete that element from the array.
		 *        If the script retrieves an element by key that is not defined, it will get this value.
		 *        This value can be stored in and retrieved from variables of type 'object'.
		 *        It is a runtime error to cast this value to any type, eg, we don't allow string variables to be null pointers.
		 */
		private CompRVal GenerateFromRValUndef (TokenRValUndef rValUndef)
		{
			CompRVal rVal = new CompRVal (new TokenTypeObject (rValUndef), "null");
			rVal.isFinal = true;
			return rVal;
		}

		/**
		 * @brief create a vector object from the x,y,z value expressions.
		 */
		private CompRVal GenerateFromRValVec (TokenRValVec rValVec)
		{
			CompRVal xRVal, yRVal, zRVal;
			TokenTypeFloat flToken = new TokenTypeFloat (rValVec);

			xRVal = GenerateFromRVal (null, rValVec.xRVal);
			yRVal = GenerateFromRVal (null, rValVec.yRVal);
			zRVal = GenerateFromRVal (null, rValVec.zRVal);
			return new CompRVal (new TokenTypeVec (rValVec),
					"(new " + TypeName (typeof (LSL_Vector)) + "(" + StringWithCast (flToken, xRVal) + "," +
					StringWithCast (flToken, yRVal) + "," + StringWithCast (flToken, zRVal) + "))");
		}

		/**
		 * @brief Generate code to process an <rVal> is <type> expression, and produce a boolean value.
		 */
		private CompRVal GenerateFromRValIsType (TokenRValIsType rValIsType)
		{
			CompRVal rValExp = GenerateFromRVal (null, rValIsType.rValExp);
			CompRVal rValRet = new CompRVal (tokenTypeBool, "(" + CheckTypeIsExp (rValExp, rValIsType.typeExp) + ")");
			return rValRet;
		}

		private string CheckTypeIsExp (CompRVal rValExp, TokenTypeExp typeExp)
		{
			if (typeExp is TokenTypeExpBinOp) {
				return CheckTypeIsExp (rValExp, ((TokenTypeExpBinOp)typeExp).leftOp) + 
				       " " + ((TokenTypeExpBinOp)typeExp).binOp.ToString () + 
				       ((TokenTypeExpBinOp)typeExp).binOp.ToString () + " " +
				       CheckTypeIsExp (rValExp, ((TokenTypeExpBinOp)typeExp).rightOp);
			}

			if (typeExp is TokenTypeExpNot) {
				return "!(" + CheckTypeIsExp (rValExp, ((TokenTypeExpNot)typeExp).typeExp) + ")";
			}

			if (typeExp is TokenTypeExpPar) {
				return "(" + CheckTypeIsExp (rValExp, ((TokenTypeExpPar)typeExp).typeExp) + ")";
			}

			if (typeExp is TokenTypeExpType) {
				return "(" + rValExp.locstr + " is " + TypeName (((TokenTypeExpType)typeExp).typeToken.typ) + ")";
			}

			if (typeExp is TokenTypeExpUndef) {
				return "(" + rValExp.locstr + " == null)";
			}

			throw new Exception ("unknown type expression");
		}

		/**
		 * @brief output the "goto __Return;" statement that jumps to the function epilog.
		 * @param stmt = source statement that's causing the return to happen
		 * Note that __retval has already been updated with return value, if any.
		 */
		private void OutputGotoReturn (Token stmt)
		{

			/*
			 * If returning from an inner block, pop memory usage for all but the outermost block.
			 */
			for (TokenStmtBlock stmtBlock = curStmtBlock; stmtBlock.outerStmtBlock != null; stmtBlock = stmtBlock.outerStmtBlock) {
				GenMemUseBlockEpilog (stmtBlock, false);
			}

			/*
			 * Now we can jump to the common epilog where it will pop memory usage for the outermost block.
			 */
			WriteOutput (stmt, "goto __Return;");
		}

		/**
		 * @brief get the default (null) value for a particular type
		 * @param type = type to get the default value for
		 * @returns default value string for that type
		 */
		private string DefaultValue (TokenType type)
		{
			if (type is TokenTypeArray) {
				return "new " + TypeName (typeof (XMR_Array)) + "()";
			}
			if (type is TokenTypeKey) {
				return "MMR.ScriptConst.lslconst_NULL_KEY";
			}
			if (type is TokenTypeList) {
				return "new " + TypeName (typeof (LSL_List)) + "()";
			}
			if (type is TokenTypeRot) {
				return "MMR.ScriptConst.lslconst_ZERO_ROTATION";
			}
			if (type is TokenTypeStr) {
				return "\"\"";
			}
			if (type is TokenTypeVec) {
				return "MMR.ScriptConst.lslconst_ZERO_VECTOR";
			}
			return "(" + type.typ + ")0";
		}

		/**
		 * @brief create a dictionary of legal type casts.
		 * Defines what EXPLICIT type casts are allowed in addition to the IMPLICIT ones.
		 * Key is of the form <oldtype> <newtype> for IMPLICIT casting.
		 * Key is of the form <oldtype>*<newtype> for EXPLICIT casting.
		 * Value is a format string to convert the old value to new value.
		 */
		private static Dictionary<string, string> CreateLegalTypeCasts ()
		{
			Dictionary<string, string> ltc = new Dictionary<string, string> ();

			// IMPLICIT type casts (a space is in middle of the key)
			ltc.Add ("array object",    "((object){0})");
			ltc.Add ("bool float",      "({0}?1f:0f)");
			ltc.Add ("bool integer",    "({0}?1:0)");
			ltc.Add ("float bool",      "({0}!=0.0)");
			ltc.Add ("float integer",   "((int){0})");
			ltc.Add ("float object",    "((object){0})");
			ltc.Add ("integer bool",    "({0}!=0)");
			ltc.Add ("integer float",   "((float){0})");
			ltc.Add ("integer object",  "((object){0})");
			ltc.Add ("key bool",        "({0}!=NULL_KEY)");
			ltc.Add ("key object",      "((object){0})");
			ltc.Add ("key string",      "{0}");
			ltc.Add ("list object",     "((object){0})");
			ltc.Add ("object array",    TypeName (typeof (XMR_Array)) + ".Obj2Array({0})");   // disallow null
			ltc.Add ("object float",    "(" + TypeName (typeof (float))        + "){0}");     // value type disallows null
			ltc.Add ("object integer",  "(" + TypeName (typeof (int))          + "){0}");     // value type disallows null
			ltc.Add ("object key",      TypeName (typeof (XMR_Array)) + ".Obj2Key({0})");     // disallow null
			ltc.Add ("object list",     TypeName (typeof (XMR_Array)) + ".Obj2List({0})");    // disallow null
			ltc.Add ("object rotation", "(" + TypeName (typeof (LSL_Rotation)) + "){0}");     // value type disallows null
			ltc.Add ("object string",   TypeName (typeof (XMR_Array)) + ".Obj2String({0})");  // disallow null
			ltc.Add ("object vector",   "(" + TypeName (typeof (LSL_Vector))   + "){0}");     // value type disallows null
			ltc.Add ("rotation object", "((object){0})");
			ltc.Add ("string bool",     "({0}!=\"\")");
			ltc.Add ("string key",      "new " + TypeName (typeof (LSL_Key)) + "({0})");
			ltc.Add ("string object",   "((object){0})");
			ltc.Add ("string vector",   "((object){0})");
			ltc.Add ("vector object",   "((object){0})");

			// EXPLICIT type casts (an * is in middle of the key)
			ltc.Add ("bool*string",     "({0}?\"true\":\"false\")");
			ltc.Add ("float*string",    "{0}.ToString()");
			ltc.Add ("integer*string",  "{0}.ToString()");
			ltc.Add ("list*string",     "{0}.ToString()");
			ltc.Add ("rotation*string", "{0}.ToString()");
			ltc.Add ("vector*string",   "{0}.ToString()");

			return ltc;
		}

		/**
		 * @brief output an implicit cast to cast the 'inRVal' to the 'outType'.
		 * If inRVal is already the correct type, output it as is.
		 * @param explicitAllowed = false: only allow implicit casts
		 *                           true: accept implicit and explicit casts
		 */
		private void OutputWithCastToBool (CompRVal inRVal)
		{
			OutputWithCast (tokenTypeBool, inRVal);
		}
		private void OutputWithCast (TokenType outType, CompRVal inRVal)
		{
			WriteOutput (outType, StringWithCast (outType, inRVal));
		}
		private string StringWithCast (TokenType outType, CompRVal inRVal)
		{
			return StringWithCast (outType, inRVal, false);
		}
		private string StringWithCast (TokenType outType, CompRVal inRVal, bool explicitAllowed)
		{
			string result;

			/*
			 * First get inRVal converted to the type that outType says.
			 */
			string inName = inRVal.type.ToString();
			string outName = outType.ToString();
			if (inName != outName) {

				/*
				 * Different types, see if we allow implicit casting.
				 */
				string key = inName + " " + outName;
				if (!legalTypeCasts.ContainsKey (key)) {

					/*
					 * See if we even know how to cast explicitly.
					 */
					key = inName + "*" + outName;
					bool explicitExists = legalTypeCasts.ContainsKey (key);
					string qualif = explicitExists ? " implicitly" : "";
					if (!explicitAllowed || !explicitExists) {
						qualif = " implicitly";
						if (!explicitExists) qualif = "";
						ErrorMsg (inRVal.type, "cannot" + qualif + " convert " + inName + " to " + outName);
						return inRVal.locstr;
					}
				}

				/*
				 * Cast is allowed, generate code.
				 */
				string fmt = legalTypeCasts[key];
				result = String.Format (fmt, inRVal.locstr);
			} else if (inName == "float") {

				/*
				 * If both claim to be float, output cast anyway in case inRVal is really a double in disguise.
				 */
				result = "(float)" + inRVal.locstr;
			} else {

				/*
				 * All others just output without any casting at all.
				 */
				result = inRVal.locstr;
			}

			/*
			 * Now maybe we have to box it LSL style.
			 * This converts things like 'int' to LSL_Integer.
			 */
			if (outType.lslBoxing != null) {
				result = "new " + TypeName (outType.lslBoxing) + "(" + result + ")";
			}
			return result;
		}

		/**
		 * @brief output code to fill in a row of the ScriptEventHandlerTable
		 * @param name = state name string, "default" for default state
		 * @param body = state declaration, all emitted
		 */
		private void GenerateSEHTFill (string name, TokenStateBody body)
		{
			for (Token t = body.eventFuncs; t != null; t = t.nextToken) {
				TokenDeclFunc tdf = (TokenDeclFunc)t;
				WriteOutput (0, "seht[" + body.index + "," + (int)Enum.Parse(typeof (ScriptEventCode), tdf.funcName.val));
				WriteOutput (0, "] = __seh_" + name + "_" + tdf.funcName.val + ";");
			}
		}

		/**
		 * @brief maintain variable definition stack.
		 * It translates a variable name string to its declaration.
		 */
		private Dictionary<string, TokenType> PushVarDefnBlock ()
		{
			Dictionary<string, TokenType> frame = new Dictionary<string, TokenType> ();
			scriptVariablesStack.Push (frame);
			return frame;
		}
		private void PopVarDefnBlock ()
		{
			scriptVariablesStack.Pop ();
		}
		private void AddVarDefinition (TokenType type, string name)
		{
			Dictionary<string, TokenType> vars = scriptVariablesStack.Peek ();

			if (vars.ContainsKey (name)) {
				ErrorMsg (type, "duplicate var definition " + name);
			} else {
				vars.Add (name, type);
			}
		}

		/**
		 * @brief Generate code at the beginning of a statement block to keep track of memory use in the block.
		 * @param stmtBlock = statement block { } that was just started
		 * @param callFrame = true: this is top-level block that defines a function
		 *                   false: this is an inner-level block
		 */
		private void GenMemUseBlockProlog (TokenStmtBlock stmtBlock, bool callFrame)
		{

			/*
			 * Total up static memory usage for all variables in the block
			 * no matter where in the block they are declared.  This is more
			 * efficient than updating the usage for each individual var as
			 * it is declared, and easier to manage in case a jump or return
			 * exits the block.
			 */
			int staticSize = 0;
			if (callFrame) {
				staticSize = CALL_FRAME_MEMUSE;
				TokenArgDecl args = stmtBlock.function.argDecl;
				for (int i = 0; i < args.types.Length; i ++) {
					TokenType type = args.types[i];
					staticSize += TokenType.StaticSize (type.typ);
					if (type is TokenTypeList) {
						WriteOutput (stmtBlock, "__sm.memUsage += __lcl_" + args.names[i].val + ".dynamicSize;");
					}
					if (type is TokenTypeStr) {
						WriteOutput (stmtBlock, "__sm.memUsage += __lcl_" + args.names[i].val + ".Length * " + STRING_LEN_TO_MEMUSE + ";");
					}
				}
			}
			foreach (KeyValuePair<string, TokenDeclVar> kvp in stmtBlock.variables) {
				staticSize += TokenType.StaticSize (kvp.Value.type.typ);
			}
			if (staticSize > 0) {
				WriteOutput (stmtBlock, "__sm.memUsage += " + staticSize.ToString () + ";");
			}
		}

		/**
		 * @brief a value was just assigned to a variable
		 *        adjust memory usage accordingly
		 */
		private void GenMemUseDecrement (string locstr, TokenType type)
		{
			if (type is TokenTypeList) {
				WriteOutput (type, "__sm.memUsage -= " + locstr + ".dynamicSize;");
			}
			if (type is TokenTypeStr) {
				WriteOutput (type, "__sm.memUsage -= " + locstr + ".Length * " + STRING_LEN_TO_MEMUSE + ";");
			}
		}
		private void GenMemUseIncrement (string locstr, TokenType type)
		{
			if (type is TokenTypeList) {
				WriteOutput (type, "__sm.memUsage += " + locstr + ".dynamicSize;");
			}
			if (type is TokenTypeStr) {
				WriteOutput (type, "__sm.memUsage += " + locstr + ".Length * " + STRING_LEN_TO_MEMUSE + ";");
			}
		}

		/**
		 * @brief Generate code at the end of a statement block to keep track of memory use in the block.
		 * @param stmtBlock = statement block { } that is about to be ended
		 * @param callFrame = true: this is top-level block that defines a function
		 *                   false: this is an inner-level block
		 */
		private void GenMemUseBlockEpilog (TokenStmtBlock stmtBlock, bool callFrame)
		{
			int staticSize = 0;
			if (callFrame) {
				staticSize = CALL_FRAME_MEMUSE;
				TokenArgDecl args = stmtBlock.function.argDecl;
				for (int i = 0; i < args.types.Length; i ++) {
					TokenType type = args.types[i];
					staticSize += TokenType.StaticSize (type.typ);
					if (type is TokenTypeList) {
						WriteOutput (stmtBlock.closebr, "__sm.memUsage -= __lcl_" + args.names[i].val + ".dynamicSize;");
					}
					if (type is TokenTypeStr) {
						WriteOutput (stmtBlock.closebr, "__sm.memUsage -= __lcl_" + args.names[i].val + ".Length * " + STRING_LEN_TO_MEMUSE + ";");
					}
				}
			}
			foreach (KeyValuePair<string, TokenDeclVar> kvp in stmtBlock.variables) {
				staticSize += TokenType.StaticSize (kvp.Value.type.typ);
			}
			if (staticSize > 0) {
				WriteOutput (stmtBlock.closebr, "__sm.memUsage -= " + staticSize.ToString ());
				foreach (KeyValuePair<string, TokenDeclVar> kvp in stmtBlock.variables) {
					TokenDeclVar declVar = kvp.Value;
					if (declVar.type is TokenTypeList) {
						WriteOutput (stmtBlock.closebr, " + __lcl_" + declVar.name.val + ".dynamicSize");
					}
					if (declVar.type is TokenTypeStr) {
						WriteOutput (stmtBlock.closebr, " + __lcl_" + declVar.name.val + ".Length * " + STRING_LEN_TO_MEMUSE);
					}
				}
				WriteOutput (stmtBlock.closebr, ";");
			}
		}

		/**
		 * @brief Get a string for use in creating temporary variable names.
		 */
		private static Int64 tempNo = 0;
		public static string GetTempNo ()
		{
			return System.Threading.Interlocked.Increment (ref tempNo).ToString ();
		}

		/**
		 * @brief Create a dictionary for processing binary operators.
		 *        This tells us, for a given type, an operator and another type,
		 *        is the operation permitted, and if so, what is the type of the result?
		 * The key is <lefttype><opcode><righttype>,
		 *   where <lefttype> and <righttype> are strings returned by (TokenType...).ToString()
		 *   and <opcode> is string returned by (TokenKw...).ToString()
		 * The value is a BinOpStr struct giving the resultant type and how to format the computation.
		 * Operand {0} to the format is the left value and is of type lefttype.
		 * Operand {1} to the format is the right value and is of type righttype.
		 * The '#' indicates where an optional '=' may be substituted for read-modify-write assignments.
		 */
		private static Dictionary<string, BinOpStr> DefineBinOps ()
		{
			Dictionary<string, BinOpStr> bos = new Dictionary<string, BinOpStr> ();

			string[] booltypes = new string[] { "bool", "float", "integer", "key", "list", "string" };
			string[] boolcasts = new string[] { "{0}", "({0}!=0.0)", "({0}!=0)", "({0}!=NULL_KEY.val)", "(!{0}.IsEmpty())", "({0}!=\"\")" };

			/*
			 * Get the && and || all out of the way...
			 * Simply cast their left and right operands to boolean then process.
			 */
			for (int i = 0; i < booltypes.Length; i++) {
				for (int j = 0; j < booltypes.Length; j++) {
					int k = boolcasts[j].IndexOf ('0');
					string r = boolcasts[j].Substring (0, k) + "1" + boolcasts[j].Substring (k + 1);
					bos.Add (booltypes[i] + "&&" + booltypes[j], new BinOpStr (typeof (bool), boolcasts[i] + " && " + r));
					bos.Add (booltypes[i] + "||" + booltypes[j], new BinOpStr (typeof (bool), boolcasts[i] + " || " + r));
				}
			}

			/*
			 * Pound through all the other combinations we support.
			 */

			// boolean : somethingelse
			DefineBinOpsBoolX (bos, "bool",    "{1}");
			DefineBinOpsBoolX (bos, "float",   "({1} != 0.0)");
			DefineBinOpsBoolX (bos, "integer", "({1} != 0)");
			DefineBinOpsBoolX (bos, "key",     "({1} != NULL_KEY.val)");
			DefineBinOpsBoolX (bos, "list",    "!{1}.IsEmpty()");
			DefineBinOpsBoolX (bos, "string",  "({1} != \"\")");

			// somethingelse : boolean
			DefineBinOpsXBool (bos, "float",   "({0} != 0.0)");
			DefineBinOpsXBool (bos, "integer", "({0} != 0)");
			DefineBinOpsXBool (bos, "key",     "({0} != NULL_KEY.val)");
			DefineBinOpsXBool (bos, "list",    "!{0}.IsEmpty()");
			DefineBinOpsXBool (bos, "string",  "({0} != \"\")");

			// float : somethingelse
			DefineBinOpsFloatX (bos, "float",   "(float){1}");
			DefineBinOpsFloatX (bos, "integer", "(float){1}");

			// integer : float
			DefineBinOpsXFloat (bos, "integer", "(float){0}");

			// things with integers
			DefineBinOpsInteger (bos);

			// key : somethingelse
			DefineBinOpsKeyX (bos, "key", "{1}");
			DefineBinOpsKeyX (bos, "string", "{1}");

			// string : key
			DefineBinOpsXKey (bos, "string", "{0}");

			// things with lists
			DefineBinOpsList (bos);

			// things with rotations
			DefineBinOpsRotation (bos);
			DefineBinOpsRotationX (bos, "float",   "(float){1}");
			DefineBinOpsRotationX (bos, "integer", "(float){1}");
			DefineBinOpsXRotation (bos, "float",   "(float){0}");
			DefineBinOpsXRotation (bos, "integer", "(float){0}");

			// things with strings
			DefineBinOpsString (bos);

			// things with vectors
			DefineBinOpsVector (bos);
			DefineBinOpsVectorX (bos, "float",   "(float){1}");
			DefineBinOpsVectorX (bos, "integer", "(float){1}");
			DefineBinOpsXVector (bos, "float",   "(float){0}");
			DefineBinOpsXVector (bos, "integer", "(float){0}");

			return bos;
		}

		private static void DefineBinOpsBoolX (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add ("bool|"  + x, new BinOpStr (typeof (bool), "{0} |# " + y));
			bos.Add ("bool^"  + x, new BinOpStr (typeof (bool), "{0} ^# " + y));
			bos.Add ("bool&"  + x, new BinOpStr (typeof (bool), "{0} &# " + y));
			bos.Add ("bool==" + x, new BinOpStr (typeof (bool), "{0} == " + y));
			bos.Add ("bool!=" + x, new BinOpStr (typeof (bool), "{0} != " + y));
		}

		private static void DefineBinOpsXBool (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add (x + "|bool",  new BinOpStr (typeof (bool), y + " |# {1}"));
			bos.Add (x + "^bool",  new BinOpStr (typeof (bool), y + " ^# {1}"));
			bos.Add (x + "&bool",  new BinOpStr (typeof (bool), y + " &# {1}"));
			bos.Add (x + "==bool", new BinOpStr (typeof (bool), y + " == {1}"));
			bos.Add (x + "!=bool", new BinOpStr (typeof (bool), y + " != {1}"));
		}

		private static void DefineBinOpsFloatX (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add ("float==" + x, new BinOpStr (typeof (bool),  "(float){0} == " + y));
			bos.Add ("float!=" + x, new BinOpStr (typeof (bool),  "(float){0} != " + y));
			bos.Add ("float<"  + x, new BinOpStr (typeof (bool),  "(float){0} <  " + y));
			bos.Add ("float<=" + x, new BinOpStr (typeof (bool),  "(float){0} <= " + y));
			bos.Add ("float>"  + x, new BinOpStr (typeof (bool),  "(float){0} >  " + y));
			bos.Add ("float>=" + x, new BinOpStr (typeof (bool),  "(float){0} >= " + y));
			bos.Add ("float+"  + x, new BinOpStr (typeof (float), "(float){0} +  " + y));
			bos.Add ("float-"  + x, new BinOpStr (typeof (float), "(float){0} -  " + y));
			bos.Add ("float*"  + x, new BinOpStr (typeof (float), "(float){0} *  " + y));
			bos.Add ("float/"  + x, new BinOpStr (typeof (float), "(float){0} /  " + y));
			bos.Add ("float+=" + x, new BinOpStr (typeof (float), "{0} += " + y));
			bos.Add ("float-=" + x, new BinOpStr (typeof (float), "{0} -= " + y));
			bos.Add ("float*=" + x, new BinOpStr (typeof (float), "{0} *= " + y));
			bos.Add ("float/=" + x, new BinOpStr (typeof (float), "{0} /= " + y));
		}

		private static void DefineBinOpsXFloat (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add (x + "==float", new BinOpStr (typeof (bool),  y + " == (float){1}"));
			bos.Add (x + "!=float", new BinOpStr (typeof (bool),  y + " != (float){1}"));
			bos.Add (x + "<float",  new BinOpStr (typeof (bool),  y + " <  (float){1}"));
			bos.Add (x + "<=float", new BinOpStr (typeof (bool),  y + " <= (float){1}"));
			bos.Add (x + ">float",  new BinOpStr (typeof (bool),  y + " >  (float){1}"));
			bos.Add (x + ">=float", new BinOpStr (typeof (bool),  y + " >= (float){1}"));
			bos.Add (x + "+float",  new BinOpStr (typeof (float), y + " +# (float){1}"));
			bos.Add (x + "-float",  new BinOpStr (typeof (float), y + " -# (float){1}"));
			bos.Add (x + "*float",  new BinOpStr (typeof (float), y + " *# (float){1}"));
			bos.Add (x + "/float",  new BinOpStr (typeof (float), y + " /# (float){1}"));
		}

		private static void DefineBinOpsInteger (Dictionary<string, BinOpStr> bos)
		{
			bos.Add ("integer|integer",  new BinOpStr (typeof (int),  "{0} |# {1}"));
			bos.Add ("integer^integer",  new BinOpStr (typeof (int),  "{0} ^# {1}"));
			bos.Add ("integer&integer",  new BinOpStr (typeof (int),  "{0} &# {1}"));
			bos.Add ("integer==integer", new BinOpStr (typeof (bool), "{0} == {1}"));
			bos.Add ("integer!=integer", new BinOpStr (typeof (bool), "{0} != {1}"));
			bos.Add ("integer<integer",  new BinOpStr (typeof (bool), "{0} <  {1}"));
			bos.Add ("integer<=integer", new BinOpStr (typeof (bool), "{0} <= {1}"));
			bos.Add ("integer>integer",  new BinOpStr (typeof (bool), "{0} >  {1}"));
			bos.Add ("integer>=integer", new BinOpStr (typeof (bool), "{0} >= {1}"));
			bos.Add ("integer+integer",  new BinOpStr (typeof (int),  "{0} +# {1}"));
			bos.Add ("integer-integer",  new BinOpStr (typeof (int),  "{0} -# {1}"));
			bos.Add ("integer*integer",  new BinOpStr (typeof (int),  "{0} *# {1}"));
			bos.Add ("integer/integer",  new BinOpStr (typeof (int),  "{0} /# {1}"));
			bos.Add ("integer%integer",  new BinOpStr (typeof (int),  "{0} %# {1}"));
		}

		private static void DefineBinOpsKeyX (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add ("key==" + x, new BinOpStr (typeof (bool), "{0} == " + y));
			bos.Add ("key!=" + x, new BinOpStr (typeof (bool), "{0} != " + y));
		}

		private static void DefineBinOpsXKey (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add (x + "==key", new BinOpStr (typeof (bool), y + " == {1}"));
			bos.Add (x + "!=key", new BinOpStr (typeof (bool), y + " != {1}"));
		}

		private static void DefineBinOpsList (Dictionary<string, BinOpStr> bos)
		{
			BinOpStr add    = new BinOpStr (typeof (LSL_List), "{0}+{1}");
			bos.Add ("list+float",     new BinOpStr (typeof (LSL_List), "{0}+(float){1}"));
			bos.Add ("list+integer",   add);
			bos.Add ("list+key",       add);
			bos.Add ("list+rotation",  add);
			bos.Add ("list+string",    add);
			bos.Add ("list+vector",    add);

			BinOpStr revadd = new BinOpStr (typeof (LSL_List), "new " + TypeName (typeof (LSL_List)) + "((object){0})+{1}");
			bos.Add ("float+list",     new BinOpStr (typeof (LSL_List), "new " + TypeName (typeof (LSL_List)) + "((object)(float){0})+{1}"));
			bos.Add ("integer+list",   revadd);
			bos.Add ("key+list",       revadd);
			bos.Add ("rotation+list",  revadd);
			bos.Add ("string+list",    revadd);
			bos.Add ("vector+list",    revadd);

			BinOpStr addto  = new BinOpStr (typeof (LSL_List), "{0}.Add((object){1})");
			bos.Add ("list+=float",    new BinOpStr (typeof (LSL_List), "{0}.Add((object)(float){1})"));
			bos.Add ("list+=integer",  addto);
			bos.Add ("list+=key",      addto);
			bos.Add ("list+=rotation", addto);
			bos.Add ("list+=string",   addto);
			bos.Add ("list+=vector",   addto);

			bos.Add ("list==list",     new BinOpStr (typeof (bool), "{0}=={1}"));
			bos.Add ("list!=list",     new BinOpStr (typeof (bool), "{0}!={1}"));
		}

		private static void DefineBinOpsRotation (Dictionary<string, BinOpStr> bos)
		{
			bos.Add ("rotation==rotation", new BinOpStr (typeof (bool), "{0}.EqualsRot({1})"));
			bos.Add ("rotation!=rotation", new BinOpStr (typeof (bool), "!{0}.EqualsRot({1})"));
		}

		private static void DefineBinOpsRotationX (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add ("rotation*" + x, new BinOpStr (typeof (LSL_Rotation), "{0} * " + y));
			bos.Add ("rotation/" + x, new BinOpStr (typeof (LSL_Rotation), "{0} / " + y));
		}

		private static void DefineBinOpsXRotation (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add (x + "*rotation", new BinOpStr (typeof (LSL_Rotation), "{1} * " + y));
		}

		private static void DefineBinOpsString (Dictionary<string, BinOpStr> bos)
		{
			bos.Add ("string==string", new BinOpStr (typeof (bool),   "{0} == {1}"));
			bos.Add ("string!=string", new BinOpStr (typeof (bool),   "{0} != {1}"));
			bos.Add ("string<string",  new BinOpStr (typeof (bool),   "{0} <  {1}"));
			bos.Add ("string<=string", new BinOpStr (typeof (bool),   "{0} <= {1}"));
			bos.Add ("string>string",  new BinOpStr (typeof (bool),   "{0} >  {1}"));
			bos.Add ("string>=string", new BinOpStr (typeof (bool),   "{0} >= {1}"));
			bos.Add ("string+string",  new BinOpStr (typeof (string), "{0} +# {1}"));
		}

		private static void DefineBinOpsVector (Dictionary<string, BinOpStr> bos)
		{
			bos.Add ("vector==vector",  new BinOpStr (typeof (bool),       "{0}.EqualsVec({1})"));
			bos.Add ("vector!=vector",  new BinOpStr (typeof (bool),       "!{0}.EqualsVec({1})"));
			bos.Add ("vector*vector",   new BinOpStr (typeof (float),      "{0} * {1}"));
			bos.Add ("vector%vector",   new BinOpStr (typeof (LSL_Vector), "{0} % {1}"));
			bos.Add ("vector*rotation", new BinOpStr (typeof (LSL_Vector), "{0} * {1}"));
		}

		private static void DefineBinOpsVectorX (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add ("vector*" + x, new BinOpStr (typeof (LSL_Vector), "{0} * " + y));
			bos.Add ("vector/" + x, new BinOpStr (typeof (LSL_Vector), "{0} / " + y));
		}

		private static void DefineBinOpsXVector (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add (x + "*vector", new BinOpStr (typeof (LSL_Vector), "{1} * " + y));
		}

		private class BinOpStr {
			public Type outtype;
			public string format;
			public BinOpStr (Type outtype, string format)
			{
				this.outtype = outtype;
				this.format = format;
			}
		}

		/**
		 * @brief handle a unary operator, such as -x.
		 */
		private CompRVal UnOpGenerate (CompRVal inRVal, Token opcode)
		{
			/*
			 * - Negate
			 */
			if (opcode is TokenKwSub) {
				if ((inRVal.type is TokenTypeFloat) || (inRVal.type is TokenTypeInt) ||
				    (inRVal.type is TokenTypeRot) || (inRVal.type is TokenTypeVec)) {
					CompRVal outRVal = new CompRVal (inRVal.type, "(-" + inRVal.locstr + ")");
					outRVal.isFinal = inRVal.isFinal;
					return outRVal;
				}
				ErrorMsg (opcode, "can't negate " + inRVal.type.ToString ());
				return inRVal;
			}

			/*
			 * ~ Complement
			 */
			if (opcode is TokenKwTilde) {
				if (inRVal.type is TokenTypeInt) {
					CompRVal outRVal = new CompRVal (inRVal.type, "(~" + inRVal.locstr + ")");
					outRVal.isFinal = inRVal.isFinal;
					return outRVal;
				}
				ErrorMsg (opcode, "can't complement " + inRVal.type.ToString ());
				return inRVal;
			}

			/*
			 * ! Not (boolean)
			 */
			if (opcode is TokenKwExclam) {
				CompRVal outRVal = new CompRVal (tokenTypeBool, "(!" + StringWithCast (tokenTypeBool, inRVal) + ")");
				outRVal.isFinal = inRVal.isFinal;
				return outRVal;
			}

			throw new Exception ("unhandled opcode " + opcode.ToString ());
		}

		/**
		 * @brief get type name string suitable for output to C# file.
		 */
		private static string TypeName (TokenType tokenType)
		{
			return TypeName (tokenType.typ);
		}
		private static string TypeName (Type type)
		{
			return type.FullName.Replace("+", ".");
		}

		/**
		 * @brief Write text to output file.
		 * It keeps track of output->source line number translations
		 * as well as format the output for braces and semi-colons.
		 */
		private bool atBegOfLine = true;
		private bool quoted      = false;
		private int indentLevel  = 0;
		private int outputLineNo = 1;
		private LinkedList<int> lineNoTrans = new LinkedList<int> ();

		private void WriteOutput (Token token, string text)
		{
			WriteOutput (token.line, text);
		}
		private void WriteOutput (int sourceLineNo, string text)
		{
			bool lastWasBackslash, thisIsBackslash;
			char c;
			int i, j;

			j = 0;
			thisIsBackslash = false;
			for (i = 0; i < text.Length; i ++) {
				c = text[i];
				lastWasBackslash = thisIsBackslash;
				thisIsBackslash  = (c == '\\');
				if (c == '\n') {
					if (quoted) throw new Exception ("quoted newline");
					WriteOutputLine (sourceLineNo, text.Substring (j, i - j));
					j = i + 1;
					continue;
				}
				if (!quoted && (c == ';')) {
					WriteOutputLine (sourceLineNo, text.Substring (j, i - j + 1));
					j = i + 1;
					continue;
				}
				if (!quoted && (c == '{')) {
					WriteOutputLine (sourceLineNo, text.Substring (j, i - j + 1));
					j = i + 1;
					indentLevel += 3;
					continue;
				}
				if (!quoted && (c == '}')) {
					indentLevel -= 3;
					WriteOutputLine (sourceLineNo, text.Substring (j, i - j + 1));
					j = i + 1;
					continue;
				}
				if (c == '"') {
					if (!lastWasBackslash) quoted = !quoted;
					continue;
				}
			}
			if (i > j) {
				if (atBegOfLine) {
					lineNoTrans.AddLast (sourceLineNo);
					objectWriter.Write ("/*{0,5}:{1,5}*/ ", outputLineNo, sourceLineNo);
					objectWriter.Write ("".PadLeft (indentLevel));
				}
				objectWriter.Write (text.Substring (j, i - j));
				atBegOfLine = false;
			}
		}

		/**
		 * @brief output line of code and finish the line off
		 * @param line = line of code to output
		 * @returns with position set to beginning of next line
		 */
		private void WriteOutputLine (int sourceLineNo, string line)
		{
			if (atBegOfLine && (line.TrimStart (' ')[0] != '#')) {
				lineNoTrans.AddLast (sourceLineNo);
				objectWriter.Write ("/*{0,5}:{1,5}*/ ", outputLineNo, sourceLineNo);
				if (line.EndsWith (":;") && (indentLevel >= 3)) {
					objectWriter.Write ("".PadLeft (indentLevel - 3));
				} else {
					objectWriter.Write ("".PadLeft (indentLevel));
				}
			}
			objectWriter.WriteLine (line);
			atBegOfLine = true;
			outputLineNo ++;
		}

		/**
		 * @brief output error message and remember that we did
		 */
		private void ErrorMsg (Token token, string message)
		{
			token.ErrorMsg (message);
			youveAnError = true;
		}

		/**
		 * @brief L-value location
		 *        For now, all we have are simple variables
		 *        But someday, this will include array elements, etc.
		 */
		private class CompLVal {
			public TokenType type;  // type contained in the location
			public string locstr;   // where the variable is
			                        // = its name

			public CompLVal (TokenType type, string locstr)
			{
				this.type = type;
				this.locstr = locstr;
			}
		};
	}

	/**
	 * @brief R-value location
	 *        Includes constants, expressions and temp variables.
	 *        Can also be anything an L-value can be, 
	 *            when the L-value is being used as an R-value.
	 */
	public class CompRVal {
		public TokenType type;  // type of the value
		public string locstr;   // where the value is
		                        // = could a constant itself
		                        //   or an expression in parentheses
		                        //   or name of temp variable
		                        // ... anything that's a single C# value
		public bool isFinal;    // true iff value cannot be changed by any side effects
		                        // - temps do not change because we allocate a new one each time
		                        // - constants never change because they are constant
		                        // - expressions consisting of all isFinal operands are final

		/*
		 * Create a temp variable to hold the given type.
		 */
		public CompRVal (TokenType type)
		{
			this.type    = type;
			this.locstr  = "__tmp_" + ScriptCodeGen.GetTempNo ();
			this.isFinal = true;
		}

		/*
		 * We already have a place for the value and
		 * know what its type is.
		 */
		public CompRVal (TokenType type, string locstr)
		{
			this.type    = type;
			this.locstr  = locstr;
			this.isFinal = false;
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
