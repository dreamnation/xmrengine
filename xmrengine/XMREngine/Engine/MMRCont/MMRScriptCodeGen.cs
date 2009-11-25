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

		public const int CALL_FRAME_MEMUSE = 64;
		public const int STRING_LEN_TO_MEMUSE = 2;

        private static CSharpCodeProvider CSCodeProvider = new CSharpCodeProvider();

		/*
		 * Static tables that there only needs to be one copy of for all.
		 */
		public static Dictionary<string, TokenDeclFunc> beAPIFunctions = null;

		private static Dictionary<string, int> arithPrecedTable = null;
		private static Dictionary<string, BinOpStr> binOpStrings = null;
		private static TokenTypeBool tokenTypeBool = new TokenTypeBool (null);
		private static Dictionary<string, string> implicitTypeCasts = null;

		public static bool CodeGen (TokenScript tokenScript, string binaryName)
		{

			/*
			 * Set up static variables.
			 */
			if (arithPrecedTable == null) {

				/*
				 * Convert an arithmetic binary operator to its precedence.
				 */
				Dictionary<string, int> apt = new Dictionary<string, int> ();
				// http://wiki.secondlife.com/w/index.php?title=LSL_Operators&curid=3817&diff=339462&oldid=293743
				apt.Add ("&&", 100);
				apt.Add ("||", 120);
				apt.Add ("|", 140);
				apt.Add ("^", 160);
				apt.Add ("&", 180);
				apt.Add ("==", 200);
				apt.Add ("!=", 220);
				apt.Add ("<", 240);
				apt.Add ("<=", 240);
				apt.Add (">", 240);
				apt.Add (">=", 240);
				apt.Add (">>", 260);
				apt.Add ("<<", 260);
				apt.Add ("+", 280);
				apt.Add ("-", 300);
				apt.Add ("*", 320);
				apt.Add ("/", 320);
				apt.Add ("%", 320);
				//MB()
				arithPrecedTable = apt;
			}

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
				InternalFuncDict beapif = new InternalFuncDict (typeof (ScriptBaseClass));

				//MB()

				/*
				 * Now that the dictionary is complete, let other threads see it.
				 */
				beAPIFunctions = beapif;
			}
			//MB()

			if (implicitTypeCasts == null) {
				Dictionary<string, string> itc = CreateImplicitTypeCasts ();
				//MB()
				implicitTypeCasts = itc;
			}
			//MB()

			/*
			 * Run compiler such that it has a 'this' context for convenience.
			 */
			ScriptCodeGen sc = new ScriptCodeGen (tokenScript, binaryName);
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

		private ScriptCodeGen (TokenScript tokenScript, string binaryName)
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
				WriteOutput (declVar, "private " + declVar.type.typ.FullName.Replace("+", ".") + " __gbl_" + declVar.name.val + ";");
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

				/*
				 * Now fill in field initialization values.
				 */
				int initialSize = 0;
				foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclVar> kvp in tokenScript.vars) {
					TokenDeclVar declVar = kvp.Value;
					initialSize += TokenType.StaticSize (declVar.type.typ);
					if (declVar.init != null) {
						CompRVal rVal = GenerateFromRVal (declVar.init);
						WriteOutput (declVar, "__sm.__gbl_" + declVar.name.val + " = " + StringWithCast (declVar.type, rVal) + ";");
						if (declVar.type is TokenTypeList) {
							WriteOutput (declVar, "__sm.memUsage += __sm.__gbl_" + declVar.name.val + ".dynamicSize;");
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
				WriteOutput (0, "__gbl_" + declVar.name.val + " = (" + declVar.type.typ.FullName.Replace("+", ".") + ")recvObj (stream);");
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

            UTF8Encoding encoding = new UTF8Encoding();
            string text = encoding.GetString(objectFile.ToArray());

            m_log.Debug(text);

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
            parameters.ReferencedAssemblies.Add(Path.Combine(rootPath,
                "Mono.Tasklets.dll"));
            parameters.ReferencedAssemblies.Add(Path.Combine(rootPath,
                " OpenSim.Region.ScriptEngine.Interfaces"));

            parameters.GenerateExecutable = false;
            parameters.OutputAssembly = binaryName;

            CompilerResults results;

            try
            {
                lock(CSCodeProvider)
                {
                    results =
                            CSCodeProvider.CompileAssemblyFromSource(parameters,
                            text);
                }

                if (results.Errors.Count > 0)
                {
                    foreach (CompilerError CompErr in results.Errors)
                    {
                        string severity = CompErr.IsWarning ? "Warning" : "Error";
                        string errtext = CompErr.ErrorText;

                        m_log.DebugFormat("[MMR]: ({0},{1}]) {2}: {3}",
                                CompErr.Line, CompErr.Column,
                                severity, errtext);
                    }
                    exitCode = 1;
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
			 */
			WriteOutput (declFunc, smClassName + " __sm = (" + smClassName + ")__sw;");

			/*
			 * Output args as variable definitions and initialize each from __sw.ehArgs[].
			 * If the script writer goofed, the typecast will complain.
			 * ??? add arg count and static type checking to match our type using reflection
			 */
			if (argDecl.types.Length > 0) {
				// warning CS0219: The variable `__lcl_change' is assigned but its value is never used
				WriteOutput (0, "#pragma warning disable 219\n");
				for (int i = 0; i < argDecl.types.Length; i ++) {
					WriteOutput (argDecl, argDecl.types[i].typ.FullName.Replace("+", ".") + " __lcl_" + argDecl.names[i].val + " = (" +
							argDecl.types[i].typ + ")__sw.ehArgs[" + i + "];");
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
			if (declFunc.retType.typ == typeof (void)) {
				WriteOutput (declFunc, "void");
			} else {
				WriteOutput (declFunc, declFunc.retType.typ.FullName.Replace("+", "."));
			}
			WriteOutput (declFunc, " __fun_");
			WriteOutput (declFunc, name);
			WriteOutput (declFunc, "(" + smClassName + " __sm");
			if (argDecl.types.Length > 0) {
				for (int i = 0; i < argDecl.types.Length; i ++) {
					WriteOutput (argDecl, "," + argDecl.types[i].typ.FullName.Replace("+", ".") + " __lcl_" + argDecl.names[i].val);
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
			WriteOutput (declFunc.body, "__sm.continuation.CheckRun();");

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
				WriteOutput (declFunc, declFunc.retType.typ.FullName.Replace("+", ".") + " __retval = " + DefaultValue (declFunc.retType) + ";");
			}

			/*
			 * Output code body.
			 */
			GenerateStmtBlock (declFunc.body, true);

			/*
			 * All return statements goto __Return so we can do mem use epilog.
			 */
			WriteOutput (declFunc.body.closebr, "__Return:;");
			GenMemUseBlockEpilog (declFunc.body, true);
			if (!(declFunc.retType is TokenTypeVoid)) {
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
			if (stmt is TokenStmtBlock) { GenerateStmtBlock ((TokenStmtBlock)stmt, false); return; }
			if (stmt is TokenStmtRVal)  { GenerateFromRVal  (((TokenStmtRVal)stmt).rVal);  return; }
			if (stmt is TokenStmtDo)    { GenerateStmtDo    ((TokenStmtDo)stmt);           return; }
			if (stmt is TokenStmtFor)   { GenerateStmtFor   ((TokenStmtFor)stmt);          return; }
			if (stmt is TokenStmtIf)    { GenerateStmtIf    ((TokenStmtIf)stmt);           return; }
			if (stmt is TokenStmtJump)  { GenerateStmtJump  ((TokenStmtJump)stmt);         return; }
			if (stmt is TokenStmtLabel) { GenerateStmtLabel ((TokenStmtLabel)stmt);        return; }
			if (stmt is TokenStmtRet)   { GenerateStmtRet   ((TokenStmtRet)stmt);          return; }
			if (stmt is TokenStmtState) { GenerateStmtState ((TokenStmtState)stmt);        return; }
			if (stmt is TokenStmtWhile) { GenerateStmtWhile ((TokenStmtWhile)stmt);        return; }
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
					WriteOutput (declVar, declVar.type.typ.FullName.Replace("+", ".") + " __lcl_" + declVar.name.val + " = " + DefaultValue (declVar.type) + ";");
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

		private void GenerateStmtDo (TokenStmtDo doStmt)
		{
			string lbl = GetTempNo ();

			string breakLabel = "__DoBrk_" + lbl;
			string contLabel = "__DoBot_" + lbl;

			WriteOutput (doStmt, "__DoTop_" + lbl + ":;");
			GenerateStmt (doStmt.bodyStmt);
			WriteOutput (doStmt, contLabel + ":;");
			WriteOutput (doStmt, "__sm.continuation.CheckRun();");
			CompRVal testRVal = GenerateFromRVal (doStmt.testRVal);
			if (testRVal != null) {
				WriteOutput (doStmt.testRVal, "if (");
				OutputWithCastToBool (testRVal);
				WriteOutput (doStmt.testRVal, ") goto __DoTop_" + lbl + ";");
			}
			WriteOutput (doStmt, breakLabel + ":;");
		}

		private void GenerateStmtFor (TokenStmtFor forStmt)
		{
			string lbl = GetTempNo ();

			string breakLabel = "__ForBrk_" + lbl;
			string contLabel = "__ForTop_" + lbl;

			if (forStmt.initStmt != null) {
				GenerateStmt (forStmt.initStmt);
			}
			WriteOutput (forStmt, contLabel + ":;");
			WriteOutput (forStmt, "__sm.continuation.CheckRun();");
			if (forStmt.testRVal != null) {
				CompRVal testRVal = GenerateFromRVal (forStmt.testRVal);
				if (testRVal != null) {
					WriteOutput (forStmt.testRVal, "if (!");
					OutputWithCastToBool (testRVal);
					WriteOutput (forStmt.testRVal, ") goto " + breakLabel + ";");
				}
			}
			GenerateStmt (forStmt.bodyStmt);
			WriteOutput (forStmt, "goto " + contLabel + ";");
			WriteOutput (forStmt, breakLabel + ":;");
		}

		private void GenerateStmtIf (TokenStmtIf ifStmt)
		{
			string lbl = GetTempNo ();

			CompRVal testRVal = GenerateFromRVal (ifStmt.testRVal);
			WriteOutput (ifStmt, "if (!");
			OutputWithCastToBool (testRVal);
			WriteOutput (ifStmt, ") ");
			if (ifStmt.elseStmt == null) {
				WriteOutput (ifStmt, "goto __EndIf_" + lbl + ";");
				GenerateStmt (ifStmt.trueStmt);
			} else {
				WriteOutput (ifStmt, "goto __Else_" + lbl + ";");
				GenerateStmt (ifStmt.trueStmt);
				WriteOutput (ifStmt, "goto __EndIf_" + lbl + ";");
				WriteOutput (ifStmt, "__Else_" + lbl + ":;");
				GenerateStmt (ifStmt.elseStmt);
			}
			WriteOutput (ifStmt, "__EndIf_" + lbl + ":;");
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
				WriteOutput (labelStmt, " __sm.continuation.CheckRun();");
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
				CompRVal rVal = GenerateFromRVal (retStmt.rVal);
				WriteOutput (retStmt, "__retval = " + StringWithCast (curDeclFunc.retType, rVal) + ";");
			} else {
				if ((curDeclFunc.retType != null) && !(curDeclFunc.retType is TokenTypeVoid)) {
					ErrorMsg (retStmt, "function requires return value type " + curDeclFunc.retType.ToString ());
				}
			}

			/*
			 * If returning from an inner block, pop memory usage for all but the outermost block.
			 */
			for (TokenStmtBlock stmtBlock = curStmtBlock; stmtBlock.outerStmtBlock != null; stmtBlock = stmtBlock.outerStmtBlock) {
				GenMemUseBlockEpilog (stmtBlock, false);
			}

			/*
			 * Now we can jump to the common epilog where it will pop memory usage for the outermost block.
			 */
			WriteOutput (retStmt, "goto __Return;");
		}

		/**
		 * @brief generate code for a 'state' statement that transitions state.
		 * It sets the 'stateCode' variable in ScriptWrapper then performs a 'return (void);' statement.
		 */
		private void GenerateStmtState (TokenStmtState stateStmt)
		{
			if (stateStmt.state == null) {
				WriteOutput (stateStmt, "__sm.stateCode = 0;");
			} else if (!stateIndices.ContainsKey (stateStmt.state.val)) {
				ErrorMsg (stateStmt, "undefined state " + stateStmt.state.val);
			} else {
				int index = stateIndices[stateStmt.state.val];
				WriteOutput (stateStmt, "__sm.stateCode = " + index + ";");
			}
			GenerateStmtRet (new TokenStmtRet (stateStmt));
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
			WriteOutput (whileStmt, "__sm.continuation.CheckRun();");
			CompRVal testRVal = GenerateFromRVal (whileStmt.testRVal);
			if (testRVal != null) {
				WriteOutput (whileStmt.testRVal, "if (!" + testRVal.locstr + ") goto " + breakLabel + ";");
			}
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
				CompRVal rVal = GenerateFromRVal (declVar.init);
				if (!declVar.preDefd) {
					WriteOutput (declVar, declVar.type.typ.FullName.Replace("+", ".") + " ");
				}
				WriteOutput (declVar, "__lcl_" + declVar.name.val);
				WriteOutput (declVar, " = " + StringWithCast (declVar.type, rVal) + ";");
				GenMemUseIncrement ("__lcl_" + declVar.name.val, declVar.type);
			} else if (!declVar.preDefd) {

				/*
				 * Scripts have paths that don't initialize variables.
				 * So initialize them with something so C# compiler doesn't complain.
				 */
				WriteOutput (declVar, declVar.type.typ.FullName.Replace("+", ".") + " __lcl_" + declVar.name.val + " = " + DefaultValue (declVar.type) + ";");
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
			if (lVal is TokenLValField) return GenerateFromLValField ((TokenLValField)lVal);
			if (lVal is TokenLValName) return GenerateFromLValName ((TokenLValName)lVal);
			throw new Exception ("bad lval class");
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
			 * Since we only have rotation and vector with fields, just pound them out.
			 * To expand, we can make a table, possibly using reflection to look up the baseLVal.type definition.
			 */
			if (baseLVal.type.GetType () == typeof (TokenTypeRot)) {
				if ((fieldName.Length == 1) && (((fieldName[0] >= 'x') && (fieldName[0] <= 'z')) || (fieldName[0] == 's'))) {
					return new CompLVal (new TokenTypeFloat (lVal), "(" + baseLVal.locstr + ")." + fieldName);
				}
			}
			if (baseLVal.type.GetType () == typeof (TokenTypeVec)) {
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
		 * For anything that does some type of computation, the result it put in a temp var and the temp var name is returned.
		 * For anything like constants and variable names, the constant or variable name is returned as is (in string form).
		 * Things like rotations and vectors are put in an object assigned to a temp var, and the temp var name is returned.
		 */
		private CompRVal GenerateFromRVal (TokenRVal rVal)
		{
			if (rVal is TokenRValAsnPost)  return GenerateFromRValAsnPost ((TokenRValAsnPost)rVal);
			if (rVal is TokenRValAsnPre)   return GenerateFromRValAsnPre  ((TokenRValAsnPre)rVal);
			if (rVal is TokenRValCall)     return GenerateFromRValCall    ((TokenRValCall)rVal);
			if (rVal is TokenRValCast)     return GenerateFromRValCast    ((TokenRValCast)rVal);
			if (rVal is TokenRValFloat)    return GenerateFromRValFloat   ((TokenRValFloat)rVal);
			if (rVal is TokenRValInt)      return GenerateFromRValInt     ((TokenRValInt)rVal);
			if (rVal is TokenRValList)     return GenerateFromRValList    ((TokenRValList)rVal);
			if (rVal is TokenRValConst)    return ((TokenRValConst)rVal).val.rVal;
			if (rVal is TokenRValLVal)     return GenerateFromRValLVal    ((TokenRValLVal)rVal);
			if (rVal is TokenRValOpBin)    return GenerateFromRValOpBin   ((TokenRValOpBin)rVal);
			if (rVal is TokenRValOpUn)     return GenerateFromRValOpUn    ((TokenRValOpUn)rVal);
			if (rVal is TokenRValParen)    return GenerateFromRValParen   ((TokenRValParen)rVal);
			if (rVal is TokenRValRot)      return GenerateFromRValRot     ((TokenRValRot)rVal);
			if (rVal is TokenRValStr)      return GenerateFromRValStr     ((TokenRValStr)rVal);
			if (rVal is TokenRValVec)      return GenerateFromRValVec     ((TokenRValVec)rVal);
			throw new Exception ("bad rval class " + rVal.GetType ().ToString ());
		}

		/**
		 * @brief compute the result of a binary operator
		 * @param token = binary operator token, includes the left and right operands
		 * @returns where the resultant R-value is
		 */
		private CompRVal GenerateFromRValOpBin (TokenRValOpBin token)
		{
			CompLVal leftLVal = null;
			CompRVal left;
			CompRVal right = GenerateFromRVal (token.rValRight);

			/*
			 * If left operand is an L-value, create an leftLVal location marker for it.
			 * In either case, create a R-value location marker for it.
			 */
			if (token.rValLeft is TokenRValLVal) {
				leftLVal = GenerateFromLVal (((TokenRValLVal)token.rValLeft).lvToken);
				left = new CompRVal (leftLVal.type, leftLVal.locstr);
			} else {
				left = GenerateFromRVal (token.rValLeft);
			}

			/*
			 * Formulate key string for binOpStrings = (lefttype)(operator)(righttype)
			 */
			string leftIndex = left.type.ToString ();
			string opcodeIndex = token.opcode.ToString ();
			string rightIndex = right.type.ToString ();
			string key = leftIndex + opcodeIndex + rightIndex;

			/*
			 * If that key exists in table, then the operation is defined between those types
			 * ... and it produces an R-value of type as given in the table.
			 */
			if (binOpStrings.ContainsKey (key)) {
				BinOpStr binOpStr = binOpStrings[key];
				CompRVal resultRVal = new CompRVal (TokenType.FromSysType (token.opcode, binOpStr.outtype));
				WriteOutput (token, String.Format ("{0} {1} = ", binOpStr.outtype.ToString (), resultRVal.locstr));
				string fmt = binOpStr.format;
				int whack = binOpStr.format.IndexOf ('#');
				if (whack >= 0) {
					fmt = binOpStr.format.Substring (0, whack) + binOpStr.format.Substring (whack + 1);
				}
				WriteOutput (token, String.Format (fmt, left.locstr, right.locstr));
				WriteOutput (token, ";");
				return resultRVal;
			}

			/*
			 * Simple overwriting assignments are their own special case,
			 * as we want to cast the R-value to the type of the L-value.
			 * StringWithCast() is what determines if it is legal or not.
			 */
			if (opcodeIndex == "=") {
				if (leftLVal == null) {
					ErrorMsg (token, "invalid L-value");
				} else {
					GenMemUseDecrement (leftLVal.locstr, leftLVal.type);
					WriteOutput (token, leftLVal.locstr + " = " + StringWithCast (leftLVal.type, right) + ";");
					GenMemUseIncrement (leftLVal.locstr, leftLVal.type);
				}
				return left;
			}

			/*
			 * If the opcode ends with "=", it may be something like "+=".
			 * So look up the key as if we didn't have the "=" to tell us if the operation is legal.
			 * Also, the binary operation's output type must be the same as the L-value type.
			 */
			if (opcodeIndex.EndsWith ("=")) {
				key = leftIndex + opcodeIndex.Substring (0, opcodeIndex.Length - 1) + rightIndex;
				if (binOpStrings.ContainsKey (key)) {
					BinOpStr binOpStr = binOpStrings[key];
					if (binOpStr.outtype == left.type.typ) {
						int whack = binOpStr.format.IndexOf ('#');
						if (whack >= 0) {
							if (leftLVal == null) {
								ErrorMsg (token, "invalid L-value");
							} else {
								GenMemUseDecrement (leftLVal.locstr, leftLVal.type);
								string fmt = binOpStr.format.Substring (0, whack) + '=' + binOpStr.format.Substring (whack + 1);
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
			return new CompRVal (new TokenTypeVoid (token.opcode));
		}

		/**
		 * @brief compute the result of an unary operator
		 * @param token = unary operator token, includes the operand
		 * @returns where the resultant R-value is
		 */
		private CompRVal GenerateFromRValOpUn (TokenRValOpUn token)
		{
			CompRVal inRVal = GenerateFromRVal (token.rVal);
			return UnOpGenerate (inRVal, token.opcode);
		}

		/**
		 * @brief postfix operator -- this returns the type and location of the resultant value
		 */
		private CompRVal GenerateFromRValAsnPost (TokenRValAsnPost asnPost)
		{
			CompLVal lVal = GenerateFromLVal (asnPost.lVal);
			CompRVal rVal = new CompRVal (lVal.type);
			WriteOutput (asnPost, lVal.type.typ.FullName.Replace("+", ".") + " " + rVal.locstr + " = " + lVal.locstr + " " + asnPost.postfix.ToString () + ";");
			return rVal;
		}

		/**
		 * @brief prefix operator -- this returns the type and location of the resultant value
		 */
		private CompRVal GenerateFromRValAsnPre (TokenRValAsnPre asnPre)
		{
			CompLVal lVal = GenerateFromLVal (asnPre.lVal);
			CompRVal rVal = new CompRVal (lVal.type);
			WriteOutput (asnPre, lVal.type.typ.FullName.Replace("+", ".") + " " + rVal.locstr + " = " + asnPre.prefix.ToString () + " " + lVal.locstr + ";");
			return rVal;
		}

		/**
		 * @brief Generate code that calls a function.
		 * @returns where the call's return value is stored.
		 */
		private CompRVal GenerateFromRValCall (TokenRValCall call)
		{
			bool isBEAPIFunc;
			CompRVal[] argRVals = null;
			CompRVal retRVal = null;
			int i, nargs;
			string name = call.name.val;
			TokenDeclFunc declFunc;

			/*
			 * Look the function up.
			 */
			isBEAPIFunc = false;
			if (scriptFunctions.ContainsKey (name)) {
				declFunc = scriptFunctions[name];
			} else if ((name[0] >= 'a') && (name[0] <= 'z') &&
			            beAPIFunctions.ContainsKey (name)) {
				declFunc = beAPIFunctions[name];
				isBEAPIFunc = true;
			} else {
				ErrorMsg (call, "undefined function " + name);
				declFunc = new TokenDeclFunc (call);
				declFunc.retType = new TokenTypeVoid (call);
				declFunc.funcName = new TokenName (call, name);
				declFunc.argDecl = new TokenArgDecl (call);
				declFunc.argDecl.types = new TokenType[0];
				declFunc.argDecl.names = new TokenName[0];
			}

			/*
			 * Compute the values of all the function's call arguments.
			 * Save where the computation results are in the argRVals[] array.
			 */
			nargs = call.nArgs;
			if (nargs > 0) {
				argRVals = new CompRVal[nargs];
				i = 0;
				for (TokenRVal arg = call.args; arg != null; arg = (TokenRVal)arg.nextToken) {
					argRVals[i++] = GenerateFromRVal (arg);
				}
			}

			/*
			 * Number of arguments passed should match number of params the function was declared with.
			 */
			if (nargs != declFunc.argDecl.types.Length) {
				ErrorMsg (call, name + " has " + declFunc.argDecl.types.Length.ToString () + " param(s), but call has " + nargs.ToString ());
				if (nargs > declFunc.argDecl.types.Length) nargs = declFunc.argDecl.types.Length;
			}

			/*
			 * If return type isn't void, set up a temp to put return value in.
			 */
			retRVal = new CompRVal (declFunc.retType);
			if (!(declFunc.retType is TokenTypeVoid)) {
				WriteOutput (call, declFunc.retType.typ.FullName.Replace("+", ".") + " " + retRVal.locstr + " = ");
			}

			/*
			 * If calling a backend api function, prefix with beapi object reference.
			 * If calling a script-defined function, first arg is __sm to pass context along as it is static.
			 */
			if (isBEAPIFunc) {
				if (declFunc.retType.lslBoxing == typeof (LSL_Float)) {
					WriteOutput (call, "(float)");  // because LSL_Float.value is a 'double'
				}
				WriteOutput (call, "__sm.beAPI." + name + "(");
				if (nargs > 0) {
					OutputWithCast (declFunc.argDecl.types[0], argRVals[0]);
					for (i = 0; ++ i < nargs;) {
						WriteOutput (call, ",");
						OutputWithCast (declFunc.argDecl.types[i], argRVals[i]);
					}
				}
				WriteOutput (call, ")");

				/*
				 * Maybe we have to unbox the LSL-style boxed return value.
				 * This converts things like LSL_Integer madness to int.
				 */
				if (declFunc.retType.lslBoxing != null) {
					WriteOutput (call, ".value");
				}

				WriteOutput (call, ";");

				/*
				 * Also, we want to call CheckRun() after every backend call as
				 * the backend call may have set flags for CheckRun() to process.
				 */
				WriteOutput (call, "__sm.continuation.CheckRun();");
			} else {
				WriteOutput (call, "__fun_" + name + "(__sm");
				for (i = 0; i < nargs; i ++) {
					WriteOutput (call, ",");
					OutputWithCast (declFunc.argDecl.types[i], argRVals[i]);
				}
				WriteOutput (call, ");");
			}
			return retRVal;
		}

		/**
		 * @brief Generate code that casts a value to a particular type.
		 * @returns where the result of the conversion is stored.
		 */
		private CompRVal GenerateFromRValCast (TokenRValCast cast)
		{
			CompRVal inRVal = GenerateFromRVal (cast.rVal);
			TokenType outType = cast.castTo;
			string inName = inRVal.type.ToString ();
			string outName = outType.ToString ();
			string key = inName + " " + outName;

			if (inName == outName) return inRVal;

			/*
			 * They can do any implicit typecast explicitly.
			 */
			if (!implicitTypeCasts.ContainsKey (key)) {

				/*
				 * Check for allowed explicit typecasts.
				 */
				key += "*";
				if (!implicitTypeCasts.ContainsKey (key)) {
					ErrorMsg (cast, "can't convert from " + inName + " to " + outName);
					return inRVal;
				}
			}

			CompRVal outRVal = new CompRVal (outType);
			WriteOutput (cast, outType.typ.FullName.Replace("+", ".") + " " + outRVal.locstr + " = (" + outType.typ.FullName.Replace("+", ".") + ")" + inRVal.locstr + ";");
			return outRVal;
		}

		/**
		 * @brief floating-point constant.
		 */
		private CompRVal GenerateFromRValFloat (TokenRValFloat rValFloat)
		{
			return new CompRVal (new TokenTypeFloat (rValFloat), rValFloat.flToken.ToString ());
		}

		/**
		 * @brief integer constant.
		 */
		private CompRVal GenerateFromRValInt (TokenRValInt rValInt)
		{
			return new CompRVal (new TokenTypeInt (rValInt), rValInt.inToken.ToString ());
		}

		/**
		 * @brief generate a new list object
		 * @param rValList = an rVal to create it from
		 */
		private CompRVal GenerateFromRValList (TokenRValList rValList)
		{
			CompRVal compRVal = new CompRVal (new TokenTypeList (rValList.rVal));
			WriteOutput (rValList, typeof (LSL_List).FullName.Replace("+", ".") + " " + compRVal.locstr + " = new " + typeof (LSL_List).FullName.Replace("+", ".") + "();");
			for (TokenRVal val = rValList.rVal; val != null; val = (TokenRVal)val.nextToken) {
				CompRVal eRVal = GenerateFromRVal (val);
				WriteOutput (val, compRVal.locstr + ".AddLast((object)" + eRVal.locstr + ");");
			}
			return compRVal;
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
			return GenerateFromRVal (rValParen.rVal);
		}

		/**
		 * @brief create a rotation object from the x,y,z,w value expressions.
		 */
		private CompRVal GenerateFromRValRot (TokenRValRot rValRot)
		{
			CompRVal xRVal, yRVal, zRVal, wRVal;
			TokenTypeFloat flToken = new TokenTypeFloat (rValRot);

			xRVal = GenerateFromRVal (rValRot.xRVal);
			yRVal = GenerateFromRVal (rValRot.yRVal);
			zRVal = GenerateFromRVal (rValRot.zRVal);
			wRVal = GenerateFromRVal (rValRot.wRVal);
			return new CompRVal (new TokenTypeRot (rValRot),
					"new LSL_Rotation(" + StringWithCast (flToken, xRVal) + "," + StringWithCast (flToken, yRVal) + "," +
					StringWithCast (flToken, zRVal) + "," + StringWithCast (flToken, wRVal) + ")");
		}

		/**
		 * @brief string constant.
		 */
		private CompRVal GenerateFromRValStr (TokenRValStr rValStr)
		{
			return new CompRVal (new TokenTypeStr (rValStr), rValStr.strToken.ToString ());
		}

		/**
		 * @brief create a vector object from the x,y,z value expressions.
		 */
		private CompRVal GenerateFromRValVec (TokenRValVec rValVec)
		{
			CompRVal xRVal, yRVal, zRVal;
			TokenTypeFloat flToken = new TokenTypeFloat (rValVec);

			xRVal = GenerateFromRVal (rValVec.xRVal);
			yRVal = GenerateFromRVal (rValVec.yRVal);
			zRVal = GenerateFromRVal (rValVec.zRVal);
			return new CompRVal (new TokenTypeVec (rValVec),
					"new LSL_Vector(" + StringWithCast (flToken, xRVal) + "," +
					StringWithCast (flToken, yRVal) + "," + StringWithCast (flToken, zRVal) + ")");
		}

		/**
		 * @brief get the default (null) value for a particular type
		 * @param type = type to get the default value for
		 * @returns default value string for that type
		 */
		private string DefaultValue (TokenType type)
		{
			if (type is TokenTypeKey) {
				return "MMR.ScriptConst.lslconst_NULL_KEY";
			}
			if (type is TokenTypeList) {
				return "new LSL_List ()";
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
		 * @brief create a dictionary of allowed IMPLICIT type casts.
		 * Also defines what EXPLICIT type casts are allowed in addition to the IMPLICIT ones.
		 * Key is of the form <oldtype> <newtype>
		 * Value is a format string to convert the old value to new value
		 */
		private static Dictionary<string, string> CreateImplicitTypeCasts ()
		{
			Dictionary<string, string> itc = new Dictionary<string, string> ();

			// IMPLICIT type casts
			itc.Add ("bool integer", "({0}?1:0)");
			itc.Add ("bool string", "({0}?\"true\":\"false\")");
			itc.Add ("float bool", "({0}!=0.0)");
			itc.Add ("float string", "{0}.ToString()");
			itc.Add ("integer bool", "({0}!=0)");
			itc.Add ("integer float", "(float){0}");
			itc.Add ("integer string", "{0}.ToString()");
			itc.Add ("key bool", "({0}.val!=NULL_KEY.val)");
			itc.Add ("key string", "{0}.val");
			itc.Add ("list string", "{0}.ToString()");
			itc.Add ("rotation string", "{0}.ToString()");
			itc.Add ("vector string", "{0}.ToString()");

			// EXPLICIT type casts (an * is on the end of the key)
			itc.Add ("float integer*", "(int){0}");
			itc.Add ("string key*", "new " + typeof(LSL_Key).FullName.Replace("+", ".") + "({0})");

			return itc;
		}

		/**
		 * @brief output an implicit cast to cast the 'inRVal' to the 'outType'.
		 * If inRVal is already the correct type, output it as is.
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
			string result;

			/*
			 * First get inRVal converted to the type that outType says.
			 */
			string inName = inRVal.type.ToString();
			string outName = outType.ToString();
			if (inName == outName) {
				result = inRVal.locstr;
			} else {
				string key = inName + " " + outName;
				if (!implicitTypeCasts.ContainsKey (key)) {
					ErrorMsg (inRVal.type, "can't implicitly convert " + inName + " to " + outName);
					return inRVal.locstr;
				}
				string fmt = implicitTypeCasts[key];
				result = String.Format (fmt, inRVal.locstr);
			}

			/*
			 * Now maybe we have to box it LSL style.
			 * This converts things like 'int' to LSL_Integer.
			 */
			if (outType.lslBoxing != null) {
				result = "new " + outType.lslBoxing.FullName.Replace("+", ".") + "(" + result + ")";
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
			string[] boolcasts = new string[] { "{0}", "({0}!=0.0)", "({0}!=0)", "({0}.val!=NULL_KEY.val)", "(!{0}.IsEmpty())", "({0}!=\"\")" };

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
			DefineBinOpsBoolX (bos, "bool", "{1}");
			DefineBinOpsBoolX (bos, "float", "({1} != 0.0)");
			DefineBinOpsBoolX (bos, "integer", "({1} != 0)");
			DefineBinOpsBoolX (bos, "key", "({1} != NULL_KEY.val)");
			DefineBinOpsBoolX (bos, "list", "!{1}.IsEmpty()");
			DefineBinOpsBoolX (bos, "string", "({1} != \"\")");

			// somethingelse : boolean
			DefineBinOpsXBool (bos, "float", "({0} != 0.0)");
			DefineBinOpsXBool (bos, "integer", "({0} != 0)");
			DefineBinOpsXBool (bos, "key", "({0} != NULL_KEY.val)");
			DefineBinOpsXBool (bos, "list", "!{0}.IsEmpty()");
			DefineBinOpsXBool (bos, "string", "({0} != \"\")");

			// float : somethingelse
			DefineBinOpsFloatX (bos, "float", "{1}");
			DefineBinOpsFloatX (bos, "integer", "(float){1}");

			// integer : float
			DefineBinOpsXFloat (bos, "integer", "(float){0}");

			// things with integers
			DefineBinOpsInteger (bos);

			// key : somethingelse
			DefineBinOpsKeyX (bos, "key", "{1}.val");
			DefineBinOpsKeyX (bos, "string", "{1}");

			// string : key
			DefineBinOpsXKey (bos, "string", "{0}");

			// things with lists
			DefineBinOpsList (bos);

			// things with rotations
			DefineBinOpsRotation (bos);
			DefineBinOpsRotationX (bos, "float", "{1}");
			DefineBinOpsRotationX (bos, "integer", "(float){1}");
			DefineBinOpsXRotation (bos, "float", "{0}");
			DefineBinOpsXRotation (bos, "integer", "(float){0}");

			// things with strings
			DefineBinOpsString (bos);

			// things with vectors
			DefineBinOpsVector (bos);
			DefineBinOpsVectorX (bos, "float", "{1}");
			DefineBinOpsVectorX (bos, "integer", "(float){1}");
			DefineBinOpsXVector (bos, "float", "{0}");
			DefineBinOpsXVector (bos, "integer", "(float){0}");

			return bos;
		}

		private static void DefineBinOpsBoolX (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add ("bool|" + x, new BinOpStr (typeof (bool), "{0} |# " + y));
			bos.Add ("bool^" + x, new BinOpStr (typeof (bool), "{0} ^# " + y));
			bos.Add ("bool&" + x, new BinOpStr (typeof (bool), "{0} &# " + y));
			bos.Add ("bool==" + x, new BinOpStr (typeof (bool), "{0} == " + y));
			bos.Add ("bool!=" + x, new BinOpStr (typeof (bool), "{0} != " + y));
		}

		private static void DefineBinOpsXBool (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add (x + "|bool", new BinOpStr (typeof (bool), y + " |# {1}"));
			bos.Add (x + "^bool", new BinOpStr (typeof (bool), y + " ^# {1}"));
			bos.Add (x + "&bool", new BinOpStr (typeof (bool), y + " &# {1}"));
			bos.Add (x + "==bool", new BinOpStr (typeof (bool), y + " == {1}"));
			bos.Add (x + "!=bool", new BinOpStr (typeof (bool), y + " != {1}"));
		}

		private static void DefineBinOpsFloatX (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add ("float==" + x, new BinOpStr (typeof (bool), "{0} == " + y));
			bos.Add ("float!=" + x, new BinOpStr (typeof (bool), "{0} != " + y));
			bos.Add ("float<" + x, new BinOpStr (typeof (bool), "{0} < " + y));
			bos.Add ("float<=" + x, new BinOpStr (typeof (bool), "{0} <= " + y));
			bos.Add ("float>" + x, new BinOpStr (typeof (bool), "{0} > " + y));
			bos.Add ("float>=" + x, new BinOpStr (typeof (bool), "{0} >= " + y));
			bos.Add ("float+" + x, new BinOpStr (typeof (float), "{0} +# " + y));
			bos.Add ("float-" + x, new BinOpStr (typeof (float), "{0} -# " + y));
			bos.Add ("float*" + x, new BinOpStr (typeof (float), "{0} *# " + y));
			bos.Add ("float/" + x, new BinOpStr (typeof (float), "{0} /# " + y));
		}

		private static void DefineBinOpsXFloat (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add (x + "==float", new BinOpStr (typeof (bool), y + " == {1}"));
			bos.Add (x + "!=float", new BinOpStr (typeof (bool), y + " != {1}"));
			bos.Add (x + "<float", new BinOpStr (typeof (bool), y + " < {1}"));
			bos.Add (x + "<=float", new BinOpStr (typeof (bool), y + " <= {1}"));
			bos.Add (x + ">float", new BinOpStr (typeof (bool), y + " > {1}"));
			bos.Add (x + ">=float", new BinOpStr (typeof (bool), y + " >= {1}"));
			bos.Add (x + "+float", new BinOpStr (typeof (float), y + " +# {1}"));
			bos.Add (x + "-float", new BinOpStr (typeof (float), y + " -# {1}"));
			bos.Add (x + "*float", new BinOpStr (typeof (float), y + " *# {1}"));
			bos.Add (x + "/float", new BinOpStr (typeof (float), y + " /# {1}"));
		}

		private static void DefineBinOpsInteger (Dictionary<string, BinOpStr> bos)
		{
			bos.Add ("integer|integer", new BinOpStr (typeof (int), "{0} |# {1}"));
			bos.Add ("integer^integer", new BinOpStr (typeof (int), "{0} ^# {1}"));
			bos.Add ("integer&integer", new BinOpStr (typeof (int), "{0} &# {1}"));
			bos.Add ("integer==integer", new BinOpStr (typeof (bool), "{0} == {1}"));
			bos.Add ("integer!=integer", new BinOpStr (typeof (bool), "{0} != {1}"));
			bos.Add ("integer<integer", new BinOpStr (typeof (bool), "{0} < {1}"));
			bos.Add ("integer<=integer", new BinOpStr (typeof (bool), "{0} <= {1}"));
			bos.Add ("integer>integer", new BinOpStr (typeof (bool), "{0} > {1}"));
			bos.Add ("integer>=integer", new BinOpStr (typeof (bool), "{0} >= {1}"));
			bos.Add ("integer+integer", new BinOpStr (typeof (int), "{0} +# {1}"));
			bos.Add ("integer-integer", new BinOpStr (typeof (int), "{0} -# {1}"));
			bos.Add ("integer*integer", new BinOpStr (typeof (int), "{0} *# {1}"));
			bos.Add ("integer/integer", new BinOpStr (typeof (int), "{0} /# {1}"));
			bos.Add ("integer%integer", new BinOpStr (typeof (int), "{0} %# {1}"));
		}

		private static void DefineBinOpsKeyX (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add ("key==" + x, new BinOpStr (typeof (bool), "{0}.val == " + y));
			bos.Add ("key!=" + x, new BinOpStr (typeof (bool), "{0}.val != " + y));
		}

		private static void DefineBinOpsXKey (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add (x + "==key", new BinOpStr (typeof (bool), y + " == {1}.val"));
			bos.Add (x + "!=key", new BinOpStr (typeof (bool), y + " != {1}.val"));
		}

		private static void DefineBinOpsList (Dictionary<string, BinOpStr> bos)
		{
			bos.Add ("list==list", new BinOpStr (typeof (bool), "{0}.EqualsList({1})"));
			bos.Add ("list!=list", new BinOpStr (typeof (bool), "!{0}.EqualsList({1})"));
		}

		private static void DefineBinOpsRotation (Dictionary<string, BinOpStr> bos)
		{
			bos.Add ("rotation==rotation", new BinOpStr (typeof (bool), "{0}.EqualsRot({1})"));
			bos.Add ("rotation!=rotation", new BinOpStr (typeof (bool), "!{0}.EqualsRot({1})"));
		}

		private static void DefineBinOpsRotationX (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add ("rotation*" + x, new BinOpStr (typeof (LSL_Rotation), "{0}.CreateMul(" + y + ")"));
			bos.Add ("rotation/" + x, new BinOpStr (typeof (LSL_Rotation), "{0}.CreateDiv(" + y + ")"));
		}

		private static void DefineBinOpsXRotation (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add (x + "*rotation", new BinOpStr (typeof (LSL_Rotation), "{1}.CreateMul(" + y + ")"));
		}

		private static void DefineBinOpsString (Dictionary<string, BinOpStr> bos)
		{
			bos.Add ("string==string", new BinOpStr (typeof (bool), "{0} == {1}"));
			bos.Add ("string!=string", new BinOpStr (typeof (bool), "{0} != {1}"));
			bos.Add ("string<string", new BinOpStr (typeof (bool), "{0} < {1}"));
			bos.Add ("string<=string", new BinOpStr (typeof (bool), "{0} <= {1}"));
			bos.Add ("string>string", new BinOpStr (typeof (bool), "{0} > {1}"));
			bos.Add ("string>=string", new BinOpStr (typeof (bool), "{0} >= {1}"));
			bos.Add ("string+string", new BinOpStr (typeof (string), "{0} +# {1}"));
		}

		private static void DefineBinOpsVector (Dictionary<string, BinOpStr> bos)
		{
			bos.Add ("vector==vector", new BinOpStr (typeof (bool), "{0}.EqualsVec({1})"));
			bos.Add ("vector!=vector", new BinOpStr (typeof (bool), "!{0}.EqualsVec({1})"));
			bos.Add ("vector*vector", new BinOpStr (typeof (float), "{0}.DotBy({1})"));
			bos.Add ("vector%vector", new BinOpStr (typeof (LSL_Vector), "{0}.CreateCross({1})"));
			bos.Add ("vector*rotation", new BinOpStr (typeof (LSL_Vector), "{0}.CreateRot({1})"));
		}

		private static void DefineBinOpsVectorX (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add ("vector*" + x, new BinOpStr (typeof (LSL_Vector), "{0}.CreateMul(" + y + ")"));
			bos.Add ("vector/" + x, new BinOpStr (typeof (LSL_Vector), "{0}.CreateDiv(" + y + ")"));
		}

		private static void DefineBinOpsXVector (Dictionary<string, BinOpStr> bos, string x, string y)
		{
			bos.Add (x + "*vector", new BinOpStr (typeof (LSL_Vector), "{1}.CreateMul(" + y + ")"));
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
				if ((inRVal.type is TokenTypeFloat) || (inRVal.type is TokenTypeInt)) {
					CompRVal outRVal = new CompRVal (inRVal.type);
					WriteOutput (opcode, inRVal.type.typ.FullName.Replace("+", ".") + " " + outRVal.locstr + " = -" + inRVal.locstr + ";");
					return outRVal;
				}
				if (inRVal.type is TokenTypeRot) {
					CompRVal outRVal = new CompRVal (inRVal.type);
					WriteOutput (opcode, "LSL_Rotation " + outRVal.locstr + " = new LSL_Rotation(-" + inRVal.locstr + ".x,-" +
							inRVal.locstr + ".y,-" + inRVal.locstr + ".z,-" + inRVal.locstr + ".w);");
					return outRVal;
				}
				if (inRVal.type is TokenTypeVec) {
					CompRVal outRVal = new CompRVal (inRVal.type);
					WriteOutput (opcode, "LSL_Vector " + outRVal.locstr + " = new LSL_Vector(-" + inRVal.locstr + ".x,-" +
							inRVal.locstr + ".y,-" + inRVal.locstr + ".z);");
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
					CompRVal outRVal = new CompRVal (inRVal.type);
					WriteOutput (opcode, inRVal.type.typ.FullName.Replace("+", ".") + " " + outRVal.locstr + " = ~" + inRVal.locstr + ";");
					return outRVal;
				}
				ErrorMsg (opcode, "can't complement " + inRVal.type.ToString ());
				return inRVal;
			}

			/*
			 * ! Not (boolean)
			 */
			if (opcode is TokenKwExclam) {
				CompRVal outRVal = new CompRVal (tokenTypeBool);
				WriteOutput (opcode, "bool " + outRVal.locstr + " = !" + StringWithCast (tokenTypeBool, inRVal) + ";");
				return outRVal;
			}

			throw new Exception ("unhandled opcode " + opcode.ToString ());
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
					objectWriter.Write ("/*{0,5}*/ ", sourceLineNo);
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
				objectWriter.Write ("/*{0,5}*/ ", sourceLineNo);
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
		 * @brief contains left-hand operand and corresponding binary operator.
		 *        right-hand operand is at nextBO.rVal.
		 */
		private class BinaryOp {
			public BinaryOp nextBO;  // next in list (null if first)
			public BinaryOp prevBO;  // prev in list (null if last)
			public CompRVal rVal;    // operand
			public Token opcode;     // opcode token (null if last)
			public int preced;       // precedence (0 if last)
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
	 *        Includes constants and temp variables.
	 *        Can also be anything an L-value can be, 
	 *            when the L-value is being used as an R-value.
	 */
	public class CompRVal {
		public TokenType type;  // type of the value
		public string locstr;   // where the value is
		// = could be the constant itself
		//   or name of temp variable

		/*
		 * Create a temp variable to hold the given type.
		 */
		public CompRVal (TokenType type)
		{
			this.type = type;
			locstr = "__tmp_" + ScriptCodeGen.GetTempNo ();
		}

		/*
		 * We already have a place for the value and
		 * know what its type is.
		 */
		public CompRVal (TokenType type, string locstr)
		{
			this.type = type;
			this.locstr = locstr;
		}

	}
}
