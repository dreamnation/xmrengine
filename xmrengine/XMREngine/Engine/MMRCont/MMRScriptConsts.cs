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


namespace MMR {

	public class ScriptConst {

		private static Dictionary<string, ScriptConst> scriptConstants = null;

		/**
		 * @brief look up the value of a given built-in constant.
		 * @param name = name of constant
		 * @returns null: no constant by that name defined
		 *          else: pointer to ScriptConst struct
		 */
		public static ScriptConst Lookup (string name)
		{
			if (scriptConstants == null) {
				Init ();
			}
			//MB();
			if (scriptConstants.ContainsKey (name)) {
				return scriptConstants[name];
			}
			return null;
		}

		private static void Init ()
		{
			Dictionary<string, ScriptConst> sc = new Dictionary<string, ScriptConst> ();

			/*
			 * Scan through all fields defined by ScriptBaseClass.
			 * Specifically, we want the ones defined in LSL_Constants.cs.
			 */
			FieldInfo[] constFields = typeof (ScriptBaseClass).GetFields ();
			string scriptBaseClassFullName = typeof (ScriptBaseClass).FullName.Replace ("+", ".");

			foreach (FieldInfo constField in constFields) {

				/*
				 * Only deal with constants named with all uppercase letters, numbers and underscores.
				 * Anything with anything else is ignored.
				 */
				string fieldName = constField.Name;
				int i;
				for (i = fieldName.Length; -- i >= 0;) {
					if ("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".IndexOf (fieldName[i]) < 0) break;
				}
				if (i < 0) {
					Type fieldType = constField.FieldType;

					/*
					 * The location of a simple number is the number's string as such.
					 */
					if (fieldType == typeof (double)) {
						new ScriptConst (sc, fieldName, typeof (float), ((double)constField.GetValue (null)).ToString ());
					} else if (fieldType == typeof (int)) {
						new ScriptConst (sc, fieldName, typeof (int), ((int)constField.GetValue (null)).ToString ());
					} else if (fieldType == typeof (LSL_Integer)) {
						new ScriptConst (sc, fieldName, typeof (int), ((LSL_Integer)constField.GetValue (null)).ToString ());
					}

					/*
					 * The location of everything else (objects) is the static field in the ScriptBaseClass definition.
					 */
					else new ScriptConst (sc, fieldName, fieldType, scriptBaseClassFullName + "." + fieldName);
				}
			}

			//MB();
			scriptConstants = sc;
		}

		/*
		 * Instance variables
		 */
		public string name;
		public Type type;
		public CompRVal rVal;

		private ScriptConst (Dictionary<string, ScriptConst> lc, string name, Type type, string location)
		{
			lc.Add (name, this);
			this.name = name;
			this.type = type;
			this.rVal = new CompRVal (TokenType.FromSysType (null, type), location);
		}
	}
}
