/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using System;
using System.Collections.Generic;
using System.Reflection;
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
 * @brief Backend API call definitions.
 */
namespace MMR
{
	public class InlineFunction {
		public string signature;      // name(arglsltypes,...)
		public Type retType;          // return value system type (typeof (void) for void)
		public TokenType[] argTypes;  // argument types
		public string code;           // code to be inlined

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
		 * following variables avaialble:
		 *    __sm = the current ScriptModule instance, which inherits from ScriptWrapper
		 *    __sc = __sm.continuation (useful for calling __sc.CheckRun())
		 * Also, any substituted parameters or return value identifiers will always begin
		 * with two underscores (__), so any local variables created for internal use by
		 * the inline code must begin with something other than __.
		 */
		public static Dictionary<string, InlineFunction> CreateDictionary ()
		{
			Dictionary<string, InlineFunction> ifd = new Dictionary<string, InlineFunction> ();

			/*
			 * Mono generates an FPU instruction for many math calls.
			 */
			new InlineFunction (ifd, "llAbs(integer)",       typeof (int),   "{0} < 0 ? -{0} : {0}");
			new InlineFunction (ifd, "llAcos(float)",        typeof (float), "System.Math.Acos({0})");
			new InlineFunction (ifd, "llAsin(float)",        typeof (float), "System.Math.Asin({0})");
			new InlineFunction (ifd, "llAtan2(float,float)", typeof (float), "System.Math.Atan2({0},{1})");
			new InlineFunction (ifd, "llCeil(float)",        typeof (float), "System.Math.Ceiling({0})");
			new InlineFunction (ifd, "llCos(float)",         typeof (float), "System.Math.Cos({0})");
			new InlineFunction (ifd, "llFabs(float)",        typeof (float), "System.Math.Abs({0})");
			new InlineFunction (ifd, "llFloor(float)",       typeof (float), "System.Math.Floor({0})");
			new InlineFunction (ifd, "llLog(float)",         typeof (float), "System.Math.Log({0})");
			new InlineFunction (ifd, "llLog10(float)",       typeof (float), "System.Math.Log10({0})");
			new InlineFunction (ifd, "llPow(float,float)",   typeof (float), "System.Math.Pow({0},{1})");
			new InlineFunction (ifd, "llRound(float)",       typeof (float), "System.Math.Round({0},System.MidpointRounding.AwayFromZero)");
			new InlineFunction (ifd, "llSin(float)",         typeof (float), "System.Math.Sin({0})");
			new InlineFunction (ifd, "llSqrt(float)",        typeof (float), "System.Math.Sqrt({0})");
			new InlineFunction (ifd, "llTan(float)",         typeof (float), "System.Math.Tan({0})");

			/*
			 * Finally for any API functions defined by ScriptBaseClass that are not overridden 
			 * by anything already defined above, create an inline definition to call it.
			 *
			 * We create statement-block forms like this for each:
			 *    {
			 *       {#} = __sm.beAPI.methodname({0},{1},...);
			 *       __sc.CheckRun();
			 *    }
			 */
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
					StringBuilder sig = new StringBuilder (key);
					sig.Append ('(');
					for (int i = 0; i < parameters.Length; i ++) {
						if (i > 0) sig.Append (',');
						sig.Append (TokenType.FromSysType (null, parameters[i].ParameterType).ToString ());
					}
					sig.Append (')');
					key = sig.ToString ();

					/*
					 * If that signature isn't already in dictionary, add it.
					 */
					if (!ifd.ContainsKey (key)) {
						Type retType = ifaceMethod.ReturnType;
						if (retType == typeof (LSL_Float))   retType = typeof (float);
						if (retType == typeof (LSL_Integer)) retType = typeof (int);
						if (retType == typeof (LSL_String))  retType = typeof (string);
						StringBuilder code = new StringBuilder ("{ ");
						if (ifaceMethod.ReturnType != typeof (void)) {
							code.Append ("{#} = ");
						}
						if (retType == typeof (float)) {
							code.Append ("(float)");
						}
						code.Append ("__sm.beAPI.");
						code.Append (ifaceMethod.Name);
						code.Append ('(');
						for (int i = 0; i < parameters.Length; i ++) {
							if (i > 0) code.Append (',');
							code.Append ("{" + i.ToString () + "}");
						}
						code.Append (')');
						if (ifaceMethod.ReturnType == typeof (LSL_Float))   code.Append (".value");
						if (ifaceMethod.ReturnType == typeof (LSL_Integer)) code.Append (".value");
						code.Append ("; __sc.CheckRun(); }");
						new InlineFunction (ifd, key, retType, code.ToString ());
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
		 * @param code      = C# code (in either single-expression or statement-block form)
		 */
		private InlineFunction (Dictionary<string, InlineFunction> ifd, string signature, Type retType, string code)
		{
			int i, j, nargs;

			this.signature = signature;
			this.retType   = retType;
			this.code      = code;
			nargs = 0;
			for (i = signature.IndexOf ("("); i < signature.Length; i = j) {
				j = signature.IndexOf (",", ++ i);
				if (j < 0) j = signature.IndexOf (")", i);
				if (j <= i) break;
				nargs ++;
			}
			this.argTypes = new TokenType[nargs];
			nargs = 0;
			for (i = signature.IndexOf ("("); i < signature.Length; i = j) {
				j = signature.IndexOf (",", ++ i);
				if (j < 0) j = signature.IndexOf (")", i);
				if (j <= i) break;
				this.argTypes[nargs++] = TokenType.FromLSLType (null, signature.Substring (i, j - i));
			}
			ifd.Add (signature, this);
		}
	}
}
