/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Region.ScriptEngine.XMREngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

/**
 * @brief Compute values used during code generation to keep track of where computed values are stored.
 */

namespace OpenSim.Region.ScriptEngine.XMREngine
{

	/**
	 * @brief Location of a value
	 *        Includes constants, expressions and temp variables.
	 */
	public abstract class CompValu {
		public TokenType type;     // type of the value and where in the source is was used
		public bool isFinal;       // true iff value cannot be changed by any side effects
		                           // - ie, PushVal() is idempotent (no calls, autoincrements, assignment, etc)
		                           // - temps do not change because we allocate a new one each time
		                           // - constants never change because they are constant
		                           // - expressions consisting of all isFinal operands are final

		public CompValu (TokenType type)
		{
			this.type    = type;
			this.isFinal = true;
		}

		public Type SysType() {
			return (type.lslBoxing != null) ? type.lslBoxing : type.typ;
		}

		public CompValu (TokenType type, bool isFinal)
		{
			this.type    = type;
			this.isFinal = isFinal;
		}

		// emit code to push value onto stack
		public void PushVal (ScriptCodeGen scg, TokenType stackType)
		{
			this.PushVal (scg, stackType, false);
		}
		public void PushVal (ScriptCodeGen scg, TokenType stackType, bool explicitAllowed)
		{
			this.PushVal (scg);
			TypeCast.CastTopOfStack (scg, this.type, stackType, explicitAllowed);
		}
		public abstract void PushVal (ScriptCodeGen scg);
		public abstract void PushByRef (ScriptCodeGen scg);

		// emit code to pop value from stack
		public void PopPost (ScriptCodeGen scg, TokenType stackType)
		{
			TypeCast.CastTopOfStack (scg, stackType, this.type, false);
			this.PopPost (scg);
		}
		public virtual void PopPre (ScriptCodeGen scg) { }  // call this before pushing value to be popped
		public abstract void PopPost (ScriptCodeGen scg);   // call this after pushing value to be popped
	}

	// The value is kept in an array element
	public class CompValuArEle : CompValu {
		CompValu arr;
		CompValu idx;

		public CompValuArEle (TokenType type, CompValu arr, CompValu idx) : base (type)
		{
			this.arr = arr;
			this.idx = idx;
		}
		public override void PushVal (ScriptCodeGen scg)
		{
			arr.PushVal (scg);
			idx.PushVal (scg);
			if (type is TokenTypeFloat) {
				scg.ilGen.Emit (OpCodes.Ldelem_R4);
			} else if (type is TokenTypeInt) {
				scg.ilGen.Emit (OpCodes.Ldelem_I4);
			} else {
				scg.ilGen.Emit (OpCodes.Ldelem, SysType());
			}
		}
		public override void PushByRef (ScriptCodeGen scg)
		{
			arr.PushVal (scg);
			idx.PushVal (scg);
			scg.ilGen.Emit (OpCodes.Ldelema, SysType());
		}
		public override void PopPre (ScriptCodeGen scg)
		{
			arr.PushVal (scg);
			idx.PushVal (scg);
		}
		public override void PopPost (ScriptCodeGen scg)
		{
			if (type is TokenTypeFloat) {
				scg.ilGen.Emit (OpCodes.Stelem_R4);
			} else if (type is TokenTypeInt) {
				scg.ilGen.Emit (OpCodes.Stelem_I4);
			} else {
				scg.ilGen.Emit (OpCodes.Stelem, SysType());
			}
		}
	}

	// The value is kept in the current function's argument list
	public class CompValuArg : CompValu {
		public int index;

		public CompValuArg (TokenType type, int index) : base (type)
		{
			this.index = index;
		}
		public override void PushVal (ScriptCodeGen scg)
		{
			scg.ilGen.Emit (OpCodes.Ldarg, index);
		}
		public override void PushByRef (ScriptCodeGen scg)
		{
			scg.ilGen.Emit (OpCodes.Ldarga, index);
		}
		public override void PopPost (ScriptCodeGen scg)
		{
			scg.ilGen.Emit (OpCodes.Starg, index);
		}
	}

	// The value is kept in a struct/class field
	public class CompValuField : CompValu {
		CompValu obj;
		FieldInfo field;

