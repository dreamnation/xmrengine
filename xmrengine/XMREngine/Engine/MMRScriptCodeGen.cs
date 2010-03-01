/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using log4net;
using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;
using Mono.Tasklets;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Region.ScriptEngine.XMREngine.Loader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;


/**
 * @brief translate a reduced script token into corresponding CIL code.
 * The single script token contains a tokenized and textured version of the whole script file.
 */

namespace OpenSim.Region.ScriptEngine.XMREngine
{

	public class ScriptCodeGen
	{
		private static readonly ILog m_log =
			LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		public static readonly int CALL_FRAME_MEMUSE = 64;
		public static readonly int STRING_LEN_TO_MEMUSE = 2;

		/*
		 * Static tables that there only needs to be one copy of for all.
		 */
		private static Dictionary<string, BinOpStr> binOpStrings = BinOpStr.DefineBinOps ();
		private static Dictionary<string, InlineFunction> inlineFunctions = InlineFunction.CreateDictionary ();
		private static Dictionary<string, TokenDeclFunc> legalEventHandlers = CreateLegalEventHandlers ();
		private static TokenTypeBool  tokenTypeBool  = new TokenTypeBool  (null);
		private static TokenTypeFloat tokenTypeFloat = new TokenTypeFloat (null);
		private static TokenTypeInt   tokenTypeInt   = new TokenTypeInt   (null);
		private static Type[] scriptWrapperTypeArg = new Type[] { typeof (ScriptWrapper) };

		private static ConstructorInfo lslFloatConstructorInfo = typeof (LSL_Float).GetConstructor (new Type[] { typeof (float) });
		private static ConstructorInfo lslIntegerConstructorInfo = typeof (LSL_Integer).GetConstructor (new Type[] { typeof (int) });
		private static ConstructorInfo lslListConstructorInfo = typeof (LSL_List).GetConstructor (new Type[] { typeof (object[]) });
		public  static ConstructorInfo lslRotationConstructorInfo = typeof (LSL_Rotation).GetConstructor (new Type[] { typeof (double), typeof (double), typeof (double), typeof (double) });
		private static ConstructorInfo lslStringConstructorInfo = typeof (LSL_String).GetConstructor (new Type[] { typeof (string) });
		public  static ConstructorInfo lslVectorConstructorInfo = typeof (LSL_Vector).GetConstructor (new Type[] { typeof (double), typeof (double), typeof (double) });
		private static ConstructorInfo scriptUndefinedStateExceptionConstructorInfo = typeof (ScriptUndefinedStateException).GetConstructor (new Type[0]);
		private static ConstructorInfo xmrArrayConstructorInfo = typeof (XMR_Array).GetConstructor (new Type[0]);
		private static FieldInfo arrayCountFieldInfo = typeof (XMR_Array).GetField ("__pub_count");
		private static FieldInfo arrayIndexFieldInfo = typeof (XMR_Array).GetField ("__pub_index");
		private static FieldInfo arrayValueFieldInfo = typeof (XMR_Array).GetField ("__pub_value");
		public  static FieldInfo beAPIFieldInfo = typeof (ScriptWrapper).GetField ("beAPI");
		private static FieldInfo continuationFieldInfo = typeof (ScriptWrapper).GetField ("continuation");
		private static FieldInfo ehArgsFieldInfo = typeof (ScriptWrapper).GetField ("ehArgs");
		private static FieldInfo rotationXFieldInfo = typeof (LSL_Rotation).GetField ("x");
		private static FieldInfo rotationYFieldInfo = typeof (LSL_Rotation).GetField ("y");
		private static FieldInfo rotationZFieldInfo = typeof (LSL_Rotation).GetField ("z");
		private static FieldInfo rotationSFieldInfo = typeof (LSL_Rotation).GetField ("s");
		private static FieldInfo stateChangedFieldInfo = typeof (ScriptWrapper).GetField ("stateChanged");
		private static FieldInfo stateCodeFieldInfo    = typeof (ScriptWrapper).GetField ("stateCode");
		private static FieldInfo vectorXFieldInfo = typeof (LSL_Vector).GetField ("x");
		private static FieldInfo vectorYFieldInfo = typeof (LSL_Vector).GetField ("y");
		private static FieldInfo vectorZFieldInfo = typeof (LSL_Vector).GetField ("z");

		private static MethodInfo checkRunMethodInfo = typeof (ScriptContinuation).GetMethod ("CheckRun", new Type[0]);
		private static MethodInfo forEachMethodInfo = typeof (XMR_Array).GetMethod ("ForEach", 
		                                                                            new Type[] { typeof (int),
		                                                                                         typeof (object).MakeByRefType (),
		                                                                                         typeof (object).MakeByRefType () });
		private static MethodInfo lslVectorNegateMethodInfo = GetStaticMethod (typeof (ScriptCodeGen), 
		                                                                       "LSLVectorNegate", 
		                                                                       new Type[] { typeof (LSL_Vector) });

		public static ScriptObjCode CodeGen (TokenScript tokenScript, string descName, string ilGenDebugName)
		{
			TypeCast.CreateLegalTypeCasts ();

			/*
			 * Run compiler such that it has a 'this' context for convenience.
			 */
			ScriptCodeGen scg = new ScriptCodeGen (tokenScript, descName, ilGenDebugName);

			/*
			 * Return pointer to resultant script object code.
			 */
			return (scg.exitCode == 0) ? scg.scriptObjCode : null;
		}

		/*
		 * There is one set of these variables for each script being compiled.
		 */
		private bool youveAnError = false;
		private int exitCode = 0;
		private int nStates = 0;
		private Token errorMessageToken = null;
		private TokenDeclFunc curDeclFunc = null;
		private TokenStmtBlock curStmtBlock = null;
		public  StreamWriter ilGenDebugWriter = null;
		private string descName = null;
		private TokenScript tokenScript = null;

		private Dictionary<string, TokenDeclFunc> scriptFunctions = null;
		private Dictionary<string, int> stateIndices = null;
		private Stack<Dictionary<string, CompValu>> scriptVariablesStack = null;
		private Dictionary<string, CompValu> scriptInstanceVariables = null;

		// code generation output
		public ScriptObjCode scriptObjCode = null;

		// These get cleared at beginning of every function definition
		public  ScriptMyILGen ilGen = null;  // the output instruction stream
		private ScriptMyLocal beLoc = null;  // "ScriptBase __be" local variable

		private ScriptCodeGen (TokenScript tokenScript, string descName, string ilGenDebugName)
		{
			this.tokenScript = tokenScript;
			this.descName    = descName;

			if (ilGenDebugName == null) ilGenDebugName = "";
			if (ilGenDebugName != "") {
				ilGenDebugWriter = File.CreateText (ilGenDebugName);
			}

			try {
				this.PerformCompilation ();
			} finally {
				if (ilGenDebugWriter != null) {
					ilGenDebugWriter.Flush ();
					ilGenDebugWriter.Close ();
				}
			}
		}

		private void PerformCompilation ()
		{

			/*
			 * errorMessageToken is used only when the given token doesn't have a
			 * output delegate associated with it such as for backend API functions
			 * that only have one copy for the whole system.  It is kept up-to-date
			 * approximately but is rarely needed so going to assume it doesn't have 
			 * to be exact.
			 */
			errorMessageToken = tokenScript;

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
			stateIndices.Add ("default", 0);
			foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclState> kvp in tokenScript.states) {
				TokenDeclState declState = kvp.Value;
				declState.body.index = nStates ++;
				stateIndices.Add (declState.name.val, declState.body.index);
			}

			/*
			 * Make up an array that translates state indices to state name strings.
			 */
			scriptObjCode = new ScriptObjCode ();
			scriptObjCode.stateNames = new string[nStates];
			foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclState> kvp in tokenScript.states) {
				TokenDeclState declState = kvp.Value;
				scriptObjCode.stateNames[declState.body.index] = declState.name.val;
			}

			/*
			 * This table gets filled in with entrypoints of all the script event handler functions.
			 */
			scriptObjCode.scriptEventHandlerTable = new ScriptEventHandler[nStates,(int)ScriptEventCode.Size];

			/*
			 * This table gets filled in with entrypoints of all dynamic methods that can be encountered
			 * on the stack by the MMRCont continuation routines.
			 */
			scriptObjCode.dynamicMethods = new Dictionary<string, MethodInfo> ();

			/*
			 * Set up stack of dictionaries to translate variable names to their declaration.
			 * Then push the first element on the stack that will get any global variables.
			 */
			scriptVariablesStack = new Stack<Dictionary<string, CompValu>> ();
			scriptInstanceVariables = PushVarDefnBlock ();

