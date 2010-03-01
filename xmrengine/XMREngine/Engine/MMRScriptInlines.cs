/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;

using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;


/**
 * @brief Generate code for the backend API calls.
 */
namespace OpenSim.Region.ScriptEngine.XMREngine
{
	public delegate void CodeGenCall (ScriptCodeGen scg, CompValu result, CompValu[] args);

	public class InlineFunction {
		public string signature;      // name(arglsltypes,...)
		public TokenType retType;     // return value type (TokenTypeVoid for void)
		public TokenType[] argTypes;  // argument types (valid only for CodeGenBEApi);
		public CodeGenCall codeGen;   // method that generates code
		public MethodInfo methInfo;   // function called by the inline
		public bool doCheckRun;       // valid for CodeGenBEApi only

		private static TokenTypeFloat tokenTypeFloat = new TokenTypeFloat (null);
		private static MethodInfo roundMethInfo = ScriptCodeGen.GetStaticMethod (typeof (System.Math), "Round", 
				new Type[] { typeof (double), typeof (MidpointRounding) });

		/**
		 * @brief Create a dictionary of inline backend API functions.
		 *
		 * The code string has two basic forms:  single-expression and statement-block.
		 *
		 * In either form, the following substitutions are made in the inline code string:
		 *    {0} is replaced with the first parameter's value
		 *    {1} is replaced with the second parameter's value
		 *    ... etc for all parameters.
		 * The compiler guarantees the substituted value is of the type given in the inline's
		 * prototype.  The compiler also guarantees that the value is free of side-effects, 
		 * eg, it does not make a function call or increment a variable, so any parameter may
		 * be located in more than one place without assigning it to a temporary.  For example
		 * an inline for a Min function can be safely done as:
		 *    {0} < {1} ? {0} : {1}
		 *
		 * For statement-block forms that have a return value, this additional substitution
		 * is made:
		 *    {#} is replaced with a variable that receives the return value.
		 * The compiler guarantees the variable is of the type given as the return type in the
		 * inline's definition.
		 *
		 * The statement-block form always begins with a { character after parameter
		 * substitution has taken place (but before return value substitution).  Otherwise, 
		 * it is assumed to be single-expressionform.
		 *
		 * For inlines that return a value, the given inline code for single-expression
		 * form must fit this template to form a valid complete C# statement:
		 *
		 *    <variable> = ( <typecast> ) <inline-code> ;
		 *
		 * For inlines that return void, the given inline code for single-expression form
		 * must fit this template to form a valid complete C# statement:
		 *
		 *    <inline-code> ;
		 *
		 * Otherwise, inline code must use the statement-block form such that this template 
		 * forms a valid complete C# statement:
		 *
		 *    <inline-code>
		 *
		 * Thus inlines of arbitrary complexity can be generated.  However, this should be
		 * discouraged as any change in the inline might require that all script .DLL files
		 * be recompiled.  In these cases, it is better to use the single-expression form
		 * to call a static function which actually performs the work.  Then that static
		 * function can be modified without having to recompile all scripts.
		 *
		 * In any case, if an incompatile change is made to an inline, script recompilation
		 * can be forced by incrementing ScriptCodeGen.COMPILED_VERSION_VALUE.
		 *
		 * In addition to the parameter values, the inline code (either form) has the
		 * following variable avaialble:
		 *    __sm = the current ScriptModule instance, which inherits from ScriptWrapper
		 * Also, any substituted parameters or return value identifiers will always begin
		 * with two underscores (__), so any local variables created for internal use by
		 * the inline code must begin with something other than __.
		 */
		public static Dictionary<string, InlineFunction> CreateDictionary ()
		{
			Dictionary<string, InlineFunction> ifd = new Dictionary<string, InlineFunction> ();
			InlineFunction inf;

			Type[] oneDoub  = new Type[] { typeof (double) };
			Type[] twoDoubs = new Type[] { typeof (double), typeof (double) };

			/*
			 * Mono generates an FPU instruction for many math calls.
			 */
			inf = new InlineFunction (ifd, "llAbs(integer)",       typeof (int),   null);                                                                      inf.codeGen = inf.CodeGenLLAbs;
			inf = new InlineFunction (ifd, "llAcos(float)",        typeof (float), ScriptCodeGen.GetStaticMethod (typeof (System.Math), "Acos",    oneDoub));  inf.codeGen = inf.CodeGenStatic;
			inf = new InlineFunction (ifd, "llAsin(float)",        typeof (float), ScriptCodeGen.GetStaticMethod (typeof (System.Math), "Asin",    oneDoub));  inf.codeGen = inf.CodeGenStatic;
			inf = new InlineFunction (ifd, "llAtan2(float,float)", typeof (float), ScriptCodeGen.GetStaticMethod (typeof (System.Math), "Atan2",   twoDoubs)); inf.codeGen = inf.CodeGenStatic;
			inf = new InlineFunction (ifd, "llCeil(float)",        typeof (float), ScriptCodeGen.GetStaticMethod (typeof (System.Math), "Ceiling", oneDoub));  inf.codeGen = inf.CodeGenStatic;
			inf = new InlineFunction (ifd, "llCos(float)",         typeof (float), ScriptCodeGen.GetStaticMethod (typeof (System.Math), "Cos",     oneDoub));  inf.codeGen = inf.CodeGenStatic;
			inf = new InlineFunction (ifd, "llFabs(float)",        typeof (float), ScriptCodeGen.GetStaticMethod (typeof (System.Math), "Abs",     oneDoub));  inf.codeGen = inf.CodeGenStatic;
			inf = new InlineFunction (ifd, "llFloor(float)",       typeof (float), ScriptCodeGen.GetStaticMethod (typeof (System.Math), "Floor",   oneDoub));  inf.codeGen = inf.CodeGenStatic;
			inf = new InlineFunction (ifd, "llLog(float)",         typeof (float), ScriptCodeGen.GetStaticMethod (typeof (System.Math), "Log",     oneDoub));  inf.codeGen = inf.CodeGenStatic;
			inf = new InlineFunction (ifd, "llLog10(float)",       typeof (float), ScriptCodeGen.GetStaticMethod (typeof (System.Math), "Log10",   oneDoub));  inf.codeGen = inf.CodeGenStatic;
			inf = new InlineFunction (ifd, "llPow(float,float)",   typeof (float), ScriptCodeGen.GetStaticMethod (typeof (System.Math), "Pow",     twoDoubs)); inf.codeGen = inf.CodeGenStatic;
			inf = new InlineFunction (ifd, "llRound(float)",       typeof (float), null);                                                                      inf.codeGen = inf.CodeGenLLRound;
			inf = new InlineFunction (ifd, "llSin(float)",         typeof (float), ScriptCodeGen.GetStaticMethod (typeof (System.Math), "Sin",     oneDoub));  inf.codeGen = inf.CodeGenStatic;
			inf = new InlineFunction (ifd, "llSqrt(float)",        typeof (float), ScriptCodeGen.GetStaticMethod (typeof (System.Math), "Sqrt",    oneDoub));  inf.codeGen = inf.CodeGenStatic;
			inf = new InlineFunction (ifd, "llTan(float)",         typeof (float), ScriptCodeGen.GetStaticMethod (typeof (System.Math), "Tan",     oneDoub));  inf.codeGen = inf.CodeGenStatic;

			/*
			 * Finally for any API functions defined by ScriptBaseClass that are not overridden 
			 * by anything already defined above, create an inline definition to call it.
			 *
			 * We create statement-block forms like this for each:
			 *    {
			 *       {#} = __be.methodname({0},{1},...);
			 *       __sm.continuation.CheckRun();
			 *    }
			 *
			 * But for those listed in noCheckRun, we generate:
			 *    (__be.methodname({0},{1},...))
			 */
			string[] noCheckRun = new string[] {
				"llBase64ToString",
				"llCSV2List",
				"llDeleteSubList",
				"llDeleteSubString",
				"llDumpList2String",
				"llEscapeURL",
				"llEuler2Rot",
				"llGetListEntryType",
				"llGetListLength",
				"llGetSubString",
				"llGetUnixTime",
				"llInsertString",
				"llList2CSV",
				"llList2Float",
				"llList2Integer",
				"llList2Key",
				"llList2List",
				"llList2ListStrided",
				"llList2Rot",
				"llList2String",
				"llList2Vector",
				"llListFindList",
				"llListInsertList",
				"llListRandomize",
				"llListReplaceList",
				"llListSort",
				"llListStatistics",
				"llMD5String",
				"llParseString2List",
				"llParseStringKeepNulls",
				"llStringLength",
				"llStringToBase64",
				"llStringTrim",
				"llSubStringIndex",
				"llUnescapeURL"
			};

			MethodInfo[] ifaceMethods = typeof (ScriptBaseClass).GetMethods ();
			foreach (MethodInfo ifaceMethod in ifaceMethods) {
				string key = ifaceMethod.Name;

				/*
				 * Only do ones that begin with lower-case letters...
				 * as any others can't be referenced by scripts.
				 */
				if ((ifaceMethod.Name[0] < 'a') || (ifaceMethod.Name[0] > 'z')) continue;

				try {

					/*
					 * Create a corresponding signature.
					 */
					ParameterInfo[] parameters = ifaceMethod.GetParameters ();
					TokenType[] argTypes = new TokenType[parameters.Length];
					StringBuilder sig = new StringBuilder (key);
					sig.Append ('(');
					for (int i = 0; i < parameters.Length; i ++) {
						if (i > 0) sig.Append (',');
						argTypes[i] = TokenType.FromSysType (null, parameters[i].ParameterType);
						sig.Append (argTypes[i].ToString ());
					}
					sig.Append (')');
					key = sig.ToString ();

					/*
					 * If that signature isn't already in dictionary, add it.
					 */
					if (!ifd.ContainsKey (key)) {
						Type retType = ifaceMethod.ReturnType;
						InlineFunction inlfun = new InlineFunction (ifd, key, retType, ifaceMethod);
						inlfun.argTypes   = argTypes;
						inlfun.codeGen    = inlfun.CodeGenBEApi;
						inlfun.doCheckRun = true;
						for (int i = noCheckRun.Length; -- i >= 0;) {
							if (noCheckRun[i] == ifaceMethod.Name) {
								inlfun.doCheckRun = false;
								break;
							}
						}
					}
				} catch (Exception except) {

					string msg = except.ToString ();
					int i = msg.IndexOf ("\n");
					if (i > 0) msg = msg.Substring (0, i);
					Console.WriteLine ("InlineFunction*: {0}:     {1}", key, msg);

					///??? IGNORE ANY THAT FAIL - LIKE UNRECOGNIZED TYPE ???///
					///???                          and OVERLOADED NAMES ???///
				}
			}

			return ifd;
		}

