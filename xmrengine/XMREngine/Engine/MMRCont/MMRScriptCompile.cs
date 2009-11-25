/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

/**
 * @brief Main program for the script compiler.
 */

using System;
using System.IO;

namespace MMR {

	public class ScriptCompile {

		/**
		 * @brief Compile a script to produce a .DLL file.
		 * @param scriptname = name of the script (unique per script)
		 * @param errorMessage = where to write error messages to
		 * @param sourceName = where to read script source from
		 * @param cSharpName = where to write temporary C# equivalent to
		 * @param binaryName = where to write .DLL file to
		 * @param source = null: read source from sourceName
		 *                 else: 'source' contains the whole script source
		 * @returns true: successful
		 *         false: failure
		 */
		public static bool Compile (string scriptname, 
		                            TokenErrorMessage errorMessage, 
		                            string sourceName, 
		                            string cSharpName, 
		                            string binaryName, 
		                            string source)
		{
			if (source == null) {
				Console.WriteLine ("READING SOURCE FILE: " + sourceName);
				FileStream sourceFile = File.OpenRead(sourceName);
				StreamReader sourceReader = new StreamReader (sourceFile);
				source = sourceReader.ReadToEnd();
				sourceReader.Close ();
			}

			Console.WriteLine ("TOKENIZING SOURCE FILE");
			TokenBegin tokenBegin = TokenBegin.Construct (errorMessage, sourceName, source);
			if (tokenBegin == null) {
				Console.WriteLine ("TOKENIZING ERROR");
				return false;
			}

			Console.WriteLine ("REDUCING SOURCE FILE");
			TokenScript tokenScript = ScriptReduce.Reduce (tokenBegin);
			if (tokenScript == null) {
				Console.WriteLine ("REDUCTION ERROR");
				return false;
			}

			bool ok = ScriptCodeGen.CodeGen (tokenScript, scriptname, cSharpName, binaryName);
			if (!ok) {
				Console.WriteLine ("CODEGEN ERROR");
				return false;
			}
			Console.WriteLine ("COMPILATION SUCCESSFUL");
			return true;
		}
	}
}
