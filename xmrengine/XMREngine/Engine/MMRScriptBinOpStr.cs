/***************************************************\
 *  COPYRIGHT 2010, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

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

namespace OpenSim.Region.ScriptEngine.XMREngine {

	/**
	 * @brief This class is used to catalog the code emit routines based on a key string
	 *        The key string has the two types (eg, "integer", "rotation") and the operator (eg, "*", "!=")
	 */
	public delegate void BinOpStrEmitBO (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result);
	public class BinOpStr {
		public Type outtype;           // type of result of computation
		public BinOpStrEmitBO emitBO;  // how to compute result
		public bool rmwOK;             // is the <operator>= form valid?

		public BinOpStr (Type outtype, BinOpStrEmitBO emitBO)
		{
			this.outtype = outtype;
			this.emitBO  = emitBO;
			this.rmwOK   = false;
		}

		public BinOpStr (Type outtype, BinOpStrEmitBO emitBO, bool rmwOK)
		{
			this.outtype = outtype;
			this.emitBO  = emitBO;
			this.rmwOK   = rmwOK;
		}

		private static TokenTypeBool  tokenTypeBool   = new TokenTypeBool  (null);
		private static TokenTypeFloat tokenTypeFloat  = new TokenTypeFloat (null);
		private static TokenTypeInt   tokenTypeInt    = new TokenTypeInt   (null);
		private static TokenTypeList  tokenTypeList   = new TokenTypeList  (null);
		private static TokenTypeRot   tokenTypeRot    = new TokenTypeRot   (null);
		private static TokenTypeStr   tokenTypeStr    = new TokenTypeStr   (null);
		private static TokenTypeVec   tokenTypeVec    = new TokenTypeVec   (null);

		private static MethodInfo stringAddStringMethInfo = ScriptCodeGen.GetStaticMethod (typeof (string), "Concat",  new Type[] { typeof (string), typeof (string) });
		private static MethodInfo stringCmpStringMethInfo = ScriptCodeGen.GetStaticMethod (typeof (string), "Compare", new Type[] { typeof (string), typeof (string) });

		private static MethodInfo infoMethListAddFloat = GetBinOpsMethod ("MethListAddFloat", new Type[] { typeof (LSL_List),     typeof (float)        });
		private static MethodInfo infoMethListAddInt   = GetBinOpsMethod ("MethListAddInt",   new Type[] { typeof (LSL_List),     typeof (int)          });
		private static MethodInfo infoMethListAddKey   = GetBinOpsMethod ("MethListAddKey",   new Type[] { typeof (LSL_List),     typeof (LSL_Key)      });
		private static MethodInfo infoMethListAddList  = GetBinOpsMethod ("MethListAddList",  new Type[] { typeof (LSL_List),     typeof (LSL_List)     });
		private static MethodInfo infoMethListAddRot   = GetBinOpsMethod ("MethListAddRot",   new Type[] { typeof (LSL_List),     typeof (LSL_Rotation) });
		private static MethodInfo infoMethListAddStr   = GetBinOpsMethod ("MethListAddStr",   new Type[] { typeof (LSL_List),     typeof (string)       });
		private static MethodInfo infoMethListAddVec   = GetBinOpsMethod ("MethListAddVec",   new Type[] { typeof (LSL_List),     typeof (LSL_Vector)   });
		private static MethodInfo infoMethFloatAddList = GetBinOpsMethod ("MethFloatAddList", new Type[] { typeof (float),        typeof (LSL_List)     });
		private static MethodInfo infoMethIntAddList   = GetBinOpsMethod ("MethIntAddList",   new Type[] { typeof (int),          typeof (LSL_List)     });
		private static MethodInfo infoMethKeyAddList   = GetBinOpsMethod ("MethKeyAddList",   new Type[] { typeof (LSL_Key),      typeof (LSL_List)     });
		private static MethodInfo infoMethRotAddList   = GetBinOpsMethod ("MethRotAddList",   new Type[] { typeof (LSL_Rotation), typeof (LSL_List)     });
		private static MethodInfo infoMethStrAddList   = GetBinOpsMethod ("MethStrAddList",   new Type[] { typeof (string),       typeof (LSL_List)     });
		private static MethodInfo infoMethVecAddList   = GetBinOpsMethod ("MethVecAddList",   new Type[] { typeof (LSL_Vector),   typeof (LSL_List)     });
		private static MethodInfo infoMethListEqList   = GetBinOpsMethod ("MethListEqList",   new Type[] { typeof (LSL_List),     typeof (LSL_List)     });
		private static MethodInfo infoMethListNeList   = GetBinOpsMethod ("MethListNeList",   new Type[] { typeof (LSL_List),     typeof (LSL_List)     });
		private static MethodInfo infoMethRotEqRot     = GetBinOpsMethod ("MethRotEqRot",     new Type[] { typeof (LSL_Rotation), typeof (LSL_Rotation) });
		private static MethodInfo infoMethRotNeRot     = GetBinOpsMethod ("MethRotNeRot",     new Type[] { typeof (LSL_Rotation), typeof (LSL_Rotation) });
		private static MethodInfo infoMethRotAddRot    = GetBinOpsMethod ("MethRotAddRot",    new Type[] { typeof (LSL_Rotation), typeof (LSL_Rotation) });
		private static MethodInfo infoMethRotSubRot    = GetBinOpsMethod ("MethRotSubRot",    new Type[] { typeof (LSL_Rotation), typeof (LSL_Rotation) });
		private static MethodInfo infoMethRotMulRot    = GetBinOpsMethod ("MethRotMulRot",    new Type[] { typeof (LSL_Rotation), typeof (LSL_Rotation) });
		private static MethodInfo infoMethRotDivRot    = GetBinOpsMethod ("MethRotDivRot",    new Type[] { typeof (LSL_Rotation), typeof (LSL_Rotation) });
		private static MethodInfo infoMethVecEqVec     = GetBinOpsMethod ("MethVecEqVec",     new Type[] { typeof (LSL_Vector),   typeof (LSL_Vector)   });
		private static MethodInfo infoMethVecNeVec     = GetBinOpsMethod ("MethVecNeVec",     new Type[] { typeof (LSL_Vector),   typeof (LSL_Vector)   });
		private static MethodInfo infoMethVecAddVec    = GetBinOpsMethod ("MethVecAddVec",    new Type[] { typeof (LSL_Vector),   typeof (LSL_Vector)   });
		private static MethodInfo infoMethVecSubVec    = GetBinOpsMethod ("MethVecSubVec",    new Type[] { typeof (LSL_Vector),   typeof (LSL_Vector)   });
		private static MethodInfo infoMethVecMulVec    = GetBinOpsMethod ("MethVecMulVec",    new Type[] { typeof (LSL_Vector),   typeof (LSL_Vector)   });
		private static MethodInfo infoMethVecModVec    = GetBinOpsMethod ("MethVecModVec",    new Type[] { typeof (LSL_Vector),   typeof (LSL_Vector)   });
		private static MethodInfo infoMethVecMulFloat  = GetBinOpsMethod ("MethVecMulFloat",  new Type[] { typeof (LSL_Vector),   typeof (float)        });
		private static MethodInfo infoMethFloatMulVec  = GetBinOpsMethod ("MethFloatMulVec",  new Type[] { typeof (float),        typeof (LSL_Vector)   });
		private static MethodInfo infoMethVecDivFloat  = GetBinOpsMethod ("MethVecDivFloat",  new Type[] { typeof (LSL_Vector),   typeof (float)        });
		private static MethodInfo infoMethVecMulInt    = GetBinOpsMethod ("MethVecMulInt",    new Type[] { typeof (LSL_Vector),   typeof (int)          });
		private static MethodInfo infoMethIntMulVec    = GetBinOpsMethod ("MethIntMulVec",    new Type[] { typeof (int),          typeof (LSL_Vector)   });
		private static MethodInfo infoMethVecDivInt    = GetBinOpsMethod ("MethVecDivInt",    new Type[] { typeof (LSL_Vector),   typeof (int)          });
		private static MethodInfo infoMethVecMulRot    = GetBinOpsMethod ("MethVecMulRot",    new Type[] { typeof (LSL_Vector),   typeof (LSL_Rotation) });
		private static MethodInfo infoMethVecDivRot    = GetBinOpsMethod ("MethVecDivRot",    new Type[] { typeof (LSL_Vector),   typeof (LSL_Rotation) });

		private static MethodInfo GetBinOpsMethod (string name, Type[] types)
		{
			return ScriptCodeGen.GetStaticMethod (typeof (BinOpStr), name, types);
		}

		/**
		 * @brief Create a dictionary for processing binary operators.
		 *        This tells us, for a given type, an operator and another type,
		 *        is the operation permitted, and if so, what is the type of the result?
		 * The key is <lefttype><opcode><righttype>,
		 *   where <lefttype> and <righttype> are strings returned by (TokenType...).ToString()
		 *   and <opcode> is string returned by (TokenKw...).ToString()
		 * The value is a BinOpStr struct giving the resultant type and a method to generate the code.
		 */
		public static Dictionary<string, BinOpStr> DefineBinOps ()
		{
			Dictionary<string, BinOpStr> bos = new Dictionary<string, BinOpStr> ();

			string[] booltypes = new string[] { "bool", "float", "integer", "key", "list", "string" };

			/*
			 * Get the && and || all out of the way...
			 * Simply cast their left and right operands to boolean then process.
			 */
			for (int i = 0; i < booltypes.Length; i ++) {
				for (int j = 0; j < booltypes.Length; j ++) {
					bos.Add (booltypes[i] + "&&" + booltypes[j], 
					         new BinOpStr (typeof (bool), BinOpStrAndAnd));
					bos.Add (booltypes[i] + "||" + booltypes[j], 
					         new BinOpStr (typeof (bool), BinOpStrOrOr));
				}
			}

			/*
			 * Pound through all the other combinations we support.
			 */

			// boolean : somethingelse
			DefineBinOpsBoolX (bos, "bool");
			DefineBinOpsBoolX (bos, "float");
			DefineBinOpsBoolX (bos, "integer");
			DefineBinOpsBoolX (bos, "key");
			DefineBinOpsBoolX (bos, "list");
			DefineBinOpsBoolX (bos, "string");

			// somethingelse : boolean
			DefineBinOpsXBool (bos, "float");
			DefineBinOpsXBool (bos, "integer");
			DefineBinOpsXBool (bos, "key");
			DefineBinOpsXBool (bos, "list");
			DefineBinOpsXBool (bos, "string");

			// float : somethingelse
			DefineBinOpsFloatX (bos, "float");
			DefineBinOpsFloatX (bos, "integer");

			// integer : float
			DefineBinOpsXFloat (bos, "integer");

			// anything else with integers
			DefineBinOpsInteger (bos);

			// key : somethingelse
			DefineBinOpsKeyX (bos, "key");
			DefineBinOpsKeyX (bos, "string");

			// string : key
			DefineBinOpsXKey (bos, "string");

			// things with lists
			DefineBinOpsList (bos);

			// things with rotations
			DefineBinOpsRotation (bos);

			// things with strings
			DefineBinOpsString (bos);

			// things with vectors
			DefineBinOpsVector (bos);

			// Contrary to some beliefs, scripts do things like string+integer and integer+string
			bos.Add ("bool+string",    new BinOpStr (typeof (string), BinOpStrStrAddStr));
			bos.Add ("float+string",   new BinOpStr (typeof (string), BinOpStrStrAddStr));
			bos.Add ("integer+string", new BinOpStr (typeof (string), BinOpStrStrAddStr));
			bos.Add ("string+bool",    new BinOpStr (typeof (string), BinOpStrStrAddStr));
			bos.Add ("string+float",   new BinOpStr (typeof (string), BinOpStrStrAddStr));
			bos.Add ("string+integer", new BinOpStr (typeof (string), BinOpStrStrAddStr));

			return bos;
		}

		private static void DefineBinOpsBoolX (Dictionary<string, BinOpStr> bos, string x)
		{
			bos.Add ("bool|"  + x, new BinOpStr (typeof (bool), BinOpStrBoolOrX));
			bos.Add ("bool^"  + x, new BinOpStr (typeof (bool), BinOpStrBoolXorX));
			bos.Add ("bool&"  + x, new BinOpStr (typeof (bool), BinOpStrBoolAndX));
			bos.Add ("bool==" + x, new BinOpStr (typeof (bool), BinOpStrBoolEqX));
			bos.Add ("bool!=" + x, new BinOpStr (typeof (bool), BinOpStrBoolNeX));
		}

		private static void DefineBinOpsXBool (Dictionary<string, BinOpStr> bos, string x)
		{
			bos.Add (x + "|bool",  new BinOpStr (typeof (bool), BinOpStrBoolOrX));
			bos.Add (x + "^bool",  new BinOpStr (typeof (bool), BinOpStrBoolXorX));
			bos.Add (x + "&bool",  new BinOpStr (typeof (bool), BinOpStrBoolAndX));
			bos.Add (x + "==bool", new BinOpStr (typeof (bool), BinOpStrBoolEqX));
			bos.Add (x + "!=bool", new BinOpStr (typeof (bool), BinOpStrBoolNeX));
		}

		private static void DefineBinOpsFloatX (Dictionary<string, BinOpStr> bos, string x)
		{
			bos.Add ("float==" + x, new BinOpStr (typeof (bool),  BinOpStrFloatEqX));
			bos.Add ("float!=" + x, new BinOpStr (typeof (bool),  BinOpStrFloatNeX));
			bos.Add ("float<"  + x, new BinOpStr (typeof (bool),  BinOpStrFloatLtX));
			bos.Add ("float<=" + x, new BinOpStr (typeof (bool),  BinOpStrFloatLeX));
			bos.Add ("float>"  + x, new BinOpStr (typeof (bool),  BinOpStrFloatGtX));
			bos.Add ("float>=" + x, new BinOpStr (typeof (bool),  BinOpStrFloatGeX));
			bos.Add ("float+"  + x, new BinOpStr (typeof (float), BinOpStrFloatAddX, true));
			bos.Add ("float-"  + x, new BinOpStr (typeof (float), BinOpStrFloatSubX, true));
			bos.Add ("float*"  + x, new BinOpStr (typeof (float), BinOpStrFloatMulX, true));
			bos.Add ("float/"  + x, new BinOpStr (typeof (float), BinOpStrFloatDivX, true));
			bos.Add ("float%"  + x, new BinOpStr (typeof (float), BinOpStrFloatModX, true));
		}

		private static void DefineBinOpsXFloat (Dictionary<string, BinOpStr> bos, string x)
		{
			bos.Add (x + "==float", new BinOpStr (typeof (bool),  BinOpStrXEqFloat));
			bos.Add (x + "!=float", new BinOpStr (typeof (bool),  BinOpStrXNeFloat));
			bos.Add (x + "<float",  new BinOpStr (typeof (bool),  BinOpStrXLtFloat));
			bos.Add (x + "<=float", new BinOpStr (typeof (bool),  BinOpStrXLeFloat));
			bos.Add (x + ">float",  new BinOpStr (typeof (bool),  BinOpStrXGtFloat));
			bos.Add (x + ">=float", new BinOpStr (typeof (bool),  BinOpStrXGeFloat));
			bos.Add (x + "+float",  new BinOpStr (typeof (float), BinOpStrXAddFloat, true));
			bos.Add (x + "-float",  new BinOpStr (typeof (float), BinOpStrXSubFloat, true));
			bos.Add (x + "*float",  new BinOpStr (typeof (float), BinOpStrXMulFloat, true));
			bos.Add (x + "/float",  new BinOpStr (typeof (float), BinOpStrXDivFloat, true));
			bos.Add (x + "%float",  new BinOpStr (typeof (float), BinOpStrXModFloat, true));
		}

		private static void DefineBinOpsInteger (Dictionary<string, BinOpStr> bos)
		{
			bos.Add ("integer==integer", new BinOpStr (typeof (bool), BinOpStrIntEqInt));
			bos.Add ("integer!=integer", new BinOpStr (typeof (bool), BinOpStrIntNeInt));
			bos.Add ("integer<integer",  new BinOpStr (typeof (bool), BinOpStrIntLtInt));
			bos.Add ("integer<=integer", new BinOpStr (typeof (bool), BinOpStrIntLeInt));
			bos.Add ("integer>integer",  new BinOpStr (typeof (bool), BinOpStrIntGtInt));
			bos.Add ("integer>=integer", new BinOpStr (typeof (bool), BinOpStrIntGeInt));
			bos.Add ("integer|integer",  new BinOpStr (typeof (int),  BinOpStrIntOrInt,  true));
			bos.Add ("integer^integer",  new BinOpStr (typeof (int),  BinOpStrIntXorInt, true));
			bos.Add ("integer&integer",  new BinOpStr (typeof (int),  BinOpStrIntAndInt, true));
			bos.Add ("integer+integer",  new BinOpStr (typeof (int),  BinOpStrIntAddInt, true));
			bos.Add ("integer-integer",  new BinOpStr (typeof (int),  BinOpStrIntSubInt, true));
			bos.Add ("integer*integer",  new BinOpStr (typeof (int),  BinOpStrIntMulInt, true));
			bos.Add ("integer/integer",  new BinOpStr (typeof (int),  BinOpStrIntDivInt, true));
			bos.Add ("integer%integer",  new BinOpStr (typeof (int),  BinOpStrIntModInt, true));
			bos.Add ("integer<<integer", new BinOpStr (typeof (int),  BinOpStrIntShlInt, true));
			bos.Add ("integer>>integer", new BinOpStr (typeof (int),  BinOpStrIntShrInt, true));
		}

		private static void DefineBinOpsKeyX (Dictionary<string, BinOpStr> bos, string x)
		{
			bos.Add ("key==" + x, new BinOpStr (typeof (bool), BinOpStrKeyEqX));
			bos.Add ("key!=" + x, new BinOpStr (typeof (bool), BinOpStrKeyNeX));
		}

		private static void DefineBinOpsXKey (Dictionary<string, BinOpStr> bos, string x)
		{
			bos.Add (x + "==key", new BinOpStr (typeof (bool), BinOpStrKeyEqX));
			bos.Add (x + "!=key", new BinOpStr (typeof (bool), BinOpStrKeyNeX));
		}

		private static void DefineBinOpsList (Dictionary<string, BinOpStr> bos)
		{
			bos.Add ("list+float",     new BinOpStr (typeof (LSL_List), BinOpStrListAddFloat, true));
			bos.Add ("list+integer",   new BinOpStr (typeof (LSL_List), BinOpStrListAddInt,   true));
			bos.Add ("list+key",       new BinOpStr (typeof (LSL_List), BinOpStrListAddKey,   true));
			bos.Add ("list+list",      new BinOpStr (typeof (LSL_List), BinOpStrListAddList,  true));
			bos.Add ("list+rotation",  new BinOpStr (typeof (LSL_List), BinOpStrListAddRot,   true));
			bos.Add ("list+string",    new BinOpStr (typeof (LSL_List), BinOpStrListAddStr,   true));
			bos.Add ("list+vector",    new BinOpStr (typeof (LSL_List), BinOpStrListAddVec,   true));

			bos.Add ("float+list",     new BinOpStr (typeof (LSL_List), BinOpStrFloatAddList));
			bos.Add ("integer+list",   new BinOpStr (typeof (LSL_List), BinOpStrIntAddList));
			bos.Add ("key+list",       new BinOpStr (typeof (LSL_List), BinOpStrKeyAddList));
			bos.Add ("rotation+list",  new BinOpStr (typeof (LSL_List), BinOpStrRotAddList));
			bos.Add ("string+list",    new BinOpStr (typeof (LSL_List), BinOpStrStrAddList));
			bos.Add ("vector+list",    new BinOpStr (typeof (LSL_List), BinOpStrVecAddList));

			bos.Add ("list==list",     new BinOpStr (typeof (bool), BinOpStrListEqList));
			bos.Add ("list!=list",     new BinOpStr (typeof (bool), BinOpStrListNeList));
		}

		// all operations allowed by LSL_Rotation definition
		private static void DefineBinOpsRotation (Dictionary<string, BinOpStr> bos)
		{
			bos.Add ("rotation==rotation", new BinOpStr (typeof (bool),         BinOpStrRotEqRot));
			bos.Add ("rotation!=rotation", new BinOpStr (typeof (bool),         BinOpStrRotNeRot));
			bos.Add ("rotation+rotation",  new BinOpStr (typeof (LSL_Rotation), BinOpStrRotAddRot, true));
			bos.Add ("rotation-rotation",  new BinOpStr (typeof (LSL_Rotation), BinOpStrRotSubRot, true));
			bos.Add ("rotation*rotation",  new BinOpStr (typeof (LSL_Rotation), BinOpStrRotMulRot, true));
			bos.Add ("rotation/rotation",  new BinOpStr (typeof (LSL_Rotation), BinOpStrRotDivRot, true));
		}

		private static void DefineBinOpsString (Dictionary<string, BinOpStr> bos)
		{
			bos.Add ("string==string", new BinOpStr (typeof (bool),   BinOpStrStrEqStr));
			bos.Add ("string!=string", new BinOpStr (typeof (bool),   BinOpStrStrNeStr));
			bos.Add ("string<string",  new BinOpStr (typeof (bool),   BinOpStrStrLtStr));
			bos.Add ("string<=string", new BinOpStr (typeof (bool),   BinOpStrStrLeStr));
			bos.Add ("string>string",  new BinOpStr (typeof (bool),   BinOpStrStrGtStr));
			bos.Add ("string>=string", new BinOpStr (typeof (bool),   BinOpStrStrGeStr));
			bos.Add ("string+string",  new BinOpStr (typeof (string), BinOpStrStrAddStr, true));
		}

		// all operations allowed by LSL_Vector definition
		private static void DefineBinOpsVector (Dictionary<string, BinOpStr> bos)
		{
			bos.Add ("vector==vector",  new BinOpStr (typeof (bool),       BinOpStrVecEqVec));
			bos.Add ("vector!=vector",  new BinOpStr (typeof (bool),       BinOpStrVecNeVec));
			bos.Add ("vector+vector",   new BinOpStr (typeof (LSL_Vector), BinOpStrVecAddVec, true));
			bos.Add ("vector-vector",   new BinOpStr (typeof (LSL_Vector), BinOpStrVecSubVec, true));
			bos.Add ("vector*vector",   new BinOpStr (typeof (float),      BinOpStrVecMulVec));
			bos.Add ("vector%vector",   new BinOpStr (typeof (LSL_Vector), BinOpStrVecModVec, true));

			bos.Add ("vector*float",    new BinOpStr (typeof (LSL_Vector), BinOpStrVecMulFloat, true));
			bos.Add ("float*vector",    new BinOpStr (typeof (LSL_Vector), BinOpStrFloatMulVec));
			bos.Add ("vector/float",    new BinOpStr (typeof (LSL_Vector), BinOpStrVecDivFloat, true));

			bos.Add ("vector*integer",  new BinOpStr (typeof (LSL_Vector), BinOpStrVecMulInt, true));
			bos.Add ("integer*vector",  new BinOpStr (typeof (LSL_Vector), BinOpStrIntMulVec));
			bos.Add ("vector/integer",  new BinOpStr (typeof (LSL_Vector), BinOpStrVecDivInt, true));

			bos.Add ("vector*rotation", new BinOpStr (typeof (LSL_Vector), BinOpStrVecMulRot, true));
			bos.Add ("vector/rotation", new BinOpStr (typeof (LSL_Vector), BinOpStrVecDivRot, true));
		}

		/**
		 * @brief These methods actually emit the code to perform the arithmetic.
		 * @param scg    = what script we are compiling
		 * @param left   = left-hand operand location in memory (type as given by BinOpStr entry)
		 * @param right  = right-hand operand location in memory (type as given by BinOpStr entry)
		 * @param result = result location in memory (type as given by BinOpStr entry)
		 */
		private static void BinOpStrAndAnd (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeBool);
			right.PushVal (scg, tokenTypeBool);
			scg.ilGen.Emit (OpCodes.And);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrOrOr (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeBool);
			right.PushVal (scg, tokenTypeBool);
			scg.ilGen.Emit (OpCodes.Or);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrBoolOrX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeBool);
			right.PushVal (scg, tokenTypeBool);
			scg.ilGen.Emit (OpCodes.Or);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrBoolXorX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeBool);
			right.PushVal (scg, tokenTypeBool);
			scg.ilGen.Emit (OpCodes.Xor);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrBoolAndX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeBool);
			right.PushVal (scg, tokenTypeBool);
			scg.ilGen.Emit (OpCodes.And);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrBoolEqX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeBool);
			right.PushVal (scg, tokenTypeBool);
			scg.ilGen.Emit (OpCodes.Ceq);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrBoolNeX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeBool);
			right.PushVal (scg, tokenTypeBool);
			scg.ilGen.Emit (OpCodes.Xor);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrFloatEqX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Ceq);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrFloatNeX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Ceq);
			scg.ilGen.Emit (OpCodes.Ldc_I4_1);
			scg.ilGen.Emit (OpCodes.Xor);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrFloatLtX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Clt);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrFloatLeX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Cgt);
			scg.ilGen.Emit (OpCodes.Ldc_I4_1);
			scg.ilGen.Emit (OpCodes.Xor);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrFloatGtX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Cgt);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrFloatGeX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Clt);
			scg.ilGen.Emit (OpCodes.Ldc_I4_1);
			scg.ilGen.Emit (OpCodes.Xor);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrFloatAddX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Add);
			result.PopPost (scg, tokenTypeFloat);
		}

		private static void BinOpStrFloatSubX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Sub);
			result.PopPost (scg, tokenTypeFloat);
		}

		private static void BinOpStrFloatMulX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Mul);
			result.PopPost (scg, tokenTypeFloat);
		}

		private static void BinOpStrFloatDivX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Div);
			result.PopPost (scg, tokenTypeFloat);
		}

		private static void BinOpStrFloatModX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Rem);
			result.PopPost (scg, tokenTypeFloat);
		}

		private static void BinOpStrXEqFloat (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Ceq);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrXNeFloat (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Ceq);
			scg.ilGen.Emit (OpCodes.Ldc_I4_1);
			scg.ilGen.Emit (OpCodes.Xor);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrXLtFloat (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Clt);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrXLeFloat (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Cgt);
			scg.ilGen.Emit (OpCodes.Ldc_I4_1);
			scg.ilGen.Emit (OpCodes.Xor);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrXGtFloat (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Cgt);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrXGeFloat (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Clt);
			scg.ilGen.Emit (OpCodes.Ldc_I4_1);
			scg.ilGen.Emit (OpCodes.Xor);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrXAddFloat (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Add);
			result.PopPost (scg, tokenTypeFloat);
		}

		private static void BinOpStrXSubFloat (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Sub);
			result.PopPost (scg, tokenTypeFloat);
		}

		private static void BinOpStrXMulFloat (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Mul);
			result.PopPost (scg, tokenTypeFloat);
		}

		private static void BinOpStrXDivFloat (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Div);
			result.PopPost (scg, tokenTypeFloat);
		}

		private static void BinOpStrXModFloat (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Rem);
			result.PopPost (scg, tokenTypeFloat);
		}

		private static void BinOpStrIntEqInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Ceq);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrIntNeInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Ceq);
			scg.ilGen.Emit (OpCodes.Ldc_I4_1);
			scg.ilGen.Emit (OpCodes.Xor);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrIntLtInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Clt);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrIntLeInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Cgt);
			scg.ilGen.Emit (OpCodes.Ldc_I4_1);
			scg.ilGen.Emit (OpCodes.Xor);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrIntGtInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Cgt);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrIntGeInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Clt);
			scg.ilGen.Emit (OpCodes.Ldc_I4_1);
			scg.ilGen.Emit (OpCodes.Xor);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrIntOrInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Or);
			result.PopPost (scg, tokenTypeInt);
		}

		private static void BinOpStrIntXorInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Xor);
			result.PopPost (scg, tokenTypeInt);
		}

		private static void BinOpStrIntAndInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.And);
			result.PopPost (scg, tokenTypeInt);
		}

		private static void BinOpStrIntAddInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Add);
			result.PopPost (scg, tokenTypeInt);
		}

		private static void BinOpStrIntSubInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Sub);
			result.PopPost (scg, tokenTypeInt);
		}

		private static void BinOpStrIntMulInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Mul);
			result.PopPost (scg, tokenTypeInt);
		}

		private static void BinOpStrIntDivInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Div);
			result.PopPost (scg, tokenTypeInt);
		}

		private static void BinOpStrIntModInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Rem);
			result.PopPost (scg, tokenTypeInt);
		}

		private static void BinOpStrIntShlInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Shl);
			result.PopPost (scg, tokenTypeInt);
		}

		private static void BinOpStrIntShrInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Shr);
			result.PopPost (scg, tokenTypeInt);
		}

		private static void BinOpStrKeyEqX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeStr);
			right.PushVal (scg, tokenTypeStr);
			scg.ilGen.Emit (OpCodes.Call, stringCmpStringMethInfo);
			scg.ilGen.Emit (OpCodes.Ldc_I4_0);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrKeyNeX (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeStr);
			right.PushVal (scg, tokenTypeStr);
			scg.ilGen.Emit (OpCodes.Call, stringCmpStringMethInfo);
			scg.ilGen.Emit (OpCodes.Ceq);
			scg.ilGen.Emit (OpCodes.Ldc_I4_1);
			scg.ilGen.Emit (OpCodes.Xor);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrListAddFloat (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeList);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Call, infoMethListAddFloat);
			result.PopPost (scg, tokenTypeList);
		}

		private static void BinOpStrListAddInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeList);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Call, infoMethListAddInt);
			result.PopPost (scg, tokenTypeList);
		}

		private static void BinOpStrListAddKey (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeList);
			right.PushVal (scg, tokenTypeStr);
			scg.ilGen.Emit (OpCodes.Call, infoMethListAddKey);
			result.PopPost (scg, tokenTypeList);
		}

		private static void BinOpStrListAddList (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeList);
			right.PushVal (scg, tokenTypeList);
			scg.ilGen.Emit (OpCodes.Call, infoMethListAddList);
			result.PopPost (scg, tokenTypeList);
		}

		private static void BinOpStrListAddRot (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeList);
			right.PushVal (scg, tokenTypeRot);
			scg.ilGen.Emit (OpCodes.Call, infoMethListAddRot);
			result.PopPost (scg, tokenTypeList);
		}

		private static void BinOpStrListAddStr (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeList);
			right.PushVal (scg, tokenTypeStr);
			scg.ilGen.Emit (OpCodes.Call, infoMethListAddStr);
			result.PopPost (scg, tokenTypeList);
		}

		private static void BinOpStrListAddVec (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeList);
			right.PushVal (scg, tokenTypeVec);
			scg.ilGen.Emit (OpCodes.Call, infoMethListAddVec);
			result.PopPost (scg, tokenTypeList);
		}

		private static void BinOpStrFloatAddList (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeList);
			scg.ilGen.Emit (OpCodes.Call, infoMethFloatAddList);
			result.PopPost (scg, tokenTypeList);
		}

		private static void BinOpStrIntAddList (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeList);
			scg.ilGen.Emit (OpCodes.Call, infoMethIntAddList);
			result.PopPost (scg, tokenTypeList);
		}

		private static void BinOpStrKeyAddList (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeStr);
			right.PushVal (scg, tokenTypeList);
			scg.ilGen.Emit (OpCodes.Call, infoMethKeyAddList);
			result.PopPost (scg, tokenTypeList);
		}

		private static void BinOpStrRotAddList (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeRot);
			right.PushVal (scg, tokenTypeList);
			scg.ilGen.Emit (OpCodes.Call, infoMethRotAddList);
			result.PopPost (scg, tokenTypeList);
		}

		private static void BinOpStrStrAddList (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeStr);
			right.PushVal (scg, tokenTypeList);
			scg.ilGen.Emit (OpCodes.Call, infoMethStrAddList);
			result.PopPost (scg, tokenTypeList);
		}

		private static void BinOpStrVecAddList (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeVec);
			right.PushVal (scg, tokenTypeList);
			scg.ilGen.Emit (OpCodes.Call, infoMethVecAddList);
			result.PopPost (scg, tokenTypeList);
		}

		private static void BinOpStrListEqList (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeList);
			right.PushVal (scg, tokenTypeList);
			scg.ilGen.Emit (OpCodes.Call, infoMethListEqList);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrListNeList (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeList);
			right.PushVal (scg, tokenTypeList);
			scg.ilGen.Emit (OpCodes.Call, infoMethListNeList);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrRotEqRot (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeRot);
			right.PushVal (scg, tokenTypeRot);
			scg.ilGen.Emit (OpCodes.Call, infoMethRotEqRot);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrRotNeRot (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeRot);
			right.PushVal (scg, tokenTypeRot);
			scg.ilGen.Emit (OpCodes.Call, infoMethRotNeRot);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrRotAddRot (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeRot);
			right.PushVal (scg, tokenTypeRot);
			scg.ilGen.Emit (OpCodes.Call, infoMethRotAddRot);
			result.PopPost (scg, tokenTypeRot);
		}

		private static void BinOpStrRotSubRot (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeRot);
			right.PushVal (scg, tokenTypeRot);
			scg.ilGen.Emit (OpCodes.Call, infoMethRotSubRot);
			result.PopPost (scg, tokenTypeRot);
		}

		private static void BinOpStrRotMulRot (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeRot);
			right.PushVal (scg, tokenTypeRot);
			scg.ilGen.Emit (OpCodes.Call, infoMethRotMulRot);
			result.PopPost (scg, tokenTypeRot);
		}

		private static void BinOpStrRotDivRot (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeRot);
			right.PushVal (scg, tokenTypeRot);
			scg.ilGen.Emit (OpCodes.Call, infoMethRotDivRot);
			result.PopPost (scg, tokenTypeRot);
		}

		private static void BinOpStrStrEqStr (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeStr);
			right.PushVal (scg, tokenTypeStr);
			scg.ilGen.Emit (OpCodes.Call, stringCmpStringMethInfo);
			scg.ilGen.Emit (OpCodes.Ldc_I4_0);
			scg.ilGen.Emit (OpCodes.Ceq);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrStrNeStr (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeStr);
			right.PushVal (scg, tokenTypeStr);
			scg.ilGen.Emit (OpCodes.Call, stringCmpStringMethInfo);
			scg.ilGen.Emit (OpCodes.Ldc_I4_0);
			scg.ilGen.Emit (OpCodes.Ceq);
			scg.ilGen.Emit (OpCodes.Ldc_I4_1);
			scg.ilGen.Emit (OpCodes.Xor);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrStrLtStr (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeStr);
			right.PushVal (scg, tokenTypeStr);
			scg.ilGen.Emit (OpCodes.Call, stringCmpStringMethInfo);
			scg.ilGen.Emit (OpCodes.Ldc_I4_0);
			scg.ilGen.Emit (OpCodes.Clt);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrStrLeStr (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeStr);
			right.PushVal (scg, tokenTypeStr);
			scg.ilGen.Emit (OpCodes.Call, stringCmpStringMethInfo);
			scg.ilGen.Emit (OpCodes.Ldc_I4_1);
			scg.ilGen.Emit (OpCodes.Clt);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrStrGtStr (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeStr);
			right.PushVal (scg, tokenTypeStr);
			scg.ilGen.Emit (OpCodes.Call, stringCmpStringMethInfo);
			scg.ilGen.Emit (OpCodes.Ldc_I4_0);
			scg.ilGen.Emit (OpCodes.Cgt);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrStrGeStr (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeStr);
			right.PushVal (scg, tokenTypeStr);
			scg.ilGen.Emit (OpCodes.Call, stringCmpStringMethInfo);
			scg.ilGen.Emit (OpCodes.Ldc_I4_M1);
			scg.ilGen.Emit (OpCodes.Cgt);
			result.PopPost (scg, tokenTypeBool);
		}

		// Called by many type combinations so both operands need to be cast to strings
		private static void BinOpStrStrAddStr (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeStr);
			right.PushVal (scg, tokenTypeStr);
			scg.ilGen.Emit (OpCodes.Call, stringAddStringMethInfo);
			result.PopPost (scg, tokenTypeStr);
		}

		private static void BinOpStrVecEqVec (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeVec);
			right.PushVal (scg, tokenTypeVec);
			scg.ilGen.Emit (OpCodes.Call, infoMethVecEqVec);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrVecNeVec (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeVec);
			right.PushVal (scg, tokenTypeVec);
			scg.ilGen.Emit (OpCodes.Call, infoMethVecNeVec);
			result.PopPost (scg, tokenTypeBool);
		}

		private static void BinOpStrVecAddVec (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeVec);
			right.PushVal (scg, tokenTypeVec);
			scg.ilGen.Emit (OpCodes.Call, infoMethVecAddVec);
			result.PopPost (scg, tokenTypeVec);
		}

		private static void BinOpStrVecSubVec (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeVec);
			right.PushVal (scg, tokenTypeVec);
			scg.ilGen.Emit (OpCodes.Call, infoMethVecSubVec);
			result.PopPost (scg, tokenTypeVec);
		}

		private static void BinOpStrVecMulVec (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeVec);
			right.PushVal (scg, tokenTypeVec);
			scg.ilGen.Emit (OpCodes.Call, infoMethVecMulVec);
			result.PopPost (scg, tokenTypeFloat);
		}

		private static void BinOpStrVecModVec (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeVec);
			right.PushVal (scg, tokenTypeVec);
			scg.ilGen.Emit (OpCodes.Call, infoMethVecModVec);
			result.PopPost (scg, tokenTypeVec);
		}

		private static void BinOpStrVecMulFloat (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeVec);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Call, infoMethVecMulFloat);
			result.PopPost (scg, tokenTypeVec);
		}

		private static void BinOpStrFloatMulVec (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeFloat);
			right.PushVal (scg, tokenTypeVec);
			scg.ilGen.Emit (OpCodes.Call, infoMethFloatMulVec);
			result.PopPost (scg, tokenTypeVec);
		}

		private static void BinOpStrVecDivFloat (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeVec);
			right.PushVal (scg, tokenTypeFloat);
			scg.ilGen.Emit (OpCodes.Call, infoMethVecDivFloat);
			result.PopPost (scg, tokenTypeVec);
		}

		private static void BinOpStrVecMulInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeVec);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Call, infoMethVecMulInt);
			result.PopPost (scg, tokenTypeVec);
		}

		private static void BinOpStrIntMulVec (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeInt);
			right.PushVal (scg, tokenTypeVec);
			scg.ilGen.Emit (OpCodes.Call, infoMethIntMulVec);
			result.PopPost (scg, tokenTypeVec);
		}

		private static void BinOpStrVecDivInt (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeVec);
			right.PushVal (scg, tokenTypeInt);
			scg.ilGen.Emit (OpCodes.Call, infoMethVecDivInt);
			result.PopPost (scg, tokenTypeVec);
		}

		private static void BinOpStrVecMulRot (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeVec);
			right.PushVal (scg, tokenTypeRot);
			scg.ilGen.Emit (OpCodes.Call, infoMethVecMulRot);
			result.PopPost (scg, tokenTypeVec);
		}

		private static void BinOpStrVecDivRot (ScriptCodeGen scg, CompValu left, CompValu right, CompValu result)
		{
			result.PopPre (scg);
			left.PushVal (scg, tokenTypeVec);
			right.PushVal (scg, tokenTypeRot);
			scg.ilGen.Emit (OpCodes.Call, infoMethVecDivRot);
			result.PopPost (scg, tokenTypeVec);
		}

		/**
		 * @brief These methods are called at runtime as helpers.
		 *        Needed to pick up functionality defined by overloaded operators of LSL_ types.
		 *        They need to be marked public or runtime says they are inaccessible.
		 */
		public static LSL_List MethListAddFloat (LSL_List left, float right)
		{
			return left + right;
		}

		public static LSL_List MethListAddInt (LSL_List left, int right)
		{
			return left + right;
		}

		public static LSL_List MethListAddKey (LSL_List left, LSL_Key right)
		{
			return left + right;
		}

		public static LSL_List MethListAddList (LSL_List left, LSL_List right)
		{
			return left + right;
		}

		public static LSL_List MethListAddRot (LSL_List left, LSL_Rotation right)
		{
			return left + right;
		}

		public static LSL_List MethListAddStr (LSL_List left, string right)
		{
			return left + right;
		}

		public static LSL_List MethListAddVec (LSL_List left, LSL_Vector right)
		{
			return left + right;
		}

		public static LSL_List MethFloatAddList (float left, LSL_List right)
		{
			return (LSL_Float)left + right;
		}

		public static LSL_List MethIntAddList (int left, LSL_List right)
		{
			return (LSL_Integer)left + right;
		}

		public static LSL_List MethKeyAddList (LSL_Key left, LSL_List right)
		{
			return left + right;
		}

		public static LSL_List MethRotAddList (LSL_Rotation left, LSL_List right)
		{
			return left + right;
		}

		public static LSL_List MethStrAddList (string left, LSL_List right)
		{
			return (LSL_String)left + right;
		}

		public static LSL_List MethVecAddList (LSL_Vector left, LSL_List right)
		{
			return left + right;
		}

		public static bool MethListEqList (LSL_List left, LSL_List right)
		{
			return left == right;
		}

		public static bool MethListNeList (LSL_List left, LSL_List right)
		{
			return left != right;
		}

		public static bool MethRotEqRot (LSL_Rotation left, LSL_Rotation right)
		{
			return left == right;
		}

		public static bool MethRotNeRot (LSL_Rotation left, LSL_Rotation right)
		{
			return left != right;
		}

		public static LSL_Rotation MethRotAddRot (LSL_Rotation left, LSL_Rotation right)
		{
			return left + right;
		}

		public static LSL_Rotation MethRotSubRot (LSL_Rotation left, LSL_Rotation right)
		{
			return left - right;
		}

		public static LSL_Rotation MethRotMulRot (LSL_Rotation left, LSL_Rotation right)
		{
			return left * right;
		}

		public static LSL_Rotation MethRotDivRot (LSL_Rotation left, LSL_Rotation right)
		{
			return left / right;
		}

		public static bool MethVecEqVec (LSL_Vector left, LSL_Vector right)
		{
			return left == right;
		}

		public static bool MethVecNeVec (LSL_Vector left, LSL_Vector right)
		{
			return left != right;
		}

		public static LSL_Vector MethVecAddVec (LSL_Vector left, LSL_Vector right)
		{
			return left + right;
		}

		public static LSL_Vector MethVecSubVec (LSL_Vector left, LSL_Vector right)
		{
			return left - right;
		}

		public static float MethVecMulVec (LSL_Vector left, LSL_Vector right)
		{
			return (float)(left * right).value;
		}

		public static LSL_Vector MethVecModVec (LSL_Vector left, LSL_Vector right)
		{
			return left % right;
		}

		public static LSL_Vector MethVecMulFloat (LSL_Vector left, float right)
		{
			return left * right;
		}

		public static LSL_Vector MethFloatMulVec (float left, LSL_Vector right)
		{
			return left * right;
		}

		public static LSL_Vector MethVecDivFloat (LSL_Vector left, float right)
		{
			return left / right;
		}

		public static LSL_Vector MethVecMulInt (LSL_Vector left, int right)
		{
			return left * right;
		}

		public static LSL_Vector MethIntMulVec (int left, LSL_Vector right)
		{
			return left * right;
		}

		public static LSL_Vector MethVecDivInt (LSL_Vector left, int right)
		{
			return left / right;
		}

		public static LSL_Vector MethVecMulRot (LSL_Vector left, LSL_Rotation right)
		{
			return left * right;
		}

		public static LSL_Vector MethVecDivRot (LSL_Vector left, LSL_Rotation right)
		{
			return left / right;
		}
	}
}