		/**
		 * @brief Add an inline function definition to the dictionary.
		 * @param ifd       = dictionary to add inline definition to
		 * @param signature = inline function signature string, in form <name>(<arglsltypes>,...)
		 * @param retType   = system return type, use typeof(void) if no return value
		 * @param methInfo  = used by codeGen to know what backend method to call
		 */
		private InlineFunction (Dictionary<string, InlineFunction> ifd, 
		                        string signature, 
		                        Type retType, 
		                        MethodInfo methInfo)
		{
			this.signature = signature;
			this.retType   = TokenType.FromSysType (null, retType);
			this.methInfo  = methInfo;
			ifd.Add (signature, this);
		}

		/**
		 * @brief Code generators...
		 * @param scg = script we are generating code for
		 * @param result = type/location for result (type matches function definition)
		 * @param args = type/location of arguments (types match function definition)
		 */

		private void CodeGenLLAbs (ScriptCodeGen scg, CompValu result, CompValu[] args)
		{
			ScriptMyLabel itsPosLabel = scg.ilGen.DefineLabel ("llAbstemp");

			result.PopPre (scg);
			args[0].PushVal (scg);
			scg.ilGen.Emit (OpCodes.Dup);
			scg.ilGen.Emit (OpCodes.Ldc_I4_0);
			scg.ilGen.Emit (OpCodes.Bge_S, itsPosLabel);
			scg.ilGen.Emit (OpCodes.Neg);
			scg.ilGen.MarkLabel (itsPosLabel);
			result.PopPost (scg, retType);
		}

