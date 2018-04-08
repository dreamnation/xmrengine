/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

/**
 * @brief Main program for the script tester.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;

using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;

namespace OpenSim.Region.ScriptEngine.XMREngine
{
    public struct PerPrim {
        public string srcFileName;
        public int linkNum;
        public string name;
        public string uuid;
    }

    public partial class XMREngTest
    {
        public static bool doCheckRun     = false;
        public static bool eventIO        = false;
        public static bool haveLinkNums   = false;
        public static bool printPeakHeap  = false;
        public static bool printPeakStack = false;
        public static Dictionary<string, ScriptObjCode> scriptObjCodes = new Dictionary<string, ScriptObjCode> ();
        public static Dictionary<string, ScriptRoot> scriptRootPrimUUIDs = new Dictionary<string, ScriptRoot> ();
        public static int consoleLine = 0;
        public static int scriptRootIndex = 0;
        public static LinkedList<int> ipcChannels = new LinkedList<int> ();
        public static LinkedList<QueuedEvent> queuedEvents = new LinkedList<QueuedEvent> ();
        public static MemoryStream serializeStream = null;
        public static ScriptRoot[] scriptRoots;
        public static Token inputTokens = null;

        /**
         * @brief Stand-alone test program.
         */
        public static void Main (string[] args)
        {
            MMRWebRequest.allowFileURL = true;

            /*
             * Tell code generator what type we do a 'new' of for script context.
             * Must be descended from XMRInstAbstract.
             */
            ScriptCodeGen.xmrInstSuperType = typeof (XMRInstance);

            /*
             * Parse command-line arguments.
             */
            bool doXmrAsm     = false;
            List<PerPrim> perPrims = new List<PerPrim> ();
            int linkNum       = 0;
            string primName   = "";
            string primUUID   = "";
            string sourceHash = null;

            for (int i = 0; i < args.Length; i ++) {
                string arg = args[i];
                if (arg == "-builtins") {
                    TokenDeclInline.PrintBuiltins (true, Console.WriteLine);
                    return;
                }
                if (arg == "-checkrun") {
                    doCheckRun = true;
                    continue;
                }
                if (arg == "-eventio") {
                    eventIO = true;
                    continue;
                }
                if (arg == "-heaplimit") {
                    try {
                        XMRInstance.HEAPLIMIT = int.Parse (args[++i]);
                    } catch {
                        goto usage;
                    }
                    continue;
                }
                if (arg == "-ipcchannel") {
                    try {
                        ipcChannels.AddLast (int.Parse (args[++i]));
                    } catch {
                        goto usage;
                    }
                    continue;
                }
                if (arg == "-linknum") {
                    if (++ i >= args.Length) goto usage;
                    try {
                        linkNum = int.Parse (args[i]);
                    } catch {
                        goto usage;
                    }
                    haveLinkNums = true;
                    continue;
                }
                if (arg == "-peakheap") {
                    printPeakHeap = true;
                    continue;
                }
                if (arg == "-peakstack") {
                    printPeakStack = true;
                    continue;
                }
                if (arg == "-primname") {
                    if (++ i >= args.Length) goto usage;
                    primName = args[i];
                    continue;
                }
                if (arg == "-primuuid") {
                    if (++ i >= args.Length) goto usage;
                    primUUID = args[i];
                    continue;
                }
                if (arg == "-serialize") {
                    doCheckRun = true;
                    serializeStream = new MemoryStream ();
                    continue;
                }
                if (arg == "-stacksize") {
                    try {
                        XMRInstance.STACKSIZE = int.Parse (args[++i]);
                    } catch {
                        goto usage;
                    }
                    continue;
                }
                if (arg == "-version") {
                    string commitInfo = gitcommithash +
                                (new string[] { "(dirty)", "" })[gitcommitclean] +
                                " " + gitcommitdate;
                    Console.WriteLine (commitInfo);
                    Environment.Exit (0);
                }
                if (arg == "-xmrasm") {
                    doXmrAsm = true;
                    continue;
                }
                if (arg[0] == '-') goto usage;
                PerPrim perPrim;
                perPrim.srcFileName = arg;
                perPrim.linkNum     = linkNum;
                perPrim.name        = primName;
                perPrim.uuid        = primUUID;
                perPrims.Add (perPrim);
            }
            if (perPrims.Count == 0) goto usage;


            /*
             * Compile each script and create script instance for each.
             */
            scriptRoots = new ScriptRoot[perPrims.Count];
            for (int i = 0; i < perPrims.Count; i ++) {
                ScriptObjCode scriptObjCode;
                string srcFileName = perPrims[i].srcFileName;

                if (!scriptObjCodes.TryGetValue (srcFileName, out scriptObjCode)) {

                    /*
                     * Read whole source into memory.
                     */
                    FileInfo srcFileInfo = new FileInfo (srcFileName);
                    string source = "# 1 \"" + srcFileName + "\"\n" + File.ReadAllText (srcFileInfo.FullName);

                    /*
                     * Parse source string into tokens.
                     */
                    if (perPrims.Count > 1) {
                        Console.WriteLine ("Compiling " + srcFileName + " ...");
                    } else {
                        Console.WriteLine ("Compiling...");
                    }
                    string cameFrom = "file://" + srcFileInfo.FullName;
                    TokenBegin tokenBegin = TokenBegin.Construct (cameFrom, null, StandAloneErrorMessage, source, out sourceHash);
                    if (tokenBegin == null) {
                        Console.WriteLine ("Parsing errors.");
                        Environment.Exit (1);
                    }

                    /*
                     * Create abstract syntax tree from raw tokens.
                     */
                    TokenScript tokenScript = ScriptReduce.Reduce(tokenBegin);
                    if (tokenScript == null) {
                        Console.WriteLine ("Reduction errors.");
                        Environment.Exit (1);
                    }

                    /*
                     * Attempt to compile AST to object code.
                     */
                    Stream objFileStream = new MemoryStream ();
                    BinaryWriter objFileWriter = new BinaryWriter (objFileStream);
                    bool ok;
                    ok = ScriptCodeGen.CodeGen (tokenScript, objFileWriter, sourceHash);
                    if (!ok) {
                        Console.WriteLine ("Compilation errors.");
                        Environment.Exit (1);
                    }

                    /*
                     * Load object code into memory.
                     */
                    objFileStream.Seek (0, SeekOrigin.Begin);
                    BinaryReader objFileReader = new BinaryReader (objFileStream);
                    TextWriter asmFileWriter = null;
                    if (doXmrAsm) {
                        string asmFileName = srcFileName;
                        if (asmFileName.EndsWith (".lsl")) {
                            asmFileName = asmFileName.Substring (0, asmFileName.Length - 4);
                        }
                        asmFileName += ".xmrasm";
                        asmFileWriter = new StreamWriter (asmFileName);
                    }
                    scriptObjCode = new ScriptObjCode (objFileReader, asmFileWriter, null);
                    objFileReader.Close ();
                    if (asmFileWriter != null) {
                        asmFileWriter.Close ();
                    }

                    scriptObjCodes.Add (srcFileName, scriptObjCode);
                }

                /*
                 * Instantiate the script and set it up to run its default state_entry() handler.
                 */
                if (perPrims.Count > 1) {
                    Console.WriteLine ("Running " + perPrims[i].name + " ...");
                } else {
                    Console.WriteLine ("Running...");
                }
                XMRInstance inst = new XMRInstance (scriptObjCode);
                inst.m_DescName  = perPrims[i].name + ":" + srcFileName;
                new ScriptRoot (inst, i, perPrims[i]);
            }

            /*
             * Index the scripts by prim uuid if given.
             */
            foreach (ScriptRoot sr in scriptRoots) {
                if (sr.primUUID != "") {
                    if (scriptRootPrimUUIDs.ContainsKey (sr.primUUID)) {
                        Console.WriteLine ("duplicate use of -primuuid " + sr.primUUID);
                        Environment.Exit (2);
                    }
                    scriptRootPrimUUIDs.Add (sr.primUUID, sr);
                }
            }

            /*
             * Step scripts until there is nothing more to do.
             */
            while (true) {

                /*
                 * Step them all until they are waiting for an event.
                 */
                bool didSomething;
                do {
                    didSomething = false;
                    foreach (ScriptRoot sr in scriptRoots) {
                        if (sr.iState != IState.WAITINGFOREVENT) {
                            sr.StepScript ();
                            didSomething = true;
                        }
                    }
                } while (didSomething);

                /*
                 * Read next event from stdin.
                 */
                while (queuedEvents.Count == 0) {
                    if (!eventIO) goto done;
                    ReadInputLine ();
                }

                /*
                 * Queue event to the script.
                 */
                QueuedEvent qe = queuedEvents.First.Value;
                queuedEvents.RemoveFirst ();
                qe.scriptRoot.ProcessQueuedEvent (qe);
            }

        done:
            if (printPeakHeap) {
                foreach (ScriptRoot sr in scriptRoots) {
                    Console.WriteLine (sr.msgPrefix + "PeakHeapUsed = " + sr.GetPeakHeapUsed ());
                }
            }
            if (printPeakStack) {
                foreach (ScriptRoot sr in scriptRoots) {
                    Console.WriteLine (sr.msgPrefix + "PeakStackUsed = " + sr.GetPeakStackUsed ());
                }
            }
            Environment.Exit (0);

        usage:
            Console.WriteLine ("usage: mono xmrengtest.exe [ -builtins ] [ -checkrun ] [ -eventio ] [ -heaplimit <numbytes> ] [ -ipcchannel <channel> ] [ -linknum <number> ] [ -peakheap ] [ -peakstack ] [ -primname <name> ] [ -primuuid <uuid> ] [ -serialize ] [ -stacksize <numbytes> ] [ -version ] <sourcefile> ...");
            Console.WriteLine ("     -builtins : print list of built-in functions and constants then exit");
            Console.WriteLine ("     -checkrun : simulate a thread slice at every opportunity");
            Console.WriteLine ("      -eventio : when default state_entry completes, read next event from stdin and keep doing so until eof");
            Console.WriteLine ("                 format is:  event_name ( arg1 , arg2 , ... )");
            Console.WriteLine ("                   example:  http_response(\"firstone\",3,[0,<1,2,3>,\"456\"],\"body\")");
            Console.WriteLine ("    -heaplimit : max number of heap bytes allowed");
            Console.WriteLine ("   -ipcchannel : channel number for which llListen/llListenControl/llListenRemove/llRegionSay/llRegionSayTo/llSay are handled internally");
            Console.WriteLine ("      -linknum : link number returned by llGetLinkNumber() and used by llMessageLinked()");
            Console.WriteLine ("     -peakheap : print peak heap used");
            Console.WriteLine ("    -peakstack : print peak stack used");
            Console.WriteLine ("     -primname : give prim name for subsequent script(s) (returned by llGetKey())");
            Console.WriteLine ("     -primuuid : give prim uuid for subsequent script(s) (returned by llGetObjectName())");
            Console.WriteLine ("    -serialize : serialize and deserialize stack at every opportunity");
            Console.WriteLine ("    -stacksize : max number of stack bytes allowed");
            Console.WriteLine ("      -version : print version information then exit");
            Environment.Exit (2);
        }

        /**
         * @brief Read a line of input and queue it wherever it goes.
         */
        public static bool ReadInputLine ()
        {
            return ReadInputLine (inputTokens);
        }
        public static bool ReadInputLine (Token t)
        {
            while ((t != null) && ((t is TokenEnd) || (t is TokenKwSemi))) t = t.nextToken;
            if (t == null) {
                string sourceHash;
                Token te = null;
                TokenBegin tb;
                TokenBegin tbb = null;

                for (string inputLine; (inputLine = Console.ReadLine ()) != null;) {
                    ++ consoleLine;
                    Console.WriteLine (inputLine);

                    /*
                     * Parse into tokens.
                     * If error parsing, print error then read another line.
                     * Also ignore blank lines.
                     */
                    inputLine = "# " + consoleLine + " \"stdin\"\n" + inputLine;
                    tb = TokenBegin.Construct ("input", null, StandAloneErrorMessage, inputLine, out sourceHash);
                    if (tb == null) continue;
                    if (tb.nextToken is TokenEnd) continue;

                    /*
                     * If this is our very first line, remember where it begins.
                     */
                    if (tbb == null) tbb = tb;

                    /*
                     * If this is a continuation, splice away.
                     *   te = hyphen from previous line
                     *   tb = begin token from this line
                     */
                    if (te != null) {
                        te.prevToken.nextToken = tb.nextToken;
                        tb.nextToken.prevToken = te.prevToken;
                    }

                    /*
                     * See if there is a continuation of this line by looking for a hyphen at the end.
                     * If so, loop back to read in another line.
                     */
                    for (t = tb; !(t is TokenEnd); t = t.nextToken) { }
                    if (t.prevToken is TokenKwSub) {
                        te = t.prevToken;
                        continue;
                    }

                    /*
                     * Otherwise, it is our input tokens.
                     */
                    for (t = tbb; (t is TokenBegin); t = t.nextToken) { }
                    return ReadInputLine (t);
                }

                // end of file, won't get anything more
                eventIO = false;
                return false;
            }

            /*
             * STATUS prints the script status.
             */
            if ((t is TokenName) && (((TokenName)t).val == "STATUS")) {
                foreach (ScriptRoot srr in scriptRoots) {
                    srr.PrintStatus ();
                }
                return ReadInputLine (t.nextToken);
            }

            /*
             * Decode which script the event/return value goes to.
             */
            // integer) : set sri to the given integer
            //  string) : set sri to the given prim name
            if ((t is TokenInt) && (t.nextToken is TokenKwParClose)) {
                int i = ((TokenInt)t).val;
                if ((i < 0) || (i >= scriptRoots.Length)) {
                    t.ErrorMsg ("script index out of range 0.." + (scriptRoots.Length - 1));
                    return ReadInputLine (null);
                }
                scriptRootIndex = i;
                return ReadInputLine (t.nextToken.nextToken);
            }
            if ((t is TokenStr) && (t.nextToken is TokenKwParClose)) {
                string uuid = ((TokenStr)t).val;
                if (!scriptRootPrimUUIDs.ContainsKey (uuid)) {
                    t.ErrorMsg ("unknown script prim uuid");
                    return ReadInputLine (null);
                }
                scriptRootIndex = scriptRootPrimUUIDs[uuid].index;
                return ReadInputLine (t.nextToken.nextToken);
            }

            /*
             * We know what script the event/value is for.
             * Set up to process whatever comes after it.
             */
            for (inputTokens = t; !(inputTokens is TokenEnd) && !(inputTokens is TokenKwSemi); inputTokens = inputTokens.nextToken) { }

            /*
             * Queue the event or value to its script.
             */
            ScriptRoot sr = scriptRoots[scriptRootIndex];

            // <name> : <value>       is a return <value> for API function <name>
            // <name> ( <arg>... )    is an event to queue to the script

            if (!(t is TokenName)) {
                t.ErrorMsg ("expecting event or API function name");
                return ReadInputLine (null);
            }
            string name = ((TokenName)t).val;

            // if name followed by (, queue the event to the event queue
            if (t.nextToken is TokenKwParOpen) {
                QueuedEvent qe = new QueuedEvent (sr, name, t.nextToken.nextToken);
                queuedEvents.AddLast (qe);
                return true;
            }

            // if name followed by :, queue the value to the script's value queue
            if (t.nextToken is TokenKwColon) {
                t = t.nextToken.nextToken;
                QueuedValue qv = new QueuedValue ();
                qv.name  = name;
                qv.value = ParseEHArg (ref t);
                sr.queuedValues.AddLast (qv);
                return true;
            }

            // neither, it's an error
            t.ErrorMsg ("event or API name must be followed by ( or :");
            return ReadInputLine (null);
        }

        public static void StandAloneErrorMessage (Token token, string message)
        {
            Console.WriteLine ("{0} {1}", token.SrcLoc, message);
        }

        /**
         * @brief Parse out constant for event handler argument value.
         * @param token = points to beginning of constant
         * @returns null: error parsing constant
         *          else: constant value
         *                token = points just past constant
         */
        public static object ParseEHArg (ref Token token)
        {
            /*
             * Simple unary operators.
             */
            if (token is TokenKwSub) {
                token = token.nextToken;
                Token opTok = token;
                object val = ParseEHArg (ref token);
                if (val is int) return -(int)val;
                if (val is double) return -(double)val;
                opTok.ErrorMsg ("invalid - operand");
                return null;
            }
            if (token is TokenKwTilde) {
                token = token.nextToken;
                Token opTok = token;
                object val = ParseEHArg (ref token);
                if (val is int) return ~(int)val;
                opTok.ErrorMsg ("invalid ~ operand");
                return null;
            }

            /*
             * Constants.
             */
            if (token is TokenFloat) {
                object v = ((TokenFloat)token).val;
                token = token.nextToken;
                return v;
            }
            if (token is TokenInt) {
                object v = ((TokenInt)token).val;
                token = token.nextToken;
                return v;
            }
            if (token is TokenStr) {
                object v = ((TokenStr)token).val;
                token = token.nextToken;
                return v;
            }

            /*
             * '<'value,...'>', ie, rotation or vector
             */
            if (token is TokenKwCmpLT) {
                List<double> valist = new List<double> ();
                Token openBkt = token;
                do {
                    token = token.nextToken;
                    Token valTok = token;
                    object val = ParseEHArg (ref token);
                    if (val == null) return null;
                    if (val is int) val = (double)(int)val;
                    if (!(val is double)) {
                        valTok.ErrorMsg ("invalid rotation/vector constant");
                        return null;
                    }
                    valist.Add ((double)val);
                } while (token is TokenKwComma);
                if (!(token is TokenKwCmpGT)) {
                    token.ErrorMsg ("expecting , or > at end of rotation or vector");
                    return null;
                }
                token = token.nextToken;
                double[] valarr = valist.ToArray ();
                switch (valarr.Length) {
                    case 3: {
                        return new LSL_Vector (valarr[0], valarr[1], valarr[2]);
                    }
                    case 4: {
                        return new LSL_Rotation (valarr[0], valarr[1], valarr[2], valarr[3]);
                    }
                    default: {
                        openBkt.ErrorMsg ("bad rotation/vector");
                        return null;
                    }
                }
            }

            /*
             * '['value,...']', ie, list
             */
            if (token is TokenKwBrkOpen) {
                List<object> values = new List<object> ();
                do {
                    token = token.nextToken;
                    if ((values.Count == 0) && (token is TokenKwBrkClose)) break;
                    object val = ParseEHArg (ref token);
                    if (val == null) return null;
                    if (val is int)         val = new LSL_Integer ((int)val);
                    if (val is double) val = new LSL_Float   ((double)val);
                    if (val is string)      val = new LSL_String  ((string)val);
                    values.Add (val);
                } while (token is TokenKwComma);
                if (!(token is TokenKwBrkClose)) {
                    token.ErrorMsg ("expecting , or ] in list");
                    return null;
                }
                token = token.nextToken;
                return new LSL_List (values.ToArray ());
            }

            /*
             * All we got left is <name>, lookup pre-defined constant.
             */
            if (token is TokenName) {
                FieldInfo fi = typeof (ScriptBaseClass).GetField (((TokenName)token).val);
                if ((fi == null) || !fi.IsPublic || !fi.IsStatic) {
                    token.ErrorMsg ("unknown constant");
                    return null;
                }
                token = token.nextToken;
                object val = fi.GetValue (null);
                if (val is LSL_Float)   val = (double)((LSL_Float)  val);
                if (val is LSL_Integer) val = (int)        ((LSL_Integer)val);
                if (val is LSL_String)  val = (string)     ((LSL_String) val);
                return val;
            }

            /*
             * Who knows what it is supposed to be?
             */
            token.ErrorMsg ("invalid operand token " + token.ToString ());
            return null;
        }
    }

    public class StackCaptureException : Exception, IXMRUncatchable { }
    public class ScriptResetException  : Exception, IXMRUncatchable { }
    public class ScriptDieException    : Exception, IXMRUncatchable { }

    public enum IState {
        YIELDING,
        RUNNING,
        WAITINGFOREVENT
    }

    public class Listener {
        public int channel;
        public string name;
        public string id;
        public string msg;
        public int active;
    }

    // script event read from input file
    public class QueuedEvent {
        public ScriptRoot scriptRoot;
        public ScriptEventCode eventCode;
        public object[] ehArgs;

        // null stuff indicates where an api return value is in the input
        public QueuedEvent () { }

        // from input line
        //  sr = which script it is queued to
        //  name = name of event handler
        //  t = event handler argument tokens, just after the '('
        public QueuedEvent (ScriptRoot sr, string name, Token t)
        {
            scriptRoot = sr;
            eventCode  = (ScriptEventCode) Enum.Parse (typeof (ScriptEventCode), name);

            LinkedList<object> ehargs = new LinkedList<object> ();
            if (!(t is TokenKwParClose)) {
                while (true) {
                    object val = XMREngTest.ParseEHArg (ref t);
                    if (val == null) return;
                    ehargs.AddLast (val);
                    if (!(t is TokenKwComma)) break;
                    t = t.nextToken;
                }
                if (!(t is TokenKwParClose)) {
                    t.ErrorMsg ("expecting , or )");
                    return;
                }
            }
            ehArgs = new object[ehargs.Count];
            int i = 0;
            foreach (object eharg in ehargs) ehArgs[i++] = eharg;
        }

        // internally generated
        public QueuedEvent (ScriptRoot sr, ScriptEventCode ec, int nargs)
        {
            scriptRoot = sr;
            eventCode  = ec;
            ehArgs     = new object[nargs];
        }
    }

    // api function return value read from input file
    public class QueuedValue {
        public string name;
        public object value;
    }

    /**
     * @brief This stuff does not re-instantiate on serialization.
     */
    public class ScriptRoot {
        public  int         index;
        public  int         linkNum;
        private int         peakHeapUsed;
        private int         peakStackUsed;
        public  IState      iState;
        public  Listener[]  listeners = new Listener[65];
        public  object      wfiValue;
        public  string      msgPrefix;
        public  string      primName;
        public  string      primUUID;
        public  string      wfiName;
        public  string      wfiType;
        private XMRInstance inst;

        public  LinkedList<QueuedValue> queuedValues = new LinkedList<QueuedValue> ();

        public ScriptRoot (XMRInstance inst, int index, PerPrim perPrim)
        {
            this.index    = index;
            this.inst     = inst;
            this.iState   = IState.YIELDING;
            this.linkNum  = perPrim.linkNum;
            this.primName = perPrim.name;
            this.primUUID = perPrim.uuid;

            this.msgPrefix = "";
            if (XMREngTest.scriptRoots.Length > 1) {
                if (primUUID != "") {
                    StringBuilder sb = new StringBuilder ();
                    TokenDeclInline.PrintParam (sb, primUUID);
                    sb.Append (") ");
                    this.msgPrefix = sb.ToString ();
                } else {
                    this.msgPrefix = index.ToString () + ") ";
                }
            }
            XMREngTest.scriptRoots[index] = this;

            inst.scriptRoot = this;
        }

        public void PrintStatus ()
        {
            StringBuilder sb = new StringBuilder ();
            sb.Append (msgPrefix);
            sb.Append ('[');
            sb.Append (primName);
            sb.Append (':');
            sb.Append (primUUID);
            sb.Append ("] [");
            sb.Append (inst.m_ObjCode.stateNames[inst.stateCode]);
            if (inst.eventCode != ScriptEventCode.None) {
                sb.Append ('.');
                sb.Append (inst.eventCode.ToString ());
                if (inst.ehArgs != null) {
                    sb.Append ('(');
                    bool first = true;
                    foreach (object ehArg in inst.ehArgs) {
                        if (!first) sb.Append (',');
                        TokenDeclInline.PrintParam (sb, ehArg);
                        first = false;
                    }
                    sb.Append (')');
                }
            }
            sb.Append ("] ");
            sb.Append (iState.ToString ());
            Console.WriteLine (sb.ToString ());
        }

        public int GetPeakHeapUsed ()
        {
            if (peakHeapUsed < inst.peakHeapUsed) {
                peakHeapUsed = inst.peakHeapUsed;
            }
            return peakHeapUsed;
        }

        public int GetPeakStackUsed ()
        {
            if (peakStackUsed < inst.peakStackUsed) {
                peakStackUsed = inst.peakStackUsed;
            }
            return peakStackUsed;
        }

        /**
         * @brief Process internally queued events.
         */
        public void ProcessQueuedEvent (QueuedEvent qe)
        {
            inst.eventCode = qe.eventCode;
            inst.ehArgs    = qe.ehArgs;
            iState = IState.YIELDING;
        }

        /**
         * @brief Step script and wait for it until it wants more input.
         */
        public void StepScript ()
        {
            /*
            Console.WriteLine ("StepScript*: iState=" + iState);
            for (XMRStackFrame sf = inst.stackFrames; sf != null; sf = sf.nextSF) {
                Console.WriteLine ("StepScript*:   " + sf.funcName + " " + sf.callNo);
            }
            */

            /*
             * Run the script event handler code on the uthread stack until it calls
             * Hiber() or it finishes.
             * If the stack is valid (ie it called Hiber() last time), resume from where 
             * it left off.  Otherwise, the stack is not valid, so start the event hander
             * from very beginning.
             */
            iState = IState.RUNNING;
            try {
                inst.CallSEH ();
                iState = IState.WAITINGFOREVENT;
            } catch (StackCaptureException) {
                iState = IState.YIELDING;
            } catch (Exception e) {
                Console.WriteLine (msgPrefix + "exception in script: " + e.Message);
                Console.WriteLine (inst.xmrExceptionStackTrace (e));
                Environment.Exit (3);
            }

            if (peakHeapUsed < inst.peakHeapUsed) {
                peakHeapUsed = inst.peakHeapUsed;
            }

            if (peakStackUsed < inst.peakStackUsed) {
                peakStackUsed = inst.peakStackUsed;
            }

            /*
             * Maybe we are doing -serialize.
             */
            if (XMREngTest.serializeStream != null) {

                /*
                 * Write global variables and stack frames out and discard them.
                 */
                XMREngTest.serializeStream.Position = 0;
                inst.MigrateOut (new BinaryWriter (XMREngTest.serializeStream));
                long savePos = XMREngTest.serializeStream.Position;

                /*
                 * Read global variables and stack frames back in and reconstruct.
                 * Create a new XMRInstance to simulate the script hibernating for
                 * days and sim crashes etc.
                 */
                XMREngTest.serializeStream.Position = 0;
                inst = new XMRInstance (inst);
                inst.MigrateIn (new BinaryReader (XMREngTest.serializeStream));
                if (savePos != XMREngTest.serializeStream.Position) {
                    throw new Exception ("save/restore positions different");
                }
            }
        }
    }

    /**
     * @brief This stuff is re-instantiated on serialization.
     */
    public partial class XMRInstance : XMRInstAbstract {
        public static int HEAPLIMIT = 65536;
        public static int STACKSIZE = 65536;
        public ScriptBaseClass m_ApiManager_LSL;
        public OpenSim.Region.ScriptEngine.Shared.Api.Interfaces.IOSSL_Api m_ApiManager_OSSL;

        public int peakHeapUsed  = 0;
        public int peakStackUsed = 0;
        public string m_DescName;

        /**
         * @brief Initial instance for the given script
         */
        public XMRInstance (ScriptObjCode soc)
        {
            m_ObjCode   = soc;
            heapLimit   = HEAPLIMIT;
            m_StackLeft = STACKSIZE;
            glblVars.AllocVarArrays (m_ObjCode.glblSizes);
            suspendOnCheckRunHold = XMREngTest.doCheckRun || XMREngTest.printPeakStack;

            /*
             * Run default state_entry() handler until it is ready for more input.
             */
            ehArgs    = new object[0];
            stateCode = 0;  // default
            eventCode = ScriptEventCode.state_entry;
        }

        /**
         * @brief Create a new instance that is being reloaded for serialization test.
         */
        public XMRInstance (XMRInstance inst)
        {
            scriptRoot  = inst.scriptRoot;
            m_ObjCode   = inst.m_ObjCode;
            m_DescName  = inst.m_DescName;
            heapLimit   = HEAPLIMIT;
            m_StackLeft = STACKSIZE;
            suspendOnCheckRunHold = XMREngTest.doCheckRun || XMREngTest.printPeakStack;
        }

        /*********************\
         *  XMRInstAbstract  *
        \*********************/

        public override int UpdateHeapUse (int olduse, int newuse)
        {
            int rc = base.UpdateHeapUse (olduse, newuse);
            int hu = xmrHeapUsed ();
            if (peakHeapUsed < hu) peakHeapUsed = hu;
            return rc;
        }

        /**
         * @brief Gets called by scripts on entry to functions or in loops 
         *        whenever suspendOnCheckRunHold or suspendOnCheckRunTemp set.
         *        In the tester, it gets called only when -checkrun, -serialize
         *        or -printstack options were given on the command line.
         */
        public override void CheckRunWork ()
        {
            int stackUsed = STACKSIZE - m_StackLeft;
            if (peakStackUsed < stackUsed) peakStackUsed = stackUsed;

            if (!XMREngTest.doCheckRun) return;

            if (stackFrames != null) throw new Exception ("frames left over");

            switch (callMode) {

                case CallMode_RESTORE: {
                    callMode = CallMode_NORMAL;
                    return;
                }

                case CallMode_NORMAL: {
                    callMode = CallMode_SAVE;
                    throw new StackCaptureException ();
                }

                default: throw new Exception ("callMode=" + callMode);
            }
        }

        public override void StateChange ()
        {
            Console.WriteLine ("Change to state " + m_ObjCode.stateNames[stateCode]);
            scriptRoot.listeners = new Listener[scriptRoot.listeners.Length];
        }

        [xmrMethodIsNoisyAttribute]  // calls Stub<somethingorother>
        public override LSL_List xmrEventDequeue (double timeout, int returnMask1, int returnMask2,
                                                  int backgroundMask1, int backgroundMask2)
        {
            return StubLSLList ("xmrEventDequeue", timeout, returnMask1, returnMask2, backgroundMask1, backgroundMask2);
        }

        [xmrMethodIsNoisyAttribute]  // calls Stub<somethingorother>
        public override void xmrEventEnqueue (LSL_List ev)
        {
            StubVoid ("xmrEventEnqueue", ev);
        }

        [xmrMethodIsNoisyAttribute]  // calls Stub<somethingorother>
        public override LSL_List xmrEventSaveDets ()
        {
            return StubLSLList ("xmrEventSaveDets");
        }

        [xmrMethodIsNoisyAttribute]  // calls Stub<somethingorother>
        public override void xmrEventLoadDets (LSL_List dpList)
        {
            StubVoid ("xmrEventLoadDets", dpList);
        }

        [xmrMethodIsNoisyAttribute]  // calls Stub<somethingorother>
        public override void xmrTrapRegionCrossing (int enable)
        {
            StubVoid ("xmrTrapRegionCrossing", enable);
        }

        [xmrMethodIsNoisyAttribute]  // calls Stub<somethingorother>
        public override bool xmrSetObjRegPosRotAsync (LSL_Vector pos, LSL_Rotation rot, int options, int evcode, LSL_List evargs)
        {
            return StubLSLInteger ("xmrSetObjRegPosRotAsync", pos, rot, options, evcode, evargs) != 0;
        }

        /************************************\
         *  Copied from XMRInstScriptDB.cs  *
        \************************************/

        /**
         * Write list, one element per line.
         *  Input:
         *   key = object unique key
         *   value = list of lines to write
         */
        public override void xmrScriptDBWriteLines (string key, LSL_List value)
        {
            StringBuilder sb = new StringBuilder ();
            for (int i = 0; i < value.Length; i ++) {
                sb.Append (value.GetLSLStringItem (i).m_string);
                sb.Append ('\n');
            }
            xmrScriptDBWrite (key, sb.ToString ());
        }

        /**
         * Read single line of a particular element.
         *  Input:
         *   key = as given to xmrScriptDBWriteList()
         *   notfound = "ERROR!"
         *   endoffile = "\n\n\n" (EOF)
         *  Output:
         *   returns contents of the line or notfound or endoffile
         */
        public override string xmrScriptDBReadLine (string key, int line, string notfound, string endoffile)
        {
            int i, j;
            string whole = xmrScriptDBReadOne (key, null);
            if (whole == null) return notfound;
            for (i = 0; (j = whole.IndexOf ('\n', i)) >= 0; i = ++ j) {
                if (-- line < 0) return whole.Substring (i, j - i);
            }
            return endoffile;
        }

        /**
         * Get number of lines in notecard.
         *  Input:
         *   key = as given to xmrScriptDBWriteList()
         *  Output:
         *   returns -1: notecard not found
         *         else: number of lines
         */
        public override int xmrScriptDBNumLines (string key)
        {
            int i, j, n;
            string whole = xmrScriptDBReadOne (key, null);
            if (whole == null) return -1;
            n = 0;
            for (i = 0; (j = whole.IndexOf ('\n', i)) >= 0; i = ++ j) {
                n ++;
            }
            return n;
        }

        /**
         * Read all lines of a particular element.
         *  Input:
         *   key = as given to xmrScriptDBWriteList()
         *   notfound = [ "ERROR!" ]
         *  Output:
         *   returns contents of the element or notfound
         */
        public override LSL_List xmrScriptDBReadLines (string key, LSL_List notfound)
        {
            int i, j, n;
            string whole = xmrScriptDBReadOne (key, null);
            if (whole == null) return notfound;
            n = 0;
            for (i = 0; (j = whole.IndexOf ('\n', i)) >= 0; i = ++ j) {
                n ++;
            }
            object[] array = new object[n];
            n = 0;
            for (i = 0; (j = whole.IndexOf ('\n', i)) >= 0; i = ++ j) {
                array[n++] = new LSL_String (whole.Substring (i, j - i));
            }
            return new LSL_List (array);
        }

        /*********************************************\
         *  Test implementation of rest of ScriptDB  *
        \*********************************************/

        private static SortedDictionary<string,string> scriptdb =
                new SortedDictionary<string,string> ();

        public override void xmrScriptDBWrite (string key, string value)
        {
            scriptdb[key] = value;
        }

        public override string xmrScriptDBReadOne (string key, string notfound)
        {
            string value;
            return scriptdb.TryGetValue (key, out value) ? value : notfound;
        }

        public override int xmrScriptDBCount (string keylike)
        {
            int count = 0;
            foreach (KeyValuePair<string,string> kvp in scriptdb) {
                if (MatchesLike (kvp.Key, keylike)) count ++;
            }
            return count;
        }

        public override LSL_List xmrScriptDBList (string keylike, int limit, int offset)
        {
            LinkedList<string> list = new LinkedList<string> ();
            foreach (KeyValuePair<string,string> kvp in scriptdb) {
                if (MatchesLike (kvp.Key, keylike) && (-- offset < 0)) {
                    if (-- limit < 0) break;
                    list.AddLast (kvp.Key);
                }
            }
            object[] array = new object[list.Count];
            int i = 0;
            foreach (string key in list) array[i++] = key;
            return new LSL_List (array);
        }

        public override XMR_Array xmrScriptDBReadMany (string keylike, int limit, int offset)
        {
            XMR_Array array = new XMR_Array (this);
            foreach (KeyValuePair<string,string> kvp in scriptdb) {
                if (MatchesLike (kvp.Key, keylike) && (-- offset < 0)) {
                    if (-- limit < 0) break;
                    array.SetByKey (kvp.Key, kvp.Value);
                }
            }
            return array;
        }

        public override int xmrScriptDBDelete (string keylike)
        {
            LinkedList<string> list = new LinkedList<string> ();
            foreach (KeyValuePair<string,string> kvp in scriptdb) {
                if (MatchesLike (kvp.Key, keylike)) {
                    list.AddLast (kvp.Key);
                }
            }
            foreach (string key in list) scriptdb.Remove (key);
            return list.Count;
        }

        private static bool MatchesLike (string key, string like)
        {
            int ki = 0;
            int kj = key.Length;
            int li = 0;
            int lj = like.Length;

            // optimization: trim matching chars off ends
            if (like.IndexOf ('\\') < 0) {
                while ((ki < kj) && (li < lj)) {
                    char kc = key[kj-1];
                    char lc = like[lj-1];
                    if (lc == '%') break;
                    if ((lc != '_') && (kc != lc)) break;
                    -- kj;
                    -- lj;
                }
            }

            // keep going as long as ttere are 'like' chars
            while (li < lj) {

                // get a like char and decode it
                char lc = like[li++];
                switch (lc) {

                    // match any number of key chars
                    case '%': {

                        // optimization: if like was just the '%;", instant match
                        if (li == lj) return true;

                        // try to match key against remaining like
                        // trimming one char at a time from front of key
                        for (int kk = ki; kk < kj; kk ++) {
                            if (MatchesLike (key.Substring (kk, kj - kk),
                                            like.Substring (li, lj - li))) return true;
                        }

                        // that didn't work
                        return false;
                    }

                    // match exactly one char from key
                    case '_': {
                        if (ki == kj) return false;
                        ki ++;
                        break;
                    }

                    // escape next like char
                    case '\\': {
                        if (li == lj) return ki == kj;
                        lc = like[li++];
                        if (ki == kj) return false;
                        char kc = key[ki++];
                        if (kc != lc) return false;
                        break;
                    }

                    // match exact char in key
                    default: {
                        if (ki == kj) return false;
                        char kc = key[ki++];
                        if (kc != lc) return false;
                        break;
                    }
                }
            }

            // no more like, key better be all matched up
            return ki == kj;
        }
    }

    public partial class ScriptBaseClass :
            OpenSim.Region.ScriptEngine.Shared.Api.Interfaces.ILSL_Api,
            OpenSim.Region.ScriptEngine.Shared.Api.Interfaces.IOSSL_Api {

        public ScriptRoot scriptRoot;

        public void llResetScript ()
        {
            throw new ScriptResetException ();
        }

        public void llDie ()
        {
            throw new ScriptDieException ();
        }

        public System.Collections.Hashtable osParseJSON (string json)
        {
            throw new Exception ("not implemented - should be using XMRInstAbstract.osParseJSON()");
        }

        public void state (string newstate)
        {
            string[] stateNames = ((XMRInstance)this).m_ObjCode.stateNames;
            for (int i = 0; i < stateNames.Length; i ++) {
                if (stateNames[i] == newstate) {
                    throw new ScriptChangeStateException (i);
                }
            }
            throw new ScriptUndefinedStateException (newstate);
        }

        /**
         * @brief If channel is enabled via -ipcchannel, we handle message passing internally.
         *        Otherwise, we play dumb and print the call out and read the return value in.
         */
        [xmrMethodIsNoisyAttribute]
        public LSL_Integer llListen (int channel, string name, string id, string msg)
        {
            foreach (int ipc in XMREngTest.ipcChannels) {
                if (channel == ipc) goto gotit;
            }
            return StubLSLInteger ("llListen", channel, name, id, msg);

        gotit:
            if (id == NULL_KEY) id = "";

            Listener lner = new Listener ();
            lner.channel  = channel;
            lner.name     = name;
            lner.id       = id;
            lner.msg      = msg;
            lner.active   = 1;

            for (int i = 0; i < scriptRoot.listeners.Length; i ++) {
                if (scriptRoot.listeners[i] == null) {
                    scriptRoot.listeners[i] = lner;
                    return ++ i;
                }
            }
            throw new Exception ("too many listens");
        }

        /**
         * @brief Try to remove the listener from -ipcchannel list.
         *        Then always play dumb and print it out.
         */
        [xmrMethodIsNoisyAttribute]
        public void llListenRemove (int handle)
        {
            int i = handle;
            if ((-- i >= 0) && (i < scriptRoot.listeners.Length)) {
                scriptRoot.listeners[i] = null;
            }
            StubVoid ("llListenRemove", handle);
        }

        /**
         * @brief Try to update the listener from -ipcchannel list.
         *        Then always play dumb and print it out.
         */
        [xmrMethodIsNoisyAttribute]
        public void llListenControl (int handle, int active)
        {
            int i = handle;
            if ((-- i >= 0) && (i < scriptRoot.listeners.Length) && (scriptRoot.listeners[i] != null)) {
                scriptRoot.listeners[i].active = active;
            }
            StubVoid ("llListenControl", handle, active);
        }

        /**
         * @brief If doing llSay() to an -ipcchannel enabled channel and there is something
         *        listening on that channel, pass the message along quietly.
         *        Otherwise announce message.
         * Note:  For xmrengtest purposes, this function is the same as llRegionSay().
         */
        [xmrMethodIsNoisyAttribute]
        public void llSay (int channel, string msg)
        {
            ISay ("llSay", null, channel, msg);
        }

        /**
         * @brief If doing llRegionSay() to an -ipcchannel enabled channel and there is something
         *        listening on that channel, pass the message along quietly.
         *        Otherwise announce message.
         * Note:  For xmrengtest purposes, this function is the same as llSay().
         */
        [xmrMethodIsNoisyAttribute]
        public void llRegionSay (int channel, string msg)
        {
            ISay ("llRegionSay", null, channel, msg);
        }

        /**
         * @brief If doing llRegionSayTo() to an -ipcchannel enabled channel and there is something
         *        listening on that channel, pass the message along quietly.
         *        Otherwise announce message.
         */
        [xmrMethodIsNoisyAttribute]
        public void llRegionSayTo (string target, int channel, string msg)
        {
            ISay ("llRegionSayTo", target, channel, msg);
        }

        private void ISay (string fname, string target, int channel, string msg)
        {
            if (msg.Length > 1023) msg = msg.Substring (0, 1023);

            /*
             * Maybe it gets queued as an -ipcchannel listen event somewhere.
             */
            bool queued = false;
            foreach (ScriptRoot targsr in XMREngTest.scriptRoots) {
                if ((targsr != scriptRoot) && (target == null || targsr.primUUID == target)) {
                    foreach (Listener lner in targsr.listeners) {
                        if (lner == null) continue;
                        if ((lner.channel == channel) && (lner.active != 0)) {
                            if ((lner.id   != "") && (lner.id   != scriptRoot.primUUID)) continue;
                            if ((lner.name != "") && (lner.name != scriptRoot.primName)) continue;
                            if ((lner.msg  != "") && (lner.msg  != msg))                 continue;

                            QueuedEvent qe = new QueuedEvent (targsr, ScriptEventCode.listen, 4);
                            qe.ehArgs[0]   = channel;
                            qe.ehArgs[1]   = scriptRoot.primName;
                            qe.ehArgs[2]   = scriptRoot.primUUID;
                            qe.ehArgs[3]   = msg;
                            XMREngTest.queuedEvents.AddLast (qe);
                            queued = true;
                            break;
                        }
                    }
                }
            }

            /*
             * Maybe print it out.
             */
            if (!queued || (channel == PUBLIC_CHANNEL) || (channel == DEBUG_CHANNEL)) {
                if (target == null) {
                    StubVoid (fname, channel, msg);
                } else {
                    StubVoid (fname, target, channel, msg);
                }
            }
        }

        [xmrMethodIsNoisyAttribute]
        public LSL_String llGetKey ()
        {
            if (scriptRoot.primUUID == "") {
                return StubLSLString ("llGetKey");
            }
            return scriptRoot.primUUID;
        }

        [xmrMethodIsNoisyAttribute]
        public LSL_String llGetObjectName ()
        {
            if (scriptRoot.primName == "") {
                return StubLSLString ("llGetObjectName");
            }
            return scriptRoot.primName;
        }

        [xmrMethodIsNoisyAttribute]
        public LSL_List llListRandomize(LSL_List src, int stride)
        {
            object[] parms = new object[] { src, stride };
            PrintParms ("llListRandomize", parms);
            object rv = ReadRetVal ("llListRandomize", "integer");
            if (!(rv is int)) return (LSL_List)rv;

            if (stride <= 0) stride = 1;
            object[] source = src.Data;
            int length = source.Length;
            if ((stride >= length) || (length % stride != 0)) return src;

            Random rand = new Random ((int)rv);
            object[] output = new object[length];

            if (stride == 1) {
                Array.Copy (source, 0, output, 0, length);
                for (int i = length; -- i >= 0;) {
                    int j = rand.Next (length);
                    object tmp = output[i];
                    output[i] = output[j];
                    output[j] = tmp;
                }
            } else {
                int nbucks = length / stride;
                int[] buckts = new int[nbucks];
                for (int i = 0; i < nbucks; i ++) {
                    buckts[i] = i;
                }

                for (int i = nbucks; -- i >= 0;) {
                    int j = rand.Next (nbucks);
                    int tmp = buckts[i];
                    buckts[i] = buckts[j];
                    buckts[j] = tmp;
                }

                for (int i = 0; i < nbucks; i ++) {
                    int j = buckts[i];
                    Array.Copy (source, i * stride, output, j * stride, stride);
                }
            }

            return new LSL_List (output);
        }

        [xmrMethodIsNoisyAttribute]
        public LSL_Integer llGetLinkNumber ()
        {
            if (!XMREngTest.haveLinkNums) {
                return StubLSLInteger ("llGetLinkNumber");
            }
            return scriptRoot.linkNum;
        }

        [xmrMethodIsNoisyAttribute]
        public void llMessageLinked (int link, int num, string str, string id)
        {
            if (!XMREngTest.haveLinkNums) {
                StubVoid ("llMessageLinked", link, num, str, id);
                return;
            }

            /*
             * See if it can be queued to any scripts.
             */
            foreach (ScriptRoot targsr in XMREngTest.scriptRoots) {
                switch (link) {
                    case LINK_SET: {
                        break;
                    }
                    case LINK_ALL_OTHERS: {
                        if (targsr != scriptRoot) break;
                        continue;
                    }
                    case LINK_ALL_CHILDREN: {
                        if (targsr.linkNum > 1) break;
                        continue;
                    }
                    case LINK_THIS: {
                        if (targsr == scriptRoot) break;
                        continue;
                    }
                    default: {
                        if (targsr.linkNum == link) break;
                        continue;
                    }
                }

                QueuedEvent qe = new QueuedEvent (targsr, ScriptEventCode.link_message, 4);
                qe.ehArgs[0]   = scriptRoot.linkNum;
                qe.ehArgs[1]   = num;
                qe.ehArgs[2]   = str;
                qe.ehArgs[3]   = id;
                XMREngTest.queuedEvents.AddLast (qe);
            }
        }

        /*************************\
         *  Copied from OpenSim  *
        \*************************/

        public LSL_Integer llSubStringIndex(string source, string pattern)
        {
            return source.IndexOf(pattern);
        }

        public LSL_String llGetSubString(string src, int start, int end)
        {
            // Normalize indices (if negative).
            // After normlaization they may still be
            // negative, but that is now relative to
            // the start, rather than the end, of the
            // sequence.

            if (start < 0)
            {
                start = src.Length+start;
            }
            if (end < 0)
            {
                end = src.Length+end;
            }

            // Conventional substring
            if (start <= end)
            {
                // Implies both bounds are out-of-range.
                if (end < 0 || start >= src.Length)
                {
                    return String.Empty;
                }
                // If end is positive, then it directly
                // corresponds to the lengt of the substring
                // needed (plus one of course). BUT, it
                // must be within bounds.
                if (end >= src.Length)
                {
                    end = src.Length-1;
                }

                if (start < 0)
                {
                    return src.Substring(0,end+1);
                }
                // Both indices are positive
                return src.Substring(start, (end+1) - start);
            }

            // Inverted substring (end < start)
            else
            {
                // Implies both indices are below the
                // lower bound. In the inverted case, that
                // means the entire string will be returned
                // unchanged.
                if (start < 0)
                {
                    return src;
                }
                // If both indices are greater than the upper
                // bound the result may seem initially counter
                // intuitive.
                if (end >= src.Length)
                {
                    return src;
                }

                if (end < 0)
                {
                    if (start < src.Length)
                    {
                        return src.Substring(start);
                    }
                    else
                    {
                        return String.Empty;
                    }
                }
                else
                {
                    if (start < src.Length)
                    {
                        return src.Substring(0,end+1) + src.Substring(start);
                    }
                    else
                    {
                        return src.Substring(0,end+1);
                    }
                }
            }
        }

        protected static readonly int[] c2itable =
        {
            -1,-1,-1,-1,-1,-1,-1,-1,    // 0x
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1,    // 1x
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1,    // 2x
            -1,-1,-1,63,-1,-1,-1,64,
            53,54,55,56,57,58,59,60,    // 3x
            61,62,-1,-1,-1,0,-1,-1,
            -1,1,2,3,4,5,6,7,           // 4x
            8,9,10,11,12,13,14,15,
            16,17,18,19,20,21,22,23,    // 5x
            24,25,26,-1,-1,-1,-1,-1,
            -1,27,28,29,30,31,32,33,    // 6x
            34,35,36,37,38,39,40,41,
            42,43,44,45,46,47,48,49,    // 7x
            50,51,52,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1,    // 8x
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1,    // 9x
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1,    // Ax
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1,    // Bx
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1,    // Cx
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1,    // Dx
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1,    // Ex
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1,    // Fx
            -1,-1,-1,-1,-1,-1,-1,-1
        };
        protected static readonly char[] i2ctable =
        {
            'A','B','C','D','E','F','G','H',
            'I','J','K','L','M','N','O','P',
            'Q','R','S','T','U','V','W','X',
            'Y','Z',
            'a','b','c','d','e','f','g','h',
            'i','j','k','l','m','n','o','p',
            'q','r','s','t','u','v','w','x',
            'y','z',
            '0','1','2','3','4','5','6','7',
            '8','9',
            '+','/'
        };

        public LSL_Float llAcos (double val)
        {
            return (double)Math.Acos(val);
        }
        public LSL_Float llAngleBetween (LSL_Rotation a, LSL_Rotation b)
        {
            double angle = Math.Acos(a.x * b.x + a.y * b.y + a.z * b.z + a.s * b.s) * 2;
            if (angle < 0) angle = -angle;
            if (angle > Math.PI) return (Math.PI * 2 - angle);
            return angle;
        }
        public LSL_Float llAsin (double val)
        {
            return (double)Math.Asin(val);
        }
        public LSL_Float llAtan2 (double x, double y)
        {
            return (double)Math.Atan2(x, y);
        }
        public LSL_Float llCos (double f)
        {
            return (double)Math.Cos(f);
        }
        public LSL_Float llFabs (double f)
        {
            return (double)Math.Abs(f);
        }
        public LSL_Float llList2Float (LSL_List src, int index)
        {
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length || index < 0)
            {
                return 0.0;
            }
            try
            {
                if (src.Data[index] is LSL_Integer)
                    return Convert.ToDouble(((LSL_Integer) src.Data[index]).value);
                else if (src.Data[index] is LSL_Float)
                    return Convert.ToDouble(((LSL_Float) src.Data[index]).value);
                else if (src.Data[index] is LSL_String)
                    return Convert.ToDouble(((LSL_String) src.Data[index]).m_string);
                return Convert.ToDouble(src.Data[index]);
            }
            catch (FormatException)
            {
                return 0.0;
            }
        }
        public LSL_Float llListStatistics (int operation, LSL_List src)
        {
            LSL_List nums = LSL_List.ToDoubleList(src);
            switch (operation)
            {
                case LIST_STAT_RANGE:
                    return nums.Range();
                case LIST_STAT_MIN:
                    return nums.Min();
                case LIST_STAT_MAX:
                    return nums.Max();
                case LIST_STAT_MEAN:
                    return nums.Mean();
                case LIST_STAT_MEDIAN:
                    return nums.Median();
                case LIST_STAT_NUM_COUNT:
                    return nums.NumericLength();
                case LIST_STAT_STD_DEV:
                    return nums.StdDev();
                case LIST_STAT_SUM:
                    return nums.Sum();
                case LIST_STAT_SUM_SQUARES:
                    return nums.SumSqrs();
                case LIST_STAT_GEOMETRIC_MEAN:
                    return nums.GeometricMean();
                case LIST_STAT_HARMONIC_MEAN:
                    return nums.HarmonicMean();
                default:
                    return 0.0;
            }
        }
        public LSL_Float llLog10 (double val)
        {
            return (double)Math.Log10(val);
        }
        public LSL_Float llLog (double val)
        {
            return (double)Math.Log(val);
        }
        public LSL_Float llPow (double fbase, double fexponent)
        {
            return (double)Math.Pow(fbase, fexponent);
        }
        public LSL_Float llRot2Angle (LSL_Rotation rot)
        {

            if (rot.s > 1) // normalization needed
            {
                double length = Math.Sqrt(rot.x * rot.x + rot.y * rot.y +
                        rot.z * rot.z + rot.s * rot.s);

                rot.x /= length;
                rot.y /= length;
                rot.z /= length;
                rot.s /= length;
            }

            double angle = 2 * Math.Acos(rot.s);

            return angle;
        }
        public LSL_Float llSin (double f)
        {
            return (double)Math.Sin(f);
        }
        public LSL_Float llSqrt (double f)
        {
            return (double)Math.Sqrt(f);
        }
        public LSL_Float llTan (double f)
        {
            return (double)Math.Tan(f);
        }
        public LSL_Float llVecDist (LSL_Vector a, LSL_Vector b)
        {
            double dx = a.x - b.x;
            double dy = a.y - b.y;
            double dz = a.z - b.z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        public LSL_Float llVecMag (LSL_Vector v)
        {
            return LSL_Vector.Mag(v);
        }
        public LSL_Integer llAbs (int i)
        {
            // changed to replicate LSL behaviour whereby minimum int value is returned untouched.
            if (i == Int32.MinValue)
                return i;
            else
                return (int)Math.Abs(i);
        }
        public LSL_Integer llBase64ToInteger (string str)
        {
            int number = 0;
            int digit;


            //    Require a well-fromed base64 string

            if (str.Length > 8)
                return 0;

            //    The loop is unrolled in the interests
            //    of performance and simple necessity.
            //
            //    MUST find 6 digits to be well formed
            //      -1 == invalid
            //       0 == padding

            if ((digit = c2itable[str[0]]) <= 0)
            {
                return digit < 0 ? (int)0 : number;
            }
            number += --digit<<26;

            if ((digit = c2itable[str[1]]) <= 0)
            {
                return digit < 0 ? (int)0 : number;
            }
            number += --digit<<20;

            if ((digit = c2itable[str[2]]) <= 0)
            {
                return digit < 0 ? (int)0 : number;
            }
            number += --digit<<14;

            if ((digit = c2itable[str[3]]) <= 0)
            {
                return digit < 0 ? (int)0 : number;
            }
            number += --digit<<8;

            if ((digit = c2itable[str[4]]) <= 0)
            {
                return digit < 0 ? (int)0 : number;
            }
            number += --digit<<2;

            if ((digit = c2itable[str[5]]) <= 0)
            {
                return digit < 0 ? (int)0 : number;
            }
            number += --digit>>4;

            // ignore trailing padding

            return number;
        }
        public LSL_Integer llCeil (double f)
        {
            return (int)Math.Ceiling(f);
        }
        public LSL_Integer llFloor (double f)
        {
            return (int)Math.Floor(f);
        }
        public LSL_Integer llGetListEntryType (LSL_List src, int index)
        {
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length)
            {
                return 0;
            }

            if (src.Data[index] is LSL_Integer || src.Data[index] is Int32)
                return 1;
            if (src.Data[index] is LSL_Float || src.Data[index] is Single || src.Data[index] is Double)
                return 2;
            if (src.Data[index] is LSL_String || src.Data[index] is String)
            {
                OpenMetaverse.UUID tuuid;
                if (OpenMetaverse.UUID.TryParse(src.Data[index].ToString(), out tuuid))
                {
                    return 4;
                }
                else
                {
                    return 3;
                }
            }
            if (src.Data[index] is LSL_Vector)
                return 5;
            if (src.Data[index] is LSL_Rotation)
                return 6;
            if (src.Data[index] is LSL_List)
                return 7;
            return 0;

        }
        public LSL_Integer llGetListLength (LSL_List src)
        {
            return src.Length;
        }
        public LSL_Integer llList2Integer (LSL_List src, int index)
        {
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length || index < 0)
            {
                return 0;
            }
            try
            {
                if (src.Data[index] is LSL_Integer)
                    return (LSL_Integer) src.Data[index];
                else if (src.Data[index] is LSL_Float)
                    return Convert.ToInt32(((LSL_Float) src.Data[index]).value);
                return new LSL_Integer(src.Data[index].ToString());
            }
            catch (FormatException)
            {
                return 0;
            }
        }
        public LSL_Integer llListFindList (LSL_List src, LSL_List test)
        {

            int index  = -1;
            int length = src.Length - test.Length + 1;


            // If either list is empty, do not match

            if (src.Length != 0 && test.Length != 0)
            {
                for (int i = 0; i < length; i++)
                {
                    if (src.Data[i].Equals(test.Data[0]))
                    {
                        int j;
                        for (j = 1; j < test.Length; j++)
                            if (!src.Data[i+j].Equals(test.Data[j]))
                                break;
                        if (j == test.Length)
                        {
                            index = i;
                            break;
                        }
                    }
                }
            }

            return index;

        }
        public LSL_Integer llModPow (int a, int b, int c)
        {
            Int64 tmp = 0;
            Math.DivRem(Convert.ToInt64(Math.Pow(a, b)), c, out tmp);
            return Convert.ToInt32(tmp);
        }
        public LSL_Integer llRound (double f)
        {
            return (int)Math.Round(f, MidpointRounding.AwayFromZero);
        }
        public LSL_Integer llStringLength (string str)
        {
            if (str.Length > 0)
            {
                return str.Length;
            }
            else
            {
                return 0;
            }
        }
        public LSL_List llCSV2List (string src)
        {

            LSL_List result = new LSL_List();
            int parens = 0;
            int start  = 0;
            int length = 0;


            for (int i = 0; i < src.Length; i++)
            {
                switch (src[i])
                {
                    case '<':
                        parens++;
                        length++;
                        break;
                    case '>':
                        if (parens > 0)
                            parens--;
                        length++;
                        break;
                    case ',':
                        if (parens == 0)
                        {
                            result.Add(new LSL_String(src.Substring(start,length).Trim()));
                            start += length+1;
                            length = 0;
                        }
                        else
                        {
                            length++;
                        }
                        break;
                    default:
                        length++;
                        break;
                }
            }

            result.Add(new LSL_String(src.Substring(start,length).Trim()));

            return result;
        }
        public LSL_List llDeleteSubList (LSL_List src, int start, int end)
        {
            return src.DeleteSublist(start, end);
        }
        public LSL_List llList2List (LSL_List src, int start, int end)
        {
            return src.GetSublist(start, end);
        }
        public LSL_List llList2ListStrided (LSL_List src, int start, int end, int stride)
        {

            LSL_List result = new LSL_List();
            int[] si = new int[2];
            int[] ei = new int[2];
            bool twopass = false;


            //  First step is always to deal with negative indices

            if (start < 0)
                start = src.Length+start;
            if (end   < 0)
                end   = src.Length+end;

            //  Out of bounds indices are OK, just trim them
            //  accordingly

            if (start > src.Length)
                start = src.Length;

            if (end > src.Length)
                end = src.Length;

            if (stride == 0)
                stride = 1;

            //  There may be one or two ranges to be considered

            if (start != end)
            {

                if (start <= end)
                {
                   si[0] = start;
                   ei[0] = end;
                }
                else
                {
                   si[1] = start;
                   ei[1] = src.Length;
                   si[0] = 0;
                   ei[0] = end;
                   twopass = true;
                }

                //  The scan always starts from the beginning of the
                //  source list, but members are only selected if they
                //  fall within the specified sub-range. The specified
                //  range values are inclusive.
                //  A negative stride reverses the direction of the
                //  scan producing an inverted list as a result.

                if (stride > 0)
                {
                    for (int i = 0; i < src.Length; i += stride)
                    {
                        if (i<=ei[0] && i>=si[0])
                            result.Add(src.Data[i]);
                        if (twopass && i>=si[1] && i<=ei[1])
                            result.Add(src.Data[i]);
                    }
                }
                else if (stride < 0)
                {
                    for (int i = src.Length - 1; i >= 0; i += stride)
                    {
                        if (i <= ei[0] && i >= si[0])
                            result.Add(src.Data[i]);
                        if (twopass && i >= si[1] && i <= ei[1])
                            result.Add(src.Data[i]);
                    }
                }
            }
            else
            {
                if (start%stride == 0)
                {
                    result.Add(src.Data[start]);
                }
            }

            return result;
        }
        public LSL_List llListInsertList (LSL_List dest, LSL_List src, int index)
        {

            LSL_List pref;
            LSL_List suff;


            if (index < 0)
            {
                index = index+dest.Length;
                if (index < 0)
                {
                    index = 0;
                }
            }

            if (index != 0)
            {
                pref = dest.GetSublist(0,index-1);
                if (index < dest.Length)
                {
                    suff = dest.GetSublist(index,-1);
                    return pref + src + suff;
                }
                else
                {
                    return pref + src;
                }
            }
            else
            {
                if (index < dest.Length)
                {
                    suff = dest.GetSublist(index,-1);
                    return src + suff;
                }
                else
                {
                    return src;
                }
            }

        }
        public LSL_List llListReplaceList (LSL_List dest, LSL_List src, int start, int end)
        {
            LSL_List pref;


            // Note that although we have normalized, both
            // indices could still be negative.
            if (start < 0)
            {
                start = start+dest.Length;
            }

            if (end < 0)
            {
                end = end+dest.Length;
            }
            // The comventional case, remove a sequence starting with
            // start and ending with end. And then insert the source
            // list.
            if (start <= end)
            {
                // If greater than zero, then there is going to be a
                // surviving prefix. Otherwise the inclusive nature
                // of the indices mean that we're going to add the
                // source list as a prefix.
                if (start > 0)
                {
                    pref = dest.GetSublist(0,start-1);
                    // Only add a suffix if there is something
                    // beyond the end index (it's inclusive too).
                    if (end + 1 < dest.Length)
                    {
                        return pref + src + dest.GetSublist(end + 1, -1);
                    }
                    else
                    {
                        return pref + src;
                    }
                }
                // If start is less than or equal to zero, then
                // the new list is simply a prefix. We still need to
                // figure out any necessary surgery to the destination
                // based upon end. Note that if end exceeds the upper
                // bound in this case, the entire destination list
                // is removed.
                else
                {
                    if (end + 1 < dest.Length)
                    {
                        return src + dest.GetSublist(end + 1, -1);
                    }
                    else
                    {
                        return src;
                    }
                }
            }
            // Finally, if start > end, we strip away a prefix and
            // a suffix, to leave the list that sits <between> ens
            // and start, and then tag on the src list. AT least
            // that's my interpretation. We can get sublist to do
            // this for us. Note that one, or both of the indices
            // might have been negative.
            else
            {
                return dest.GetSublist(end + 1, start - 1) + src;
            }
        }
        public LSL_List llListSort (LSL_List src, int stride, int ascending)
        {
            if (stride <= 0)
            {
                stride = 1;
            }
            return src.Sort(stride, ascending);
        }
        public LSL_List llParseString2List (string src, LSL_List separators, LSL_List spacers)
        {
            return ParseString(src, separators, spacers, false);
        }
        public LSL_List llParseStringKeepNulls (string src, LSL_List separators, LSL_List spacers)
        {
            return ParseString(src, separators, spacers, true);
        }
        public LSL_Rotation llAxes2Rot (LSL_Vector fwd, LSL_Vector left, LSL_Vector up)
        {
            double s;
            double tr = fwd.x + left.y + up.z + 1.0;

            if (tr >= 1.0)
            {
                s = 0.5 / Math.Sqrt(tr);
                return new LSL_Rotation(
                        (left.z - up.y) * s,
                        (up.x - fwd.z) * s,
                        (fwd.y - left.x) * s,
                        0.25 / s);
            }
            else
            {
                double max = (left.y > up.z) ? left.y : up.z;

                if (max < fwd.x)
                {
                    s = Math.Sqrt(fwd.x - (left.y + up.z) + 1.0);
                    double x = s * 0.5;
                    s = 0.5 / s;
                    return new LSL_Rotation(
                            x,
                            (fwd.y + left.x) * s,
                            (up.x + fwd.z) * s,
                            (left.z - up.y) * s);
                }
                else if (max == left.y)
                {
                    s = Math.Sqrt(left.y - (up.z + fwd.x) + 1.0);
                    double y = s * 0.5;
                    s = 0.5 / s;
                    return new LSL_Rotation(
                            (fwd.y + left.x) * s,
                            y,
                            (left.z + up.y) * s,
                            (up.x - fwd.z) * s);
                }
                else
                {
                    s = Math.Sqrt(up.z - (fwd.x + left.y) + 1.0);
                    double z = s * 0.5;
                    s = 0.5 / s;
                    return new LSL_Rotation(
                            (up.x + fwd.z) * s,
                            (left.z + up.y) * s,
                            z,
                            (fwd.y - left.x) * s);
                }
            }
        }
        public LSL_Rotation llAxisAngle2Rot (LSL_Vector axis, double angle)
        {

            double x, y, z, s, t;

            s = Math.Cos(angle * 0.5);
            t = Math.Sin(angle * 0.5); // temp value to avoid 2 more sin() calcs
            x = axis.x * t;
            y = axis.y * t;
            z = axis.z * t;

            return new LSL_Rotation(x,y,z,s);
        }
        public LSL_Rotation llEuler2Rot (LSL_Vector v)
        {

            double x,y,z,s;

            double c1 = Math.Cos(v.x * 0.5);
            double c2 = Math.Cos(v.y * 0.5);
            double c3 = Math.Cos(v.z * 0.5);
            double s1 = Math.Sin(v.x * 0.5);
            double s2 = Math.Sin(v.y * 0.5);
            double s3 = Math.Sin(v.z * 0.5);

            x = s1 * c2 * c3 + c1 * s2 * s3;
            y = c1 * s2 * c3 - s1 * c2 * s3;
            z = s1 * s2 * c3 + c1 * c2 * s3;
            s = c1 * c2 * c3 - s1 * s2 * s3;

            return new LSL_Rotation(x, y, z, s);
        }
        public LSL_Rotation llList2Rot (LSL_List src, int index)
        {
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length || index < 0)
            {
                return new LSL_Rotation(0, 0, 0, 1);
            }
            if (src.Data[index].GetType() == typeof(LSL_Rotation))
            {
                return (LSL_Rotation)src.Data[index];
            }
            else
            {
                return new LSL_Rotation(src.Data[index].ToString());
            }
        }
        public LSL_Rotation llRotBetween (LSL_Vector a, LSL_Vector b)
        {
            //A and B should both be normalized
            LSL_Rotation rotBetween;
            // Check for zero vectors. If either is zero, return zero rotation. Otherwise,
            // continue calculation.
            if (a == new LSL_Vector(0.0f, 0.0f, 0.0f) || b == new LSL_Vector(0.0f, 0.0f, 0.0f))
            {
                rotBetween = new LSL_Rotation(0.0f, 0.0f, 0.0f, 1.0f);
            }
            else
            {
                a = LSL_Vector.Norm(a);
                b = LSL_Vector.Norm(b);
                double dotProduct = LSL_Vector.Dot(a, b);
                // There are two degenerate cases possible. These are for vectors 180 or
                // 0 degrees apart. These have to be detected and handled individually.
                //
                // Check for vectors 180 degrees apart.
                // A dot product of -1 would mean the angle between vectors is 180 degrees.
                if (dotProduct < -0.9999999f)
                {
                    // First assume X axis is orthogonal to the vectors.
                    LSL_Vector orthoVector = new LSL_Vector(1.0f, 0.0f, 0.0f);
                    orthoVector = orthoVector - a * (a.x / LSL_Vector.Dot(a, a));
                    // Check for near zero vector. A very small non-zero number here will create
                    // a rotation in an undesired direction.
                    if (LSL_Vector.Mag(orthoVector) > 0.0001)
                    {
                        rotBetween = new LSL_Rotation(orthoVector.x, orthoVector.y, orthoVector.z, 0.0f);
                    }
                    // If the magnitude of the vector was near zero, then assume the X axis is not
                    // orthogonal and use the Z axis instead.
                    else
                    {
                        // Set 180 z rotation.
                        rotBetween = new LSL_Rotation(0.0f, 0.0f, 1.0f, 0.0f);
                    }
                }
                // Check for parallel vectors.
                // A dot product of 1 would mean the angle between vectors is 0 degrees.
                else if (dotProduct > 0.9999999f)
                {
                    // Set zero rotation.
                    rotBetween = new LSL_Rotation(0.0f, 0.0f, 0.0f, 1.0f);
                }
                else
                {
                    // All special checks have been performed so get the axis of rotation.
                    LSL_Vector crossProduct = LSL_Vector.Cross(a, b);
                    // Quarternion s value is the length of the unit vector + dot product.
                    double qs = 1.0 + dotProduct;
                    rotBetween = new LSL_Rotation(crossProduct.x, crossProduct.y, crossProduct.z, qs);
                    // Normalize the rotation.
                    double mag = LSL_Rotation.Mag(rotBetween);
                    // We shouldn't have to worry about a divide by zero here. The qs value will be
                    // non-zero because we already know if we're here, then the dotProduct is not -1 so
                    // qs will not be zero. Also, we've already handled the input vectors being zero so the
                    // crossProduct vector should also not be zero.
                    rotBetween.x = rotBetween.x / mag;
                    rotBetween.y = rotBetween.y / mag;
                    rotBetween.z = rotBetween.z / mag;
                    rotBetween.s = rotBetween.s / mag;
                    // Check for undefined values and set zero rotation if any found. This code might not actually be required
                    // any longer since zero vectors are checked for at the top.
                    if (Double.IsNaN(rotBetween.x) || Double.IsNaN(rotBetween.y) || Double.IsNaN(rotBetween.z) || Double.IsNaN(rotBetween.s))
                    {
                        rotBetween = new LSL_Rotation(0.0f, 0.0f, 0.0f, 1.0f);
                    }
                }
            }
            return rotBetween;
        }
        public LSL_String llBase64ToString (string str)
        {
            try
            {
                return Util.Base64ToString(str);
            }
            catch (Exception e)
            {
                throw new Exception("Error in base64Decode" + e.Message);
            }
        }
        public LSL_String llDeleteSubString (string src, int start, int end)
        {

            // Normalize indices (if negative).
            // After normlaization they may still be
            // negative, but that is now relative to
            // the start, rather than the end, of the
            // sequence.
            if (start < 0)
            {
                start = src.Length+start;
            }
            if (end < 0)
            {
                end = src.Length+end;
            }
            // Conventionally delimited substring
            if (start <= end)
            {
                // If both bounds are outside of the existing
                // string, then return unchanges.
                if (end < 0 || start >= src.Length)
                {
                    return src;
                }
                // At least one bound is in-range, so we
                // need to clip the out-of-bound argument.
                if (start < 0)
                {
                    start = 0;
                }

                if (end >= src.Length)
                {
                    end = src.Length-1;
                }

                return src.Remove(start,end-start+1);
            }
            // Inverted substring
            else
            {
                // In this case, out of bounds means that
                // the existing string is part of the cut.
                if (start < 0 || end >= src.Length)
                {
                    return String.Empty;
                }

                if (end > 0)
                {
                    if (start < src.Length)
                    {
                        return src.Remove(start).Remove(0,end+1);
                    }
                    else
                    {
                        return src.Remove(0,end+1);
                    }
                }
                else
                {
                    if (start < src.Length)
                    {
                        return src.Remove(start);
                    }
                    else
                    {
                        return src;
                    }
                }
            }
        }
        public LSL_String llDumpList2String (LSL_List src, string seperator)
        {
            if (src.Length == 0)
            {
                return String.Empty;
            }
            string ret = String.Empty;
            foreach (object o in src.Data)
            {
                ret = ret + o.ToString() + seperator;
            }
            ret = ret.Substring(0, ret.Length - seperator.Length);
            return ret;
        }
        public LSL_String llEscapeURL (string url)
        {
            try
            {
                return Uri.EscapeDataString(url);
            }
            catch (Exception ex)
            {
                return "llEscapeURL: " + ex.ToString();
            }
        }
        public LSL_String llInsertString (string dest, int index, string src)
        {

            // Normalize indices (if negative).
            // After normlaization they may still be
            // negative, but that is now relative to
            // the start, rather than the end, of the
            // sequence.
            if (index < 0)
            {
                index = dest.Length+index;

                // Negative now means it is less than the lower
                // bound of the string.

                if (index < 0)
                {
                    return src+dest;
                }

            }

            if (index >= dest.Length)
            {
                return dest+src;
            }

            // The index is in bounds.
            // In this case the index refers to the index that will
            // be assigned to the first character of the inserted string.
            // So unlike the other string operations, we do not add one
            // to get the correct string length.
            return dest.Substring(0,index)+src+dest.Substring(index);

        }
        public LSL_String llIntegerToBase64 (int number)
        {
            // uninitialized string

            char[] imdt = new char[8];


            // Manually unroll the loop

            imdt[7] = '=';
            imdt[6] = '=';
            imdt[5] = i2ctable[number<<4  & 0x3F];
            imdt[4] = i2ctable[number>>2  & 0x3F];
            imdt[3] = i2ctable[number>>8  & 0x3F];
            imdt[2] = i2ctable[number>>14 & 0x3F];
            imdt[1] = i2ctable[number>>20 & 0x3F];
            imdt[0] = i2ctable[number>>26 & 0x3F];

            return new string(imdt);
        }
        public LSL_String llList2CSV (LSL_List src)
        {

            string ret = String.Empty;
            int    x   = 0;


            if (src.Data.Length > 0)
            {
                ret = src.Data[x++].ToString();
                for (; x < src.Data.Length; x++)
                {
                    ret += ", "+src.Data[x].ToString();
                }
            }

            return ret;
        }
        public LSL_String llList2Key (LSL_List src, int index)
        {
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length || index < 0)
            {
                return "";
            }
            return src.Data[index].ToString();
        }
        public LSL_String llList2String (LSL_List src, int index)
        {
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length || index < 0)
            {
                return String.Empty;
            }
            return src.Data[index].ToString();
        }
        public LSL_String llMD5String (string src, int nonce)
        {
            return Util.Md5Hash(String.Format("{0}:{1}", src, nonce.ToString()));
        }
        public LSL_String llSHA1String (string src)
        {
            return Util.SHA1Hash(src).ToLower();
        }
        public LSL_String llStringToBase64 (string str)
        {
            try
            {
                byte[] encData_byte = new byte[str.Length];
                encData_byte = Util.UTF8.GetBytes(str);
                string encodedData = Convert.ToBase64String(encData_byte);
                return encodedData;
            }
            catch (Exception e)
            {
                throw new Exception("Error in base64Encode" + e.Message);
            }
        }
        public LSL_String llStringTrim (string src, int type)
        {
            if (type == (int)STRING_TRIM_HEAD) { return src.TrimStart(); }
            if (type == (int)STRING_TRIM_TAIL) { return src.TrimEnd(); }
            if (type == (int)STRING_TRIM) { return src.Trim(); }
            return src;
        }
        public LSL_String llToLower (string src)
        {
            return src.ToLower();
        }
        public LSL_String llToUpper (string src)
        {
            return src.ToUpper();
        }
        public LSL_String llUnescapeURL (string url)
        {
            try
            {
                return Uri.UnescapeDataString(url);
            }
            catch (Exception ex)
            {
                return "llUnescapeURL: " + ex.ToString();
            }
        }
        public LSL_String llXorBase64StringsCorrect (string str1, string str2)
        {
            string ret = String.Empty;
            string src1 = llBase64ToString(str1);
            string src2 = llBase64ToString(str2);
            int c = 0;
            for (int i = 0; i < src1.Length; i++)
            {
                ret += (char) (src1[i] ^ src2[c]);

                c++;
                if (c >= src2.Length)
                    c = 0;
            }
            return llStringToBase64(ret);
        }
        public LSL_Vector llList2Vector (LSL_List src, int index)
        {
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length || index < 0)
            {
                return new LSL_Vector(0, 0, 0);
            }
            if (src.Data[index].GetType() == typeof(LSL_Vector))
            {
                return (LSL_Vector)src.Data[index];
            }
            else
            {
                return new LSL_Vector(src.Data[index].ToString());
            }
        }
        public LSL_Vector llRot2Axis (LSL_Rotation rot)
        {
            double x,y,z;

            if (rot.s > 1) // normalization needed
            {
                double length = Math.Sqrt(rot.x * rot.x + rot.y * rot.y +
                        rot.z * rot.z + rot.s * rot.s);

                rot.x /= length;
                rot.y /= length;
                rot.z /= length;
                rot.s /= length;

            }

            // double angle = 2 * Math.Acos(rot.s);
            double s = Math.Sqrt(1 - rot.s * rot.s);
            if (s < 0.001)
            {
                x = 1;
                y = z = 0;
            }
            else
            {
                x = rot.x / s; // normalise axis
                y = rot.y / s;
                z = rot.z / s;
            }

            return new LSL_Vector(x,y,z);
        }
        public LSL_Vector llRot2Euler (LSL_Rotation r)
        {
            //This implementation is from http://lslwiki.net/lslwiki/wakka.php?wakka=LibraryRotationFunctions. ckrinke
            LSL_Rotation t = new LSL_Rotation(r.x * r.x, r.y * r.y, r.z * r.z, r.s * r.s);
            double m = (t.x + t.y + t.z + t.s);
            if (m == 0) return new LSL_Vector();
            double n = 2 * (r.y * r.s + r.x * r.z);
            double p = m * m - n * n;
            if (p > 0)
                return new LSL_Vector(Math.Atan2(2.0 * (r.x * r.s - r.y * r.z), (-t.x - t.y + t.z + t.s)),
                                             Math.Atan2(n, Math.Sqrt(p)),
                                             Math.Atan2(2.0 * (r.z * r.s - r.x * r.y), (t.x - t.y - t.z + t.s)));
            else if (n > 0)
                return new LSL_Vector(0.0, Math.PI * 0.5, Math.Atan2((r.z * r.s + r.x * r.y), 0.5 - t.x - t.z));
            else
                return new LSL_Vector(0.0, -Math.PI * 0.5, Math.Atan2((r.z * r.s + r.x * r.y), 0.5 - t.x - t.z));
        }
        public LSL_Vector llRot2Fwd (LSL_Rotation r)
        {

            double x, y, z, m;

            m = r.x * r.x + r.y * r.y + r.z * r.z + r.s * r.s;
            // m is always greater than zero
            // if m is not equal to 1 then Rotation needs to be normalized
            if (Math.Abs(1.0 - m) > 0.000001) // allow a little slop here for calculation precision
            {
                m = 1.0 / Math.Sqrt(m);
                r.x *= m;
                r.y *= m;
                r.z *= m;
                r.s *= m;
            }

            // Fast Algebric Calculations instead of Vectors & Quaternions Product
            x = r.x * r.x - r.y * r.y - r.z * r.z + r.s * r.s;
            y = 2 * (r.x * r.y + r.z * r.s);
            z = 2 * (r.x * r.z - r.y * r.s);
            return (new LSL_Vector(x, y, z));
        }
        public LSL_Vector llRot2Left (LSL_Rotation r)
        {

            double x, y, z, m;

            m = r.x * r.x + r.y * r.y + r.z * r.z + r.s * r.s;
            // m is always greater than zero
            // if m is not equal to 1 then Rotation needs to be normalized
            if (Math.Abs(1.0 - m) > 0.000001) // allow a little slop here for calculation precision
            {
                m = 1.0 / Math.Sqrt(m);
                r.x *= m;
                r.y *= m;
                r.z *= m;
                r.s *= m;
            }

            // Fast Algebric Calculations instead of Vectors & Quaternions Product
            x = 2 * (r.x * r.y - r.z * r.s);
            y = -r.x * r.x + r.y * r.y - r.z * r.z + r.s * r.s;
            z = 2 * (r.x * r.s + r.y * r.z);
            return (new LSL_Vector(x, y, z));
        }
        public LSL_Vector llRot2Up (LSL_Rotation r)
        {
            double x, y, z, m;

            m = r.x * r.x + r.y * r.y + r.z * r.z + r.s * r.s;
            // m is always greater than zero
            // if m is not equal to 1 then Rotation needs to be normalized
            if (Math.Abs(1.0 - m) > 0.000001) // allow a little slop here for calculation precision
            {
                m = 1.0 / Math.Sqrt(m);
                r.x *= m;
                r.y *= m;
                r.z *= m;
                r.s *= m;
            }

            // Fast Algebric Calculations instead of Vectors & Quaternions Product
            x = 2 * (r.x * r.z + r.y * r.s);
            y = 2 * (-r.x * r.s + r.y * r.z);
            z = -r.x * r.x - r.y * r.y + r.z * r.z + r.s * r.s;
            return (new LSL_Vector(x, y, z));
        }
        public LSL_Vector llVecNorm (LSL_Vector v)
        {
            return LSL_Vector.Norm(v);
        }


        private LSL_List ParseString(string src, LSL_List separators, LSL_List spacers, bool keepNulls)
        {
            int         beginning = 0;
            int         srclen    = src.Length;
            int         seplen    = separators.Length;
            object[]    separray  = separators.Data;
            int         spclen    = spacers.Length;
            object[]    spcarray  = spacers.Data;
            int         mlen      = seplen+spclen;

            int[]       offset    = new int[mlen+1];
            bool[]      active    = new bool[mlen];

            int         best;
            int         j;

            //    Initial capacity reduces resize cost

            LSL_List tokens = new LSL_List();

            //    All entries are initially valid

            for (int i = 0; i < mlen; i++)
                active[i] = true;

            offset[mlen] = srclen;

            while (beginning < srclen)
            {

                best = mlen;    // as bad as it gets

                //    Scan for separators

                for (j = 0; j < seplen; j++)
                {
                    if (separray[j].ToString() == String.Empty)
                        active[j] = false;

                    if (active[j])
                    {
                        // scan all of the markers
                        if ((offset[j] = src.IndexOf(separray[j].ToString(), beginning)) == -1)
                        {
                            // not present at all
                            active[j] = false;
                        }
                        else
                        {
                            // present and correct
                            if (offset[j] < offset[best])
                            {
                                // closest so far
                                best = j;
                                if (offset[best] == beginning)
                                    break;
                            }
                        }
                    }
                }

                //    Scan for spacers

                if (offset[best] != beginning)
                {
                    for (j = seplen; (j < mlen) && (offset[best] > beginning); j++)
                    {
                        if (spcarray[j-seplen].ToString() == String.Empty)
                            active[j] = false;

                        if (active[j])
                        {
                            // scan all of the markers
                            if ((offset[j] = src.IndexOf(spcarray[j-seplen].ToString(), beginning)) == -1)
                            {
                                // not present at all
                                active[j] = false;
                            }
                            else
                            {
                                // present and correct
                                if (offset[j] < offset[best])
                                {
                                    // closest so far
                                    best = j;
                                }
                            }
                        }
                    }
                }

                //    This is the normal exit from the scanning loop

                if (best == mlen)
                {
                    // no markers were found on this pass
                    // so we're pretty much done
                    if ((keepNulls) || ((!keepNulls) && (srclen - beginning) > 0))
                        tokens.Add(new LSL_String(src.Substring(beginning, srclen - beginning)));
                    break;
                }

                //    Otherwise we just add the newly delimited token
                //    and recalculate where the search should continue.
                if ((keepNulls) || ((!keepNulls) && (offset[best] - beginning) > 0))
                    tokens.Add(new LSL_String(src.Substring(beginning,offset[best]-beginning)));

                if (best < seplen)
                {
                    beginning = offset[best] + (separray[best].ToString()).Length;
                }
                else
                {
                    beginning = offset[best] + (spcarray[best - seplen].ToString()).Length;
                    string str = spcarray[best - seplen].ToString();
                    if ((keepNulls) || ((!keepNulls) && (str.Length > 0)))
                        tokens.Add(new LSL_String(str));
                }
            }

            //    This an awkward an not very intuitive boundary case. If the
            //    last substring is a tokenizer, then there is an implied trailing
            //    null list entry. Hopefully the single comparison will not be too
            //    arduous. Alternatively the 'break' could be replced with a return
            //    but that's shabby programming.

            if ((beginning == srclen) && (keepNulls))
            {
                if (srclen != 0)
                    tokens.Add(new LSL_String(""));
            }

            return tokens;
        }

        ////////////////////////////

        public string osMovePen(string drawList, int x, int y)
        {
            drawList += "MoveTo " + x + "," + y + ";";
            return drawList;
        }

        public string osDrawLine(string drawList, int startX, int startY, int endX, int endY)
        {
            drawList += "MoveTo "+ startX+","+ startY +"; LineTo "+endX +","+endY +"; ";
            return drawList;
        }

        public string osDrawLine(string drawList, int endX, int endY)
        {
            drawList += "LineTo " + endX + "," + endY + "; ";
            return drawList;
        }

        public string osDrawText(string drawList, string text)
        {
            drawList += "Text " + text + "; ";
            return drawList;
        }

        public string osDrawEllipse(string drawList, int width, int height)
        {
            drawList += "Ellipse " + width + "," + height + "; ";
            return drawList;
        }

        public string osDrawRectangle(string drawList, int width, int height)
        {
            drawList += "Rectangle " + width + "," + height + "; ";
            return drawList;
        }

        public string osDrawFilledRectangle(string drawList, int width, int height)
        {
            drawList += "FillRectangle " + width + "," + height + "; ";
            return drawList;
        }

        public string osDrawFilledPolygon(string drawList, LSL_List x, LSL_List y)
        {
            if (x.Length != y.Length || x.Length < 3)
            {
                return "";
            }
            drawList += "FillPolygon " + x.GetLSLStringItem(0) + "," + y.GetLSLStringItem(0);
            for (int i = 1; i < x.Length; i++)
            {
                drawList += "," + x.GetLSLStringItem(i) + "," + y.GetLSLStringItem(i);
            }
            drawList += "; ";
            return drawList;
        }

        public string osDrawPolygon(string drawList, LSL_List x, LSL_List y)
        {
            if (x.Length != y.Length || x.Length < 3)
            {
                return "";
            }
            drawList += "Polygon " + x.GetLSLStringItem(0) + "," + y.GetLSLStringItem(0);
            for (int i = 1; i < x.Length; i++)
            {
                drawList += "," + x.GetLSLStringItem(i) + "," + y.GetLSLStringItem(i);
            }
            drawList += "; ";
            return drawList;
        }

        public string osSetFontSize(string drawList, int fontSize)
        {
            drawList += "FontSize "+ fontSize +"; ";
            return drawList;
        }

        public string osSetFontName(string drawList, string fontName)
        {
            drawList += "FontName "+ fontName +"; ";
            return drawList;
        }

        public string osSetPenSize(string drawList, int penSize)
        {
            drawList += "PenSize " + penSize + "; ";
            return drawList;
        }

        public string osSetPenColor(string drawList, string color)
        {
            drawList += "PenColor " + color + "; ";
            return drawList;
        }

        // Deprecated
        public string osSetPenColour(string drawList, string colour)
        {
            drawList += "PenColour " + colour + "; ";
            return drawList;
        }

        public string osSetPenCap(string drawList, string direction, string type)
        {
            drawList += "PenCap " + direction + "," + type + "; ";
            return drawList;
        }

        public string osDrawImage(string drawList, int width, int height, string imageUrl)
        {
            drawList +="Image " +width + "," + height+ ","+ imageUrl +"; " ;
            return drawList;
        }

        public double osList2Double(LSL_List src, int index)
        {
            // There is really no double type in OSSL. C# and other
            // have one, but the current implementation of LSL_Types.list
            // is not allowed to contain any.
            // This really should be removed.
            //
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length)
            {
                return 0.0;
            }
            return Convert.ToDouble(src.Data[index]);
        }

        public LSL_String osFormatString(string str, LSL_List strings)
        {
            return String.Format(str, strings.Data);
        }

        public LSL_List osMatchString(string src, string pattern, int start)
        {
            LSL_List result = new LSL_List();

            // Normalize indices (if negative).
            // After normlaization they may still be
            // negative, but that is now relative to
            // the start, rather than the end, of the
            // sequence.
            if (start < 0)
            {
                start = src.Length + start;
            }

            if (start < 0 || start >= src.Length)
            {
                return result;  // empty list
            }

            // Find matches beginning at start position
            System.Text.RegularExpressions.Regex matcher = new System.Text.RegularExpressions.Regex(pattern);
            System.Text.RegularExpressions.Match match = matcher.Match(src, start);
            while (match.Success)
            {
                foreach (System.Text.RegularExpressions.Group g in match.Groups)
                {
                    if (g.Success)
                    {
                        result.Add(new LSL_String(g.Value));
                        result.Add(new LSL_Integer(g.Index));
                    }
                }

                match = match.NextMatch();
            }

            return result;
        }

        public LSL_String osUnixTimeToTimestamp(long time)
        {
            long baseTicks = 621355968000000000;
            long tickResolution = 10000000;
            long epochTicks = (time * tickResolution) + baseTicks;
            DateTime date = new DateTime(epochTicks);

            return date.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
        }

        /**************************\
         *  The rest of ILSL_Api  *
        \**************************/

        /**
         * @brief Called by the various ILSL_Api functions to print their parameters
         *        and if non-void return value, read return value from stdin.
         */

        protected void StubVoid (string name, params object[] parms)
        {
            PrintParms (name, parms);
        }
        protected LSL_Float StubLSLFloat (string name, params object[] parms)
        {
            PrintParms (name, parms);
            object val = ReadRetVal (name, "float");
            if (val is int) val = (double)(int)val;
            return new LSL_Float ((double)val);
        }
        protected LSL_Integer StubLSLInteger (string name, params object[] parms)
        {
            PrintParms (name, parms);
            return new LSL_Integer ((int)ReadRetVal (name, "integer"));
        }
        protected LSL_List StubLSLList (string name, params object[] parms)
        {
            PrintParms (name, parms);
            return (LSL_List)ReadRetVal (name, "list");
        }
        protected LSL_Rotation StubLSLRotation (string name, params object[] parms)
        {
            PrintParms (name, parms);
            return (LSL_Rotation)ReadRetVal (name, "rotation");
        }
        protected LSL_String StubLSLString (string name, params object[] parms)
        {
            PrintParms (name, parms);
            return new LSL_String ((string)ReadRetVal (name, "string"));
        }
        protected LSL_Vector StubLSLVector (string name, params object[] parms)
        {
            PrintParms (name, parms);
            return (LSL_Vector)ReadRetVal (name, "vector");
        }
        protected bool StubSysBoolean (string name, params object[] parms)
        {
            PrintParms (name, parms);
            return (int)ReadRetVal (name, "boolean") != 0;
        }
        protected double StubSysDouble (string name, params object[] parms)
        {
            PrintParms (name, parms);
            object val = ReadRetVal (name, "float");
            if (val is int) val = (double)(int)val;
            return (double)(double)val;
        }
        protected int StubSysInteger (string name, params object[] parms)
        {
            PrintParms (name, parms);
            return (int)ReadRetVal (name, "integer");
        }
        protected object StubSysObject (string name, params object[] parms)
        {
            PrintParms (name, parms);
            return ReadRetVal (name, "object");
        }
        protected string StubSysString (string name, params object[] parms)
        {
            PrintParms (name, parms);
            return (string)ReadRetVal (name, "string");
        }

        /**
         * @brief Print api function and parameter values.
         */
        private void PrintParms (string name, object[] parms)
        {
            StringBuilder sb = new StringBuilder (scriptRoot.msgPrefix);
            sb.Append (name);
            if (name == "llOwnerSay") {
                sb.Append (": ");
                sb.Append (parms[0].ToString ());
            } else {
                sb.Append ('(');
                for (int i = 0; i < parms.Length; i ++) {
                    object p = parms[i];
                    if (i > 0) sb.Append (", ");
                    TokenDeclInline.PrintParam (sb, p);
                }
                sb.Append (')');
            }
            Console.WriteLine (sb.ToString ());
        }

        /**
         * @brief Read API function return value from stdin.
         *        Must be in form <funcname>:<value>
         * @param name = name of function that the return value is for
         * @param type = script-visible return value type
         * @returns value read from stdin
         */
        private object ReadRetVal (string name, string type)
        {
            while (true) {
                if (scriptRoot.queuedValues.Count == 0) {
                    if (!XMREngTest.ReadInputLine ()) {
                        throw new Exception ("eof reading return value for " + name);
                    }
                } else {
                    QueuedValue qv = scriptRoot.queuedValues.First.Value;
                    scriptRoot.queuedValues.RemoveFirst ();
                    if (qv.name == name) return qv.value;
                    Console.WriteLine (scriptRoot.msgPrefix + "expecting return value for " + name + ", not " + qv.name);
                    scriptRoot.queuedValues.Clear ();
                }
            }
        }
    }

    /*************************\
     *  Copied from OpenSim  *
    \*************************/

    public class Util {
        public static Encoding UTF8 = Encoding.UTF8;

        public static string Base64ToString(string str)
        {
            UTF8Encoding encoder = new UTF8Encoding();
            Decoder utf8Decode = encoder.GetDecoder();

            byte[] todecode_byte = Convert.FromBase64String(str);
            int charCount = utf8Decode.GetCharCount(todecode_byte, 0, todecode_byte.Length);
            char[] decoded_char = new char[charCount];
            utf8Decode.GetChars(todecode_byte, 0, todecode_byte.Length, decoded_char, 0);
            string result = new String(decoded_char);
            return result;
        }

        public static string Md5Hash(string data)
        {
            byte[] dataMd5 = ComputeMD5Hash(data);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < dataMd5.Length; i++)
                sb.AppendFormat("{0:x2}", dataMd5[i]);
            return sb.ToString();
        }
        private static byte[] ComputeMD5Hash(string data)
        {
            System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create();
            return md5.ComputeHash(Encoding.Default.GetBytes(data));
        }

        public static string SHA1Hash(string data)
        {
            return SHA1Hash(Encoding.Default.GetBytes(data));
        }
        public static string SHA1Hash(byte[] data)
        {
            byte[] hash = ComputeSHA1Hash(data);
            return BitConverter.ToString(hash).Replace("-", String.Empty);
        }
        private static byte[] ComputeSHA1Hash(byte[] src)
        {
            System.Security.Cryptography.SHA1CryptoServiceProvider SHA1 =
                    new System.Security.Cryptography.SHA1CryptoServiceProvider();
            return SHA1.ComputeHash(src);
        }
    }

    /**********************\
     *  Copied from Mono  *
    \**********************/

    ////////////////////////////////////////////////////////////////////////
    //  Copy of Random from Mono 2.6.7 mcs/class/corlib/System/Random.cs  //
    //      so we do the same in both Mono and Microsoft                  //
    ////////////////////////////////////////////////////////////////////////

    //
    // System.Random.cs
    //
    // Authors:
    //   Bob Smith (bob@thestuff.net)
    //   Ben Maurer (bmaurer@users.sourceforge.net)
    //
    // (C) 2001 Bob Smith.  http://www.thestuff.net
    // (C) 2003 Ben Maurer
    //

    //
    // Copyright (C) 2004 Novell, Inc (http://www.novell.com)
    //
    // Permission is hereby granted, free of charge, to any person obtaining
    // a copy of this software and associated documentation files (the
    // "Software"), to deal in the Software without restriction, including
    // without limitation the rights to use, copy, modify, merge, publish,
    // distribute, sublicense, and/or sell copies of the Software, and to
    // permit persons to whom the Software is furnished to do so, subject to
    // the following conditions:
    //
    // The above copyright notice and this permission notice shall be
    // included in all copies or substantial portions of the Software.
    //
    // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
    // EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    // MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
    // NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
    // LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
    // OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
    // WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
    //

    public class Random
    {
        const int MBIG = int.MaxValue;
        const int MSEED = 161803398;
        const int MZ = 0;

        int inext, inextp;
        int [] SeedArray = new int [56];

        public Random ()
            : this (Environment.TickCount)
        {
        }

        public Random (int Seed)
        {
            int ii;
            int mj, mk;

            // Numerical Recipes in C online @ http://www.library.cornell.edu/nr/bookcpdf/c7-1.pdf

            // Math.Abs throws on Int32.MinValue, so we need to work around that case.
            // Fixes: 605797
            if (Seed == Int32.MinValue)
                mj = MSEED - Math.Abs (Int32.MinValue + 1);
            else
                mj = MSEED - Math.Abs (Seed);

            SeedArray [55] = mj;
            mk = 1;
            for (int i = 1; i < 55; i++) {  //  [1, 55] is special (Knuth)
                ii = (21 * i) % 55;
                SeedArray [ii] = mk;
                mk = mj - mk;
                if (mk < 0)
                    mk += MBIG;
                mj = SeedArray [ii];
            }
            for (int k = 1; k < 5; k++) {
                for (int i = 1; i < 56; i++) {
                    SeedArray [i] -= SeedArray [1 + (i + 30) % 55];
                    if (SeedArray [i] < 0)
                        SeedArray [i] += MBIG;
                }
            }
            inext = 0;
            inextp = 31;
        }

        protected virtual double Sample ()
        {
            int retVal;

            if (++inext  >= 56) inext  = 1;
            if (++inextp >= 56) inextp = 1;

            retVal = SeedArray [inext] - SeedArray [inextp];

            if (retVal < 0)
                retVal += MBIG;

            SeedArray [inext] = retVal;

            return retVal * (1.0 / MBIG);
        }

        public virtual int Next ()
        {
            return (int)(Sample () * int.MaxValue);
        }

        public virtual int Next (int maxValue)
        {
            if (maxValue < 0)
                throw new ArgumentOutOfRangeException(
                    "Max value is less than min value.");

            return (int)(Sample () * maxValue);
        }

        public virtual int Next (int minValue, int maxValue)
        {
            if (minValue > maxValue)
                throw new ArgumentOutOfRangeException (
                    "Min value is greater than max value.");

            // special case: a difference of one (or less) will always return the minimum
            // e.g. -1,-1 or -1,0 will always return -1
            uint diff = (uint) (maxValue - minValue);
            if (diff <= 1)
                return minValue;

            return (int)((uint)(Sample () * diff) + minValue);
        }

        public virtual void NextBytes (byte [] buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException ("buffer");

            for (int i = 0; i < buffer.Length; i++) {
                buffer [i] = (byte)(Sample () * (byte.MaxValue + 1));
            }
        }

        public virtual double NextDouble ()
        {
            return this.Sample ();
        }
    }
}
