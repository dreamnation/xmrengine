/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace OpenSim.Region.ScriptEngine.XMREngine {

	public class InternalFuncDict : Dictionary<string, TokenDeclFunc> {

		/**
		 * @brief build dictionary of internal functions from an interface.
		 * @param iface = interface with function definitions
		 * @param inclSig = false: key is just function name, no overloading possible, ie, <name>
		 *                   true: key also contains LSL-style argument types, ie, <name>(<arg1type>,<arg2type,...)
		 * @returns dictionary of function definition tokens
		 */
		public InternalFuncDict (Type iface, bool inclSig)
		{
			/*
			 * Loop through list of all methods declared in the interface.
			 */
			System.Reflection.MethodInfo[] ifaceMethods = iface.GetMethods ();
			foreach (System.Reflection.MethodInfo ifaceMethod in ifaceMethods) {
				string key = ifaceMethod.Name;

				/*
				 * Only do ones that begin with lower-case letters...
				 * as any others can't be referenced by scripts
				 */
				if ((ifaceMethod.Name[0] < 'a') || (ifaceMethod.Name[0] > 'z')) continue;

				try {

					/*
					 * Create a corresponding TokenDeclFunc struct.
					 */
					TokenDeclFunc declFunc = new TokenDeclFunc (null);
					declFunc.retType = TokenType.FromSysType (null, ifaceMethod.ReturnType);
					declFunc.funcName = new TokenName (null, ifaceMethod.Name);
					System.Reflection.ParameterInfo[] parameters = ifaceMethod.GetParameters ();
					TokenArgDecl argDecl = new TokenArgDecl (null);
					argDecl.names = new TokenName[parameters.Length];
					argDecl.types = new TokenType[parameters.Length];
					key = declFunc.funcName.val;
					if (inclSig) key += "(";
					for (int i = 0; i < parameters.Length; i++) {
						System.Reflection.ParameterInfo param = parameters[i];
						argDecl.names[i] = new TokenName (null, param.Name);
						argDecl.types[i] = TokenType.FromSysType (null, param.ParameterType);
						if (inclSig) {
							if (i > 0) key += ",";
							key += argDecl.types[i].ToString ();
						}
					}
					declFunc.argDecl = argDecl;
					if (inclSig) key += ")";

					/*
					 * Add the TokenDeclFunc struct to the dictionary.
					 * Key = name(arg1type,arg2type,...) or just name
					 * ... where the types are the LSL-style type such as 'key', 'rotation', etc.
					 */
					this.Add (key, declFunc);
				} catch (Exception except) {

					string msg = except.ToString ();
					int i = msg.IndexOf ("\n");
					if (i > 0) msg = msg.Substring (0, i);
					Console.WriteLine ("InternalFuncDict*: {0}:     {1}", key, msg);

					///??? IGNORE ANY THAT FAIL - LIKE UNRECOGNIZED TYPE ???///
					///???                          and OVERLOADED NAMES ???///
				}
			}
		}
	}
}
