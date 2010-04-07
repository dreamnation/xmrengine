/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

using Mono.Tasklets;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;

using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;


/**
 * @brief C# callable wrappers for xmrhelpers.so functions.
 */
namespace OpenSim.Region.ScriptEngine.XMREngine
{
	public class XMRHelpers {

		/*
		 * This just calls the XMRHelperInitialize() routine in xmrhelpers.so.
		 * Uses 'protected' instead of 'private' to avoid silly compiler warning.
		 */
		protected delegate int XMRHelperInitializeDel (Type lslStringType);
		protected static int dummy = ((XMRHelperInitializeDel)MMRDLOpen.GetDelegate ("./xmrhelpers.so", 
		                                                                             "XMRHelperInitialize", 
		                                                                             typeof (XMRHelperInitializeDel), 
		                                                                             null)) (typeof (LSL_String));

		/*
		 * Get a delegate for ParseString2List in xmrhelpers.so.
		 */
		private delegate Exception ParseString2ListDel (string str, 
		                                                object[] separators, 
		                                                object[] spacers, 
		                                                bool keepNulls, 
		                                                out object[] outArray);
		private static ParseString2ListDel parseString2List = 
				(ParseString2ListDel)MMRDLOpen.GetDelegate ("./xmrhelpers.so",
				                                            "ParseString2List",
				                                            typeof (ParseString2ListDel),
				                                            null);

		/**
		 * @brief Script references to llParseString2List() call this instead of the normal
		 *        backend llParseString2List().  This routine is a simple wrapper to a 
		 *        C-language routine in xmrhelpers.so which does most of the processing.
		 *        Likewise for llParseStringKeepNulls().
		 */
		public static LSL_List ParseString2List (string str, LSL_List separators, LSL_List spacers, bool keepNulls)
		{
			object[] sepperArray = separators.Data;
			object[] spacerArray = spacers.Data;
			object[] outputArray;

			Exception e = parseString2List (str, sepperArray, spacerArray, keepNulls, out outputArray);
			if (e != null) throw e;

			return new LSL_List (outputArray);
		}
	}
}
