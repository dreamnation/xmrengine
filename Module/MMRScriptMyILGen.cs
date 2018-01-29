/***************************************************\
 *  COPYRIGHT 2012, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using System;
using System.Reflection;
using System.Reflection.Emit;

namespace OpenSim.Region.ScriptEngine.XMREngine
{
	public interface ScriptMyILGen
	{
		string methName { get; }
		ScriptMyLocal DeclareLocal (Type type, string name);
		ScriptMyLabel DefineLabel (string name);
		void BeginExceptionBlock ();
		void BeginCatchBlock (Type excType);
		void BeginFinallyBlock ();
		void EndExceptionBlock ();
		void Emit (Token errorAt, OpCode opcode);
		void Emit (Token errorAt, OpCode opcode, FieldInfo field);
		void Emit (Token errorAt, OpCode opcode, ScriptMyLocal myLocal);
		void Emit (Token errorAt, OpCode opcode, Type type);
		void Emit (Token errorAt, OpCode opcode, ScriptMyLabel myLabel);
		void Emit (Token errorAt, OpCode opcode, ScriptMyLabel[] myLabels);
		void Emit (Token errorAt, OpCode opcode, ScriptObjWriter method);
		void Emit (Token errorAt, OpCode opcode, MethodInfo method);
		void Emit (Token errorAt, OpCode opcode, ConstructorInfo ctor);
		void Emit (Token errorAt, OpCode opcode, double value);
		void Emit (Token errorAt, OpCode opcode, float value);
		void Emit (Token errorAt, OpCode opcode, int value);
		void Emit (Token errorAt, OpCode opcode, string value);
		void MarkLabel (ScriptMyLabel myLabel);
	}

	/**
	 * @brief One of these per label defined in the function.
	 */
	public class ScriptMyLabel {
		public string name;
		public int number;

		public GraphNodeMarkLabel whereAmI;
		public Type[] stackDepth;
		public bool[] stackBoxeds;
	}

	/**
	 * @brief One of these per local variable defined in the function.
	 */
	public class ScriptMyLocal {
		public string name;
		public Type type;
		public int number;

		public bool isReferenced;
	}
}
