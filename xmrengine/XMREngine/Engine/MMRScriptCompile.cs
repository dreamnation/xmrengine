/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

/**
 * @brief Main program for the script compiler.
 */

using System;
using System.IO;
using System.Reflection;
using log4net;
using OpenMetaverse;

namespace OpenSim.Region.ScriptEngine.XMREngine
{
    public class ScriptCompile
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static uint fileno = 0;

        /**
         * @brief Compile a script to produce a .DLL file.
         * @param source = 'source' contains the whole script source
         * @param binaryName = where to write .DLL file to
         * @param assetID = the script asset ID, for error messages
         * @param errorMessage = where to write error messages to
         * @returns true: successful
         *         false: failure
         */
        public static bool Compile (string source, 
                                    string binaryName,
                                    string assetID,
                                    string debugFileName,
                                    TokenErrorMessage errorMessage)
        {
            string fname = assetID + "_" + (++ fileno).ToString () + "_script_compile";
            string envar = Environment.GetEnvironmentVariable ("MMRScriptCompileSaveSource");
            if ((envar != null) && ((envar[0] & 1) != 0)) {
                m_log.DebugFormat("[XMREngine]: MMRScriptCopmileSaveSource: saving to {0}.lsl", fname);
                File.WriteAllText (fname + ".lsl", source);
            }

            TokenBegin tokenBegin =
                        TokenBegin.Construct(errorMessage, source);
            if (tokenBegin == null)
            {
                m_log.DebugFormat("[XMREngine]: Tokenizing error on {0}", assetID);
                return false;
            }

            TokenScript tokenScript = ScriptReduce.Reduce(tokenBegin);
            if (tokenScript == null)
            {
                m_log.DebugFormat("[XMREngine]: Reducing error on {0}", assetID);
                return false;
            }

            envar = Environment.GetEnvironmentVariable ("MMRScriptCompileSaveCSharp");
            if ((envar != null) && ((envar[0] & 1) != 0)) {
                m_log.DebugFormat("[XMREngine]: MMRScriptCopmileSaveCSharp: saving to {0}.cs", fname);
                debugFileName = fname + ".cs";
            }

            bool ok = ScriptCodeGen.CodeGen(tokenScript, binaryName, debugFileName);
            if (!ok)
            {
                m_log.DebugFormat("[XMREngine]: Codegen error on {0}", assetID);
                return false;
            }

            return true;
        }
    }
}