			/*
			 * Write out COMPILED_VERSION.  This is used by ScriptWrapper to determine if the
			 * compilation is suitable for it to use.  If it sees the wrong version, it will
			 * recompile the script so it has the correct version.
			 */
			scriptObjCode.compiledVersion = ScriptWrapper.COMPILED_VERSION_VALUE;

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
			 * If function x calls function y, and function y has a 'state' statement,
			 * then pretend x has a 'state' statement in it.  Recurse as needed so that
			 * any number of call levels are handled.
			 */
			bool foundChangedState;
			do {
				foundChangedState = false;
				foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclFunc> kvpX in tokenScript.funcs) {
					TokenDeclFunc declFuncX = kvpX.Value;
					if (!declFuncX.changesState) {
						foreach (System.Collections.Generic.KeyValuePair<string, TokenName> kvpY in declFuncX.calledFuncs) {
							string nameY = kvpY.Key;
							if (scriptFunctions.ContainsKey (nameY)) {
								TokenDeclFunc declFuncY = scriptFunctions[nameY];
								if (declFuncY.changesState) {
									declFuncX.changesState = true;
									foundChangedState = true;
									break;
								}
							}
						}
					}
				}
			} while (foundChangedState);

			/*
			 * Translate all the global variable declarations to indices in the ScriptWrapper.gbl<Type>s[] arrays.
			 */
			foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclVar> kvp in tokenScript.vars) {
				TokenDeclVar declVar = kvp.Value;
				AddVarDefinition (declVar.name, new CompValuGlobal (declVar, scriptObjCode));
			}

			/*
			 * Output each global function as a private method.
			 *
			 * Prefix the names with __fun_ to keep them separate from any ScriptWrapper functions.
			 */

			/*
			 * Output function headers then bodies.
			 * Do all headers first in case bodies do forward references.
			 */
			foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclFunc> kvp in tokenScript.funcs) {
				TokenDeclFunc declFunc = kvp.Value;
				GenerateMethodHeader (declFunc);
			}
			foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclFunc> kvp in tokenScript.funcs) {
				TokenDeclFunc declFunc = kvp.Value;
				GenerateMethodBody (declFunc);
			}

			/*
			 * Output default state event handler methods.
			 * Each event handler is a private static method named __seh_default_<eventname>
			 */
			GenerateStateHandler ("default", tokenScript.defaultState.body);

			/*
			 * Output script-defined state event handler methods.
			 * Each event handler is a private static method named __seh_<statename>_<eventname>
			 */
			foreach (System.Collections.Generic.KeyValuePair<string, TokenDeclState> kvp in tokenScript.states) {
				TokenDeclState declState = kvp.Value;
				GenerateStateHandler (declState.name.val, declState.body);
			}

			/*
			 * Check for error and abort if so.
			 */
			if (youveAnError) {
				exitCode = -1;
			}

			if (ilGenDebugWriter != null) {
				ilGenDebugWriter.WriteLine ("\nThe End.");
			}
		}

		/**
		 * @brief generate event handler code
		 * Writes out a function definition for each state handler
		 * named __seh_<statename>_<eventname>
		 *
		 * However, each has just 'ScriptWrapper __sw' as its single argument
		 * and each of its user-visible argments is extracted from __sw.ehArgs[].
		 *
		 * So we end up generating something like this:
		 *
		 *   private static void __seh_<statename_<eventname>$MMRContableAttribute$(ScriptWrapper __sw)
		 *   {
		 *      ScriptBaseClass __be  = __sw.beAPI;
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
			int nargs;
			string eventname = declFunc.funcName.val;
			TokenArgDecl argDecl = declFunc.argDecl;

			/*
			 * Make sure event handler name is valid and that number and type of arguments is correct.
			 */
			if (!legalEventHandlers.ContainsKey (eventname)) {
				ErrorMsg (declFunc, "unknown event handler " + eventname);
				return;
			}
			TokenDeclFunc protoDeclFunc = legalEventHandlers[eventname];
			nargs = declFunc.argDecl.types.Length;
			if (protoDeclFunc.argDecl.types.Length != nargs) {
				ErrorMsg (declFunc, eventname + "(...) supposed to have " + protoDeclFunc.argDecl.types.Length + 
				                    " arg(s), not " + nargs);
				if (nargs > protoDeclFunc.argDecl.types.Length) nargs = protoDeclFunc.argDecl.types.Length;
			}
			for (int i = 0; i < nargs; i ++) {
				if (protoDeclFunc.argDecl.types[i].typ != declFunc.argDecl.types[i].typ) {
					ErrorMsg (declFunc, eventname + "(... " + declFunc.argDecl.types[i].ToString () + " " + 
					                    declFunc.argDecl.names[i] + " ...) should be " + 
					                    protoDeclFunc.argDecl.types[i].ToString ());
				}
			}

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
			 * They just have the ScriptWrapper pointer as the one argument.
			 */
			string functionName = "__seh_" + statename + "_" + eventname + "$MMRContableAttribute$" + descName;
			DynamicMethod functionMethodInfo = new DynamicMethod (functionName,
			                                                      typeof (void),
			                                                      scriptWrapperTypeArg,
			                                                      typeof (ScriptWrapper));
			scriptObjCode.dynamicMethods.Add (functionName, functionMethodInfo);
			ilGen = new ScriptMyILGen (functionMethodInfo, ilGenDebugWriter);

			/*
			 * Set up __be local variable for calling the backend API functions = scriptWrapper.beAPI
			 */
			beLoc = ilGen.DeclareLocal (typeof (ScriptBaseClass), "__be");
			ilGen.Emit (OpCodes.Ldarg_0);                // scriptWrapper
			ilGen.Emit (OpCodes.Ldfld, beAPIFieldInfo);  // scriptWrapper.beAPI
			ilGen.Emit (OpCodes.Stloc, beLoc);

			/*
			 * If this is the default state_entry() handler, output code to set all global
			 * variables to their initial values.  Note that every script must have a
			 * default state_entry() handler.
			 */
			if ((statename == "default") && (eventname == "state_entry")) {
				foreach (KeyValuePair<string, TokenDeclVar> kvp in tokenScript.vars) {
					TokenDeclVar gblDeclVar = kvp.Value;

					CompValu var = scriptInstanceVariables[gblDeclVar.name.val];
					var.PopPre (this);
					if (gblDeclVar.init != null) {
						CompValu rVal = GenerateFromRVal (gblDeclVar.init);
						rVal.PushVal (this, gblDeclVar.type);
					} else {
						PushDefaultValue (gblDeclVar.type);
					}
					var.PopPost (this);
				}
			}

			/*
			 * Output args as variable definitions and initialize each from __sw.ehArgs[].
			 * If the script writer goofed, the typecast will complain.
			 */
			if (argDecl.types.Length > 0) {
				ScriptMyLocal swehArgs = ilGen.DeclareLocal (typeof (object[]), "ehArgs");

				ilGen.Emit (OpCodes.Ldarg_0);                 // scriptWrapper
				ilGen.Emit (OpCodes.Ldfld, ehArgsFieldInfo);  // scriptWrapper.ehArgs
				ilGen.Emit (OpCodes.Stloc, swehArgs);

				for (int i = 0; i < nargs; i ++) {

					// <argtype> __lcl_<argname> = (<argtype>)__sw.ehArgs[i];
					CompValu local = new CompValuTemp (argDecl.types[i], argDecl.names[i].val, this);
					local.PopPre (this);
					ilGen.Emit (OpCodes.Ldloc, swehArgs);          // __sw.ehArgs
					PushConstantI4 (i);                            // array index = i
					ilGen.Emit (OpCodes.Ldelem, typeof (object));  // it is an array of objects
					local.PopPost (this, new TokenTypeObject (argDecl.names[i]));

					/*
					 * The argument is now defined as a local variable accessible to the function body.
					 */
					AddVarDefinition (argDecl.names[i], local);
				}
			}

			/*
			 * Output code for the statements and clean up.
			 */
			GenerateFuncBody (declFunc);
			curDeclFunc = oldDeclFunc;

			/*
			 * Now that we have all the code generated, fill in table entry to point to the entrypoint.
			 * Don't bother if there has been a compilation error because CreateDelegate() might throw
			 * an exception if it sees faulty generated code (and the user won't see the compile error
			 * messages).
			 */
			if (!youveAnError) {
				scriptObjCode.scriptEventHandlerTable[stateIndices[statename],
			                                      (int)Enum.Parse(typeof(ScriptEventCode),eventname)] = 
						(ScriptEventHandler)functionMethodInfo.CreateDelegate 
								(typeof (ScriptEventHandler));
			}
		}

		/**
		 * @brief generate header for an arbitrary script-defined function.
		 * @param name = name of the function
		 * @param argDecl = argument declarations
		 * @param body = function's code body
		 */
		private void GenerateMethodHeader (TokenDeclFunc declFunc)
		{
			string name = declFunc.funcName.val;
			TokenArgDecl argDecl = declFunc.argDecl;

			/*
			 * Make up array of all argument types.
			 * We splice in ScriptWrapper for the first arg as the function is static.
			 */
			Type[] argTypes = new Type[argDecl.types.Length+1];
			argTypes[0] = typeof (ScriptWrapper);
			for (int i = 0; i < argDecl.types.Length; i ++) {
				argTypes[i+1] = argDecl.types[i].typ;
			}

			/*
			 * Set up entrypoint.
			 */
			string methodName = "__fun_" + name + "$MMRContableAttribute$" + descName;
			declFunc.dynamicMethod = new DynamicMethod (methodName,
			                                            declFunc.retType.typ,
			                                            argTypes,
			                                            typeof (ScriptWrapper));
			scriptObjCode.dynamicMethods.Add (methodName, declFunc.dynamicMethod);
		}

		/**
		 * @brief generate code for an arbitrary script-defined function.
		 * @param name = name of the function
		 * @param argDecl = argument declarations
		 * @param body = function's code body
		 */
		private void GenerateMethodBody (TokenDeclFunc declFunc)
		{
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
			 * Define all arguments as named variables so script body can reference them.
			 * The argument indices need to have +1 added to them because ScriptWrapper is spliced in at arg 0.
			 */
			for (int i = 0; i < argDecl.types.Length; i ++) {
				AddVarDefinition (argDecl.names[i], new CompValuArg (argDecl.types[i], i + 1));
			}

			/*
			 * Set up code generator for the function's contents.
			 */
			ilGen = new ScriptMyILGen (declFunc.dynamicMethod, ilGenDebugWriter);

			/*
			 * Set up local var 'ScriptBaseClass __be = __sw.beAPI;'
			 */
			beLoc = ilGen.DeclareLocal (typeof (ScriptBaseClass), "__be");
			ilGen.Emit (OpCodes.Ldarg_0);                // scriptWrapper
			ilGen.Emit (OpCodes.Ldfld, beAPIFieldInfo);  // scriptWrapper.beAPI
			ilGen.Emit (OpCodes.Stloc, beLoc);

			/*
			 * See if time to suspend in case they are doing a loop with recursion.
			 */
			EmitCallCheckRun ();

			/*
			 * Output code for the statements and clean up.
			 */
			GenerateFuncBody (declFunc);
			curDeclFunc = oldDeclFunc;
		}

		/**
		 * @brief Output function body.
		 */
		private void GenerateFuncBody (TokenDeclFunc declFunc)
		{
			/*
			 * Output code body.
			 */
			GenerateStmtBlock (declFunc.body, true);

			/*
			 * Just in case it runs off end without a return statement.
			 */
			EmitDummyReturn ();

			/*
			 * Don't want to generate any more code for it by mistake.
			 */
			ilGen = null;
			beLoc = null;

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
			errorMessageToken = stmt;
			if (stmt is TokenStmtBlock)   { GenerateStmtBlock   ((TokenStmtBlock)stmt, false); return; }
			if (stmt is TokenStmtDo)      { GenerateStmtDo      ((TokenStmtDo)stmt);           return; }
			if (stmt is TokenStmtFor)     { GenerateStmtFor     ((TokenStmtFor)stmt);          return; }
			if (stmt is TokenStmtForEach) { GenerateStmtForEach ((TokenStmtForEach)stmt);      return; }
			if (stmt is TokenStmtIf)      { GenerateStmtIf      ((TokenStmtIf)stmt);           return; }
			if (stmt is TokenStmtJump)    { GenerateStmtJump    ((TokenStmtJump)stmt);         return; }
			if (stmt is TokenStmtLabel)   { GenerateStmtLabel   ((TokenStmtLabel)stmt);        return; }
			if (stmt is TokenStmtNull)    {                                                    return; }
			if (stmt is TokenStmtRet)     { GenerateStmtRet     ((TokenStmtRet)stmt);          return; }
			if (stmt is TokenStmtRVal)    { GenerateStmtRVal    ((TokenStmtRVal)stmt);         return; }
			if (stmt is TokenStmtState)   { GenerateStmtState   ((TokenStmtState)stmt);        return; }
			if (stmt is TokenStmtWhile)   { GenerateStmtWhile   ((TokenStmtWhile)stmt);        return; }
			throw new Exception ("unknown TokenStmt type " + stmt.GetType ().ToString ());
		}

		/**
		 * @brief generate statement block (ie, with braces)
		 */
		private void GenerateStmtBlock (TokenStmtBlock stmtBlock, bool fromFunc)
		{
			/*
			 * If this is an inner statement block, start a new variable defintion block.
			 * If this is a top-level statement block, the caller must do this as necessary.
			 */
			if (!fromFunc) {
				PushVarDefnBlock ();
			}

			/*
			 * Push new current statement block pointer for anyone who cares.
			 */
			TokenStmtBlock oldStmtBlock = curStmtBlock;
			curStmtBlock = stmtBlock;

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
			 * If this is an inner statement block, pop the local var definition stack.
			 * If this is a top-level statement block, the caller must do this as necessary.
			 */
			if (!fromFunc) {
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
			ScriptMyLabel loopLabel = ilGen.DefineLabel ("doloop_" + doStmt.line + "_" + doStmt.posn);

			ilGen.MarkLabel (loopLabel);
			GenerateStmt (doStmt.bodyStmt);
			EmitCallCheckRun ();
			CompValu testRVal = GenerateFromRVal (doStmt.testRVal);
			testRVal.PushVal (this, tokenTypeBool);
			ilGen.Emit (OpCodes.Brtrue, loopLabel);
		}

		/**
		 * @brief output code for a 'for' statement
		 * Must use labels and if/goto's instead of braces as the test expression may generate temp 
		 * assignment statements and then we can't cram all the temp assignment statments in a real
		 * for statement.
		 */
		private void GenerateStmtFor (TokenStmtFor forStmt)
		{
			ScriptMyLabel doneLabel = ilGen.DefineLabel ("fordone_" + forStmt.line + "_" + forStmt.posn);
			ScriptMyLabel loopLabel = ilGen.DefineLabel ("forloop_" + forStmt.line + "_" + forStmt.posn);

			if (forStmt.initStmt != null) {
				GenerateStmt (forStmt.initStmt);
			}
			ilGen.MarkLabel (loopLabel);
			EmitCallCheckRun ();
			if (forStmt.testRVal != null) {
				CompValu testRVal = GenerateFromRVal (forStmt.testRVal);
				testRVal.PushVal (this, tokenTypeBool);
				ilGen.Emit (OpCodes.Brfalse, doneLabel);
			}
			GenerateStmt (forStmt.bodyStmt);
			if (forStmt.incrRVal != null) {
				GenerateFromRVal (forStmt.incrRVal);
			}
			ilGen.Emit (OpCodes.Br, loopLabel);
			ilGen.MarkLabel (doneLabel);
		}

		private void GenerateStmtForEach (TokenStmtForEach forEachStmt)
		{
			CompValu keyLVal   = null;
			CompValu valLVal   = null;
			CompValu arrayLVal = GenerateFromLVal (forEachStmt.arrayLVal);

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

			ScriptMyLabel doneLabel = ilGen.DefineLabel ("foreachdone_" + forEachStmt.line + "_" + forEachStmt.posn);
			ScriptMyLabel loopLabel = ilGen.DefineLabel ("foreachloop_" + forEachStmt.line + "_" + forEachStmt.posn);
			ScriptMyLocal indexVar  = ilGen.DeclareLocal (typeof (int), "foreachidx_" + forEachStmt.line + "_" + forEachStmt.posn);
			ScriptMyLocal objectVar = ((keyLVal == null) || (valLVal == null)) ? ilGen.DeclareLocal (typeof (object), "foreachobj_" + forEachStmt.line + "_" + forEachStmt.posn) : null;

			ilGen.MarkLabel (loopLabel);

			// ForEach arg 0: arrayLVal
			arrayLVal.PushVal (this);

			// ForEach arg 1: indexVar ++
			ilGen.Emit (OpCodes.Ldloc, indexVar);
			ilGen.Emit (OpCodes.Dup);
			PushConstantI4 (1);
			ilGen.Emit (OpCodes.Stloc, indexVar);

			// ForEach arg 2: ref keyLVal
			if (keyLVal == null) {
				ilGen.Emit (OpCodes.Ldloca, objectVar);
			} else {
				keyLVal.PushByRef (this);
			}

			// ForEach arg 3: ref valLVal
			if (valLVal == null) {
				ilGen.Emit (OpCodes.Ldloca, objectVar);
			} else {
				valLVal.PushByRef (this);
			}

			// Call XMR_Array.ForEach (arrayLVal, index, ref keyLVal, ref valLVal)
			ilGen.Emit (OpCodes.Callvirt, forEachMethodInfo);
			ilGen.Emit (OpCodes.Brfalse, doneLabel);

			/*
			 * Make sure we aren't hogging the CPU then generate the body and loop back.
			 */
			EmitCallCheckRun ();
			GenerateStmt (forEachStmt.bodyStmt);
			ilGen.Emit (OpCodes.Br, loopLabel);
			ilGen.MarkLabel (doneLabel);
		}

		/**
		 * @brief output code for an 'if' statement
		 * Braces are necessary because what may be one statement for trueStmt or elseStmt in
		 * the script may translate to more than one statement in the resultant C# code.
		 */
		private void GenerateStmtIf (TokenStmtIf ifStmt)
		{
			CompValu testRVal = GenerateFromRVal (ifStmt.testRVal);
			testRVal.PushVal (this, tokenTypeBool);
			ScriptMyLabel elseLabel = ilGen.DefineLabel ("ifelse_" + ifStmt.line + "_" + ifStmt.posn);
			ilGen.Emit (OpCodes.Brfalse, elseLabel);
			GenerateStmt (ifStmt.trueStmt);
			if (ifStmt.elseStmt == null) {
				ilGen.MarkLabel (elseLabel);
			} else {
				ScriptMyLabel doneLabel = ilGen.DefineLabel ("ifdone_" + ifStmt.line + "_" + ifStmt.posn);
				ilGen.Emit (OpCodes.Br, doneLabel);
				ilGen.MarkLabel (elseLabel);
				GenerateStmt (ifStmt.elseStmt);
				ilGen.MarkLabel (doneLabel);
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
			TokenStmtLabel stmtLabel;
			if (!curDeclFunc.labels.TryGetValue (jumpStmt.label.val, out stmtLabel)) {
				ErrorMsg (jumpStmt, "undefined label " + jumpStmt.label.val);
				return;
			}
			if (!stmtLabel.labelTagged) {
				stmtLabel.labelStruct = ilGen.DefineLabel ("jump_" + stmtLabel.name.val);
				stmtLabel.labelTagged = true;
			}

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
			 * Finally output the equivalent 'goto' statement.
			 */
			ilGen.Emit (OpCodes.Br, stmtLabel.labelStruct);
		}

		/**
		 * @brief output code for a jump target label statement.
		 * If there are any backward jumps to the label, do a CheckRun() also.
		 */
		private void GenerateStmtLabel (TokenStmtLabel labelStmt)
		{
			if (!labelStmt.labelTagged) {
				labelStmt.labelStruct = ilGen.DefineLabel ("jump_" + labelStmt.name.val);
				labelStmt.labelTagged = true;
			}
			ilGen.MarkLabel (labelStmt.labelStruct);
			if (labelStmt.hasBkwdRefs) {
				EmitCallCheckRun ();
			}
		}

		/**
		 * @brief output code for a return statement.
		 * @param retStmt = return statement token, including return value if any
		 */
		private void GenerateStmtRet (TokenStmtRet retStmt)
		{
			if (curDeclFunc.retType is TokenTypeVoid) {
				if (retStmt.rVal != null) {
					ErrorMsg (retStmt, "function returns void, no value allowed");
					return;
				}
			} else {
				if (retStmt.rVal == null) {
					ErrorMsg (retStmt, "function requires return value type " + curDeclFunc.retType.ToString ());
					return;
				}
				CompValu rVal = GenerateFromRVal (retStmt.rVal);
				rVal.PushVal (this, curDeclFunc.retType);
			}
			ilGen.Emit (OpCodes.Ret);
		}

		/**
		 * @brief the statement is just an expression, most likely an assignment or a ++ or -- thing.
		 */
		private void GenerateStmtRVal (TokenStmtRVal rValStmt)
		{
			GenerateFromRVal (rValStmt.rVal);
		}

		/**
		 * @brief generate code for a 'state' statement that transitions state.
		 * It sets the new state then returns.
		 */
		private void GenerateStmtState (TokenStmtState stateStmt)
		{
			int index = 0;  // 'default' state

			/*
			 * Set new state value and set the global 'stateChanged' flag.
			 * The 'stateChanged' flag causes our caller to unwind to its caller.
			 */
			if ((stateStmt.state != null) && !stateIndices.TryGetValue (stateStmt.state.val, out index)) {
				// The moron XEngine compiles scripts that reference undefined states.
				// So rather than produce a compile-time error, we'll throw an exception at runtime.
				// ErrorMsg (stateStmt, "undefined state " + stateStmt.state.val);

				// throw new UndefinedStateException (stateStmt.state.val);
				ilGen.Emit (OpCodes.Ldstr, stateStmt.state.val);
				ilGen.Emit (OpCodes.Newobj, scriptUndefinedStateExceptionConstructorInfo);
				ilGen.Emit (OpCodes.Throw);
				return;
			}

			/*
			 * __sm.stateCode = index
			 */
			ilGen.Emit (OpCodes.Ldarg_0);                    // scriptWrapper
			ilGen.Emit (OpCodes.Dup);                        // scriptWrapper
			PushConstantI4 (index);                          // new state's index
			ilGen.Emit (OpCodes.Stfld, stateCodeFieldInfo);  // scriptWrapper.stateCode

			/*
			 * __sm.stateChanged = true
			 */
			PushConstantI4 (1);
			ilGen.Emit (OpCodes.Stfld, stateChangedFieldInfo);

			/*
			 * Return without doing anything more.
			 */
			EmitDummyReturn ();
		}

		/**
		 * @brief generate code for a 'while' statement including the loop body.
		 */
		private void GenerateStmtWhile (TokenStmtWhile whileStmt)
		{
			ScriptMyLabel contLabel = ilGen.DefineLabel ("whilecont_" + whileStmt.line + "_" + whileStmt.posn);
			ScriptMyLabel doneLabel = ilGen.DefineLabel ("whiledone_" + whileStmt.line + "_" + whileStmt.posn);

			ilGen.MarkLabel (contLabel);                                // cont:
			CompValu testRVal = GenerateFromRVal (whileStmt.testRVal);  //   testRVal = while test expression
			testRVal.PushVal (this, tokenTypeBool);                     //   if (!testRVal)
			ilGen.Emit (OpCodes.Brfalse, doneLabel);                    //      goto done
			GenerateStmt (whileStmt.bodyStmt);                          //   while body statement
			EmitCallCheckRun ();                                        //   __sw.CheckRun()
			ilGen.Emit (OpCodes.Br, contLabel);                         //   goto cont
			ilGen.MarkLabel (doneLabel);                                // done:
		}

		/**
		 * @brief process a variable declaration statement, possibly with initialization expression.
		 */
		private void GenerateDeclVar (TokenDeclVar declVar)
		{
			CompValu local = new CompValuTemp (declVar.type, declVar.name.val, this);

			/*
			 * Script gave us an initialization value, so just store init value in var like an assignment statement.
			 */
			if (declVar.init != null) {
				local.PopPre (this);
				CompValu rVal = GenerateFromRVal (declVar.init);
				rVal.PushVal (this, declVar.type);
				local.PopPost (this);
			}

			/*
			 * Now it's ok for subsequent expressions in the block to reference the variable.
			 */
			AddVarDefinition (declVar.name, local);
		}

		/**
		 * @brief Get the type and location of an L-value (eg, variable)
		 */
		private CompValu GenerateFromLVal (TokenLVal lVal)
		{
			if (lVal is TokenLValArEle) return GenerateFromLValArEle ((TokenLValArEle)lVal);
			if (lVal is TokenLValField) return GenerateFromLValField ((TokenLValField)lVal);
			if (lVal is TokenLValName)  return GenerateFromLValName  ((TokenLValName)lVal);
			throw new Exception ("bad lval class");
		}

		/**
		 * @brief we have an L-value token that is an element within an array.
		 * @returns a CompValu giving the type and location of the element of the array.
		 */
		private CompValu GenerateFromLValArEle (TokenLValArEle lVal)
		{
			/*
			 * Compute subscript before rest of lVal in case of multiple subscripts.
			 */
			CompValu subRVal = GenerateFromRVal (lVal.subRVal);

			/*
			 * Compute location of array itself.
			 */
			CompValu baseLVal = GenerateFromLVal (lVal.baseLVal);

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
			return new CompValuArEle (new TokenTypeObject (lVal), baseLVal, subRVal);
		}

		/**
		 * @brief we have an L-value token that is a field within a struct.
		 * @returns a CompValu giving the type and location of the field in the struct.
		 */
		private CompValu GenerateFromLValField (TokenLValField lVal)
		{
			CompValu baseLVal = GenerateFromLVal (lVal.baseLVal);
			string fieldName = lVal.field.val;

			/*
			 * Since we only have a few types with fields, just pound them out.
			 */
			if (baseLVal.type is TokenTypeArray) {
				if (fieldName == "count") {
					return new CompValuField (new TokenTypeInt (lVal.field), baseLVal, arrayCountFieldInfo);
				}
				FieldInfo fi = null;
				if (fieldName == "index") fi = arrayIndexFieldInfo;
				if (fieldName == "value") fi = arrayValueFieldInfo;
				if (fi != null) {
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
					return new CompValuField (ttm, baseLVal, fi);
				}
			}
			if (baseLVal.type is TokenTypeRot) {
				FieldInfo fi = null;
				if (fieldName == "x") fi = rotationXFieldInfo;
				if (fieldName == "y") fi = rotationYFieldInfo;
				if (fieldName == "z") fi = rotationZFieldInfo;
				if (fieldName == "s") fi = rotationSFieldInfo;
				if (fi != null) {
					return new CompValuField (new TokenTypeFloat (lVal), baseLVal, fi);
				}
			}
			if (baseLVal.type is TokenTypeVec) {
				FieldInfo fi = null;
				if (fieldName == "x") fi = vectorXFieldInfo;
				if (fieldName == "y") fi = vectorYFieldInfo;
				if (fieldName == "z") fi = vectorZFieldInfo;
				if (fi != null) {
					return new CompValuField (new TokenTypeFloat (lVal), baseLVal, fi);
				}
			}

			ErrorMsg (lVal, "type " + baseLVal.type + " does not define field " + fieldName);
			return baseLVal;
		}

		/**
		 * @brief we have an L-value token that is a variable name.
		 * @returns a CompValu giving the type and location of the variable.
		 */
		private CompValu GenerateFromLValName (TokenLValName lVal)
		{
			string name = lVal.name.val;

			foreach (Dictionary<string, CompValu> vars in scriptVariablesStack) {
				CompValu defn;
				if (vars.TryGetValue (name, out defn)) {
					return defn;
				}
			}

			ErrorMsg (lVal, "undefined variable " + name);
			return new CompValuVoid (lVal);
		}

		/**
		 * @brief generate code from an RVal expression and return its type and where the result is stored.
		 * For anything that has side-effects, statements are generated that perform the computation then
		 * the result it put in a temp var and the temp var name is returned.
		 * For anything without side-effects, they are returned as an equivalent sequence of Emits.
		 * @param rVal = rVal token to be evaluated
		 * @returns resultant type and location
		 */
		private CompValu GenerateFromRVal (TokenRVal rVal)
		{
			errorMessageToken = rVal;
			if (rVal is TokenRValAsnPost)  return GenerateFromRValAsnPost ((TokenRValAsnPost)rVal);
			if (rVal is TokenRValAsnPre)   return GenerateFromRValAsnPre  ((TokenRValAsnPre)rVal);
			if (rVal is TokenRValCall)     return GenerateFromRValCall    ((TokenRValCall)rVal);
			if (rVal is TokenRValCast)     return GenerateFromRValCast    ((TokenRValCast)rVal);
			if (rVal is TokenRValConst)    return GenerateFromRValConst   ((TokenRValConst)rVal);
			if (rVal is TokenRValFloat)    return GenerateFromRValFloat   ((TokenRValFloat)rVal);
			if (rVal is TokenRValInt)      return GenerateFromRValInt     ((TokenRValInt)rVal);
			if (rVal is TokenRValIsType)   return GenerateFromRValIsType  ((TokenRValIsType)rVal);
			if (rVal is TokenRValList)     return GenerateFromRValList    ((TokenRValList)rVal);
			if (rVal is TokenRValLVal)     return GenerateFromRValLVal    ((TokenRValLVal)rVal);
			if (rVal is TokenRValOpBin)    return GenerateFromRValOpBin   ((TokenRValOpBin)rVal);
			if (rVal is TokenRValOpUn)     return GenerateFromRValOpUn    ((TokenRValOpUn)rVal);
			if (rVal is TokenRValParen)    return GenerateFromRValParen   ((TokenRValParen)rVal);
			if (rVal is TokenRValRot)      return GenerateFromRValRot     ((TokenRValRot)rVal);
			if (rVal is TokenRValStr)      return GenerateFromRValStr     ((TokenRValStr)rVal);
			if (rVal is TokenRValUndef)    return GenerateFromRValUndef   ((TokenRValUndef)rVal);
			if (rVal is TokenRValVec)      return GenerateFromRValVec     ((TokenRValVec)rVal);
			throw new Exception ("bad rval class " + rVal.GetType ().ToString ());
		}

		/**
		 * @brief compute the result of a binary operator (eg, add, subtract, multiply, lessthan)
		 * @param token = binary operator token, includes the left and right operands
		 * @returns where the resultant R-value is as something that doesn't have side effects
		 */
		private CompValu GenerateFromRValOpBin (TokenRValOpBin token)
		{
			CompValu leftLVal = null;
			CompValu left = null;
			CompValu right;

			/*
			 * If left operand is an L-value, create an leftLVal location marker for it.
			 * In either case, create a R-value location marker for it.
			 */
			if (token.rValLeft is TokenRValLVal) {
				left = leftLVal = GenerateFromLVal (((TokenRValLVal)token.rValLeft).lvToken);
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
					left = GenerateFromRVal (token.rValLeft);
				} else {
					right = GenerateFromRVal (token.rValRight);
					leftLVal.PopPre (this);
					right.PushVal (this, leftLVal.type);  // push (leftLVal.type)right
					leftLVal.PopPost (this);              // pop to leftLVal
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
					GenerateFromRVal (token.rValLeft);
				}

				/*
				 * Compute right-hand operand and that is the value of the expression.
				 */
				return GenerateFromRVal (token.rValRight);
			}

			/*
			 * Computation of some sort, compute right-hand operand value then left-hand value
			 * because LSL is supposed to be right-to-left evaluation.
			 *
			 * If left-hand operand has side effects, force right-hand operand into a temp so
			 * it will get computed first, and not just stacked for later evaluation.
			 */
			right = GenerateFromRVal (token.rValRight);
			if (token.rValLeft.sideEffects && !right.isFinal) {
				CompValu rightTemp = new CompValuTemp (right.type, null, this);
				rightTemp.PopPre (this);
				right.PushVal (this, right.type);
				rightTemp.PopPost (this);
				right = rightTemp;
			}
			left = GenerateFromRVal (token.rValLeft);

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
			BinOpStr binOpStr;
			if (binOpStrings.TryGetValue (key, out binOpStr)) {

				/*
				 * If table contained an explicit assignment type like +=, output the statement without
				 * casting the L-value, then return the L-value as the resultant value.
				 *
				 * Make sure we don't include comparisons (such as ==, >=, etc).
				 * Nothing like +=, -=, %=, etc, generate a boolean, only the comparisons.
				 */
				if ((binOpStr.outtype != typeof (bool)) && opcodeIndex.EndsWith ("=")) {
					binOpStr.emitBO (this, left, right, left);
					return left;
				}

				/*
				 * It's of the form left binop right.
				 * If either the original left or right had side effects, they should have been evaluated
				 * and put in temps already, so what we have for left and right don't have side effects.
				 * So we can simply return (outtype)(left binop right) as the location of the result.
				 *
				 * ??? optimise by creating a CompValu that can have left.PushVal(),right.PushVal(),EmitBinOpCode() as its PushVal() ???
				 */
				CompValu retRVal = new CompValuTemp (TokenType.FromSysType (token.opcode, binOpStr.outtype), null, this);
				retRVal.isFinal = left.isFinal && right.isFinal;
				binOpStr.emitBO (this, left, right, retRVal);
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
				if (binOpStrings.TryGetValue (key, out binOpStr)) {

					/*
					 * Now we know for something like %= that left%right is legal for the types given.
					 * We can only actually process it if the resultant type is of the left type.
					 * So for example, we can't do float += list, as float + list gives a list.
					 */
					if (binOpStr.outtype == left.type.typ) {

						/*
						 * Types are ok, see if the '=' (read/modify/write) form is allowed...
						 */
						if (binOpStr.rmwOK) {
							if (leftLVal == null) {
								ErrorMsg (token, "invalid L-value");
							} else {
								binOpStr.emitBO (this, leftLVal, right, leftLVal);
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
			return new CompValuVoid (token);
		}

		/**
		 * @brief compute the result of an unary operator
		 * @param token = unary operator token, includes the operand
		 * @returns where the resultant R-value is
		 */
		private CompValu GenerateFromRValOpUn (TokenRValOpUn token)
		{
			CompValu inRVal = GenerateFromRVal (token.rVal);
			return UnOpGenerate (inRVal, token.opcode);
		}

		/**
		 * @brief postfix operator -- this returns the type and location of the resultant value
		 */
		private CompValu GenerateFromRValAsnPost (TokenRValAsnPost asnPost)
		{
			CompValu lVal = GenerateFromLVal (asnPost.lVal);

			/*
			 * Make up a temp to put result in.
			 */
			CompValu result = new CompValuTemp (lVal.type, null, this);

			/*
			 * Push original value.
			 */
			lVal.PopPre (this);
			lVal.PushVal (this);

			/*
			 * Maybe caller wants value returned somewhere.
			 * If so, copy original value and store in result.
			 */
			result.PopPre (this);
			lVal.PushVal (this, result.type);
			result.PopPost (this);

			/*
			 * Perform the ++/--.
			 */
			PushConstantI4 (1);
			switch (asnPost.postfix.ToString ()) {
				case "++": {
					ilGen.Emit (OpCodes.Add);
					break;
				}
				case "--": {
					ilGen.Emit (OpCodes.Sub);
					break;
				}
				default: throw new Exception ("unknown asnPost op");
			}

			/*
			 * Store new value in original variable.
			 */
			lVal.PopPost (this);

			return result;
		}

		/**
		 * @brief prefix operator -- this returns the type and location of the resultant value
		 */
		private CompValu GenerateFromRValAsnPre (TokenRValAsnPre asnPre)
		{
			CompValu lVal = GenerateFromLVal (asnPre.lVal);

			/*
			 * Make up a temp to put result in.
			 */
			CompValu result = new CompValuTemp (lVal.type, null, this);

			/*
			 * Push original value.
			 */
			lVal.PopPre (this);
			lVal.PushVal (this);

			/*
			 * Perform the ++/--.
			 */
			PushConstantI4 (1);
			switch (asnPre.prefix.ToString ()) {
				case "++": {
					ilGen.Emit (OpCodes.Add);
					break;
				}
				case "--": {
					ilGen.Emit (OpCodes.Sub);
					break;
				}
				default: throw new Exception ("unknown asnPost op");
			}

			/*
			 * Store new value in original variable.
			 */
			lVal.PopPost (this);

			/*
			 * Maybe caller wants value returned somewhere.
			 * If so, copy original value and store in result.
			 */
			result.PopPre (this);
			lVal.PushVal (this, result.type);
			result.PopPost (this);

			return result;
		}

		/**
		 * @brief Generate code that calls a function or object's method.
		 * @returns where the call's return value is stored (a TokenTypeVoid if void)
		 */
		private CompValu GenerateFromRValCall (TokenRValCall call)
		{
			if (call.meth is TokenLValField) return GenerateFromRValCallField (call);
			if (call.meth is TokenLValName)  return GenerateFromRValCallName  (call);
			throw new Exception ("unknown call type");
		}

		/**
		 * @brief Generate code that calls a method of an object.
		 * @returns where the call's return value is stored (a TokenTypeVoid if void)
		 */
		private CompValu GenerateFromRValCallField (TokenRValCall call)
		{
			CompValu[] argRVals = null;
			int i, nargs;
			TokenLValField meth = (TokenLValField)call.meth;
			Type[] argTypes = null;

			/*
			 * Get method's entrypoint and signature.
			 */
			CompValu baseObj = GenerateFromLVal (meth.baseLVal);
			CompValu method = GenerateFromLVal (meth);
			if (((TokenTypeMeth)method.type).funcs.Length != 1) throw new Exception ("tu stOOpid");
			TokenDeclFunc declFunc = ((TokenTypeMeth)method.type).funcs[0];

			/*
			 * Make sure we have a place to put return value and prepare to pop return value into it.
			 * The PopPre() call must be done before pushing anything else so PopPost() will work.
			 */
			CompValu result = SetupReturnLocation (declFunc.retType);

			/*
			 * Push 'this' pointer as first arg to function.
			 */
			baseObj.PushVal (this);

			/*
			 * Compute and push the values of all the function's call arguments, left-to-right.
			 */
			nargs = call.nArgs;
			argTypes = new Type[nargs];
			if (nargs > 0) {
				argRVals = new CompValu[nargs];
				i = 0;
				for (TokenRVal arg = call.args; arg != null; arg = (TokenRVal)arg.nextToken) {
					argRVals[i] = GenerateFromRVal (arg);
					argRVals[i].PushVal (this);
					argTypes[i] = argRVals[i].type.typ;
					i ++;
				}
			}

			/*
			 * Number of arguments passed should match number of params the function was declared with.
			 */
			if (nargs != declFunc.argDecl.types.Length) {
				ErrorMsg (call, "method has " + declFunc.argDecl.types.Length.ToString () + " param(s), but call has " + nargs.ToString ());
				if (nargs > declFunc.argDecl.types.Length) nargs = declFunc.argDecl.types.Length;
			}

			/*
			 * Generate call, leaving the return value (if any) on the stack.
			 */
			MethodInfo methodInfo = baseObj.type.typ.GetMethod (meth.field.val, argTypes);
			ilGen.Emit (OpCodes.Callvirt, methodInfo);

			/*
			 * Deal with return value by putting it in 'result'.
			 */
			result.PopPost (this, declFunc.retType);
			return result;
		}

		/**
		 * @brief Generate code that calls a function.
		 * @returns where the call's return value is stored (a TokenTypeVoid if void)
		 */
		private CompValu GenerateFromRValCallName (TokenRValCall call)
		{
			CompValu[] argRVals = null;
			int i, nargs;
			string name;
			StringBuilder signature;
			TokenDeclFunc declFunc = null;

			name = ((TokenLValName)call.meth).name.val;

			/*
			 * Compute the values of all the function's call arguments.
			 * Save where the computation results are in the argRVals[] array.
			 * Might as well build the signature from the argument types, too.
			 */
			nargs = call.nArgs;
			signature = new StringBuilder (name);
			signature.Append ("(");
			argRVals = new CompValu[nargs];
			if (nargs > 0) {
				i = 0;
				for (TokenRVal arg = call.args; arg != null; arg = (TokenRVal)arg.nextToken) {
					argRVals[i] = GenerateFromRVal (arg);
					if (i > 0) signature.Append (",");
					signature.Append (argRVals[i].type.ToString ());
					i ++;
				}
			}
			signature.Append (")");

			/*
			 * Look the function up.
			 * First we look for a script-defined function by that name.  We only match name, 
			 * ... not signature, as we don't allow overloaded script-defined functions.
			 * Then try inline functions by signature.
			 */
			string sig = signature.ToString ();
			if (scriptFunctions.ContainsKey (name)) {
				declFunc = scriptFunctions[name];
			} else if (inlineFunctions.ContainsKey (sig)) {
				return GenerateFromRValCallInline (call, inlineFunctions[sig], argRVals);
			} else {

				/*
				 * Not found with that exact signature.
				 * If there is exactly one definition for that name but different signature,
				 * try implicit type casts to that one signature.
				 * Note that we only try internal functions as script-defined functions are
				 * matched by name only and not complete signature.
				 */
				string shortSig = name + "(";
				InlineFunction foundInline = null;
				foreach (KeyValuePair<string, InlineFunction> kvp in inlineFunctions) {
					if (kvp.Key.StartsWith (shortSig)) {
						if (foundInline != null) goto nohope;
						foundInline = kvp.Value;
					}
				}
				if (foundInline != null) {
					return GenerateFromRValCallInline (call, foundInline, argRVals);
				}

				/*
				 * No hope, output error message.
				 */
			nohope:
				ErrorMsg (call, "undefined function " + sig);
				foreach (KeyValuePair<string, InlineFunction> kvp in inlineFunctions) {
					if (kvp.Key.StartsWith (shortSig)) ErrorMsg (call, "  have " + kvp.Key);
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
			 * Make sure we have a place to put return value and prepare to pop return value into it.
			 * The PopPre() call must be done before pushing anything else so PopPost() will work.
			 */
			CompValu result = SetupReturnLocation (declFunc.retType);

			/*
			 * First arg is scriptWrapper to pass context along as the function is static.
			 * Then push the remainder of the args, left-to-right.
			 */
			ilGen.Emit (OpCodes.Ldarg_0);                  // this function's scriptWrapper gets passed as arg[0]
			for (i = 0; i < nargs; i ++) {
				if (declFunc.argDecl.types == null) {
					argRVals[i].PushVal (this);
				} else {
					argRVals[i].PushVal (this, declFunc.argDecl.types[i]);
				}
			}

			ilGen.Emit (OpCodes.Call, declFunc.dynamicMethod);

			/*
			 * Deal with the return value (if any), by putting it in 'result'.
			 */
			result.PopPost (this, declFunc.retType);

			/*
			 * Also, unwind out if the called function changed state.
			 * ???? optimize by putting just one 'dummyreturn' label at end of function ????
			 */
			if (declFunc.changesState) {

				// if (__sw.StateChanged) return;
				ScriptMyLabel noStateChangeLabel = ilGen.DefineLabel ("nostatechg_" + call.line + "_" + call.posn);
				ilGen.Emit (OpCodes.Ldarg_0);                       // scriptWrapper
				ilGen.Emit (OpCodes.Ldfld, stateChangedFieldInfo);  // scriptWrapper.stateChanged
				ilGen.Emit (OpCodes.Brfalse, noStateChangeLabel);
				EmitDummyReturn ();
				ilGen.MarkLabel (noStateChangeLabel);
			}
			return result;
		}

		/**
		 * @brief Generate call to inline function.
		 * @param call       = calling script token, encapsulates call and parameters
		 * @param inlineFunc = what inline function is being called
		 * @param argRVals   = arguments to pass to function
		 * @returns where the call's return value is stored (a TokenTypeVoid if void)
		 */
		private CompValu GenerateFromRValCallInline (TokenRValCall call, InlineFunction inlineFunc, CompValu[] argRVals)
		{
			CompValu result = SetupReturnLocation (inlineFunc.retType);
			inlineFunc.codeGen (this, result, argRVals);
			return result;
		}

		private CompValu SetupReturnLocation (TokenType retType)
		{
			CompValu result;
			if (retType is TokenTypeVoid) {
				result = new CompValuVoid (retType);
			} else {
				result = new CompValuTemp (retType, null, this);
			}
			result.PopPre (this);
			return result;
		}

		/**
		 * @brief Generate code that casts a value to a particular type.
		 * @returns where the result of the conversion is stored.
		 */
		private CompValu GenerateFromRValCast (TokenRValCast cast)
		{
			CompValu inRVal = GenerateFromRVal (cast.rVal);
			TokenType outType = cast.castTo;

			if (inRVal.type == outType) return inRVal;

			//??? optimize by having CompValu.PushVal() emit code for the conversion instead of needing a temp ???//
			CompValu outRVal = new CompValuTemp (outType, null, this);
			outRVal.isFinal = inRVal.isFinal;
			outRVal.PopPre (this);
			inRVal.PushVal (this, outType, true);
			outRVal.PopPost (this);
			return outRVal;
		}

		/**
		 * @brief Constant in MMRScriptConsts.cs
		 * @returns where the constants value is stored
		 */
		private CompValu GenerateFromRValConst (TokenRValConst rValConst)
		{
			ScriptConst sc = rValConst.val;
			return sc.rVal;
		}

		/**
		 * @brief floating-point constant.
		 */
		private CompValu GenerateFromRValFloat (TokenRValFloat rValFloat)
		{
			return new CompValuFloat (new TokenTypeFloat (rValFloat), (float)rValFloat.flToken.val);
		}

		/**
		 * @brief integer constant.
		 */
		private CompValu GenerateFromRValInt (TokenRValInt rValInt)
		{
			return new CompValuInteger (new TokenTypeInt (rValInt), rValInt.inToken.val);
		}

		/**
		 * @brief generate a new list object
		 * @param rValList = an rVal to create it from
		 */
		private CompValu GenerateFromRValList (TokenRValList rValList)
		{
			CompValu newList = new CompValuTemp (new TokenTypeList (rValList.rVal), null, this);
			newList.PopPre (this);

			/*
			 * Create a temp array to hold all the initial values.
			 */
			PushConstantI4 (rValList.nItems);
			ilGen.Emit (OpCodes.Newarr, typeof (object));

			/*
			 * Populate the array.
			 */
			int i = 0;
			for (TokenRVal val = rValList.rVal; val != null; val = (TokenRVal)val.nextToken) {

				/*
				 * Get pointer to temp array object.
				 */
				ilGen.Emit (OpCodes.Dup);

				/*
				 * Get index in that array.
				 */
				PushConstantI4 (i);

				/*
				 * Emit code to compute initial value for the element.
				 */
				CompValu eRVal = GenerateFromRVal (val);

				/*
				 * Store initialization value in array location.
				 * However, floats and ints need to be converted to LSL_Float and LSL_Integer,
				 * or things like llSetPayPrice() will puque when they try to cast the elements
				 * to LSL_Float or LSL_Integer.  Likewise with string/LSL_String.
				 *
				 * Maybe it's already LSL-boxed so we don't do anything with it except make sure
				 * it is an object, not a struct.
				 */
				eRVal.PushVal (this);
				if (eRVal.type.lslBoxing == null) {
					if (eRVal.type is TokenTypeFloat) {
						ilGen.Emit (OpCodes.Newobj, lslFloatConstructorInfo);
						ilGen.Emit (OpCodes.Box, typeof (LSL_Float));
					} else if (eRVal.type is TokenTypeInt) {
						ilGen.Emit (OpCodes.Newobj, lslIntegerConstructorInfo);
						ilGen.Emit (OpCodes.Box, typeof (LSL_Integer));
					} else if (eRVal.type is TokenTypeStr) {
						ilGen.Emit (OpCodes.Newobj, lslStringConstructorInfo);
						ilGen.Emit (OpCodes.Box, typeof (LSL_String));
					} else if (eRVal.type.typ.IsValueType) {
						ilGen.Emit (OpCodes.Box, eRVal.type.typ);
					}
				} else if (eRVal.type.lslBoxing.IsValueType) {

					// Convert the LSL value structs to an object of the LSL-boxed type
					ilGen.Emit (OpCodes.Box, eRVal.type.lslBoxing);
				}
				ilGen.Emit (OpCodes.Stelem, typeof (object));
				i ++;
			}

			/*
			 * Create new list object from temp initial value array (whose ref is still on the stack).
			 */
			ilGen.Emit (OpCodes.Newobj, lslListConstructorInfo);
			newList.PopPost (this);
			return newList;
		}

		/**
		 * @brief get the R-value from an L-value
		 *        this happens when doing things like reading a variable
		 */
		private CompValu GenerateFromRValLVal (TokenRValLVal rValLVal)
		{
			if (!(rValLVal.lvToken is TokenLValName)) {
				return GenerateFromLVal (rValLVal.lvToken);
			}

			string name = ((TokenLValName)rValLVal.lvToken).name.val;

			foreach (Dictionary<string, CompValu> vars in scriptVariablesStack) {
				CompValu defn;
				if (vars.TryGetValue (name, out defn)) {
					return defn;
				}
			}

			ErrorMsg (rValLVal, "undefined variable " + name);
			return new CompValuVoid (rValLVal);
		}

		/**
		 * @brief parenthesized expression
		 * @returns type and location of the result of the computation.
		 */
		private CompValu GenerateFromRValParen (TokenRValParen rValParen)
		{
			return GenerateFromRVal (rValParen.rVal);
		}

		/**
		 * @brief create a rotation object from the x,y,z,w value expressions.
		 */
		private CompValu GenerateFromRValRot (TokenRValRot rValRot)
		{
			CompValu xRVal, yRVal, zRVal, wRVal;

			xRVal = GenerateFromRVal (rValRot.xRVal);
			yRVal = GenerateFromRVal (rValRot.yRVal);
			zRVal = GenerateFromRVal (rValRot.zRVal);
			wRVal = GenerateFromRVal (rValRot.wRVal);
			return new CompValuRot (new TokenTypeRot (rValRot), xRVal, yRVal, zRVal, wRVal);
		}

		/**
		 * @brief string constant.
		 */
		private CompValu GenerateFromRValStr (TokenRValStr rValStr)
		{
			return new CompValuString (new TokenTypeStr (rValStr), rValStr.strToken.val);
		}

		/**
		 * @brief 'undefined' constant.
		 *        If this constant gets written to an array element, it will delete that element from the array.
		 *        If the script retrieves an element by key that is not defined, it will get this value.
		 *        This value can be stored in and retrieved from variables of type 'object'.
		 *        It is a runtime error to cast this value to any type, eg, we don't allow string variables to be null pointers.
		 */
		private CompValu GenerateFromRValUndef (TokenRValUndef rValUndef)
		{
			return new CompValuNull (new TokenTypeObject (rValUndef));
		}

		/**
		 * @brief create a vector object from the x,y,z value expressions.
		 */
		private CompValu GenerateFromRValVec (TokenRValVec rValVec)
		{
			CompValu xRVal, yRVal, zRVal;

			xRVal = GenerateFromRVal (rValVec.xRVal);
			yRVal = GenerateFromRVal (rValVec.yRVal);
			zRVal = GenerateFromRVal (rValVec.zRVal);
			return new CompValuVec (new TokenTypeVec (rValVec), xRVal, yRVal, zRVal);
		}

		/**
		 * @brief Generate code to process an <rVal> is <type> expression, and produce a boolean value.
		 */
		private CompValu GenerateFromRValIsType (TokenRValIsType rValIsType)
		{
			ErrorMsg (rValIsType, "not supported");
			return new CompValuVoid (rValIsType);
		}

		/**
		 * @brief Output a return statement with a null value.
		 *        This is used when unwinding, such as from a changed state.
		 */
		private void EmitDummyReturn ()
		{
			PushDefaultValue (curDeclFunc.retType);
			ilGen.Emit (OpCodes.Ret);
		}

		/**
		 * @brief Push the default (null) value for a particular type
		 * @param type = type to get the default value for
		 * @returns with value pushed on stack
		 */
		private void PushDefaultValue (TokenType type)
		{
			if (type is TokenTypeArray) {
				ilGen.Emit (OpCodes.Newobj, xmrArrayConstructorInfo);
				return;
			}
			if (type is TokenTypeList) {
				PushConstantI4 (0);
				ilGen.Emit (OpCodes.Newarr, typeof (object));
				ilGen.Emit (OpCodes.Newobj, lslListConstructorInfo);
				return;
			}
			if (type is TokenTypeRot) {
				// Mono is tOO stOOpid to allow: ilGen.Emit (OpCodes.Ldsfld, zeroRotationFieldInfo);
				ilGen.Emit (OpCodes.Ldc_R8, ScriptBaseClass.ZERO_ROTATION.x);
				ilGen.Emit (OpCodes.Ldc_R8, ScriptBaseClass.ZERO_ROTATION.y);
				ilGen.Emit (OpCodes.Ldc_R8, ScriptBaseClass.ZERO_ROTATION.z);
				ilGen.Emit (OpCodes.Ldc_R8, ScriptBaseClass.ZERO_ROTATION.s);
				ilGen.Emit (OpCodes.Newobj, lslRotationConstructorInfo);
				return;
			}
			if (type is TokenTypeStr) {
				ilGen.Emit (OpCodes.Ldstr, "");
				return;
			}
			if (type is TokenTypeVec) {
				// Mono is tOO stOOpid to allow: ilGen.Emit (OpCodes.Ldsfld, zeroVectorFieldInfo);
				ilGen.Emit (OpCodes.Ldc_R8, ScriptBaseClass.ZERO_VECTOR.x);
				ilGen.Emit (OpCodes.Ldc_R8, ScriptBaseClass.ZERO_VECTOR.y);
				ilGen.Emit (OpCodes.Ldc_R8, ScriptBaseClass.ZERO_VECTOR.z);
				ilGen.Emit (OpCodes.Newobj, lslVectorConstructorInfo);
				return;
			}
			if (type is TokenTypeInt) {
				PushConstantI4 (0);
				return;
			}
			if (type is TokenTypeFloat) {
				ilGen.Emit (OpCodes.Ldc_R4, 0.0f);
				return;
			}

			/*
			 * Void is pushed as the default return value of a void function.
			 * So just push nothing as expected of void functions.
			 */
			if (type is TokenTypeVoid) {
				return;
			}

			throw new Exception ("unknown type " + type.ToString ());
		}

		/**
		 * @brief create table of legal event handler prototypes.
		 *        This is used to make sure script's event handler declrations are valid.
		 */
		private static Dictionary<string, TokenDeclFunc> CreateLegalEventHandlers ()
		{
			Dictionary<string, TokenDeclFunc> leh = new InternalFuncDict (typeof (IEventHandlers), false);
			return leh;
		}

		/**
		 * @brief Push an integer constant
		 */
		public void PushConstantI4 (int c)
		{
			switch (c) {
				case -1: {
					ilGen.Emit (OpCodes.Ldc_I4_M1);
					return;
				}
				case 0: {
					ilGen.Emit (OpCodes.Ldc_I4_0);
					return;
				}
				case 1: {
					ilGen.Emit (OpCodes.Ldc_I4_1);
					return;
				}
				case 2: {
					ilGen.Emit (OpCodes.Ldc_I4_2);
					return;
				}
				case 3: {
					ilGen.Emit (OpCodes.Ldc_I4_3);
					return;
				}
				case 4: {
					ilGen.Emit (OpCodes.Ldc_I4_4);
					return;
				}
				case 5: {
					ilGen.Emit (OpCodes.Ldc_I4_5);
					return;
				}
				case 6: {
					ilGen.Emit (OpCodes.Ldc_I4_6);
					return;
				}
				case 7: {
					ilGen.Emit (OpCodes.Ldc_I4_7);
					return;
				}
				case 8: {
					ilGen.Emit (OpCodes.Ldc_I4_8);
					return;
				}
				default: break;
			}
			if ((c >= 0) && (c <= 127)) {  // negatives dont seem to work
				ilGen.Emit (OpCodes.Ldc_I4_S, c);
				return;
			}
			ilGen.Emit (OpCodes.Ldc_I4, c);
		}

		/**
		 * @brief Emit a call to CheckRun(), (voluntary multitasking switch)
		 */
		public void EmitCallCheckRun ()
		{
			ilGen.Emit (OpCodes.Ldarg_0);                       // scriptWrapper
			ilGen.Emit (OpCodes.Ldfld, continuationFieldInfo);  // scriptWrapper.continuation
			ilGen.Emit (OpCodes.Call, checkRunMethodInfo);      // scriptWrapper.continuation.CheckRun()
		}

		/**
		 * @brief maintain variable definition stack.
		 * It translates a variable name string to its declaration.
		 */
		private Dictionary<string, CompValu> PushVarDefnBlock ()
		{
			Dictionary<string, CompValu> frame = new Dictionary<string, CompValu> ();
			scriptVariablesStack.Push (frame);
			return frame;
		}
		private void PopVarDefnBlock ()
		{
			scriptVariablesStack.Pop ();
		}
		private void AddVarDefinition (TokenName name, CompValu var)
		{
			Dictionary<string, CompValu> vars = scriptVariablesStack.Peek ();

			if (vars.ContainsKey (name.val)) {
				ErrorMsg (name, "duplicate var definition " + name);
			} else {
				vars.Add (name.val, var);
			}
		}

		/**
		 * @brief handle a unary operator, such as -x.
		 */
		private CompValu UnOpGenerate (CompValu inRVal, Token opcode)
		{
			/*
			 * - Negate
			 */
			if (opcode is TokenKwSub) {
				if (inRVal.type is TokenTypeFloat) {
					CompValu outRVal = new CompValuTemp (new TokenTypeFloat (opcode), null, this);
					outRVal.PopPre (this);                // set up for a pop
					inRVal.PushVal (this, outRVal.type);  // push value to negate, make sure not LSL-boxed
					ilGen.Emit (OpCodes.Neg);             // compute the negative
					outRVal.PopPost (this);               // pop into result
					return outRVal;                       // tell caller where we put it
				}
				if (inRVal.type is TokenTypeInt) {
					CompValu outRVal = new CompValuTemp (new TokenTypeInt (opcode), null, this);
					outRVal.PopPre (this);                // set up for a pop
					inRVal.PushVal (this, outRVal.type);  // push value to negate, make sure not LSL-boxed
					ilGen.Emit (OpCodes.Neg);             // compute the negative
					outRVal.PopPost (this);               // pop into result
					return outRVal;                       // tell caller where we put it
				}
				if (inRVal.type is TokenTypeVec) {
					CompValu outRVal = new CompValuTemp (inRVal.type, null, this);
					outRVal.PopPre (this);                // set up for a pop
					inRVal.PushVal (this);                // push vector, then call negate routine
					ilGen.Emit (OpCodes.Call, lslVectorNegateMethodInfo);
					outRVal.PopPost (this);               // pop into result
					return outRVal;                       // tell caller where we put it
				}
				ErrorMsg (opcode, "can't negate a " + inRVal.type.ToString ());
				return inRVal;
			}

			/*
			 * ~ Complement (bitwise integer)
			 */
			if (opcode is TokenKwTilde) {
				if (inRVal.type is TokenTypeInt) {
					CompValu outRVal = new CompValuTemp (new TokenTypeInt (opcode), null, this);
					outRVal.PopPre (this);                // set up for a pop
					inRVal.PushVal (this, outRVal.type);  // push value to negate, make sure not LSL-boxed
					ilGen.Emit (OpCodes.Not);             // compute the complement
					outRVal.PopPost (this);               // pop into result
					return outRVal;                       // tell caller where we put it
				}
				ErrorMsg (opcode, "can't complement a " + inRVal.type.ToString ());
				return inRVal;
			}

			/*
			 * ! Not (boolean)
			 *
			 * We stuff the 0/1 result in an int because I've seen x+!y in scripts
			 * and we don't want to have to create tables to handle int+bool and
			 * everything like that.
			 */
			if (opcode is TokenKwExclam) {
				CompValu outRVal = new CompValuTemp (new TokenTypeInt (opcode), null, this);
				outRVal.PopPre (this);                 // set up for a pop
				inRVal.PushVal (this, tokenTypeBool);  // anything converts to boolean
				PushConstantI4 (1);                    // then XOR with 1 to flip it
				ilGen.Emit (OpCodes.Xor);
				outRVal.PopPost (this);                // pop into result
				return outRVal;                        // tell caller where we put it
			}

			throw new Exception ("unhandled opcode " + opcode.ToString ());
		}

		/**
		 * @brief output error message and remember that we did
		 */
		public void ErrorMsg (Token token, string message)
		{
			if ((token == null) || (token.emsg == null)) token = errorMessageToken;
			token.ErrorMsg (message);
			youveAnError = true;
		}

		/**
		 * @brief Find a private static method.
		 * @param owner = class the method is part of
		 * @param name = name of method to find
		 * @param args = array of argument types
		 * @returns pointer to method
		 */
		public static MethodInfo GetStaticMethod (Type owner, string name, Type[] args)
		{
			MethodInfo mi = owner.GetMethod (name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, args, null);
			if (mi == null) {
				Console.Write ("GetStaticMethod*: " + owner.ToString () + "." + name + " (");
				for (int i = 0; i < args.Length; i ++) {
					if (i > 0) Console.Write (", ");
					Console.Write (args[i].ToString ());
				}
				Console.WriteLine (")");

				MethodInfo[] methods = owner.GetMethods (BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				for (int j = 0; j < methods.Length; j ++) {
					mi = methods[j];
					ParameterInfo[] pis = mi.GetParameters ();
					Console.Write ("GetStaticMethod*:   ." + mi.Name + " (");
					for (int i = 0; i < args.Length; i ++) {
						if (i > 0) Console.Write (", ");
						Console.Write (pis[i].ParameterType.ToString ());
					}
					Console.WriteLine (")");
				}

				throw new Exception ("undefined method " + owner.ToString () + "." + name);
			}
			return mi;
		}

		public static LSL_Vector LSLVectorNegate (LSL_Vector v) { return -v; }
	}

	/**
	 * @brief Thrown by a script when it attempts to change to an undefined state.
	 * These can be detected at compile time but the moron XEngine compiles
	 * such things, so we compile them as runtime errors.
	 */
	public class ScriptUndefinedStateException : Exception {
		public string stateName;
		public ScriptUndefinedStateException (string stateName) : base ("undefined state " + stateName) {
			this.stateName = stateName;
		}
	}
}