		public CompValuField (TokenType type, CompValu obj, FieldInfo field) : base (type)
		{
			this.obj   = obj;
			this.field = field;
		}
		public override void PushVal (ScriptCodeGen scg)
		{
			if (field.ReflectedType.IsValueType) {
				obj.PushByRef (scg);
			} else {
				obj.PushVal (scg);
			}
			scg.ilGen.Emit (OpCodes.Ldfld, field);
		}
		public override void PushByRef (ScriptCodeGen scg)
		{
			if (field.ReflectedType.IsValueType) {
				obj.PushByRef (scg);
			} else {
				obj.PushVal (scg);
			}
			scg.ilGen.Emit (OpCodes.Ldflda, field);
		}
		public override void PopPre (ScriptCodeGen scg)
		{
			if (field.ReflectedType.IsValueType) {
				obj.PushByRef (scg);
			} else {
				obj.PushVal (scg);
			}
		}
		public override void PopPost (ScriptCodeGen scg)
		{
			scg.ilGen.Emit (OpCodes.Stfld, field);
		}
	}

	// The value is a float constant
	public class CompValuFloat : CompValu {
		public float x;

		public CompValuFloat (TokenType type, float x) : base (type)
		{
			if (!(this.type is TokenTypeFloat)) {
				this.type = new TokenTypeFloat (this.type);
			}
			this.x = x;
		}
		public override void PushVal (ScriptCodeGen scg)
		{
			scg.ilGen.Emit (OpCodes.Ldc_R4, x);
		}
		public override void PushByRef (ScriptCodeGen scg)
		{
			throw new Exception ("cannot get float address");
		}
		public override void PopPost (ScriptCodeGen scg)
		{
			throw new Exception ("cannot store into float");
		}
	}

	// The value is in a script-global variable = ScriptModule instance variable
	public class CompValuGlobal : CompValu {
		public FieldInfo field;
		public int index;

		private static FieldInfo gblArraysFieldInfo    = typeof (ScriptWrapper).GetField ("gblArrays");
		private static FieldInfo gblFloatsFieldInfo    = typeof (ScriptWrapper).GetField ("gblFloats");
		private static FieldInfo gblIntegersFieldInfo  = typeof (ScriptWrapper).GetField ("gblIntegers");
		private static FieldInfo gblListsFieldInfo     = typeof (ScriptWrapper).GetField ("gblLists");
		private static FieldInfo gblRotationsFieldInfo = typeof (ScriptWrapper).GetField ("gblRotations");
		private static FieldInfo gblStringsFieldInfo   = typeof (ScriptWrapper).GetField ("gblStrings");
		private static FieldInfo gblVectorsFieldInfo   = typeof (ScriptWrapper).GetField ("gblVectors");

		public CompValuGlobal (TokenDeclVar declVar, ScriptObjCode scriptObjCode) : base (declVar.type)
		{
			if (type is TokenTypeArray) {
				this.field = gblArraysFieldInfo;
				this.index = scriptObjCode.numGblArrays ++;
			}
			if (type is TokenTypeFloat) {
				this.field = gblFloatsFieldInfo;
				this.index = scriptObjCode.numGblFloats ++;
			}
			if (type is TokenTypeInt) {
				this.field = gblIntegersFieldInfo;
				this.index = scriptObjCode.numGblIntegers ++;
			}
			if (type is TokenTypeList) {
				this.field = gblListsFieldInfo;
				this.index = scriptObjCode.numGblLists ++;
			}
			if (type is TokenTypeRot) {
				this.field = gblRotationsFieldInfo;
				this.index = scriptObjCode.numGblRotations ++;
			}
			if (type is TokenTypeStr) {
				this.field = gblStringsFieldInfo;
				this.index = scriptObjCode.numGblStrings ++;
			}
			if (type is TokenTypeVec) {
				this.field = gblVectorsFieldInfo;
				this.index = scriptObjCode.numGblVectors ++;
			}
			if (this.field == null) {
				throw new Exception ("unsupported type " + type.GetType ().ToString ());
			}
		}
		public override void PushVal (ScriptCodeGen scg)
		{
			scg.ilGen.Emit (OpCodes.Ldarg_0);            // scriptWrapper
			scg.ilGen.Emit (OpCodes.Ldfld, field);       // scriptWrapper.gbl<Type>s
			scg.PushConstantI4 (index);
			if (type is TokenTypeFloat) {
				scg.ilGen.Emit (OpCodes.Ldelem_R4);
			} else if (type is TokenTypeInt) {
				scg.ilGen.Emit (OpCodes.Ldelem_I4);
			} else {
				scg.ilGen.Emit (OpCodes.Ldelem, SysType());
			}
		}
		public override void PushByRef (ScriptCodeGen scg)
		{
			scg.ilGen.Emit (OpCodes.Ldarg_0);            // scriptWrapper
			scg.ilGen.Emit (OpCodes.Ldfld, field);       // scriptWrapper.gbl<Type>s
			scg.PushConstantI4 (index);
			scg.ilGen.Emit (OpCodes.Ldelema, SysType());
		}
		public override void PopPre (ScriptCodeGen scg)
		{
			scg.ilGen.Emit (OpCodes.Ldarg_0);            // scriptWrapper
			scg.ilGen.Emit (OpCodes.Ldfld, field);       // scriptWrapper.gbl<Type>s
			scg.PushConstantI4 (index);
		}
		public override void PopPost (ScriptCodeGen scg)
		{
			if (type is TokenTypeFloat) {
				scg.ilGen.Emit (OpCodes.Stelem_R4);
			} else if (type is TokenTypeInt) {
				scg.ilGen.Emit (OpCodes.Stelem_I4);
			} else {
				scg.ilGen.Emit (OpCodes.Stelem, SysType());
			}
		}
	}

