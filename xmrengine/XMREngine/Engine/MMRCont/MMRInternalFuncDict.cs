/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace MMR {

	public class InternalFuncDict : Dictionary<string, TokenDeclFunc> {

		/**
		 * @brief build dictionary of internal functions from an interface.
		 * @param iface = interface with function definitions
		 * @returns dictionary of function definition tokens
		 */
		public InternalFuncDict (Type iface)
		{
			/*
			 * Loop through list of all methods declared in the interface.
			 */
			System.Reflection.MethodInfo[] ifaceMethods = iface.GetMethods ();
			foreach (System.Reflection.MethodInfo ifaceMethod in ifaceMethods) {

				/*
				 * Only do ones that begin with lower-case letters...
				 * as any others can't be referenced by scripts
				 */
				if ((ifaceMethod.Name[0] < 'a') || (ifaceMethod.Name[0] > 'z')) continue;

				///??? skip duplicates == overloading ???///
				if (this.ContainsKey (ifaceMethod.Name)) continue;

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
				for (int i = 0; i < parameters.Length; i++) {
					System.Reflection.ParameterInfo param = parameters[i];
					argDecl.names[i] = new TokenName (null, param.Name);
					argDecl.types[i] = TokenType.FromSysType (null, param.ParameterType);
				}
				declFunc.argDecl = argDecl;

				/*
				 * Add the TokenDeclFunc struct to the dictionary.
				 */
				this.Add (declFunc.funcName.val, declFunc);
			}
		}
	}
}
