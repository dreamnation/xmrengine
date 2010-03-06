/***************************************************\
 *  COPYRIGHT 2010, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

/**
 * @brief Wrapper class for ILGenerator
 *        It can write out debug output.
 */

namespace OpenSim.Region.ScriptEngine.XMREngine
{

	public class ScriptMyILGen
	{
		private ILGenerator realILGen;
		private MethodInfo monoGetCurrentOffset;
		private object[] realILGenArg;
		private StreamWriter debug;

		public ScriptMyILGen (DynamicMethod method, StreamWriter debug)
		{
			this.debug     = debug;
			this.realILGen = method.GetILGenerator ();
			if (debug != null) {
				debug.WriteLine ("");
				debug.Write (method.ReturnType.Name + " " + method.Name + "(");
				ParameterInfo[] parms = method.GetParameters ();
				for (int i = 0; i < parms.Length; i ++) {
					if (i > 0) debug.Write (", ");
					debug.Write (parms[i].ParameterType.Name + " " + parms[i].Name);
				}
				debug.WriteLine (")");
				monoGetCurrentOffset = typeof (ILGenerator).GetMethod ("Mono_GetCurrentOffset",
						BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, 
						new Type[] { typeof (ILGenerator) }, null);
				realILGenArg = new object[] { realILGen };
			}
		}

		public ScriptMyLocal DeclareLocal (Type type, string name)
		{
			ScriptMyLocal myLocal = new ScriptMyLocal ();
			myLocal.type = type;
			myLocal.name = name;
			myLocal.realLocal = realILGen.DeclareLocal (type);
			return myLocal;
		}

		public ScriptMyLabel DefineLabel (string name)
		{
			ScriptMyLabel myLabel = new ScriptMyLabel ();
			myLabel.name = name;
			myLabel.realLabel = realILGen.DefineLabel ();
			return myLabel;
		}

		public void Emit (OpCode opcode)
		{
			if (debug != null) {
				debug.WriteLine (OpcodeString (opcode));
			}
			realILGen.Emit (opcode);
		}

		public void Emit (OpCode opcode, FieldInfo field)
		{
			if (opcode == OpCodes.Ldloc) {
				throw new Exception ("can't ldloc field " + field.FieldType.Name + ":" + field.Name);
			}
			if (debug != null) {
				debug.WriteLine (OpcodeString (opcode) + "  " + 
				                 field.ReflectedType.Name + ":" + field.Name + " -> " + 
				                 field.FieldType.Name + "  (field)");
			}
			realILGen.Emit (opcode, field);
		}

		public void Emit (OpCode opcode, ScriptMyLocal myLocal)
		{
			if (debug != null) {
				debug.WriteLine (OpcodeString (opcode) + "  " + ":" + myLocal.name + " -> " + 
				                 myLocal.type.Name + "  (local)");
			}
			realILGen.Emit (opcode, myLocal.realLocal);
		}

		public void Emit (OpCode opcode, Type type)
		{
			if ((opcode == OpCodes.Castclass) && type.IsValueType) {
				throw new Exception ("can't cast to value type " + type.Name);
			}
			if (debug != null) {
				debug.WriteLine (OpcodeString (opcode) + "  " + type.Name + "  (type)");
			}
			realILGen.Emit (opcode, type);
		}

		public void Emit (OpCode opcode, ScriptMyLabel myLabel)
		{
			if (debug != null) {
				debug.WriteLine (OpcodeString (opcode) + "  " + myLabel.name);
			}
			realILGen.Emit (opcode, myLabel.realLabel);
		}

		public void Emit (OpCode opcode, MethodInfo method)
		{
			if (debug != null) {
				StringBuilder sb = new StringBuilder ();

				sb.Append (OpcodeString (opcode));
				sb.Append ("  ");
				if (method.ReflectedType != null) {
					sb.Append (method.ReflectedType.Name);
				}
				sb.Append (":");
				sb.Append (method.Name);
				sb.Append ("(");
				if (!method.IsStatic) sb.Append ("<this>");

				ParameterInfo[] parms = method.GetParameters ();
				for (int i = 0; i < parms.Length; i ++) {
					if ((i > 0) || !method.IsStatic) sb.Append (",");
					sb.Append (parms[i].ParameterType.Name);
				}
				sb.Append (") -> ");
				sb.Append (method.ReturnType.Name);

				debug.WriteLine (sb.ToString ());
			}
			realILGen.Emit (opcode, method);
		}

		public void Emit (OpCode opcode, ConstructorInfo constructor)
		{
			if (debug != null) {
				debug.WriteLine (OpcodeString (opcode) + "  " + constructor.ReflectedType.Name + "()");
			}
			realILGen.Emit (opcode, constructor);
		}

		public void Emit (OpCode opcode, double value)
		{
			if (debug != null) {
				debug.WriteLine (OpcodeString (opcode) + "  " + value.ToString () + "  (double)");
			}
			realILGen.Emit (opcode, value);
		}

		public void Emit (OpCode opcode, float value)
		{
			if (debug != null) {
				debug.WriteLine (OpcodeString (opcode) + "  " + value.ToString () + "  (float)");
			}
			realILGen.Emit (opcode, value);
		}

		public void Emit (OpCode opcode, int value)
		{
			if (debug != null) {
				debug.WriteLine (OpcodeString (opcode) + "  " + value.ToString () + "  (int)");
			}
			realILGen.Emit (opcode, value);
		}

		public void Emit (OpCode opcode, string value)
		{
			if (debug != null) {
				debug.WriteLine (OpcodeString (opcode) + "  \"" + value + "\"");
			}
			realILGen.Emit (opcode, value);
		}

		public void MarkLabel (ScriptMyLabel myLabel)
		{
			if (debug != null) {
				debug.WriteLine (myLabel.name + ":");
			}
			realILGen.MarkLabel (myLabel.realLabel);
		}

		private string OpcodeString (OpCode opcode)
		{
			StringBuilder sb = new StringBuilder ();
			if (monoGetCurrentOffset != null) {
				int len = (int)monoGetCurrentOffset.Invoke (null, realILGenArg);
				sb.Append ("  ");
				sb.Append (len.ToString ("X4"));
			}
			sb.Append ("  ");
			string st = opcode.ToString ();
			sb.Append (st.PadRight (10));
			return sb.ToString ();
		}
	}

	public class ScriptMyLabel {
		public string name;
		public Label realLabel;
	}

	public class ScriptMyLocal {
		public string name;
		public Type type;
		public LocalBuilder realLocal;
	}
}