	// The value is an integer constant
	public class CompValuInteger : CompValu {
		public int x;

		public CompValuInteger (TokenType type, int x) : base (type)
		{
			if (!(this.type is TokenTypeInt)) {
				this.type = new TokenTypeInt (this.type);
			}
			this.x = x;
		}
		public override void PushVal (ScriptCodeGen scg)
		{
			scg.PushConstantI4 (x);
		}
		public override void PushByRef (ScriptCodeGen scg)
		{
			throw new Exception ("cannot get integer address");
		}
		public override void PopPost (ScriptCodeGen scg)
		{
			throw new Exception ("cannot store into integer");
		}
	}

	// The value is a null
	public class CompValuNull : CompValu {
		public CompValuNull (TokenType type) : base (type) { }
		public override void PushVal (ScriptCodeGen scg)
		{
			scg.ilGen.Emit (OpCodes.Ldnull);
		}
		public override void PushByRef (ScriptCodeGen scg)
		{
			throw new Exception ("cannot get null address");
		}
		public override void PopPost (ScriptCodeGen scg)
		{
			throw new Exception ("cannot store into null");
		}
	}

	// The value is a rotation
	public class CompValuRot : CompValu {
		public CompValu x;
		public CompValu y;
		public CompValu z;
		public CompValu w;

		private static ConstructorInfo lslRotConstructorInfo = typeof (LSL_Rotation).GetConstructor (new Type[] { typeof (float), typeof (float), typeof (float), typeof (float) });

		public CompValuRot (TokenType type, CompValu x, CompValu y, CompValu z, CompValu w) :
				base (type)
		{
			if (!(type is TokenTypeRot)) {
				this.type = new TokenTypeRot (type);
			}
			this.x = x;
			this.y = y;
			this.z = z;
			this.w = w;
		}
		public override void PushVal (ScriptCodeGen scg)
		{
			this.x.PushVal (scg, new TokenTypeFloat (this.x.type));
			this.y.PushVal (scg, new TokenTypeFloat (this.y.type));
			this.z.PushVal (scg, new TokenTypeFloat (this.z.type));
			this.w.PushVal (scg, new TokenTypeFloat (this.w.type));
			scg.ilGen.Emit (OpCodes.Newobj, lslRotConstructorInfo);
		}
		public override void PushByRef (ScriptCodeGen scg)
		{
			throw new Exception ("cannot get rotation address");
		}
		public override void PopPost (ScriptCodeGen scg)
		{
			throw new Exception ("cannot store into rotation");
		}
	}

	// The value is in a static field of a class
	public class CompValuSField : CompValu {
		private FieldInfo field;