		private void CodeGenStatic (ScriptCodeGen scg, CompValu result, CompValu[] args)
		{
			result.PopPre (scg);
			for (int i = 0; i < args.Length; i ++) {
				args[i].PushVal (scg);
			}
			scg.ilGen.Emit (OpCodes.Call, methInfo);
			result.PopPost (scg, retType);
		}

		private void CodeGenLLRound (ScriptCodeGen scg, CompValu result, CompValu[] args)
		{
			result.PopPre (scg);
			args[0].PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Ldc_I4, (int)System.MidpointRounding.AwayFromZero);
			scg.ilGen.Emit (OpCodes.Call, roundMethInfo);
			result.PopPost (scg, retType);
		}

		/**
		 * @brief Generate call to backend API function with a call to CheckRun() as well.
		 * @param scg = script being compiled
		 * @param result = where to place result (might be void)
		 * @param args = arguments to pass to API function
		 */
		private void CodeGenBEApi (ScriptCodeGen scg, CompValu result, CompValu[] args)
		{
			result.PopPre (scg);
			scg.ilGen.Emit (OpCodes.Ldarg_0);                              // scriptWrapper
			scg.ilGen.Emit (OpCodes.Ldfld, ScriptCodeGen.beAPIFieldInfo);  // scriptWrapper.beAPI = 'this' for the API function
			for (int i = 0; i < args.Length; i ++) {                       // push arguments
				args[i].PushVal (scg, argTypes[i]);                    // .. boxing/unboxing as needed
			}
			scg.ilGen.Emit (OpCodes.Call, methInfo);                       // call API function
			result.PopPost (scg, retType);                                 // pop result, boxing/unboxing as needed
			if (doCheckRun) {
				scg.EmitCallCheckRun ();                               // maybe call CheckRun()
			}
		}
	}
}
