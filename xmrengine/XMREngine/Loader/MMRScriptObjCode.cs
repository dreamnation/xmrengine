/***************************************************\
 *  COPYRIGHT 2010, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using OpenSim.Region.ScriptEngine.XMREngine.Loader;
using System.Collections.Generic;
using System.Reflection;

namespace OpenSim.Region.ScriptEngine.XMREngine
{
	/*
	 * This object represents the output of the compilation.
	 * Once the compilation is complete, its contents should be
	 * considered 'read-only', so it can be shared among multiple
	 * instances of the script.
	 *
	 * It gets created by ScriptCodeGen.
	 * It gets used by ScriptWrapper to create script instances.
	 */
	public class ScriptObjCode
	{
		public int compiledVersion;     // what COMPILED_VERSION was when it got compiled

		public int numGblArrays;        // number of array global variables it has
		public int numGblFloats;        // number of float global variables it has
		public int numGblIntegers;      // number of integer global variables it has
		public int numGblLists;         // number of list global variables it has
		public int numGblRotations;     // number of rotation global variables it has
		public int numGblStrings;       // number of string global variables it has
		public int numGblVectors;       // number of vector global variables it has

		public string[] stateNames;     // convert state number to corresponding string

		public ScriptEventHandler[,] scriptEventHandlerTable;
		                                // entrypoints to all event handler functions
		                                // 1st subscript = state code number (0=default)
		                                // 2nd subscript = event code number
		                                // null entry means no handler defined for that state,event

		public Dictionary<string, MethodInfo> dynamicMethods;
		                                // all dyanmic methods that could be encountered by checkpointing
	}
}