		public CompValuSField (TokenType type, FieldInfo field) : base (type)
		{
			this.field = field;
		}
		public override void PushVal (ScriptCodeGen scg)
		{
			if ((field.Attributes & FieldAttributes.Literal) == 0) {
				scg.ilGen.Emit (OpCodes.Ldsfld, field);
				return;
			}
			if (field.FieldType == typeof (LSL_Rotation)) {
				LSL_Rotation rot = (LSL_Rotation)field.GetValue (null);
				scg.ilGen.Emit (OpCodes.Ldc_R8, rot.x);
				scg.ilGen.Emit (OpCodes.Ldc_R8, rot.y);
				scg.ilGen.Emit (OpCodes.Ldc_R8, rot.z);
				scg.ilGen.Emit (OpCodes.Ldc_R8, rot.s);
				scg.ilGen.Emit (OpCodes.Newobj, ScriptCodeGen.lslRotationConstructorInfo);
				return;
			}
			if (field.FieldType == typeof (LSL_Vector)) {
				LSL_Vector vec = (LSL_Vector)field.GetValue (null);
				scg.ilGen.Emit (OpCodes.Ldc_R8, vec.x);
				scg.ilGen.Emit (OpCodes.Ldc_R8, vec.y);
				scg.ilGen.Emit (OpCodes.Ldc_R8, vec.z);
				scg.ilGen.Emit (OpCodes.Newobj, ScriptCodeGen.lslRotationConstructorInfo);
				return;
			}
			if (field.FieldType == typeof (string)) {
				string str = (string)field.GetValue (null);
				scg.ilGen.Emit (OpCodes.Ldstr, str);
				return;
			}
			throw new Exception ("unsupported literal type " + field.FieldType.Name);
		}
		public override void PushByRef (ScriptCodeGen scg)
		{
			if ((field.Attributes & FieldAttributes.Literal) != 0) {
				throw new Exception ("can't write a constant");
			}
			scg.ilGen.Emit (OpCodes.Ldflda, field);
		}
		public override void PopPre (ScriptCodeGen scg)
		{
		}
		public override void PopPost (ScriptCodeGen scg)
		{
			if ((field.Attributes & FieldAttributes.Literal) != 0) {
				throw new Exception ("can't write a constant");
			}
			scg.ilGen.Emit (OpCodes.Stsfld, field);
		}
	}

	// The value is a string constant
	public class CompValuString : CompValu {
		public string x;

		public CompValuString (TokenType type, string x) : base (type)
		{
			if (!(this.type is TokenTypeStr)) {
				this.type = new TokenTypeStr (this.type);
			}
			this.x = x;
		}
		public override void PushVal (ScriptCodeGen scg)
		{
			scg.ilGen.Emit (OpCodes.Ldstr, x);
		}
		public override void PushByRef (ScriptCodeGen scg)
		{
			throw new Exception ("cannot get string address");
		}
		public override void PopPost (ScriptCodeGen scg)
		{
			throw new Exception ("cannot store into string");
		}
	}

	// The value is kept in a temp local variable
	public class CompValuTemp : CompValu {
		private ScriptMyLocal localBuilder;

		private static ulong num = 0;

		public CompValuTemp (TokenType type, string name, ScriptCodeGen scg) : base (type)
		{
			if (name == null) {
				name = "__tmp_" + (++ num);
			}
			this.localBuilder = scg.ilGen.DeclareLocal (SysType(), name);
		}
		public override void PushVal (ScriptCodeGen scg)
		{
			scg.ilGen.Emit (OpCodes.Ldloc, localBuilder);
		}
		public override void PushByRef (ScriptCodeGen scg)
		{
			scg.ilGen.Emit (OpCodes.Ldloca, localBuilder);
		}
		public override void PopPost (ScriptCodeGen scg)
		{
			scg.ilGen.Emit (OpCodes.Stloc, localBuilder);
		}
	}

	// The value is a vector
	public class CompValuVec : CompValu {
		public CompValu x;
		public CompValu y;
		public CompValu z;

		private static ConstructorInfo lslVecConstructorInfo = typeof (LSL_Vector).GetConstructor (new Type[] { typeof (float), typeof (float), typeof (float) });

		public CompValuVec (TokenType type, CompValu x, CompValu y, CompValu z) : base (type)
		{
			if (!(type is TokenTypeVec)) {
				this.type = new TokenTypeVec (type);
			}
			this.x = x;
			this.y = y;
			this.z = z;
		}
		public override void PushVal (ScriptCodeGen scg)
		{
			this.x.PushVal (scg, new TokenTypeFloat (this.x.type));
			this.y.PushVal (scg, new TokenTypeFloat (this.y.type));
			this.z.PushVal (scg, new TokenTypeFloat (this.z.type));
			scg.ilGen.Emit (OpCodes.Newobj, lslVecConstructorInfo);
		}
		public override void PushByRef (ScriptCodeGen scg)
		{
			throw new Exception ("cannot get vector address");
		}
		public override void PopPost (ScriptCodeGen scg)
		{
			throw new Exception ("cannot store into vector");
		}
	}

	// Used to indicate value will be discarded (eg, where to put return value from a call)
	public class CompValuVoid : CompValu {
		public CompValuVoid (Token token) : base (null)
		{
			if (token is TokenTypeVoid) {
				this.type = (TokenTypeVoid)token;
			} else {
				this.type = new TokenTypeVoid (type);
			}
		}
		public override void PushVal (ScriptCodeGen scg) { }
		public override void PushByRef (ScriptCodeGen scg)
		{
			throw new Exception ("cannot get void address");
		}
		public override void PopPost (ScriptCodeGen scg) { }
	}
}
