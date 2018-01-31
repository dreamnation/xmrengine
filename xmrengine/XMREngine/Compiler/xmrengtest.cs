/******************************************************** *  COPYRIGHT 2012, Mike Rieker, Beverly, MA, USA       *
*
 *  All rights reserved.                                *
\********************************************************/
/**
 * @brief Main program for the script tester.
 */
using Mono.Tasklets;
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
    public class XMREngTest
    {
        public static bool doCheckRun = false;
        public static bool haveLinkNums = false;
        public static bool printPeakHeap = false;
        public static Dictionary<string, ScriptObjCode> scriptObjCodes = new Dictionary<string, ScriptObjCode> ();
        public static Dictionary<string, ScriptRoot> scriptRootPrimUUIDs = new Dictionary<string, ScriptRoot> ();
        public static int consoleLine = 0;
        public static LinkedList<int> ipcChannels = new LinkedList<int> ();
        public static LinkedList<QueuedEvent> queuedEvents = new LinkedList<QueuedEvent> ();
        public static MemoryStream serializeStream = null;
        public static ScriptRoot[] scriptRoots;
        public static string uthreadType;
        private static int scriptRootIndex;
        private static int scriptStepIndex;
        private static Token nextInputToken;
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
            bool doXmrAsm = false;
            bool eventIO = false;
            List<PerPrim> perPrims = new List<PerPrim> ();
            int linkNum = 0;
            string primName = "";
            string primUUID = "";
            string sourceHash = null;
            uthreadType = "sys";
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
                if (arg == "-uthread") {
                    if (++ i >= args.Length) goto usage;
                    uthreadType = args[i].ToLower ();
                    continue;
                }
                if (arg == "-xmrasm") {
                    doXmrAsm = true;
                    continue;
                }
                if (arg[0] == '-') goto usage;
                PerPrim perPrim;
                perPrim.srcFileName = arg;
                perPrim.linkNum = linkNum;
                perPrim.name = primName;
                perPrim.uuid = primUUID;
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
                inst.m_DescName = perPrims[i].name + ":" + srcFileName;
                if (uthreadType == "con") {
                    inst.engstack = new Mono.Tasklets.Continuation ();
                    inst.engstack.Mark ();
                    inst.scrstack = new Mono.Tasklets.Continuation ();
                    inst.scrstack.Mark ();
                    inst.wfistack = new Mono.Tasklets.Continuation ();
                    inst.wfistack.Mark ();
                }
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
            scriptRootIndex = 0;
            nextInputToken = null;
            while (true) {
                /*
                 * Keep processing -ipcchannel queues and stepping
                 * scripts until all scripts are waiting for some
                 * kind of input.
                 */
                bool didSomething;
                do {
                    didSomething = false;
                    /*
                     * Maybe we can process internally queued events.
                     * Get as many scripts ready for stepping at once as we can.
                     */
                checkQueuedEvents:
                    foreach (QueuedEvent queuedEvent in queuedEvents) {
                        if (queuedEvent.queuedTo.Count <= 0) {
                            queuedEvents.Remove (queuedEvent);
                            goto checkQueuedEvents;
                        }
                        foreach (ScriptRoot target in queuedEvent.queuedTo) {
                            if (target.iState == IState.WAITINGFOREVENT) {
                                queuedEvent.queuedTo.Remove (target);
                                target.ProcessQueuedEvent (queuedEvent);
                                didSomething = true;
                                goto checkQueuedEvents;
                            }
                        }
                    }
                    /*
                     * Step all scripts that need to be stepped.
                     * They should theoretically all be waiting for some kind of
                     * input after this completes.
                     */
                    bool steppedSomething;
                    do {
                        steppedSomething = false;
                        for (scriptStepIndex = 0; scriptStepIndex < scriptRoots.Length; scriptStepIndex ++) {
                            ScriptRoot sr = scriptRoots[(scriptRootIndex+scriptStepIndex)%scriptRoots.Length];
                            if ((sr.iState == IState.YIELDING) || (sr.iState == IState.GOTSOMEINPUT)) {
                                sr.StepScript (); // NOTE: CAN BLOW AWAY ALL STACK-LOCAL VARIABLES
                                                   //       BECAUSE OF MONO.CONTINUATIONS READING INPUT
                                steppedSomething = true;
                                didSomething = true;
                            }
                        }
                    } while (steppedSomething);
                    /*
                     * If we stepped something, repeat back in case it queued another -ipcchannel message.
                     */
                } while (didSomething);
                /*
                 * Not having -eventio means stop when everything is waiting for an event.
                 * The only thing it should allow reading for is return values.
                 */
                if (!eventIO) {
                    for (int i = 0; i < scriptRoots.Length; i ++) {
                        if (scriptRoots[i].iState == IState.WAITINGFORINPUT) goto gotone;
                    }
                    break;
                gotone:;
                }
                /*
                 * Read input and pass to script.
                 */
                if ((nextInputToken == null) || (nextInputToken is TokenEnd)) {
                    nextInputToken = ReadInputTokens ();
                    if (nextInputToken == null) break;
                }
                while (true) {
                    while ((nextInputToken is TokenBegin) || (nextInputToken is TokenKwSemi)) {
                        nextInputToken = nextInputToken.nextToken;
                    }
                    if (nextInputToken is TokenEnd) break;
                    /*
                     * If 'STATUS' print each script status.
                     */
                    if ((nextInputToken is TokenName) && (((TokenName)nextInputToken).val == "STATUS")) {
                        foreach (ScriptRoot sr in scriptRoots) {
                            sr.PrintStatus ();
                        }
                        nextInputToken = nextInputToken.nextToken;
                    } else {
                        /*
                         * Get index token from first integer on line.
                         * Can also be a string containing the prim uuid.
                         */
                        Token u = nextInputToken;
                        if ((u is TokenInt) && (u.nextToken is TokenKwParClose)) {
                            scriptRootIndex = ((TokenInt)u).val;
                            if ((scriptRootIndex < 0) || (scriptRootIndex >= scriptRoots.Length)) {
                                u.ErrorMsg ("script index out of range 0.." + (scriptRoots.Length - 1));
                                nextInputToken = null;
                                continue;
                            }
                            u = u.nextToken.nextToken;
                        } else if ((u is TokenStr) && (u.nextToken is TokenKwParClose)) {
                            string uuid = ((TokenStr)u).val;
                            if (!scriptRootPrimUUIDs.ContainsKey (uuid)) {
                                u.ErrorMsg ("unknown script prim uuid");
                                nextInputToken = null;
                                continue;
                            }
                            scriptRootIndex = scriptRootPrimUUIDs[uuid].index;
                            u = u.nextToken.nextToken;
                        }
                        /*
                         * If that script isn't ready for input, step everything until it is.
                         */
                        ScriptRoot sr = scriptRoots[scriptRootIndex];
                        if ((sr.iState != IState.WAITINGFORINPUT) &&
                            (sr.iState != IState.WAITINGFOREVENT)) break;
                        /*
                         * Process the input, getting the script ready to run again.
                         */
                        nextInputToken = sr.ProcessInput (u);
                    }
                    /*
                     * Check for end of input or if there is more to do before stepping scripts.
                     */
                    if (nextInputToken == null) break;
                    if (nextInputToken is TokenEnd) break;
                    if (!(nextInputToken is TokenKwSemi)) {
                        nextInputToken.ErrorMsg ("extra stuff at end of line");
                        nextInputToken = null;
                        break;
                    }
                }
            }
            if (printPeakHeap) {
                foreach (ScriptRoot sr in scriptRoots) {
                    Console.WriteLine (sr.msgPrefix + "PeakHeapUsed = " + sr.GetPeakHeapUsed ());
                }
            }
            Environment.Exit (0);
        usage:
            Console.WriteLine ("usage: mono xmrengtest.exe [ -builtins ] [ -checkrun ] [ -eventio ] [ -heaplimit <numbytes> ] [ -ipcchannel <channel> ] [ -linknum <number> ] [ -primname <name> ] [ -primuuid <uuid> ] [ -serialize ] [ -uthread <type> ] <sourcefile> ...");
            Console.WriteLine ("     -builtins : print list of built-in functions and constants then exit");
            Console.WriteLine ("     -checkrun : simulate a thread slice at every opportunity");
            Console.WriteLine ("      -eventio : when default state_entry completes, read next event from stdin and keep doing so until eof");
            Console.WriteLine ("                 format is:  event_name ( arg1 , arg2 , ... )");
            Console.WriteLine ("                   example:  http_response(\"firstone\",3,[0,<1,2,3>,\"456\"],\"body\")");
            Console.WriteLine ("    -heaplimit : max number of heap bytes allowed");
            Console.WriteLine ("   -ipcchannel : channel number for which llListen/llListenControl/llListenRemove/llRegionSay/llRegionSayTo/llSay are handled internally");
            Console.WriteLine ("      -linknum : link number returned by llGetLinkNumber() and used by llMessageLinked()");
            Console.WriteLine ("     -peakheap : print peak heap used");
            Console.WriteLine ("     -primname : give prim name for subsequent script(s) (returned by llGetKey())");
            Console.WriteLine ("     -primuuid : give prim uuid for subsequent script(s) (returned by llGetObjectName())");
            Console.WriteLine ("    -serialize : serialize and deserialize stack at every opportunity");
            Console.WriteLine ("      -uthread : specify Con (stack memcpying), MMR (stack switching), Sys (system thread)");
            Environment.Exit (2);
        }
        /**
         * @begin Read stream of tokens from input.
         * @returns null: end-of-file on input
         *          else: stream of tokens
         */
        public static TokenBegin ReadInputTokens ()
        {
            string sourceHash;
            Token t;
            TokenBegin tb, tbb;
            Token te = null;
            tbb = null;
            while (true) {
                /*
                 * Read input line from stdin.
                 */
                string inputLine = Console.ReadLine ();
                if (inputLine == null) return null;
                ++ consoleLine;
                Console.WriteLine (inputLine);
                /*
                 * Parse into tokens.
                 * If error parsing, print error then read another line.
                 * Also ignore blank lines.
                 */
                inputLine = "# " + consoleLine + " \"input\"\n" + inputLine;
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
                 * If not, we're all done so return the very first begin token.
                 * Otherwise, loop back to read in another line.
                 */
                for (t = tb; !(t is TokenEnd); t = t.nextToken) { }
                if (!(t.prevToken is TokenKwSub)) return tbb;
                te = t.prevToken;
            }
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
                    if (val is int) val = new LSL_Integer ((int)val);
                    if (val is double) val = new LSL_Float ((double)val);
                    if (val is string) val = new LSL_String ((string)val);
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
                if (val is LSL_Float) val = (double)((LSL_Float) val);
                if (val is LSL_Integer) val = (int) ((LSL_Integer)val);
                if (val is LSL_String) val = (string) ((LSL_String) val);
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
    public class ScriptResetException : Exception, IXMRUncatchable { }
    public class ScriptDieException : Exception, IXMRUncatchable { }
    public enum IState {
        YIELDING,
        RUNNING,
        WAITINGFORINPUT,
        WAITINGFOREVENT,
        GOTSOMEINPUT
    }
    public class Listener {
        public int channel;
        public string name;
        public string id;
        public string msg;
        public int active;
    }
    public class QueuedEvent {
        public ScriptEventCode eventCode;
        public object[] ehArgs;
        public LinkedList<ScriptRoot> queuedTo = new LinkedList<ScriptRoot> ();
        public QueuedEvent (ScriptEventCode ec, int nargs)
        {
            eventCode = ec;
            ehArgs = new object[nargs];
        }
    }
    /**
     * @brief This stuff does not re-instantiate on serialization.
     */
    public class ScriptRoot {
        public static object iStateLock = new object ();
        public int index;
        public int linkNum;
        private int peakHeapUsed;
        public IState iState;
        public Listener[] listeners = new Listener[65];
        public object wfiValue;
        public string msgPrefix;
        public string primName;
        public string primUUID;
        public string wfiName;
        public string wfiType;
        private XMRInstance inst;
        public ScriptRoot (XMRInstance inst, int index, PerPrim perPrim)
        {
            this.index = index;
            this.inst = inst;
            this.iState = IState.YIELDING;
            this.linkNum = perPrim.linkNum;
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
            if (iState == IState.WAITINGFORINPUT) {
                sb.Append (' ');
                sb.Append (wfiType);
                sb.Append (' ');
                sb.Append (wfiName);
            }
            Console.WriteLine (sb.ToString ());
        }
        public int GetPeakHeapUsed ()
        {
            if (peakHeapUsed < inst.peakHeapUsed) {
                peakHeapUsed = inst.peakHeapUsed;
            }
            return peakHeapUsed;
        }
        /**
         * @brief Process a line of input for the script and step it until it wants more.
         */
        public Token ProcessInput (Token t)
        {
            switch (iState) {
                case IState.WAITINGFOREVENT: {
                    /*
                     * Get event handler name and validate.
                     */
                    if (!(t is TokenName)) {
                        t.ErrorMsg ("expecting event name");
                        return null;
                    }
                    TokenName eventName = (TokenName)t;
                    MethodInfo[] ifaceMethods = typeof (IEventHandlers).GetMethods ();
                    MethodInfo ifaceMethod;
                    foreach (MethodInfo ifm in ifaceMethods) {
                        ifaceMethod = ifm;
                        if (ifaceMethod.Name == eventName.val) goto gotEvent;
                    }
                    eventName.ErrorMsg ("unknown event " + eventName);
                    return null;
                gotEvent:
                    inst.eventCode = (ScriptEventCode)Enum.Parse (typeof (ScriptEventCode), eventName.val);
                    if (inst.m_ObjCode.scriptEventHandlerTable[inst.stateCode,(int)inst.eventCode] == null) {
                        eventName.ErrorMsg ("event handler not defined for state " + inst.m_ObjCode.stateNames[inst.stateCode]);
                        return null;
                    }
                    t = t.nextToken;
                    /*
                     * Parse argument list.
                     */
                    if (!(t is TokenKwParOpen)) {
                        t.ErrorMsg ("expecting ( after event name");
                        return null;
                    }
                    List<object> argList = new List<object> ();
                    do {
                        t = t.nextToken;
                        if ((argList.Count == 0) && (t is TokenKwParClose)) break;
                        object val = XMREngTest.ParseEHArg (ref t);
                        if (val == null) break;
                        argList.Add (val);
                    } while (t is TokenKwComma);
                    if (!(t is TokenKwParClose)) {
                        t.ErrorMsg ("expecting , or ) in arg list");
                        return null;
                    }
                    inst.ehArgs = argList.ToArray ();
                    t = t.nextToken;
                    iState = IState.YIELDING;
                    break;
                }
                case IState.WAITINGFORINPUT: {
                    /*
                     * Get function name and validate.
                     */
                    if (!(t is TokenName) || !(t.nextToken is TokenKwColon)) {
                        t.ErrorMsg ("expecting function name :");
                        return null;
                    }
                    if (((TokenName)t).val != wfiName) {
                        t.ErrorMsg ("expecting function " + wfiName);
                        return null;
                    }
                    t = t.nextToken.nextToken;
                    /*
                     * Parse out the value.
                     */
                    wfiValue = XMREngTest.ParseEHArg (ref t);
                    if (wfiValue == null) return null;
                    iState = IState.GOTSOMEINPUT;
                    break;
                }
                default: {
                    throw new Exception ("bad istate " + iState);
                }
            }
            return t;
        }
        /**
         * @brief Process internally queued events.
         */
        public void ProcessQueuedEvent (QueuedEvent msg)
        {
            inst.eventCode = msg.eventCode;
            inst.ehArgs = msg.ehArgs;
            iState = IState.YIELDING;
        }
        /**
         * @brief Step script and wait for it until it wants more input.
         */
        public void StepScript ()
        {
            if (inst.uthread is ScriptUThread_Sys) {
                StepScriptMMRSys ();
            } else if (inst.uthread is ScriptUThread_MMR) {
                StepScriptMMRSys ();
            } else {
                StepScriptOther ();
            }
        }
        /**
         * @brief MMR/Sys thread model case - ReadRetVal() uses uthread.Hiber()
         *        when it wants input so we have to handle that case.
         */
        private void StepScriptMMRSys ()
        {
            /*
             * The uthread stack pointer better not already be loaded in some CPU.
             */
            int active = inst.uthread.Active ();
            if (active > 0) throw new Exception ("bad active [A] " + active);
            if (iState == IState.YIELDING) {
                /*
                 * Hiber() was called inside CheckRun(),
                 * say we are now running the script's code.
                 */
                iState = IState.RUNNING;
            }
            /*
             * Ok, run the script event handler code on the uthread stack until it calls
             * Hiber() or it finishes.
             * If the stack is valid (ie it called Hiber() last time), resume from where 
             * it left off.  Otherwise, the stack is not valid, so start the event hander
             * from very beginning.
             */
            Exception e = (active < 0) ? inst.uthread.ResumeEx () : inst.uthread.StartEx ();
            /*
             * It called Hiber() in either CheckRun() or ReadRetVal() or it ran to the end.
             */
            switch (iState) {
                /*
                 * Called Hiber() in CheckRun() or the event handler ran to the end or 
                 * threw an exception.
                 */
                case IState.RUNNING: {
                    StepScriptFinish (e);
                    break;
                }
                /*
                 * Called Hiber() in ReadRetVal().
                 */
                case IState.WAITINGFORINPUT: {
                    if (e != null) throw new Exception ("exception while WAITINGFORINPUT", e);
                    break;
                }
                default: throw new Exception ("bad iState " + iState);
            }
        }
        /**
         * @brief Con or Ser thread model cases - ReadRetVal() does longjmp()
         *        to wfistack when it wants input, so handle that case.
         */
        private void StepScriptOther ()
        {
            /*
             * Do a setjmp(0) that ReadRetVal() can use when it wants some input.
             */
            if (inst.wfistack.Store (0) != 0) {
                if (iState != IState.WAITINGFORINPUT) throw new Exception ("bad iState " + iState);
                return;
            }
            /*
             * Now if we are stepping because we have input for ReadRetVal(),
             * do a longjmp() back into ReadRetVal().
             */
            if (iState == IState.GOTSOMEINPUT) {
                inst.scrstack.Restore (1);
            }
            /*
             * The uthread stack better not already be in use.
             */
            int active = inst.uthread.Active ();
            if (active > 0) throw new Exception ("bad active [B] " + active);
            /*
             * Say we are now running the script's code.
             */
            iState = IState.RUNNING;
            /*
             * Ok, run the script event handler code on the uthread stack until it calls
             * Hiber() or it finishes.
             * If the stack is active (ie it called Hiber() last time), resume from where 
             * it left off.  Otherwise, the stack is not active, so start the event hander
             * from very beginning.
             */
            Exception e = (active < 0) ? inst.uthread.ResumeEx () : inst.uthread.StartEx ();
            StepScriptFinish (e);
        }
        private void StepScriptFinish (Exception e)
        {
            /*
             * Set up new state based on whether it suspended via Hiber() or it finished.
             */
            int active = inst.uthread.Active ();
            if (active > 0) {
                if (e == null) e = new Exception ("bad active [C] " + active);
                          else e = new Exception ("bad active [C] " + active, e);
                throw e;
            }
            iState = (active < 0) ? IState.YIELDING : IState.WAITINGFOREVENT;
            /*
             * Maybe we are doing -serialize.
             */
            if (e != null) {
                if (!(e is StackCaptureException)) {
                    Console.WriteLine ("exception in script: " + e.Message);
                    Console.WriteLine (inst.xmrExceptionStackTrace (e));
                    Environment.Exit (3);
                }
                if (inst.callMode != XMRInstAbstract.CallMode_SAVE) {
                    throw new Exception ("bad callmode " + inst.callMode, e);
                }
                if (XMREngTest.serializeStream == null) throw new Exception ("bad serializeStream", e);
                /*
                 * The uthread stack should be emptied, it got serialized out to inst.stackFrames.
                 */
                if (active != 0) throw new Exception ("bad active [D] " + active, e);
                if (peakHeapUsed < inst.peakHeapUsed) {
                    peakHeapUsed = inst.peakHeapUsed;
                }
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
                /*
                 * Script is ready to be run again immediately.
                 * But we will return to our caller so other scripts can get a time slice.
                 */
                iState = IState.YIELDING;
            }
        }
    }
    /**
     * @brief This stuff is re-instantiated on serialization.
     */
    public partial class XMRInstance : XMRInstAbstract {
        public static int HEAPLIMIT = 65536;
        public ScriptBaseClass m_ApiManager_LSL;
        public OpenSim.Region.ScriptEngine.Shared.Api.Interfaces.IOSSL_Api m_ApiManager_OSSL;
        public int peakHeapUsed = 0;
        public int m_StackSize = 1024*1024;
        public string m_DescName;
        public Mono.Tasklets.Continuation wfistack;
        /**
         * @brief Initial instance for the given script
         */
        public XMRInstance (ScriptObjCode soc)
        {
            m_ObjCode = soc;
            heapLimit = HEAPLIMIT;
            glblVars.AllocVarArrays (m_ObjCode.glblSizes);
            suspendOnCheckRunHold = XMREngTest.doCheckRun;
            InitUThread ();
            /*
             * Run default state_entry() handler until it is ready for more input.
             */
            ehArgs = new object[0];
            stateCode = 0; // default
            eventCode = ScriptEventCode.state_entry;
        }
        /**
         * @brief Create a new instance that is being reloaded for serialization test.
         */
        public XMRInstance (XMRInstance inst)
        {
            scriptRoot = inst.scriptRoot;
            m_ObjCode = inst.m_ObjCode;
            m_DescName = inst.m_DescName;
            heapLimit = HEAPLIMIT;
            engstack = inst.engstack;
            scrstack = inst.scrstack;
            wfistack = inst.wfistack;
            suspendOnCheckRunHold = XMREngTest.doCheckRun;
            InitUThread ();
        }
        private void InitUThread ()
        {
            switch (XMREngTest.uthreadType) {
                case "con": {
                    uthread = new ScriptUThread_Con (this);
                    break;
                }
                case "mmr": {
                    uthread = new ScriptUThread_MMR (this);
                    break;
                }
                case "sys": {
                    uthread = new ScriptUThread_Sys (this);
                    break;
                }
                default: {
                    Console.WriteLine ("unsupported -uthread type: " + XMREngTest.uthreadType);
                    Environment.Exit (2);
                    break;
                }
            }
        }
        /*********************         *  XMRInstAbstract  *
*
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
         *        In the tester, it thus gets called only when -checkrun or
         *        -serialize options were given on the command line.
         */
        public override void CheckRunWork ()
        {
            if (XMREngTest.serializeStream == null) {
                /*
                 * -checkrun:  Just switch stacks back to StepScript(),
                 *             leaving the uthread stack intact.
                 */
                uthread.Hiber ();
            } else {
                /*
                 * -serialize:  Serialize the whole uthread stack out to 
                 *              this.stackFramesand switch stacks back to 
                 *              StepScript().
                 */
                switch (this.callMode) {
                    /*
                     * Code was running normally.
                     * Suspend and write ustack frames to this.StackFrames,
                     * unwinding them as we go, then return out to StepScript().
                     */
                    case CallMode_NORMAL: {
                        this.callMode = CallMode_SAVE;
                        this.stackFrames = null;
                        throw new StackCaptureException ();
                    }
                    /*
                     * Code just restored itself from this.StackFrames building up
                     * the uthread stack just as was when we saved it.
                     * Resume script where it left off until it calls CheckRun() again.
                     */
                    case CallMode_RESTORE: {
                        if (this.stackFrames != null) throw new Exception ("frames left over");
                        this.callMode = CallMode_NORMAL;
                        break;
                    }
                    default: throw new Exception ("bad callMode " + this.callMode);
                }
            }
        }
        public override int xmrStackLeft ()
        {
            return uthread.StackLeft ();
        }
        public override void StateChange ()
        {
            Console.WriteLine ("Change to state " + m_ObjCode.stateNames[stateCode]);
            scriptRoot.listeners = new Listener[scriptRoot.listeners.Length];
        }
        [xmrMethodIsNoisyAttribute] // calls Stub<somethingorother>
        public override LSL_List xmrEventDequeue (double timeout, int returnMask1, int returnMask2,
                                                  int backgroundMask1, int backgroundMask2)
        {
            return StubLSLList ("xmrEventDequeue", timeout, returnMask1, returnMask2, backgroundMask1, backgroundMask2);
        }
        [xmrMethodIsNoisyAttribute] // calls Stub<somethingorother>
        public override void xmrEventEnqueue (LSL_List ev)
        {
            StubVoid ("xmrEventEnqueue", ev);
        }
        [xmrMethodIsNoisyAttribute] // calls Stub<somethingorother>
        public override LSL_List xmrEventSaveDets ()
        {
            return StubLSLList ("xmrEventSaveDets");
        }
        [xmrMethodIsNoisyAttribute] // calls Stub<somethingorother>
        public override void xmrEventLoadDets (LSL_List dpList)
        {
            StubVoid ("xmrEventLoadDets", dpList);
        }
        [xmrMethodIsNoisyAttribute] // calls Stub<somethingorother>
        public override void xmrTrapRegionCrossing (int enable)
        {
            StubVoid ("xmrTrapRegionCrossing", enable);
        }
        [xmrMethodIsNoisyAttribute] // calls Stub<somethingorother>
        public override bool xmrSetObjRegPosRotAsync (LSL_Vector pos, LSL_Rotation rot, int options, int evcode, LSL_List evargs)
        {
            return StubLSLInteger ("xmrSetObjRegPosRotAsync", pos, rot, options, evcode, evargs) != 0;
        }
    }
    public class ScriptBaseClass :
            OpenSim.Region.ScriptEngine.Shared.Api.Interfaces.ILSL_Api,
            OpenSim.Region.ScriptEngine.Shared.Api.Interfaces.IOSSL_Api {
        public IScriptUThread uthread;
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
            lner.channel = channel;
            lner.name = name;
            lner.id = id;
            lner.msg = msg;
            lner.active = 1;
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
            QueuedEvent queuedEvent = new QueuedEvent (ScriptEventCode.listen, 4);
            queuedEvent.ehArgs[0] = channel;
            queuedEvent.ehArgs[1] = scriptRoot.primName;
            queuedEvent.ehArgs[2] = scriptRoot.primUUID;
            queuedEvent.ehArgs[3] = msg;
            bool queued = false;
            foreach (ScriptRoot targsr in XMREngTest.scriptRoots) {
                if ((targsr != scriptRoot) && (target == null || targsr.primUUID == target)) {
                    foreach (Listener lner in targsr.listeners) {
                        if (lner == null) continue;
                        if ((lner.channel == channel) && (lner.active != 0)) {
                            if ((lner.id != "") && (lner.id != scriptRoot.primUUID)) continue;
                            if ((lner.name != "") && (lner.name != scriptRoot.primName)) continue;
                            if ((lner.msg != "") && (lner.msg != msg)) continue;
                            queuedEvent.queuedTo.AddLast (targsr);
                            queued = true;
                            break;
                        }
                    }
                }
            }
            /*
             * If so, add to end of internal event queue.
             */
            if (queued) {
                XMREngTest.queuedEvents.AddLast (queuedEvent);
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
            object rv = ReadRetVal ("llListRandomize", "list");
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
            QueuedEvent queuedEvent = new QueuedEvent (ScriptEventCode.link_message, 4);
            queuedEvent.ehArgs[0] = scriptRoot.linkNum;
            queuedEvent.ehArgs[1] = num;
            queuedEvent.ehArgs[2] = str;
            queuedEvent.ehArgs[3] = id;
            /*
             * See if it can be queued to any scripts.
             */
            bool queued = false;
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
                queuedEvent.queuedTo.AddLast (targsr);
                queued = true;
            }
            /*
             * If so, add to end of internal event queue.
             */
            if (queued) {
                XMREngTest.queuedEvents.AddLast (queuedEvent);
            }
        }
        /*************************         *  Copied from OpenSim  *
*
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
            -1,-1,-1,-1,-1,-1,-1,-1, // 0x
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1, // 1x
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1, // 2x
            -1,-1,-1,63,-1,-1,-1,64,
            53,54,55,56,57,58,59,60, // 3x
            61,62,-1,-1,-1,0,-1,-1,
            -1,1,2,3,4,5,6,7, // 4x
            8,9,10,11,12,13,14,15,
            16,17,18,19,20,21,22,23, // 5x
            24,25,26,-1,-1,-1,-1,-1,
            -1,27,28,29,30,31,32,33, // 6x
            34,35,36,37,38,39,40,41,
            42,43,44,45,46,47,48,49, // 7x
            50,51,52,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1, // 8x
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1, // 9x
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1, // Ax
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1, // Bx
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1, // Cx
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1, // Dx
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1, // Ex
            -1,-1,-1,-1,-1,-1,-1,-1,
            -1,-1,-1,-1,-1,-1,-1,-1, // Fx
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
            int index = -1;
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
            int start = 0;
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
            if (end < 0)
                end = src.Length+end;
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
            imdt[5] = i2ctable[number<<4 & 0x3F];
            imdt[4] = i2ctable[number>>2 & 0x3F];
            imdt[3] = i2ctable[number>>8 & 0x3F];
            imdt[2] = i2ctable[number>>14 & 0x3F];
            imdt[1] = i2ctable[number>>20 & 0x3F];
            imdt[0] = i2ctable[number>>26 & 0x3F];
            return new string(imdt);
        }
        public LSL_String llList2CSV (LSL_List src)
        {
            string ret = String.Empty;
            int x = 0;
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
            int beginning = 0;
            int srclen = src.Length;
            int seplen = separators.Length;
            object[] separray = separators.Data;
            int spclen = spacers.Length;
            object[] spcarray = spacers.Data;
            int mlen = seplen+spclen;
            int[] offset = new int[mlen+1];
            bool[] active = new bool[mlen];
            int best;
            int j;
            //    Initial capacity reduces resize cost
            LSL_List tokens = new LSL_List();
            //    All entries are initially valid
            for (int i = 0; i < mlen; i++)
                active[i] = true;
            offset[mlen] = srclen;
            while (beginning < srclen)
            {
                best = mlen; // as bad as it gets
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
                return result; // empty list
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
        /**************************         *  The rest of ILSL_Api  *
*
        \**************************/
        [xmrMethodIsNoisyAttribute] public void llAddToLandBanList (string avatar, double hours)
        {
            StubVoid ("llAddToLandBanList", avatar, hours);
        }
        [xmrMethodIsNoisyAttribute] public void llAddToLandPassList (string avatar, double hours)
        {
            StubVoid ("llAddToLandPassList", avatar, hours);
        }
        [xmrMethodIsNoisyAttribute] public void llAdjustSoundVolume (double volume)
        {
            StubVoid ("llAdjustSoundVolume", volume);
        }
        [xmrMethodIsNoisyAttribute] public void llAllowInventoryDrop (int add)
        {
            StubVoid ("llAllowInventoryDrop", add);
        }
        [xmrMethodIsNoisyAttribute] public void llApplyImpulse (LSL_Vector force, int local)
        {
            StubVoid ("llApplyImpulse", force, local);
        }
        [xmrMethodIsNoisyAttribute] public void llApplyRotationalImpulse (LSL_Vector force, int local)
        {
            StubVoid ("llApplyRotationalImpulse", force, local);
        }
        [xmrMethodIsNoisyAttribute] public void llAttachToAvatar (int attachment)
        {
            StubVoid ("llAttachToAvatar", attachment);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llAvatarOnSitTarget ()
        {
            return StubLSLString ("llAvatarOnSitTarget");
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llAvatarOnLinkSitTarget (int linknum)
        {
            return StubLSLString ("llAvatarOnLinkSitTarget", linknum);
        }
        [xmrMethodIsNoisyAttribute] public void llBreakAllLinks ()
        {
            StubVoid ("llBreakAllLinks");
        }
        [xmrMethodIsNoisyAttribute] public void llBreakLink (int linknum)
        {
            StubVoid ("llBreakLink", linknum);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List llCastRay (LSL_Vector start, LSL_Vector end, LSL_List options)
        {
            return StubLSLList ("llCastRay", start, end, options);
        }
        [xmrMethodIsNoisyAttribute] public void llClearCameraParams ()
        {
            StubVoid ("llClearCameraParams");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llClearLinkMedia (LSL_Integer link, LSL_Integer face)
        {
            return StubLSLInteger ("llClearLinkMedia", link, face);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llClearPrimMedia (LSL_Integer face)
        {
            return StubLSLInteger ("llClearPrimMedia", face);
        }
        [xmrMethodIsNoisyAttribute] public void llCloseRemoteDataChannel (string channel)
        {
            StubVoid ("llCloseRemoteDataChannel", channel);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llCloud (LSL_Vector offset)
        {
            return StubLSLFloat ("llCloud", offset);
        }
        [xmrMethodIsNoisyAttribute] public void llCollisionFilter (string name, string id, int accept)
        {
            StubVoid ("llCollisionFilter", name, id, accept);
        }
        [xmrMethodIsNoisyAttribute] public void llCollisionSound (string impact_sound, double impact_volume)
        {
            StubVoid ("llCollisionSound", impact_sound, impact_volume);
        }
        [xmrMethodIsNoisyAttribute] public void llCollisionSprite (string impact_sprite)
        {
            StubVoid ("llCollisionSprite", impact_sprite);
        }
        [xmrMethodIsNoisyAttribute] public void llCreateLink (string target, int parent)
        {
            StubVoid ("llCreateLink", target, parent);
        }
        [xmrMethodIsNoisyAttribute] public void llDetachFromAvatar ()
        {
            StubVoid ("llDetachFromAvatar");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llDetectedGrab (int number)
        {
            return StubLSLVector ("llDetectedGrab", number);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llDetectedGroup (int number)
        {
            return StubLSLInteger ("llDetectedGroup", number);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llDetectedKey (int number)
        {
            return StubLSLString ("llDetectedKey", number);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llDetectedLinkNumber (int number)
        {
            return StubLSLInteger ("llDetectedLinkNumber", number);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llDetectedName (int number)
        {
            return StubLSLString ("llDetectedName", number);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llDetectedOwner (int number)
        {
            return StubLSLString ("llDetectedOwner", number);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llDetectedPos (int number)
        {
            return StubLSLVector ("llDetectedPos", number);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Rotation llDetectedRot (int number)
        {
            return StubLSLRotation ("llDetectedRot", number);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llDetectedType (int number)
        {
            return StubLSLInteger ("llDetectedType", number);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llDetectedTouchBinormal (int index)
        {
            return StubLSLVector ("llDetectedTouchBinormal", index);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llDetectedTouchFace (int index)
        {
            return StubLSLInteger ("llDetectedTouchFace", index);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llDetectedTouchNormal (int index)
        {
            return StubLSLVector ("llDetectedTouchNormal", index);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llDetectedTouchPos (int index)
        {
            return StubLSLVector ("llDetectedTouchPos", index);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llDetectedTouchST (int index)
        {
            return StubLSLVector ("llDetectedTouchST", index);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llDetectedTouchUV (int index)
        {
            return StubLSLVector ("llDetectedTouchUV", index);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llDetectedVel (int number)
        {
            return StubLSLVector ("llDetectedVel", number);
        }
        [xmrMethodIsNoisyAttribute] public void llDialog (string avatar, string message, LSL_List buttons, int chat_channel)
        {
            StubVoid ("llDialog", avatar, message, buttons, chat_channel);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llEdgeOfWorld (LSL_Vector pos, LSL_Vector dir)
        {
            return StubLSLInteger ("llEdgeOfWorld", pos, dir);
        }
        [xmrMethodIsNoisyAttribute] public void llEjectFromLand (string pest)
        {
            StubVoid ("llEjectFromLand", pest);
        }
        [xmrMethodIsNoisyAttribute] public void llEmail (string address, string subject, string message)
        {
            StubVoid ("llEmail", address, subject, message);
        }
        [xmrMethodIsNoisyAttribute] public void llForceMouselook (int mouselook)
        {
            StubVoid ("llForceMouselook", mouselook);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llFrand (double mag)
        {
            return StubLSLFloat ("llFrand", mag);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGenerateKey ()
        {
            return StubLSLString ("llGenerateKey");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetAccel ()
        {
            return StubLSLVector ("llGetAccel");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetAgentInfo (string id)
        {
            return StubLSLInteger ("llGetAgentInfo", id);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetAgentLanguage (string id)
        {
            return StubLSLString ("llGetAgentLanguage", id);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List llGetAgentList (LSL_Integer scope, LSL_List options)
        {
            return StubLSLList ("llGetAgentList", scope, options);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetAgentSize (string id)
        {
            return StubLSLVector ("llGetAgentSize", id);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llGetAlpha (int face)
        {
            return StubLSLFloat ("llGetAlpha", face);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llGetAndResetTime ()
        {
            return StubLSLFloat ("llGetAndResetTime");
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetAnimation (string id)
        {
            return StubLSLString ("llGetAnimation", id);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List llGetAnimationList (string id)
        {
            return StubLSLList ("llGetAnimationList", id);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetAttached ()
        {
            return StubLSLInteger ("llGetAttached");
        }
        [xmrMethodIsNoisyAttribute] public LSL_List llGetAttachedList (string id)
        {
            return StubLSLList ("llGetAttachedList", id);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List llGetBoundingBox (string obj)
        {
            return StubLSLList ("llGetBoundingBox", obj);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetCameraPos ()
        {
            return StubLSLVector ("llGetCameraPos");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Rotation llGetCameraRot ()
        {
            return StubLSLRotation ("llGetCameraRot");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetCenterOfMass ()
        {
            return StubLSLVector ("llGetCenterOfMass");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetColor (int face)
        {
            return StubLSLVector ("llGetColor", face);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetCreator ()
        {
            return StubLSLString ("llGetCreator");
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetDate ()
        {
            return StubLSLString ("llGetDate");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llGetEnergy ()
        {
            return StubLSLFloat ("llGetEnergy");
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetEnv (LSL_String name)
        {
            return StubLSLString ("llGetEnv", name);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetForce ()
        {
            return StubLSLVector ("llGetForce");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetFreeMemory ()
        {
            return StubLSLInteger ("llGetFreeMemory");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetUsedMemory ()
        {
            return StubLSLInteger ("llGetUsedMemory");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetFreeURLs ()
        {
            return StubLSLInteger ("llGetFreeURLs");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetGeometricCenter ()
        {
            return StubLSLVector ("llGetGeometricCenter");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llGetGMTclock ()
        {
            return StubLSLFloat ("llGetGMTclock");
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetHTTPHeader (LSL_String request_id, string header)
        {
            return StubLSLString ("llGetHTTPHeader", request_id, header);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetInventoryCreator (string item)
        {
            return StubLSLString ("llGetInventoryCreator", item);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetInventoryKey (string name)
        {
            return StubLSLString ("llGetInventoryKey", name);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetInventoryName (int type, int number)
        {
            return StubLSLString ("llGetInventoryName", type, number);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetInventoryNumber (int type)
        {
            return StubLSLInteger ("llGetInventoryNumber", type);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetInventoryPermMask (string item, int mask)
        {
            return StubLSLInteger ("llGetInventoryPermMask", item, mask);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetInventoryType (string name)
        {
            return StubLSLInteger ("llGetInventoryType", name);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetLandOwnerAt (LSL_Vector pos)
        {
            return StubLSLString ("llGetLandOwnerAt", pos);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetLinkKey (int linknum)
        {
            return StubLSLString ("llGetLinkKey", linknum);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetLinkName (int linknum)
        {
            return StubLSLString ("llGetLinkName", linknum);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetLinkNumberOfSides (int link)
        {
            return StubLSLInteger ("llGetLinkNumberOfSides", link);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List llGetLinkMedia (LSL_Integer link, LSL_Integer face, LSL_List rules)
        {
            return StubLSLList ("llGetLinkMedia", link, face, rules);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List llGetLinkPrimitiveParams (int linknum, LSL_List rules)
        {
            return StubLSLList ("llGetLinkPrimitiveParams", linknum, rules);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetLocalPos ()
        {
            return StubLSLVector ("llGetLocalPos");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Rotation llGetLocalRot ()
        {
            return StubLSLRotation ("llGetLocalRot");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llGetMass ()
        {
            return StubLSLFloat ("llGetMass");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llGetMassMKS ()
        {
            return StubLSLFloat ("llGetMassMKS");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetMemoryLimit ()
        {
            return StubLSLInteger ("llGetMemoryLimit");
        }
        [xmrMethodIsNoisyAttribute] public void llGetNextEmail (string address, string subject)
        {
            StubVoid ("llGetNextEmail", address, subject);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetNotecardLine (string name, int line)
        {
            return StubLSLString ("llGetNotecardLine", name, line);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetNumberOfNotecardLines (string name)
        {
            return StubLSLString ("llGetNumberOfNotecardLines", name);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetNumberOfPrims ()
        {
            return StubLSLInteger ("llGetNumberOfPrims");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetNumberOfSides ()
        {
            return StubLSLInteger ("llGetNumberOfSides");
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetObjectDesc ()
        {
            return StubLSLString ("llGetObjectDesc");
        }
        [xmrMethodIsNoisyAttribute] public LSL_List llGetObjectDetails (string id, LSL_List args)
        {
            return StubLSLList ("llGetObjectDetails", id, args);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llGetObjectMass (string id)
        {
            return StubLSLFloat ("llGetObjectMass", id);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetObjectPermMask (int mask)
        {
            return StubLSLInteger ("llGetObjectPermMask", mask);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetObjectPrimCount (string object_id)
        {
            return StubLSLInteger ("llGetObjectPrimCount", object_id);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetOmega ()
        {
            return StubLSLVector ("llGetOmega");
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetOwner ()
        {
            return StubLSLString ("llGetOwner");
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetOwnerKey (string id)
        {
            return StubLSLString ("llGetOwnerKey", id);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List llGetParcelDetails (LSL_Vector pos, LSL_List param)
        {
            return StubLSLList ("llGetParcelDetails", pos, param);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetParcelFlags (LSL_Vector pos)
        {
            return StubLSLInteger ("llGetParcelFlags", pos);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetParcelMaxPrims (LSL_Vector pos, int sim_wide)
        {
            return StubLSLInteger ("llGetParcelMaxPrims", pos, sim_wide);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetParcelMusicURL ()
        {
            return StubLSLString ("llGetParcelMusicURL");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetParcelPrimCount (LSL_Vector pos, int category, int sim_wide)
        {
            return StubLSLInteger ("llGetParcelPrimCount", pos, category, sim_wide);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List llGetParcelPrimOwners (LSL_Vector pos)
        {
            return StubLSLList ("llGetParcelPrimOwners", pos);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetPermissions ()
        {
            return StubLSLInteger ("llGetPermissions");
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetPermissionsKey ()
        {
            return StubLSLString ("llGetPermissionsKey");
        }
        [xmrMethodIsNoisyAttribute] public LSL_List llGetPrimMediaParams (int face, LSL_List rules)
        {
            return StubLSLList ("llGetPrimMediaParams", face, rules);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetPos ()
        {
            return StubLSLVector ("llGetPos");
        }
        [xmrMethodIsNoisyAttribute] public LSL_List llGetPrimitiveParams (LSL_List rules)
        {
            return StubLSLList ("llGetPrimitiveParams", rules);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetRegionAgentCount ()
        {
            return StubLSLInteger ("llGetRegionAgentCount");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetRegionCorner ()
        {
            return StubLSLVector ("llGetRegionCorner");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetRegionFlags ()
        {
            return StubLSLInteger ("llGetRegionFlags");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llGetRegionFPS ()
        {
            return StubLSLFloat ("llGetRegionFPS");
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetRegionName ()
        {
            return StubLSLString ("llGetRegionName");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llGetRegionTimeDilation ()
        {
            return StubLSLFloat ("llGetRegionTimeDilation");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetRootPosition ()
        {
            return StubLSLVector ("llGetRootPosition");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Rotation llGetRootRotation ()
        {
            return StubLSLRotation ("llGetRootRotation");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Rotation llGetRot ()
        {
            return StubLSLRotation ("llGetRot");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetScale ()
        {
            return StubLSLVector ("llGetScale");
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetScriptName ()
        {
            return StubLSLString ("llGetScriptName");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetScriptState (string name)
        {
            return StubLSLInteger ("llGetScriptState", name);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetSimulatorHostname ()
        {
            return StubLSLString ("llGetSimulatorHostname");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetSPMaxMemory ()
        {
            return StubLSLInteger ("llGetSPMaxMemory");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetStartParameter ()
        {
            return StubLSLInteger ("llGetStartParameter");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetStatus (int status)
        {
            return StubLSLInteger ("llGetStatus", status);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetSunDirection ()
        {
            return StubLSLVector ("llGetSunDirection");
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetTexture (int face)
        {
            return StubLSLString ("llGetTexture", face);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetTextureOffset (int face)
        {
            return StubLSLVector ("llGetTextureOffset", face);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llGetTextureRot (int side)
        {
            return StubLSLFloat ("llGetTextureRot", side);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetTextureScale (int side)
        {
            return StubLSLVector ("llGetTextureScale", side);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llGetTime ()
        {
            return StubLSLFloat ("llGetTime");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llGetTimeOfDay ()
        {
            return StubLSLFloat ("llGetTimeOfDay");
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetTimestamp ()
        {
            return StubLSLString ("llGetTimestamp");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetTorque ()
        {
            return StubLSLVector ("llGetTorque");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetUnixTime ()
        {
            return StubLSLInteger ("llGetUnixTime");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGetVel ()
        {
            return StubLSLVector ("llGetVel");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llGetWallclock ()
        {
            return StubLSLFloat ("llGetWallclock");
        }
        [xmrMethodIsNoisyAttribute] public void llGiveInventory (string destination, string inventory)
        {
            StubVoid ("llGiveInventory", destination, inventory);
        }
        [xmrMethodIsNoisyAttribute] public void llGiveInventoryList (string destination, string category, LSL_List inventory)
        {
            StubVoid ("llGiveInventoryList", destination, category, inventory);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGiveMoney (string destination, int amount)
        {
            return StubLSLInteger ("llGiveMoney", destination, amount);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llTransferLindenDollars (string destination, int amount)
        {
            return StubLSLString ("llTransferLindenDollars", destination, amount);
        }
        [xmrMethodIsNoisyAttribute] public void llGodLikeRezObject (string inventory, LSL_Vector pos)
        {
            StubVoid ("llGodLikeRezObject", inventory, pos);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llGround (LSL_Vector offset)
        {
            return StubLSLFloat ("llGround", offset);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGroundContour (LSL_Vector offset)
        {
            return StubLSLVector ("llGroundContour", offset);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGroundNormal (LSL_Vector offset)
        {
            return StubLSLVector ("llGroundNormal", offset);
        }
        [xmrMethodIsNoisyAttribute] public void llGroundRepel (double height, int water, double tau)
        {
            StubVoid ("llGroundRepel", height, water, tau);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llGroundSlope (LSL_Vector offset)
        {
            return StubLSLVector ("llGroundSlope", offset);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llHTTPRequest (string url, LSL_List parameters, string body)
        {
            return StubLSLString ("llHTTPRequest", url, parameters, body);
        }
        [xmrMethodIsNoisyAttribute] public void llHTTPResponse (LSL_String id, int status, string body)
        {
            StubVoid ("llHTTPResponse", id, status, body);
        }
        [xmrMethodIsNoisyAttribute] public void llInstantMessage (string user, string message)
        {
            StubVoid ("llInstantMessage", user, message);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llKey2Name (string id)
        {
            return StubLSLString ("llKey2Name", id);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetUsername (string id)
        {
            return StubLSLString ("llGetUsername", id);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llRequestUsername (string id)
        {
            return StubLSLString ("llRequestUsername", id);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetDisplayName (string id)
        {
            return StubLSLString ("llGetDisplayName", id);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llRequestDisplayName (string id)
        {
            return StubLSLString ("llRequestDisplayName", id);
        }
        [xmrMethodIsNoisyAttribute] public void llLinkParticleSystem (int linknum, LSL_List rules)
        {
            StubVoid ("llLinkParticleSystem", linknum, rules);
        }
        [xmrMethodIsNoisyAttribute] public void llLinkSitTarget (LSL_Integer link, LSL_Vector offset, LSL_Rotation rot)
        {
            StubVoid ("llLinkSitTarget", link, offset, rot);
        }
        [xmrMethodIsNoisyAttribute] public void llLoadURL (string avatar_id, string message, string url)
        {
            StubVoid ("llLoadURL", avatar_id, message, url);
        }
        [xmrMethodIsNoisyAttribute] public void llLookAt (LSL_Vector target, double strength, double damping)
        {
            StubVoid ("llLookAt", target, strength, damping);
        }
        [xmrMethodIsNoisyAttribute] public void llLoopSound (string sound, double volume)
        {
            StubVoid ("llLoopSound", sound, volume);
        }
        [xmrMethodIsNoisyAttribute] public void llLoopSoundMaster (string sound, double volume)
        {
            StubVoid ("llLoopSoundMaster", sound, volume);
        }
        [xmrMethodIsNoisyAttribute] public void llLoopSoundSlave (string sound, double volume)
        {
            StubVoid ("llLoopSoundSlave", sound, volume);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llManageEstateAccess (int action, string avatar)
        {
            return StubLSLInteger ("llManageEstateAccess", action, avatar);
        }
        [xmrMethodIsNoisyAttribute] public void llMakeExplosion (int particles, double scale, double vel, double lifetime, double arc, string texture, LSL_Vector offset)
        {
            StubVoid ("llMakeExplosion", particles, scale, vel, lifetime, arc, texture, offset);
        }
        [xmrMethodIsNoisyAttribute] public void llMakeFire (int particles, double scale, double vel, double lifetime, double arc, string texture, LSL_Vector offset)
        {
            StubVoid ("llMakeFire", particles, scale, vel, lifetime, arc, texture, offset);
        }
        [xmrMethodIsNoisyAttribute] public void llMakeFountain (int particles, double scale, double vel, double lifetime, double arc, int bounce, string texture, LSL_Vector offset, double bounce_offset)
        {
            StubVoid ("llMakeFountain", particles, scale, vel, lifetime, arc, bounce, texture, offset, bounce_offset);
        }
        [xmrMethodIsNoisyAttribute] public void llMakeSmoke (int particles, double scale, double vel, double lifetime, double arc, string texture, LSL_Vector offset)
        {
            StubVoid ("llMakeSmoke", particles, scale, vel, lifetime, arc, texture, offset);
        }
        [xmrMethodIsNoisyAttribute] public void llMapDestination (string simname, LSL_Vector pos, LSL_Vector look_at)
        {
            StubVoid ("llMapDestination", simname, pos, look_at);
        }
        [xmrMethodIsNoisyAttribute] public void llMinEventDelay (double delay)
        {
            StubVoid ("llMinEventDelay", delay);
        }
        [xmrMethodIsNoisyAttribute] public void llModifyLand (int action, int brush)
        {
            StubVoid ("llModifyLand", action, brush);
        }
        [xmrMethodIsNoisyAttribute] public void llMoveToTarget (LSL_Vector target, double tau)
        {
            StubVoid ("llMoveToTarget", target, tau);
        }
        [xmrMethodIsNoisyAttribute] public void llOffsetTexture (double u, double v, int face)
        {
            StubVoid ("llOffsetTexture", u, v, face);
        }
        [xmrMethodIsNoisyAttribute] public void llOpenRemoteDataChannel ()
        {
            StubVoid ("llOpenRemoteDataChannel");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llOverMyLand (string id)
        {
            return StubLSLInteger ("llOverMyLand", id);
        }
        [xmrMethodIsNoisyAttribute] public void llOwnerSay (string msg)
        {
            StubVoid ("llOwnerSay", msg);
        }
        [xmrMethodIsNoisyAttribute] public void llParcelMediaCommandList (LSL_List commandList)
        {
            StubVoid ("llParcelMediaCommandList", commandList);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List llParcelMediaQuery (LSL_List aList)
        {
            return StubLSLList ("llParcelMediaQuery", aList);
        }
        [xmrMethodIsNoisyAttribute] public void llParticleSystem (LSL_List rules)
        {
            StubVoid ("llParticleSystem", rules);
        }
        [xmrMethodIsNoisyAttribute] public void llPassCollisions (int pass)
        {
            StubVoid ("llPassCollisions", pass);
        }
        [xmrMethodIsNoisyAttribute] public void llPassTouches (int pass)
        {
            StubVoid ("llPassTouches", pass);
        }
        [xmrMethodIsNoisyAttribute] public void llPlaySound (string sound, double volume)
        {
            StubVoid ("llPlaySound", sound, volume);
        }
        [xmrMethodIsNoisyAttribute] public void llPlaySoundSlave (string sound, double volume)
        {
            StubVoid ("llPlaySoundSlave", sound, volume);
        }
        [xmrMethodIsNoisyAttribute] public void llPointAt (LSL_Vector pos)
        {
            StubVoid ("llPointAt", pos);
        }
        [xmrMethodIsNoisyAttribute] public void llPreloadSound (string sound)
        {
            StubVoid ("llPreloadSound", sound);
        }
        [xmrMethodIsNoisyAttribute] public void llPushObject (string target, LSL_Vector impulse, LSL_Vector ang_impulse, int local)
        {
            StubVoid ("llPushObject", target, impulse, ang_impulse, local);
        }
        [xmrMethodIsNoisyAttribute] public void llRefreshPrimURL ()
        {
            StubVoid ("llRefreshPrimURL");
        }
        [xmrMethodIsNoisyAttribute] public void llReleaseCamera (string avatar)
        {
            StubVoid ("llReleaseCamera", avatar);
        }
        [xmrMethodIsNoisyAttribute] public void llReleaseControls ()
        {
            StubVoid ("llReleaseControls");
        }
        [xmrMethodIsNoisyAttribute] public void llReleaseURL (string url)
        {
            StubVoid ("llReleaseURL", url);
        }
        [xmrMethodIsNoisyAttribute] public void llRemoteDataReply (string channel, string message_id, string sdata, int idata)
        {
            StubVoid ("llRemoteDataReply", channel, message_id, sdata, idata);
        }
        [xmrMethodIsNoisyAttribute] public void llRemoteDataSetRegion ()
        {
            StubVoid ("llRemoteDataSetRegion");
        }
        [xmrMethodIsNoisyAttribute] public void llRemoteLoadScript (string target, string name, int running, int start_param)
        {
            StubVoid ("llRemoteLoadScript", target, name, running, start_param);
        }
        [xmrMethodIsNoisyAttribute] public void llRemoteLoadScriptPin (string target, string name, int pin, int running, int start_param)
        {
            StubVoid ("llRemoteLoadScriptPin", target, name, pin, running, start_param);
        }
        [xmrMethodIsNoisyAttribute] public void llRemoveFromLandBanList (string avatar)
        {
            StubVoid ("llRemoveFromLandBanList", avatar);
        }
        [xmrMethodIsNoisyAttribute] public void llRemoveFromLandPassList (string avatar)
        {
            StubVoid ("llRemoveFromLandPassList", avatar);
        }
        [xmrMethodIsNoisyAttribute] public void llRemoveInventory (string item)
        {
            StubVoid ("llRemoveInventory", item);
        }
        [xmrMethodIsNoisyAttribute] public void llRemoveVehicleFlags (int flags)
        {
            StubVoid ("llRemoveVehicleFlags", flags);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llRequestAgentData (string id, int data)
        {
            return StubLSLString ("llRequestAgentData", id, data);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llRequestInventoryData (string name)
        {
            return StubLSLString ("llRequestInventoryData", name);
        }
        [xmrMethodIsNoisyAttribute] public void llRequestPermissions (string agent, int perm)
        {
            StubVoid ("llRequestPermissions", agent, perm);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llRequestSecureURL ()
        {
            return StubLSLString ("llRequestSecureURL");
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llRequestSimulatorData (string simulator, int data)
        {
            return StubLSLString ("llRequestSimulatorData", simulator, data);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llRequestURL ()
        {
            return StubLSLString ("llRequestURL");
        }
        [xmrMethodIsNoisyAttribute] public void llResetLandBanList ()
        {
            StubVoid ("llResetLandBanList");
        }
        [xmrMethodIsNoisyAttribute] public void llResetLandPassList ()
        {
            StubVoid ("llResetLandPassList");
        }
        [xmrMethodIsNoisyAttribute] public void llResetOtherScript (string name)
        {
            StubVoid ("llResetOtherScript", name);
        }
        [xmrMethodIsNoisyAttribute] public void llResetTime ()
        {
            StubVoid ("llResetTime");
        }
        [xmrMethodIsNoisyAttribute] public void llRezAtRoot (string inventory, LSL_Vector position, LSL_Vector velocity, LSL_Rotation rot, int param)
        {
            StubVoid ("llRezAtRoot", inventory, position, velocity, rot, param);
        }
        [xmrMethodIsNoisyAttribute] public void llRezObject (string inventory, LSL_Vector pos, LSL_Vector vel, LSL_Rotation rot, int param)
        {
            StubVoid ("llRezObject", inventory, pos, vel, rot, param);
        }
        [xmrMethodIsNoisyAttribute] public void llRotateTexture (double rotation, int face)
        {
            StubVoid ("llRotateTexture", rotation, face);
        }
        [xmrMethodIsNoisyAttribute] public void llRotLookAt (LSL_Rotation target, double strength, double damping)
        {
            StubVoid ("llRotLookAt", target, strength, damping);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llRotTarget (LSL_Rotation rot, double error)
        {
            return StubLSLInteger ("llRotTarget", rot, error);
        }
        [xmrMethodIsNoisyAttribute] public void llRotTargetRemove (int number)
        {
            StubVoid ("llRotTargetRemove", number);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llSameGroup (string agent)
        {
            return StubLSLInteger ("llSameGroup", agent);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llScaleByFactor (double scaling_factor)
        {
            return StubLSLInteger ("llScaleByFactor", scaling_factor);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llGetMaxScaleFactor ()
        {
            return StubLSLFloat ("llGetMaxScaleFactor");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llGetMinScaleFactor ()
        {
            return StubLSLFloat ("llGetMinScaleFactor");
        }
        [xmrMethodIsNoisyAttribute] public void llScaleTexture (double u, double v, int face)
        {
            StubVoid ("llScaleTexture", u, v, face);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llScriptDanger (LSL_Vector pos)
        {
            return StubLSLInteger ("llScriptDanger", pos);
        }
        [xmrMethodIsNoisyAttribute] public void llScriptProfiler (LSL_Integer flag)
        {
            StubVoid ("llScriptProfiler", flag);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llSendRemoteData (string channel, string dest, int idata, string sdata)
        {
            return StubLSLString ("llSendRemoteData", channel, dest, idata, sdata);
        }
        [xmrMethodIsNoisyAttribute] public void llSensor (string name, string id, int type, double range, double arc)
        {
            StubVoid ("llSensor", name, id, type, range, arc);
        }
        [xmrMethodIsNoisyAttribute] public void llSensorRemove ()
        {
            StubVoid ("llSensorRemove");
        }
        [xmrMethodIsNoisyAttribute] public void llSensorRepeat (string name, string id, int type, double range, double arc, double rate)
        {
            StubVoid ("llSensorRepeat", name, id, type, range, arc, rate);
        }
        [xmrMethodIsNoisyAttribute] public void llSetAlpha (double alpha, int face)
        {
            StubVoid ("llSetAlpha", alpha, face);
        }
        [xmrMethodIsNoisyAttribute] public void llSetBuoyancy (double buoyancy)
        {
            StubVoid ("llSetBuoyancy", buoyancy);
        }
        [xmrMethodIsNoisyAttribute] public void llSetCameraAtOffset (LSL_Vector offset)
        {
            StubVoid ("llSetCameraAtOffset", offset);
        }
        [xmrMethodIsNoisyAttribute] public void llSetCameraEyeOffset (LSL_Vector offset)
        {
            StubVoid ("llSetCameraEyeOffset", offset);
        }
        [xmrMethodIsNoisyAttribute] public void llSetLinkCamera (LSL_Integer link, LSL_Vector eye, LSL_Vector at)
        {
            StubVoid ("llSetLinkCamera", link, eye, at);
        }
        [xmrMethodIsNoisyAttribute] public void llSetCameraParams (LSL_List rules)
        {
            StubVoid ("llSetCameraParams", rules);
        }
        [xmrMethodIsNoisyAttribute] public void llSetClickAction (int action)
        {
            StubVoid ("llSetClickAction", action);
        }
        [xmrMethodIsNoisyAttribute] public void llSetColor (LSL_Vector color, int face)
        {
            StubVoid ("llSetColor", color, face);
        }
        [xmrMethodIsNoisyAttribute] public void llSetContentType (LSL_String id, LSL_Integer type)
        {
            StubVoid ("llSetContentType", id, type);
        }
        [xmrMethodIsNoisyAttribute] public void llSetDamage (double damage)
        {
            StubVoid ("llSetDamage", damage);
        }
        [xmrMethodIsNoisyAttribute] public void llSetForce (LSL_Vector force, int local)
        {
            StubVoid ("llSetForce", force, local);
        }
        [xmrMethodIsNoisyAttribute] public void llSetForceAndTorque (LSL_Vector force, LSL_Vector torque, int local)
        {
            StubVoid ("llSetForceAndTorque", force, torque, local);
        }
        [xmrMethodIsNoisyAttribute] public void llSetVelocity (LSL_Vector vel, int local)
        {
            StubVoid ("llSetVelocity", vel, local);
        }
        [xmrMethodIsNoisyAttribute] public void llSetAngularVelocity (LSL_Vector angularVelocity, int local)
        {
            StubVoid ("llSetAngularVelocity", angularVelocity, local);
        }
        [xmrMethodIsNoisyAttribute] public void llSetHoverHeight (double height, int water, double tau)
        {
            StubVoid ("llSetHoverHeight", height, water, tau);
        }
        [xmrMethodIsNoisyAttribute] public void llSetInventoryPermMask (string item, int mask, int value)
        {
            StubVoid ("llSetInventoryPermMask", item, mask, value);
        }
        [xmrMethodIsNoisyAttribute] public void llSetLinkAlpha (int linknumber, double alpha, int face)
        {
            StubVoid ("llSetLinkAlpha", linknumber, alpha, face);
        }
        [xmrMethodIsNoisyAttribute] public void llSetLinkColor (int linknumber, LSL_Vector color, int face)
        {
            StubVoid ("llSetLinkColor", linknumber, color, face);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llSetLinkMedia (LSL_Integer link, LSL_Integer face, LSL_List rules)
        {
            return StubLSLInteger ("llSetLinkMedia", link, face, rules);
        }
        [xmrMethodIsNoisyAttribute] public void llSetLinkPrimitiveParams (int linknumber, LSL_List rules)
        {
            StubVoid ("llSetLinkPrimitiveParams", linknumber, rules);
        }
        [xmrMethodIsNoisyAttribute] public void llSetLinkTexture (int linknumber, string texture, int face)
        {
            StubVoid ("llSetLinkTexture", linknumber, texture, face);
        }
        [xmrMethodIsNoisyAttribute] public void llSetLinkTextureAnim (int linknum, int mode, int face, int sizex, int sizey, double start, double length, double rate)
        {
            StubVoid ("llSetLinkTextureAnim", linknum, mode, face, sizex, sizey, start, length, rate);
        }
        [xmrMethodIsNoisyAttribute] public void llSetLocalRot (LSL_Rotation rot)
        {
            StubVoid ("llSetLocalRot", rot);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llSetMemoryLimit (LSL_Integer limit)
        {
            return StubLSLInteger ("llSetMemoryLimit", limit);
        }
        [xmrMethodIsNoisyAttribute] public void llSetObjectDesc (string desc)
        {
            StubVoid ("llSetObjectDesc", desc);
        }
        [xmrMethodIsNoisyAttribute] public void llSetObjectName (string name)
        {
            StubVoid ("llSetObjectName", name);
        }
        [xmrMethodIsNoisyAttribute] public void llSetObjectPermMask (int mask, int value)
        {
            StubVoid ("llSetObjectPermMask", mask, value);
        }
        [xmrMethodIsNoisyAttribute] public void llSetParcelMusicURL (string url)
        {
            StubVoid ("llSetParcelMusicURL", url);
        }
        [xmrMethodIsNoisyAttribute] public void llSetPayPrice (int price, LSL_List quick_pay_buttons)
        {
            StubVoid ("llSetPayPrice", price, quick_pay_buttons);
        }
        [xmrMethodIsNoisyAttribute] public void llSetPos (LSL_Vector pos)
        {
            StubVoid ("llSetPos", pos);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llSetRegionPos (LSL_Vector pos)
        {
            return StubLSLInteger ("llSetRegionPos", pos);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llSetPrimMediaParams (LSL_Integer face, LSL_List rules)
        {
            return StubLSLInteger ("llSetPrimMediaParams", face, rules);
        }
        [xmrMethodIsNoisyAttribute] public void llSetPrimitiveParams (LSL_List rules)
        {
            StubVoid ("llSetPrimitiveParams", rules);
        }
        [xmrMethodIsNoisyAttribute] public void llSetLinkPrimitiveParamsFast (int linknum, LSL_List rules)
        {
            StubVoid ("llSetLinkPrimitiveParamsFast", linknum, rules);
        }
        [xmrMethodIsNoisyAttribute] public void llSetPrimURL (string url)
        {
            StubVoid ("llSetPrimURL", url);
        }
        [xmrMethodIsNoisyAttribute] public void llSetRemoteScriptAccessPin (int pin)
        {
            StubVoid ("llSetRemoteScriptAccessPin", pin);
        }
        [xmrMethodIsNoisyAttribute] public void llSetRot (LSL_Rotation rot)
        {
            StubVoid ("llSetRot", rot);
        }
        [xmrMethodIsNoisyAttribute] public void llSetScale (LSL_Vector scale)
        {
            StubVoid ("llSetScale", scale);
        }
        [xmrMethodIsNoisyAttribute] public void llSetScriptState (string name, int run)
        {
            StubVoid ("llSetScriptState", name, run);
        }
        [xmrMethodIsNoisyAttribute] public void llSetSitText (string text)
        {
            StubVoid ("llSetSitText", text);
        }
        [xmrMethodIsNoisyAttribute] public void llSetSoundQueueing (int queue)
        {
            StubVoid ("llSetSoundQueueing", queue);
        }
        [xmrMethodIsNoisyAttribute] public void llSetSoundRadius (double radius)
        {
            StubVoid ("llSetSoundRadius", radius);
        }
        [xmrMethodIsNoisyAttribute] public void llSetStatus (int status, int value)
        {
            StubVoid ("llSetStatus", status, value);
        }
        [xmrMethodIsNoisyAttribute] public void llSetText (string text, LSL_Vector color, double alpha)
        {
            StubVoid ("llSetText", text, color, alpha);
        }
        [xmrMethodIsNoisyAttribute] public void llSetTexture (string texture, int face)
        {
            StubVoid ("llSetTexture", texture, face);
        }
        [xmrMethodIsNoisyAttribute] public void llSetTextureAnim (int mode, int face, int sizex, int sizey, double start, double length, double rate)
        {
            StubVoid ("llSetTextureAnim", mode, face, sizex, sizey, start, length, rate);
        }
        [xmrMethodIsNoisyAttribute] public void llSetTimerEvent (double sec)
        {
            StubVoid ("llSetTimerEvent", sec);
        }
        [xmrMethodIsNoisyAttribute] public void llSetTorque (LSL_Vector torque, int local)
        {
            StubVoid ("llSetTorque", torque, local);
        }
        [xmrMethodIsNoisyAttribute] public void llSetTouchText (string text)
        {
            StubVoid ("llSetTouchText", text);
        }
        [xmrMethodIsNoisyAttribute] public void llSetVehicleFlags (int flags)
        {
            StubVoid ("llSetVehicleFlags", flags);
        }
        [xmrMethodIsNoisyAttribute] public void llSetVehicleFloatParam (int param, LSL_Float value)
        {
            StubVoid ("llSetVehicleFloatParam", param, value);
        }
        [xmrMethodIsNoisyAttribute] public void llSetVehicleRotationParam (int param, LSL_Rotation rot)
        {
            StubVoid ("llSetVehicleRotationParam", param, rot);
        }
        [xmrMethodIsNoisyAttribute] public void llSetVehicleType (int type)
        {
            StubVoid ("llSetVehicleType", type);
        }
        [xmrMethodIsNoisyAttribute] public void llSetVehicleVectorParam (int param, LSL_Vector vec)
        {
            StubVoid ("llSetVehicleVectorParam", param, vec);
        }
        [xmrMethodIsNoisyAttribute] public void llShout (int channelID, string text)
        {
            StubVoid ("llShout", channelID, text);
        }
        [xmrMethodIsNoisyAttribute] public void llSitTarget (LSL_Vector offset, LSL_Rotation rot)
        {
            StubVoid ("llSitTarget", offset, rot);
        }
        [xmrMethodIsNoisyAttribute] public void llSleep (double sec)
        {
            StubVoid ("llSleep", sec);
        }
        [xmrMethodIsNoisyAttribute] public void llSound (string sound, double volume, int queue, int loop)
        {
            StubVoid ("llSound", sound, volume, queue, loop);
        }
        [xmrMethodIsNoisyAttribute] public void llSoundPreload (string sound)
        {
            StubVoid ("llSoundPreload", sound);
        }
        [xmrMethodIsNoisyAttribute] public void llStartAnimation (string anim)
        {
            StubVoid ("llStartAnimation", anim);
        }
        [xmrMethodIsNoisyAttribute] public void llStopAnimation (string anim)
        {
            StubVoid ("llStopAnimation", anim);
        }
        [xmrMethodIsNoisyAttribute] public void llStopHover ()
        {
            StubVoid ("llStopHover");
        }
        [xmrMethodIsNoisyAttribute] public void llStopLookAt ()
        {
            StubVoid ("llStopLookAt");
        }
        [xmrMethodIsNoisyAttribute] public void llStopMoveToTarget ()
        {
            StubVoid ("llStopMoveToTarget");
        }
        [xmrMethodIsNoisyAttribute] public void llStopPointAt ()
        {
            StubVoid ("llStopPointAt");
        }
        [xmrMethodIsNoisyAttribute] public void llStopSound ()
        {
            StubVoid ("llStopSound");
        }
        [xmrMethodIsNoisyAttribute] public void llTakeCamera (string avatar)
        {
            StubVoid ("llTakeCamera", avatar);
        }
        [xmrMethodIsNoisyAttribute] public void llTakeControls (int controls, int accept, int pass_on)
        {
            StubVoid ("llTakeControls", controls, accept, pass_on);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llTarget (LSL_Vector position, double range)
        {
            return StubLSLInteger ("llTarget", position, range);
        }
        [xmrMethodIsNoisyAttribute] public void llTargetOmega (LSL_Vector axis, double spinrate, double gain)
        {
            StubVoid ("llTargetOmega", axis, spinrate, gain);
        }
        [xmrMethodIsNoisyAttribute] public void llTargetRemove (int number)
        {
            StubVoid ("llTargetRemove", number);
        }
        [xmrMethodIsNoisyAttribute] public void llTeleportAgentHome (string agent)
        {
            StubVoid ("llTeleportAgentHome", agent);
        }
        [xmrMethodIsNoisyAttribute] public void llTeleportAgent (string agent, string simname, LSL_Vector pos, LSL_Vector lookAt)
        {
            StubVoid ("llTeleportAgent", agent, simname, pos, lookAt);
        }
        [xmrMethodIsNoisyAttribute] public void llTeleportAgentGlobalCoords (string agent, LSL_Vector global, LSL_Vector pos, LSL_Vector lookAt)
        {
            StubVoid ("llTeleportAgentGlobalCoords", agent, global, pos, lookAt);
        }
        [xmrMethodIsNoisyAttribute] public void llTextBox (string avatar, string message, int chat_channel)
        {
            StubVoid ("llTextBox", avatar, message, chat_channel);
        }
        [xmrMethodIsNoisyAttribute] public void llTriggerSound (string sound, double volume)
        {
            StubVoid ("llTriggerSound", sound, volume);
        }
        [xmrMethodIsNoisyAttribute] public void llTriggerSoundLimited (string sound, double volume, LSL_Vector top_north_east, LSL_Vector bottom_south_west)
        {
            StubVoid ("llTriggerSoundLimited", sound, volume, top_north_east, bottom_south_west);
        }
        [xmrMethodIsNoisyAttribute] public void llUnSit (string id)
        {
            StubVoid ("llUnSit", id);
        }
        [xmrMethodIsNoisyAttribute] public void llVolumeDetect (int detect)
        {
            StubVoid ("llVolumeDetect", detect);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float llWater (LSL_Vector offset)
        {
            return StubLSLFloat ("llWater", offset);
        }
        [xmrMethodIsNoisyAttribute] public void llWhisper (int channelID, string text)
        {
            StubVoid ("llWhisper", channelID, text);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector llWind (LSL_Vector offset)
        {
            return StubLSLVector ("llWind", offset);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llXorBase64Strings (string str1, string str2)
        {
            return StubLSLString ("llXorBase64Strings", str1, str2);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer llGetLinkNumberOfSides (LSL_Integer link)
        {
            return StubLSLInteger ("llGetLinkNumberOfSides", link);
        }
        [xmrMethodIsNoisyAttribute] public void llSetPhysicsMaterial (int material_bits, LSL_Float material_gravity_modifier, LSL_Float material_restitution, LSL_Float material_friction, LSL_Float material_density)
        {
            StubVoid ("llSetPhysicsMaterial", material_bits, material_gravity_modifier, material_restitution, material_friction, material_density);
        }
        [xmrMethodIsNoisyAttribute] public void SetPrimitiveParamsEx (LSL_String prim, LSL_List rules, string originFunc)
        {
            StubVoid ("SetPrimitiveParamsEx", prim, rules, originFunc);
        }
        [xmrMethodIsNoisyAttribute] public void llSetKeyframedMotion (LSL_List frames, LSL_List options)
        {
            StubVoid ("llSetKeyframedMotion", frames, options);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List GetPrimitiveParamsEx (LSL_String prim, LSL_List rules)
        {
            return StubLSLList ("GetPrimitiveParamsEx", prim, rules);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List llGetPhysicsMaterial ()
        {
            return StubLSLList ("llGetPhysicsMaterial");
        }
        [xmrMethodIsNoisyAttribute] public void llSetAnimationOverride (LSL_String animState, LSL_String anim)
        {
            StubVoid ("llSetAnimationOverride", animState, anim);
        }
        [xmrMethodIsNoisyAttribute] public void llResetAnimationOverride (LSL_String anim_state)
        {
            StubVoid ("llResetAnimationOverride", anim_state);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llGetAnimationOverride (LSL_String anim_state)
        {
            return StubLSLString ("llGetAnimationOverride", anim_state);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llJsonGetValue (LSL_String json, LSL_List specifiers)
        {
            return StubLSLString ("llJsonGetValue", json, specifiers);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List llJson2List (LSL_String json)
        {
            return StubLSLList ("llJson2List", json);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llList2Json (LSL_String type, LSL_List values)
        {
            return StubLSLString ("llList2Json", type, values);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llJsonSetValue (LSL_String json, LSL_List specifiers, LSL_String value)
        {
            return StubLSLString ("llJsonSetValue", json, specifiers, value);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String llJsonValueType (LSL_String json, LSL_List specifiers)
        {
            return StubLSLString ("llJsonValueType", json, specifiers);
        }
        [xmrMethodIsNoisyAttribute] public void CheckThreatLevel (OpenSim.Region.ScriptEngine.Shared.Api.Interfaces.ThreatLevel level, string function)
        {
            StubVoid ("CheckThreatLevel", level, function);
        }
        [xmrMethodIsNoisyAttribute] public string osSetDynamicTextureURL (string dynamicID, string contentType, string url, string extraParams, int timer)
        {
            return StubSysString ("osSetDynamicTextureURL", dynamicID, contentType, url, extraParams, timer);
        }
        [xmrMethodIsNoisyAttribute] public string osSetDynamicTextureURLBlend (string dynamicID, string contentType, string url, string extraParams, int timer, int alpha)
        {
            return StubSysString ("osSetDynamicTextureURLBlend", dynamicID, contentType, url, extraParams, timer, alpha);
        }
        [xmrMethodIsNoisyAttribute] public string osSetDynamicTextureURLBlendFace (string dynamicID, string contentType, string url, string extraParams, System.Boolean blend, int disp, int timer, int alpha, int face)
        {
            return StubSysString ("osSetDynamicTextureURLBlendFace", dynamicID, contentType, url, extraParams, blend, disp, timer, alpha, face);
        }
        [xmrMethodIsNoisyAttribute] public string osSetDynamicTextureData (string dynamicID, string contentType, string data, string extraParams, int timer)
        {
            return StubSysString ("osSetDynamicTextureData", dynamicID, contentType, data, extraParams, timer);
        }
        [xmrMethodIsNoisyAttribute] public string osSetDynamicTextureDataFace (string dynamicID, string contentType, string data, string extraParams, int timer, int face)
        {
            return StubSysString ("osSetDynamicTextureDataFace", dynamicID, contentType, data, extraParams, timer, face);
        }
        [xmrMethodIsNoisyAttribute] public string osSetDynamicTextureDataBlend (string dynamicID, string contentType, string data, string extraParams, int timer, int alpha)
        {
            return StubSysString ("osSetDynamicTextureDataBlend", dynamicID, contentType, data, extraParams, timer, alpha);
        }
        [xmrMethodIsNoisyAttribute] public string osSetDynamicTextureDataBlendFace (string dynamicID, string contentType, string data, string extraParams, System.Boolean blend, int disp, int timer, int alpha, int face)
        {
            return StubSysString ("osSetDynamicTextureDataBlendFace", dynamicID, contentType, data, extraParams, blend, disp, timer, alpha, face);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float osGetTerrainHeight (int x, int y)
        {
            return StubLSLFloat ("osGetTerrainHeight", x, y);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float osTerrainGetHeight (int x, int y)
        {
            return StubLSLFloat ("osTerrainGetHeight", x, y);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer osSetTerrainHeight (int x, int y, double val)
        {
            return StubLSLInteger ("osSetTerrainHeight", x, y, val);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer osTerrainSetHeight (int x, int y, double val)
        {
            return StubLSLInteger ("osTerrainSetHeight", x, y, val);
        }
        [xmrMethodIsNoisyAttribute] public void osTerrainFlush ()
        {
            StubVoid ("osTerrainFlush");
        }
        [xmrMethodIsNoisyAttribute] public int osRegionRestart (double seconds)
        {
            return StubSysInteger ("osRegionRestart", seconds);
        }
        [xmrMethodIsNoisyAttribute] public int osRegionRestart (double seconds, string msg)
        {
            return StubSysInteger ("osRegionRestart", seconds, msg);
        }
        [xmrMethodIsNoisyAttribute] public void osRegionNotice (string msg)
        {
            StubVoid ("osRegionNotice", msg);
        }
        [xmrMethodIsNoisyAttribute] public System.Boolean osConsoleCommand (string Command)
        {
            return StubSysBoolean ("osConsoleCommand", Command);
        }
        [xmrMethodIsNoisyAttribute] public void osSetParcelMediaURL (string url)
        {
            StubVoid ("osSetParcelMediaURL", url);
        }
        [xmrMethodIsNoisyAttribute] public void osSetPrimFloatOnWater (int floatYN)
        {
            StubVoid ("osSetPrimFloatOnWater", floatYN);
        }
        [xmrMethodIsNoisyAttribute] public void osSetParcelSIPAddress (string SIPAddress)
        {
            StubVoid ("osSetParcelSIPAddress", SIPAddress);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List osGetAgents ()
        {
            return StubLSLList ("osGetAgents");
        }
        [xmrMethodIsNoisyAttribute] public string osGetAgentIP (string agent)
        {
            return StubSysString ("osGetAgentIP", agent);
        }
        [xmrMethodIsNoisyAttribute] public void osTeleportAgent (string agent, string regionName, LSL_Vector position, LSL_Vector lookat)
        {
            StubVoid ("osTeleportAgent", agent, regionName, position, lookat);
        }
        [xmrMethodIsNoisyAttribute] public void osTeleportAgent (string agent, int regionX, int regionY, LSL_Vector position, LSL_Vector lookat)
        {
            StubVoid ("osTeleportAgent", agent, regionX, regionY, position, lookat);
        }
        [xmrMethodIsNoisyAttribute] public void osTeleportAgent (string agent, LSL_Vector position, LSL_Vector lookat)
        {
            StubVoid ("osTeleportAgent", agent, position, lookat);
        }
        [xmrMethodIsNoisyAttribute] public void osTeleportOwner (string regionName, LSL_Vector position, LSL_Vector lookat)
        {
            StubVoid ("osTeleportOwner", regionName, position, lookat);
        }
        [xmrMethodIsNoisyAttribute] public void osTeleportOwner (int regionX, int regionY, LSL_Vector position, LSL_Vector lookat)
        {
            StubVoid ("osTeleportOwner", regionX, regionY, position, lookat);
        }
        [xmrMethodIsNoisyAttribute] public void osTeleportOwner (LSL_Vector position, LSL_Vector lookat)
        {
            StubVoid ("osTeleportOwner", position, lookat);
        }
        [xmrMethodIsNoisyAttribute] public void osAvatarPlayAnimation (string avatar, string animation)
        {
            StubVoid ("osAvatarPlayAnimation", avatar, animation);
        }
        [xmrMethodIsNoisyAttribute] public void osAvatarStopAnimation (string avatar, string animation)
        {
            StubVoid ("osAvatarStopAnimation", avatar, animation);
        }
        [xmrMethodIsNoisyAttribute] public void osForceAttachToAvatar (int attachment)
        {
            StubVoid ("osForceAttachToAvatar", attachment);
        }
        [xmrMethodIsNoisyAttribute] public void osForceAttachToAvatarFromInventory (string itemName, int attachment)
        {
            StubVoid ("osForceAttachToAvatarFromInventory", itemName, attachment);
        }
        [xmrMethodIsNoisyAttribute] public void osForceAttachToOtherAvatarFromInventory (string rawAvatarId, string itemName, int attachmentPoint)
        {
            StubVoid ("osForceAttachToOtherAvatarFromInventory", rawAvatarId, itemName, attachmentPoint);
        }
        [xmrMethodIsNoisyAttribute] public void osForceDetachFromAvatar ()
        {
            StubVoid ("osForceDetachFromAvatar");
        }
        [xmrMethodIsNoisyAttribute] public LSL_List osGetNumberOfAttachments (LSL_String avatar, LSL_List attachmentPoints)
        {
            return StubLSLList ("osGetNumberOfAttachments", avatar, attachmentPoints);
        }
        [xmrMethodIsNoisyAttribute] public void osMessageAttachments (LSL_String avatar, string message, LSL_List attachmentPoints, int flags)
        {
            StubVoid ("osMessageAttachments", avatar, message, attachmentPoints, flags);
        }
        [xmrMethodIsNoisyAttribute] public string osDrawFilledEllipse (string drawList, int width, int height)
        {
            return StubSysString ("osDrawFilledEllipse", drawList, width, height);
        }
        [xmrMethodIsNoisyAttribute] public string osDrawResetTransform (string drawList)
        {
            return StubSysString ("osDrawResetTransform", drawList);
        }
        [xmrMethodIsNoisyAttribute] public string osDrawRotationTransform (string drawList, LSL_Float x)
        {
            return StubSysString ("osDrawRotationTransform", drawList, x);
        }
        [xmrMethodIsNoisyAttribute] public string osDrawScaleTransform (string drawList, LSL_Float x, LSL_Float y)
        {
            return StubSysString ("osDrawScaleTransform", drawList, x, y);
        }
        [xmrMethodIsNoisyAttribute] public string osDrawTranslationTransform (string drawList, LSL_Float x, LSL_Float y)
        {
            return StubSysString ("osDrawTranslationTransform", drawList, x, y);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector osGetDrawStringSize (string contentType, string text, string fontName, int fontSize)
        {
            return StubLSLVector ("osGetDrawStringSize", contentType, text, fontName, fontSize);
        }
        [xmrMethodIsNoisyAttribute] public void osSetStateEvents (int events)
        {
            StubVoid ("osSetStateEvents", events);
        }
        [xmrMethodIsNoisyAttribute] public void osSetRegionWaterHeight (double height)
        {
            StubVoid ("osSetRegionWaterHeight", height);
        }
        [xmrMethodIsNoisyAttribute] public void osSetRegionSunSettings (System.Boolean useEstateSun, System.Boolean sunFixed, double sunHour)
        {
            StubVoid ("osSetRegionSunSettings", useEstateSun, sunFixed, sunHour);
        }
        [xmrMethodIsNoisyAttribute] public void osSetEstateSunSettings (System.Boolean sunFixed, double sunHour)
        {
            StubVoid ("osSetEstateSunSettings", sunFixed, sunHour);
        }
        [xmrMethodIsNoisyAttribute] public double osGetCurrentSunHour ()
        {
            return StubSysDouble ("osGetCurrentSunHour");
        }
        [xmrMethodIsNoisyAttribute] public double osGetSunParam (string param)
        {
            return StubSysDouble ("osGetSunParam", param);
        }
        [xmrMethodIsNoisyAttribute] public double osSunGetParam (string param)
        {
            return StubSysDouble ("osSunGetParam", param);
        }
        [xmrMethodIsNoisyAttribute] public void osSetSunParam (string param, double value)
        {
            StubVoid ("osSetSunParam", param, value);
        }
        [xmrMethodIsNoisyAttribute] public void osSunSetParam (string param, double value)
        {
            StubVoid ("osSunSetParam", param, value);
        }
        [xmrMethodIsNoisyAttribute] public string osWindActiveModelPluginName ()
        {
            return StubSysString ("osWindActiveModelPluginName");
        }
        [xmrMethodIsNoisyAttribute] public void osSetWindParam (string plugin, string param, LSL_Float value)
        {
            StubVoid ("osSetWindParam", plugin, param, value);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float osGetWindParam (string plugin, string param)
        {
            return StubLSLFloat ("osGetWindParam", plugin, param);
        }
        [xmrMethodIsNoisyAttribute] public void osParcelJoin (LSL_Vector pos1, LSL_Vector pos2)
        {
            StubVoid ("osParcelJoin", pos1, pos2);
        }
        [xmrMethodIsNoisyAttribute] public void osParcelSubdivide (LSL_Vector pos1, LSL_Vector pos2)
        {
            StubVoid ("osParcelSubdivide", pos1, pos2);
        }
        [xmrMethodIsNoisyAttribute] public void osSetParcelDetails (LSL_Vector pos, LSL_List rules)
        {
            StubVoid ("osSetParcelDetails", pos, rules);
        }
        [xmrMethodIsNoisyAttribute] public void osParcelSetDetails (LSL_Vector pos, LSL_List rules)
        {
            StubVoid ("osParcelSetDetails", pos, rules);
        }
        [xmrMethodIsNoisyAttribute] public string osGetScriptEngineName ()
        {
            return StubSysString ("osGetScriptEngineName");
        }
        [xmrMethodIsNoisyAttribute] public string osGetSimulatorVersion ()
        {
            return StubSysString ("osGetSimulatorVersion");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer osCheckODE ()
        {
            return StubLSLInteger ("osCheckODE");
        }
        [xmrMethodIsNoisyAttribute] public string osGetPhysicsEngineType ()
        {
            return StubSysString ("osGetPhysicsEngineType");
        }
        [xmrMethodIsNoisyAttribute] public string osGetPhysicsEngineName ()
        {
            return StubSysString ("osGetPhysicsEngineName");
        }
        [xmrMethodIsNoisyAttribute] public System.Object osParseJSONNew (string JSON)
        {
            return StubSysObject ("osParseJSONNew", JSON);
        }
        [xmrMethodIsNoisyAttribute] public void osMessageObject (LSL_String objectUUID, string message)
        {
            StubVoid ("osMessageObject", objectUUID, message);
        }
        [xmrMethodIsNoisyAttribute] public void osMakeNotecard (string notecardName, LSL_List contents)
        {
            StubVoid ("osMakeNotecard", notecardName, contents);
        }
        [xmrMethodIsNoisyAttribute] public string osGetNotecardLine (string name, int line)
        {
            return StubSysString ("osGetNotecardLine", name, line);
        }
        [xmrMethodIsNoisyAttribute] public string osGetNotecard (string name)
        {
            return StubSysString ("osGetNotecard", name);
        }
        [xmrMethodIsNoisyAttribute] public int osGetNumberOfNotecardLines (string name)
        {
            return StubSysInteger ("osGetNumberOfNotecardLines", name);
        }
        [xmrMethodIsNoisyAttribute] public string osAvatarName2Key (string firstname, string lastname)
        {
            return StubSysString ("osAvatarName2Key", firstname, lastname);
        }
        [xmrMethodIsNoisyAttribute] public string osKey2Name (string id)
        {
            return StubSysString ("osKey2Name", id);
        }
        [xmrMethodIsNoisyAttribute] public string osGetGridNick ()
        {
            return StubSysString ("osGetGridNick");
        }
        [xmrMethodIsNoisyAttribute] public string osGetGridName ()
        {
            return StubSysString ("osGetGridName");
        }
        [xmrMethodIsNoisyAttribute] public string osGetGridLoginURI ()
        {
            return StubSysString ("osGetGridLoginURI");
        }
        [xmrMethodIsNoisyAttribute] public string osGetGridHomeURI ()
        {
            return StubSysString ("osGetGridHomeURI");
        }
        [xmrMethodIsNoisyAttribute] public string osGetGridGatekeeperURI ()
        {
            return StubSysString ("osGetGridGatekeeperURI");
        }
        [xmrMethodIsNoisyAttribute] public string osGetGridCustom (string key)
        {
            return StubSysString ("osGetGridCustom", key);
        }
        [xmrMethodIsNoisyAttribute] public string osGetAvatarHomeURI (string uuid)
        {
            return StubSysString ("osGetAvatarHomeURI", uuid);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String osReplaceString (string src, string pattern, string replace, int count, int start)
        {
            return StubLSLString ("osReplaceString", src, pattern, replace, count, start);
        }
        [xmrMethodIsNoisyAttribute] public string osLoadedCreationDate ()
        {
            return StubSysString ("osLoadedCreationDate");
        }
        [xmrMethodIsNoisyAttribute] public string osLoadedCreationTime ()
        {
            return StubSysString ("osLoadedCreationTime");
        }
        [xmrMethodIsNoisyAttribute] public string osLoadedCreationID ()
        {
            return StubSysString ("osLoadedCreationID");
        }
        [xmrMethodIsNoisyAttribute] public LSL_List osGetLinkPrimitiveParams (int linknumber, LSL_List rules)
        {
            return StubLSLList ("osGetLinkPrimitiveParams", linknumber, rules);
        }
        [xmrMethodIsNoisyAttribute] public void osForceCreateLink (string target, int parent)
        {
            StubVoid ("osForceCreateLink", target, parent);
        }
        [xmrMethodIsNoisyAttribute] public void osForceBreakLink (int linknum)
        {
            StubVoid ("osForceBreakLink", linknum);
        }
        [xmrMethodIsNoisyAttribute] public void osForceBreakAllLinks ()
        {
            StubVoid ("osForceBreakAllLinks");
        }
        [xmrMethodIsNoisyAttribute] public void osDie (LSL_String objectUUID)
        {
            StubVoid ("osDie", objectUUID);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer osIsNpc (LSL_String npc)
        {
            return StubLSLInteger ("osIsNpc", npc);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String osNpcCreate (string user, string name, LSL_Vector position, string notecard)
        {
            return StubLSLString ("osNpcCreate", user, name, position, notecard);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String osNpcCreate (string user, string name, LSL_Vector position, string notecard, int options)
        {
            return StubLSLString ("osNpcCreate", user, name, position, notecard, options);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String osNpcSaveAppearance (LSL_String npc, string notecard)
        {
            return StubLSLString ("osNpcSaveAppearance", npc, notecard);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcLoadAppearance (LSL_String npc, string notecard)
        {
            StubVoid ("osNpcLoadAppearance", npc, notecard);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector osNpcGetPos (LSL_String npc)
        {
            return StubLSLVector ("osNpcGetPos", npc);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcMoveTo (LSL_String npc, LSL_Vector position)
        {
            StubVoid ("osNpcMoveTo", npc, position);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcMoveToTarget (LSL_String npc, LSL_Vector target, int options)
        {
            StubVoid ("osNpcMoveToTarget", npc, target, options);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String osNpcGetOwner (LSL_String npc)
        {
            return StubLSLString ("osNpcGetOwner", npc);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Rotation osNpcGetRot (LSL_String npc)
        {
            return StubLSLRotation ("osNpcGetRot", npc);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcSetRot (LSL_String npc, LSL_Rotation rot)
        {
            StubVoid ("osNpcSetRot", npc, rot);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcStopMoveToTarget (LSL_String npc)
        {
            StubVoid ("osNpcStopMoveToTarget", npc);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcSetProfileAbout (LSL_String npc, string about)
        {
            StubVoid ("osNpcSetProfileAbout", npc, about);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcSetProfileImage (LSL_String npc, string image)
        {
            StubVoid ("osNpcSetProfileImage", npc, image);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcSay (LSL_String npc, string message)
        {
            StubVoid ("osNpcSay", npc, message);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcSay (LSL_String npc, int channel, string message)
        {
            StubVoid ("osNpcSay", npc, channel, message);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcShout (LSL_String npc, int channel, string message)
        {
            StubVoid ("osNpcShout", npc, channel, message);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcSit (LSL_String npc, LSL_String target, int options)
        {
            StubVoid ("osNpcSit", npc, target, options);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcStand (LSL_String npc)
        {
            StubVoid ("osNpcStand", npc);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcRemove (LSL_String npc)
        {
            StubVoid ("osNpcRemove", npc);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcPlayAnimation (LSL_String npc, string animation)
        {
            StubVoid ("osNpcPlayAnimation", npc, animation);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcStopAnimation (LSL_String npc, string animation)
        {
            StubVoid ("osNpcStopAnimation", npc, animation);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcTouch (LSL_String npcLSL_Key, LSL_String object_key, LSL_Integer link_num)
        {
            StubVoid ("osNpcTouch", npcLSL_Key, object_key, link_num);
        }
        [xmrMethodIsNoisyAttribute] public void osNpcWhisper (LSL_String npc, int channel, string message)
        {
            StubVoid ("osNpcWhisper", npc, channel, message);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String osOwnerSaveAppearance (string notecard)
        {
            return StubLSLString ("osOwnerSaveAppearance", notecard);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String osAgentSaveAppearance (LSL_String agentId, string notecard)
        {
            return StubLSLString ("osAgentSaveAppearance", agentId, notecard);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String osGetGender (LSL_String rawAvatarId)
        {
            return StubLSLString ("osGetGender", rawAvatarId);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String osGetMapTexture ()
        {
            return StubLSLString ("osGetMapTexture");
        }
        [xmrMethodIsNoisyAttribute] public LSL_String osGetRegionMapTexture (string regionName)
        {
            return StubLSLString ("osGetRegionMapTexture", regionName);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List osGetRegionStats ()
        {
            return StubLSLList ("osGetRegionStats");
        }
        [xmrMethodIsNoisyAttribute] public LSL_Vector osGetRegionSize ()
        {
            return StubLSLVector ("osGetRegionSize");
        }
        [xmrMethodIsNoisyAttribute] public int osGetSimulatorMemory ()
        {
            return StubSysInteger ("osGetSimulatorMemory");
        }
        [xmrMethodIsNoisyAttribute] public int osGetSimulatorMemoryKB ()
        {
            return StubSysInteger ("osGetSimulatorMemoryKB");
        }
        [xmrMethodIsNoisyAttribute] public void osKickAvatar (string FirstName, string SurName, string alert)
        {
            StubVoid ("osKickAvatar", FirstName, SurName, alert);
        }
        [xmrMethodIsNoisyAttribute] public void osSetSpeed (string UUID, LSL_Float SpeedModifier)
        {
            StubVoid ("osSetSpeed", UUID, SpeedModifier);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float osGetHealth (string avatar)
        {
            return StubLSLFloat ("osGetHealth", avatar);
        }
        [xmrMethodIsNoisyAttribute] public void osCauseHealing (string avatar, double healing)
        {
            StubVoid ("osCauseHealing", avatar, healing);
        }
        [xmrMethodIsNoisyAttribute] public void osSetHealth (string avatar, double health)
        {
            StubVoid ("osSetHealth", avatar, health);
        }
        [xmrMethodIsNoisyAttribute] public void osSetHealRate (string avatar, double health)
        {
            StubVoid ("osSetHealRate", avatar, health);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float osGetHealRate (string avatar)
        {
            return StubLSLFloat ("osGetHealRate", avatar);
        }
        [xmrMethodIsNoisyAttribute] public void osCauseDamage (string avatar, double damage)
        {
            StubVoid ("osCauseDamage", avatar, damage);
        }
        [xmrMethodIsNoisyAttribute] public void osForceOtherSit (string avatar)
        {
            StubVoid ("osForceOtherSit", avatar);
        }
        [xmrMethodIsNoisyAttribute] public void osForceOtherSit (string avatar, string target)
        {
            StubVoid ("osForceOtherSit", avatar, target);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List osGetPrimitiveParams (LSL_String prim, LSL_List rules)
        {
            return StubLSLList ("osGetPrimitiveParams", prim, rules);
        }
        [xmrMethodIsNoisyAttribute] public void osSetPrimitiveParams (LSL_String prim, LSL_List rules)
        {
            StubVoid ("osSetPrimitiveParams", prim, rules);
        }
        [xmrMethodIsNoisyAttribute] public void osSetProjectionParams (System.Boolean projection, LSL_String texture, double fov, double focus, double amb)
        {
            StubVoid ("osSetProjectionParams", projection, texture, fov, focus, amb);
        }
        [xmrMethodIsNoisyAttribute] public void osSetProjectionParams (LSL_String prim, System.Boolean projection, LSL_String texture, double fov, double focus, double amb)
        {
            StubVoid ("osSetProjectionParams", prim, projection, texture, fov, focus, amb);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List osGetAvatarList ()
        {
            return StubLSLList ("osGetAvatarList");
        }
        [xmrMethodIsNoisyAttribute] public LSL_List osGetNPCList ()
        {
            return StubLSLList ("osGetNPCList");
        }
        [xmrMethodIsNoisyAttribute] public LSL_String osGetInventoryDesc (string item)
        {
            return StubLSLString ("osGetInventoryDesc", item);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer osInviteToGroup (LSL_String agentId)
        {
            return StubLSLInteger ("osInviteToGroup", agentId);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer osEjectFromGroup (LSL_String agentId)
        {
            return StubLSLInteger ("osEjectFromGroup", agentId);
        }
        [xmrMethodIsNoisyAttribute] public void osSetTerrainTexture (int level, LSL_String texture)
        {
            StubVoid ("osSetTerrainTexture", level, texture);
        }
        [xmrMethodIsNoisyAttribute] public void osSetTerrainTextureHeight (int corner, double low, double high)
        {
            StubVoid ("osSetTerrainTextureHeight", corner, low, high);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer osIsUUID (string thing)
        {
            return StubLSLInteger ("osIsUUID", thing);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float osMin (double a, double b)
        {
            return StubLSLFloat ("osMin", a, b);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Float osMax (double a, double b)
        {
            return StubLSLFloat ("osMax", a, b);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String osGetRezzingObject ()
        {
            return StubLSLString ("osGetRezzingObject");
        }
        [xmrMethodIsNoisyAttribute] public void osSetContentType (LSL_String id, string type)
        {
            StubVoid ("osSetContentType", id, type);
        }
        [xmrMethodIsNoisyAttribute] public void osDropAttachment ()
        {
            StubVoid ("osDropAttachment");
        }
        [xmrMethodIsNoisyAttribute] public void osForceDropAttachment ()
        {
            StubVoid ("osForceDropAttachment");
        }
        [xmrMethodIsNoisyAttribute] public void osDropAttachmentAt (LSL_Vector pos, LSL_Rotation rot)
        {
            StubVoid ("osDropAttachmentAt", pos, rot);
        }
        [xmrMethodIsNoisyAttribute] public void osForceDropAttachmentAt (LSL_Vector pos, LSL_Rotation rot)
        {
            StubVoid ("osForceDropAttachmentAt", pos, rot);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer osListenRegex (int channelID, string name, string ID, string msg, int regexBitfield)
        {
            return StubLSLInteger ("osListenRegex", channelID, name, ID, msg, regexBitfield);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer osRegexIsMatch (string input, string pattern)
        {
            return StubLSLInteger ("osRegexIsMatch", input, pattern);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String osRequestURL (LSL_List options)
        {
            return StubLSLString ("osRequestURL", options);
        }
        [xmrMethodIsNoisyAttribute] public LSL_String osRequestSecureURL (LSL_List options)
        {
            return StubLSLString ("osRequestSecureURL", options);
        }
        [xmrMethodIsNoisyAttribute] public void osCollisionSound (string impact_sound, double impact_volume)
        {
            StubVoid ("osCollisionSound", impact_sound, impact_volume);
        }
        [xmrMethodIsNoisyAttribute] public void osVolumeDetect (int detect)
        {
            StubVoid ("osVolumeDetect", detect);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List osGetInertiaData ()
        {
            return StubLSLList ("osGetInertiaData");
        }
        [xmrMethodIsNoisyAttribute] public void osClearInertia ()
        {
            StubVoid ("osClearInertia");
        }
        [xmrMethodIsNoisyAttribute] public void osSetInertia (LSL_Float mass, LSL_Vector centerOfMass, LSL_Vector principalInertiaScaled, LSL_Rotation rot)
        {
            StubVoid ("osSetInertia", mass, centerOfMass, principalInertiaScaled, rot);
        }
        [xmrMethodIsNoisyAttribute] public void osSetInertiaAsBox (LSL_Float mass, LSL_Vector boxSize, LSL_Vector centerOfMass, LSL_Rotation rot)
        {
            StubVoid ("osSetInertiaAsBox", mass, boxSize, centerOfMass, rot);
        }
        [xmrMethodIsNoisyAttribute] public void osSetInertiaAsSphere (LSL_Float mass, LSL_Float radius, LSL_Vector centerOfMass)
        {
            StubVoid ("osSetInertiaAsSphere", mass, radius, centerOfMass);
        }
        [xmrMethodIsNoisyAttribute] public void osSetInertiaAsCylinder (LSL_Float mass, LSL_Float radius, LSL_Float lenght, LSL_Vector centerOfMass, LSL_Rotation lslrot)
        {
            StubVoid ("osSetInertiaAsCylinder", mass, radius, lenght, centerOfMass, lslrot);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer osTeleportObject (LSL_String objectUUID, LSL_Vector targetPos, LSL_Rotation targetrotation, LSL_Integer flags)
        {
            return StubLSLInteger ("osTeleportObject", objectUUID, targetPos, targetrotation, flags);
        }
        [xmrMethodIsNoisyAttribute] public LSL_Integer osGetLinkNumber (LSL_String name)
        {
            return StubLSLInteger ("osGetLinkNumber", name);
        }
        [xmrMethodIsNoisyAttribute] public LSL_List osTranslatorControl (LSL_String cmd, LSL_List args)
        {
            return StubLSLList ("osTranslatorControl", cmd, args);
        }
        public const int WL_WATER_COLOR = 0;
        public const int WL_WATER_FOG_DENSITY_EXPONENT = 1;
        public const int WL_UNDERWATER_FOG_MODIFIER = 2;
        public const int WL_REFLECTION_WAVELET_SCALE = 3;
        public const int WL_FRESNEL_SCALE = 4;
        public const int WL_FRESNEL_OFFSET = 5;
        public const int WL_REFRACT_SCALE_ABOVE = 6;
        public const int WL_REFRACT_SCALE_BELOW = 7;
        public const int WL_BLUR_MULTIPLIER = 8;
        public const int WL_BIG_WAVE_DIRECTION = 9;
        public const int WL_LITTLE_WAVE_DIRECTION = 10;
        public const int WL_NORMAL_MAP_TEXTURE = 11;
        public const int WL_HORIZON = 12;
        public const int WL_HAZE_HORIZON = 13;
        public const int WL_BLUE_DENSITY = 14;
        public const int WL_HAZE_DENSITY = 15;
        public const int WL_DENSITY_MULTIPLIER = 16;
        public const int WL_DISTANCE_MULTIPLIER = 17;
        public const int WL_MAX_ALTITUDE = 18;
        public const int WL_SUN_MOON_COLOR = 19;
        public const int WL_AMBIENT = 20;
        public const int WL_EAST_ANGLE = 21;
        public const int WL_SUN_GLOW_FOCUS = 22;
        public const int WL_SUN_GLOW_SIZE = 23;
        public const int WL_SCENE_GAMMA = 24;
        public const int WL_STAR_BRIGHTNESS = 25;
        public const int WL_CLOUD_COLOR = 26;
        public const int WL_CLOUD_XY_DENSITY = 27;
        public const int WL_CLOUD_COVERAGE = 28;
        public const int WL_CLOUD_SCALE = 29;
        public const int WL_CLOUD_DETAIL_XY_DENSITY = 30;
        public const int WL_CLOUD_SCROLL_X = 31;
        public const int WL_CLOUD_SCROLL_Y = 32;
        public const int WL_CLOUD_SCROLL_Y_LOCK = 33;
        public const int WL_CLOUD_SCROLL_X_LOCK = 34;
        public const int WL_DRAW_CLASSIC_CLOUDS = 35;
        public const int WL_SUN_MOON_POSITION = 36;
        public static readonly LSL_Integer TRUE = new LSL_Integer(1);
        public static readonly LSL_Integer FALSE = new LSL_Integer(0);
        public const int STATUS_PHYSICS = 1;
        public const int STATUS_ROTATE_X = 2;
        public const int STATUS_ROTATE_Y = 4;
        public const int STATUS_ROTATE_Z = 8;
        public const int STATUS_PHANTOM = 16;
        public const int STATUS_SANDBOX = 32;
        public const int STATUS_BLOCK_GRAB = 64;
        public const int STATUS_DIE_AT_EDGE = 128;
        public const int STATUS_RETURN_AT_EDGE = 256;
        public const int STATUS_CAST_SHADOWS = 512;
        public const int STATUS_BLOCK_GRAB_OBJECT = 1024;
        public const int AGENT = 1;
        public const int AGENT_BY_LEGACY_NAME = 1;
        public const int AGENT_BY_USERNAME = 16;
        public const int NPC = 32;
        public const int ACTIVE = 2;
        public const int PASSIVE = 4;
        public const int SCRIPTED = 8;
        public const int CONTROL_FWD = 1;
        public const int CONTROL_BACK = 2;
        public const int CONTROL_LEFT = 4;
        public const int CONTROL_RIGHT = 8;
        public const int CONTROL_UP = 16;
        public const int CONTROL_DOWN = 32;
        public const int CONTROL_ROT_LEFT = 256;
        public const int CONTROL_ROT_RIGHT = 512;
        public const int CONTROL_LBUTTON = 268435456;
        public const int CONTROL_ML_LBUTTON = 1073741824;
        public const int PERMISSION_DEBIT = 2;
        public const int PERMISSION_TAKE_CONTROLS = 4;
        public const int PERMISSION_REMAP_CONTROLS = 8;
        public const int PERMISSION_TRIGGER_ANIMATION = 16;
        public const int PERMISSION_ATTACH = 32;
        public const int PERMISSION_RELEASE_OWNERSHIP = 64;
        public const int PERMISSION_CHANGE_LINKS = 128;
        public const int PERMISSION_CHANGE_JOINTS = 256;
        public const int PERMISSION_CHANGE_PERMISSIONS = 512;
        public const int PERMISSION_TRACK_CAMERA = 1024;
        public const int PERMISSION_CONTROL_CAMERA = 2048;
        public const int PERMISSION_TELEPORT = 4096;
        public const int PERMISSION_OVERRIDE_ANIMATIONS = 32768;
        public const int AGENT_FLYING = 1;
        public const int AGENT_ATTACHMENTS = 2;
        public const int AGENT_SCRIPTED = 4;
        public const int AGENT_MOUSELOOK = 8;
        public const int AGENT_SITTING = 16;
        public const int AGENT_ON_OBJECT = 32;
        public const int AGENT_AWAY = 64;
        public const int AGENT_WALKING = 128;
        public const int AGENT_IN_AIR = 256;
        public const int AGENT_TYPING = 512;
        public const int AGENT_CROUCHING = 1024;
        public const int AGENT_BUSY = 2048;
        public const int AGENT_ALWAYS_RUN = 4096;
        public const int AGENT_MALE = 8192;
        public const int PSYS_PART_INTERP_COLOR_MASK = 1;
        public const int PSYS_PART_INTERP_SCALE_MASK = 2;
        public const int PSYS_PART_BOUNCE_MASK = 4;
        public const int PSYS_PART_WIND_MASK = 8;
        public const int PSYS_PART_FOLLOW_SRC_MASK = 16;
        public const int PSYS_PART_FOLLOW_VELOCITY_MASK = 32;
        public const int PSYS_PART_TARGET_POS_MASK = 64;
        public const int PSYS_PART_TARGET_LINEAR_MASK = 128;
        public const int PSYS_PART_EMISSIVE_MASK = 256;
        public const int PSYS_PART_RIBBON_MASK = 1024;
        public const int PSYS_PART_FLAGS = 0;
        public const int PSYS_PART_START_COLOR = 1;
        public const int PSYS_PART_START_ALPHA = 2;
        public const int PSYS_PART_END_COLOR = 3;
        public const int PSYS_PART_END_ALPHA = 4;
        public const int PSYS_PART_START_SCALE = 5;
        public const int PSYS_PART_END_SCALE = 6;
        public const int PSYS_PART_MAX_AGE = 7;
        public const int PSYS_SRC_ACCEL = 8;
        public const int PSYS_SRC_PATTERN = 9;
        public const int PSYS_SRC_INNERANGLE = 10;
        public const int PSYS_SRC_OUTERANGLE = 11;
        public const int PSYS_SRC_TEXTURE = 12;
        public const int PSYS_SRC_BURST_RATE = 13;
        public const int PSYS_SRC_BURST_PART_COUNT = 15;
        public const int PSYS_SRC_BURST_RADIUS = 16;
        public const int PSYS_SRC_BURST_SPEED_MIN = 17;
        public const int PSYS_SRC_BURST_SPEED_MAX = 18;
        public const int PSYS_SRC_MAX_AGE = 19;
        public const int PSYS_SRC_TARGET_KEY = 20;
        public const int PSYS_SRC_OMEGA = 21;
        public const int PSYS_SRC_ANGLE_BEGIN = 22;
        public const int PSYS_SRC_ANGLE_END = 23;
        public const int PSYS_PART_BLEND_FUNC_SOURCE = 24;
        public const int PSYS_PART_BLEND_FUNC_DEST = 25;
        public const int PSYS_PART_START_GLOW = 26;
        public const int PSYS_PART_END_GLOW = 27;
        public const int PSYS_PART_BF_ONE = 0;
        public const int PSYS_PART_BF_ZERO = 1;
        public const int PSYS_PART_BF_DEST_COLOR = 2;
        public const int PSYS_PART_BF_SOURCE_COLOR = 3;
        public const int PSYS_PART_BF_ONE_MINUS_DEST_COLOR = 4;
        public const int PSYS_PART_BF_ONE_MINUS_SOURCE_COLOR = 5;
        public const int PSYS_PART_BF_SOURCE_ALPHA = 7;
        public const int PSYS_PART_BF_ONE_MINUS_SOURCE_ALPHA = 9;
        public const int PSYS_SRC_PATTERN_DROP = 1;
        public const int PSYS_SRC_PATTERN_EXPLODE = 2;
        public const int PSYS_SRC_PATTERN_ANGLE = 4;
        public const int PSYS_SRC_PATTERN_ANGLE_CONE = 8;
        public const int PSYS_SRC_PATTERN_ANGLE_CONE_EMPTY = 16;
        public const int VEHICLE_TYPE_NONE = 0;
        public const int VEHICLE_TYPE_SLED = 1;
        public const int VEHICLE_TYPE_CAR = 2;
        public const int VEHICLE_TYPE_BOAT = 3;
        public const int VEHICLE_TYPE_AIRPLANE = 4;
        public const int VEHICLE_TYPE_BALLOON = 5;
        public const int VEHICLE_LINEAR_FRICTION_TIMESCALE = 16;
        public const int VEHICLE_ANGULAR_FRICTION_TIMESCALE = 17;
        public const int VEHICLE_LINEAR_MOTOR_DIRECTION = 18;
        public const int VEHICLE_LINEAR_MOTOR_OFFSET = 20;
        public const int VEHICLE_ANGULAR_MOTOR_DIRECTION = 19;
        public const int VEHICLE_HOVER_HEIGHT = 24;
        public const int VEHICLE_HOVER_EFFICIENCY = 25;
        public const int VEHICLE_HOVER_TIMESCALE = 26;
        public const int VEHICLE_BUOYANCY = 27;
        public const int VEHICLE_LINEAR_DEFLECTION_EFFICIENCY = 28;
        public const int VEHICLE_LINEAR_DEFLECTION_TIMESCALE = 29;
        public const int VEHICLE_LINEAR_MOTOR_TIMESCALE = 30;
        public const int VEHICLE_LINEAR_MOTOR_DECAY_TIMESCALE = 31;
        public const int VEHICLE_ANGULAR_DEFLECTION_EFFICIENCY = 32;
        public const int VEHICLE_ANGULAR_DEFLECTION_TIMESCALE = 33;
        public const int VEHICLE_ANGULAR_MOTOR_TIMESCALE = 34;
        public const int VEHICLE_ANGULAR_MOTOR_DECAY_TIMESCALE = 35;
        public const int VEHICLE_VERTICAL_ATTRACTION_EFFICIENCY = 36;
        public const int VEHICLE_VERTICAL_ATTRACTION_TIMESCALE = 37;
        public const int VEHICLE_BANKING_EFFICIENCY = 38;
        public const int VEHICLE_BANKING_MIX = 39;
        public const int VEHICLE_BANKING_TIMESCALE = 40;
        public const int VEHICLE_REFERENCE_FRAME = 44;
        public const int VEHICLE_RANGE_BLOCK = 45;
        public const int VEHICLE_ROLL_FRAME = 46;
        public const int VEHICLE_FLAG_NO_DEFLECTION_UP = 1;
        public const int VEHICLE_FLAG_LIMIT_ROLL_ONLY = 2;
        public const int VEHICLE_FLAG_HOVER_WATER_ONLY = 4;
        public const int VEHICLE_FLAG_HOVER_TERRAIN_ONLY = 8;
        public const int VEHICLE_FLAG_HOVER_GLOBAL_HEIGHT = 16;
        public const int VEHICLE_FLAG_HOVER_UP_ONLY = 32;
        public const int VEHICLE_FLAG_LIMIT_MOTOR_UP = 64;
        public const int VEHICLE_FLAG_MOUSELOOK_STEER = 128;
        public const int VEHICLE_FLAG_MOUSELOOK_BANK = 256;
        public const int VEHICLE_FLAG_CAMERA_DECOUPLED = 512;
        public const int VEHICLE_FLAG_NO_X = 1024;
        public const int VEHICLE_FLAG_NO_Y = 2048;
        public const int VEHICLE_FLAG_NO_Z = 4096;
        public const int VEHICLE_FLAG_LOCK_HOVER_HEIGHT = 8192;
        public const int VEHICLE_FLAG_NO_DEFLECTION = 16392;
        public const int VEHICLE_FLAG_LOCK_ROTATION = 32784;
        public const int INVENTORY_ALL = -1;
        public const int INVENTORY_NONE = -1;
        public const int INVENTORY_TEXTURE = 0;
        public const int INVENTORY_SOUND = 1;
        public const int INVENTORY_LANDMARK = 3;
        public const int INVENTORY_CLOTHING = 5;
        public const int INVENTORY_OBJECT = 6;
        public const int INVENTORY_NOTECARD = 7;
        public const int INVENTORY_SCRIPT = 10;
        public const int INVENTORY_BODYPART = 13;
        public const int INVENTORY_ANIMATION = 20;
        public const int INVENTORY_GESTURE = 21;
        public const int ATTACH_CHEST = 1;
        public const int ATTACH_HEAD = 2;
        public const int ATTACH_LSHOULDER = 3;
        public const int ATTACH_RSHOULDER = 4;
        public const int ATTACH_LHAND = 5;
        public const int ATTACH_RHAND = 6;
        public const int ATTACH_LFOOT = 7;
        public const int ATTACH_RFOOT = 8;
        public const int ATTACH_BACK = 9;
        public const int ATTACH_PELVIS = 10;
        public const int ATTACH_MOUTH = 11;
        public const int ATTACH_CHIN = 12;
        public const int ATTACH_LEAR = 13;
        public const int ATTACH_REAR = 14;
        public const int ATTACH_LEYE = 15;
        public const int ATTACH_REYE = 16;
        public const int ATTACH_NOSE = 17;
        public const int ATTACH_RUARM = 18;
        public const int ATTACH_RLARM = 19;
        public const int ATTACH_LUARM = 20;
        public const int ATTACH_LLARM = 21;
        public const int ATTACH_RHIP = 22;
        public const int ATTACH_RULEG = 23;
        public const int ATTACH_RLLEG = 24;
        public const int ATTACH_LHIP = 25;
        public const int ATTACH_LULEG = 26;
        public const int ATTACH_LLLEG = 27;
        public const int ATTACH_BELLY = 28;
        public const int ATTACH_RPEC = 29;
        public const int ATTACH_LPEC = 30;
        public const int ATTACH_LEFT_PEC = 29;
        public const int ATTACH_RIGHT_PEC = 30;
        public const int ATTACH_HUD_CENTER_2 = 31;
        public const int ATTACH_HUD_TOP_RIGHT = 32;
        public const int ATTACH_HUD_TOP_CENTER = 33;
        public const int ATTACH_HUD_TOP_LEFT = 34;
        public const int ATTACH_HUD_CENTER_1 = 35;
        public const int ATTACH_HUD_BOTTOM_LEFT = 36;
        public const int ATTACH_HUD_BOTTOM = 37;
        public const int ATTACH_HUD_BOTTOM_RIGHT = 38;
        public const int ATTACH_NECK = 39;
        public const int ATTACH_AVATAR_CENTER = 40;
        public const int ATTACH_LHAND_RING1 = 41;
        public const int ATTACH_RHAND_RING1 = 42;
        public const int ATTACH_TAIL_BASE = 43;
        public const int ATTACH_TAIL_TIP = 44;
        public const int ATTACH_LWING = 45;
        public const int ATTACH_RWING = 46;
        public const int ATTACH_FACE_JAW = 47;
        public const int ATTACH_FACE_LEAR = 48;
        public const int ATTACH_FACE_REAR = 49;
        public const int ATTACH_FACE_LEYE = 50;
        public const int ATTACH_FACE_REYE = 51;
        public const int ATTACH_FACE_TONGUE = 52;
        public const int ATTACH_GROIN = 53;
        public const int ATTACH_HIND_LFOOT = 54;
        public const int ATTACH_HIND_RFOOT = 55;
        public const int OS_ATTACH_MSG_ALL = -65535;
        public const int OS_ATTACH_MSG_INVERT_POINTS = 1;
        public const int OS_ATTACH_MSG_OBJECT_CREATOR = 2;
        public const int OS_ATTACH_MSG_SCRIPT_CREATOR = 4;
        public const int LAND_LEVEL = 0;
        public const int LAND_RAISE = 1;
        public const int LAND_LOWER = 2;
        public const int LAND_SMOOTH = 3;
        public const int LAND_NOISE = 4;
        public const int LAND_REVERT = 5;
        public const int LAND_SMALL_BRUSH = 1;
        public const int LAND_MEDIUM_BRUSH = 2;
        public const int LAND_LARGE_BRUSH = 3;
        public const int DATA_ONLINE = 1;
        public const int DATA_NAME = 2;
        public const int DATA_BORN = 3;
        public const int DATA_RATING = 4;
        public const int DATA_SIM_POS = 5;
        public const int DATA_SIM_STATUS = 6;
        public const int DATA_SIM_RATING = 7;
        public const int DATA_PAYINFO = 8;
        public const int DATA_SIM_RELEASE = 128;
        public const int ANIM_ON = 1;
        public const int LOOP = 2;
        public const int REVERSE = 4;
        public const int PING_PONG = 8;
        public const int SMOOTH = 16;
        public const int ROTATE = 32;
        public const int SCALE = 64;
        public const int ALL_SIDES = -1;
        public const int LINK_SET = -1;
        public const int LINK_ROOT = 1;
        public const int LINK_ALL_OTHERS = -2;
        public const int LINK_ALL_CHILDREN = -3;
        public const int LINK_THIS = -4;
        public const int CHANGED_INVENTORY = 1;
        public const int CHANGED_COLOR = 2;
        public const int CHANGED_SHAPE = 4;
        public const int CHANGED_SCALE = 8;
        public const int CHANGED_TEXTURE = 16;
        public const int CHANGED_LINK = 32;
        public const int CHANGED_ALLOWED_DROP = 64;
        public const int CHANGED_OWNER = 128;
        public const int CHANGED_REGION = 256;
        public const int CHANGED_TELEPORT = 512;
        public const int CHANGED_REGION_RESTART = 1024;
        public const int CHANGED_REGION_START = 1024;
        public const int CHANGED_MEDIA = 2048;
        public const int CHANGED_ANIMATION = 16384;
        public const int CHANGED_POSITION = 32768;
        public const int TYPE_INVALID = 0;
        public const int TYPE_INTEGER = 1;
        public const int TYPE_FLOAT = 2;
        public const int TYPE_STRING = 3;
        public const int TYPE_KEY = 4;
        public const int TYPE_VECTOR = 5;
        public const int TYPE_ROTATION = 6;
        public const int REMOTE_DATA_CHANNEL = 1;
        public const int REMOTE_DATA_REQUEST = 2;
        public const int REMOTE_DATA_REPLY = 3;
        public const int HTTP_METHOD = 0;
        public const int HTTP_MIMETYPE = 1;
        public const int HTTP_BODY_MAXLENGTH = 2;
        public const int HTTP_VERIFY_CERT = 3;
        public const int HTTP_VERBOSE_THROTTLE = 4;
        public const int HTTP_CUSTOM_HEADER = 5;
        public const int HTTP_PRAGMA_NO_CACHE = 6;
        public const int CONTENT_TYPE_TEXT = 0;
        public const int CONTENT_TYPE_HTML = 1;
        public const int CONTENT_TYPE_XML = 2;
        public const int CONTENT_TYPE_XHTML = 3;
        public const int CONTENT_TYPE_ATOM = 4;
        public const int CONTENT_TYPE_JSON = 5;
        public const int CONTENT_TYPE_LLSD = 6;
        public const int CONTENT_TYPE_FORM = 7;
        public const int CONTENT_TYPE_RSS = 8;
        public const int PRIM_MATERIAL = 2;
        public const int PRIM_PHYSICS = 3;
        public const int PRIM_TEMP_ON_REZ = 4;
        public const int PRIM_PHANTOM = 5;
        public const int PRIM_POSITION = 6;
        public const int PRIM_SIZE = 7;
        public const int PRIM_ROTATION = 8;
        public const int PRIM_TYPE = 9;
        public const int PRIM_TEXTURE = 17;
        public const int PRIM_COLOR = 18;
        public const int PRIM_BUMP_SHINY = 19;
        public const int PRIM_FULLBRIGHT = 20;
        public const int PRIM_FLEXIBLE = 21;
        public const int PRIM_TEXGEN = 22;
        public const int PRIM_POINT_LIGHT = 23;
        public const int PRIM_CAST_SHADOWS = 24;
        public const int PRIM_GLOW = 25;
        public const int PRIM_TEXT = 26;
        public const int PRIM_NAME = 27;
        public const int PRIM_DESC = 28;
        public const int PRIM_ROT_LOCAL = 29;
        public const int PRIM_PHYSICS_SHAPE_TYPE = 30;
        public const int PRIM_PHYSICS_MATERIAL = 31;
        public const int PRIM_OMEGA = 32;
        public const int PRIM_POS_LOCAL = 33;
        public const int PRIM_LINK_TARGET = 34;
        public const int PRIM_SLICE = 35;
        public const int PRIM_SPECULAR = 36;
        public const int PRIM_NORMAL = 37;
        public const int PRIM_ALPHA_MODE = 38;
        public const int PRIM_ALLOW_UNSIT = 39;
        public const int PRIM_SCRIPTED_SIT_ONLY = 40;
        public const int PRIM_SIT_TARGET = 41;
        public const int PRIM_TEXGEN_DEFAULT = 0;
        public const int PRIM_TEXGEN_PLANAR = 1;
        public const int PRIM_TYPE_BOX = 0;
        public const int PRIM_TYPE_CYLINDER = 1;
        public const int PRIM_TYPE_PRISM = 2;
        public const int PRIM_TYPE_SPHERE = 3;
        public const int PRIM_TYPE_TORUS = 4;
        public const int PRIM_TYPE_TUBE = 5;
        public const int PRIM_TYPE_RING = 6;
        public const int PRIM_TYPE_SCULPT = 7;
        public const int PRIM_HOLE_DEFAULT = 0;
        public const int PRIM_HOLE_CIRCLE = 16;
        public const int PRIM_HOLE_SQUARE = 32;
        public const int PRIM_HOLE_TRIANGLE = 48;
        public const int PRIM_MATERIAL_STONE = 0;
        public const int PRIM_MATERIAL_METAL = 1;
        public const int PRIM_MATERIAL_GLASS = 2;
        public const int PRIM_MATERIAL_WOOD = 3;
        public const int PRIM_MATERIAL_FLESH = 4;
        public const int PRIM_MATERIAL_PLASTIC = 5;
        public const int PRIM_MATERIAL_RUBBER = 6;
        public const int PRIM_MATERIAL_LIGHT = 7;
        public const int PRIM_SHINY_NONE = 0;
        public const int PRIM_SHINY_LOW = 1;
        public const int PRIM_SHINY_MEDIUM = 2;
        public const int PRIM_SHINY_HIGH = 3;
        public const int PRIM_BUMP_NONE = 0;
        public const int PRIM_BUMP_BRIGHT = 1;
        public const int PRIM_BUMP_DARK = 2;
        public const int PRIM_BUMP_WOOD = 3;
        public const int PRIM_BUMP_BARK = 4;
        public const int PRIM_BUMP_BRICKS = 5;
        public const int PRIM_BUMP_CHECKER = 6;
        public const int PRIM_BUMP_CONCRETE = 7;
        public const int PRIM_BUMP_TILE = 8;
        public const int PRIM_BUMP_STONE = 9;
        public const int PRIM_BUMP_DISKS = 10;
        public const int PRIM_BUMP_GRAVEL = 11;
        public const int PRIM_BUMP_BLOBS = 12;
        public const int PRIM_BUMP_SIDING = 13;
        public const int PRIM_BUMP_LARGETILE = 14;
        public const int PRIM_BUMP_STUCCO = 15;
        public const int PRIM_BUMP_SUCTION = 16;
        public const int PRIM_BUMP_WEAVE = 17;
        public const int PRIM_SCULPT_TYPE_SPHERE = 1;
        public const int PRIM_SCULPT_TYPE_TORUS = 2;
        public const int PRIM_SCULPT_TYPE_PLANE = 3;
        public const int PRIM_SCULPT_TYPE_CYLINDER = 4;
        public const int PRIM_SCULPT_FLAG_INVERT = 64;
        public const int PRIM_SCULPT_FLAG_MIRROR = 128;
        public const int PRIM_PHYSICS_SHAPE_PRIM = 0;
        public const int PRIM_PHYSICS_SHAPE_NONE = 1;
        public const int PRIM_PHYSICS_SHAPE_CONVEX = 2;
        public const int PROFILE_NONE = 0;
        public const int PROFILE_SCRIPT_MEMORY = 1;
        public const int MASK_BASE = 0;
        public const int MASK_OWNER = 1;
        public const int MASK_GROUP = 2;
        public const int MASK_EVERYONE = 3;
        public const int MASK_NEXT = 4;
        public const int PERM_TRANSFER = 8192;
        public const int PERM_MODIFY = 16384;
        public const int PERM_COPY = 32768;
        public const int PERM_MOVE = 524288;
        public const int PERM_ALL = 2147483647;
        public const int PARCEL_MEDIA_COMMAND_STOP = 0;
        public const int PARCEL_MEDIA_COMMAND_PAUSE = 1;
        public const int PARCEL_MEDIA_COMMAND_PLAY = 2;
        public const int PARCEL_MEDIA_COMMAND_LOOP = 3;
        public const int PARCEL_MEDIA_COMMAND_TEXTURE = 4;
        public const int PARCEL_MEDIA_COMMAND_URL = 5;
        public const int PARCEL_MEDIA_COMMAND_TIME = 6;
        public const int PARCEL_MEDIA_COMMAND_AGENT = 7;
        public const int PARCEL_MEDIA_COMMAND_UNLOAD = 8;
        public const int PARCEL_MEDIA_COMMAND_AUTO_ALIGN = 9;
        public const int PARCEL_MEDIA_COMMAND_TYPE = 10;
        public const int PARCEL_MEDIA_COMMAND_SIZE = 11;
        public const int PARCEL_MEDIA_COMMAND_DESC = 12;
        public const int PARCEL_FLAG_ALLOW_FLY = 1;
        public const int PARCEL_FLAG_ALLOW_SCRIPTS = 2;
        public const int PARCEL_FLAG_ALLOW_LANDMARK = 8;
        public const int PARCEL_FLAG_ALLOW_TERRAFORM = 16;
        public const int PARCEL_FLAG_ALLOW_DAMAGE = 32;
        public const int PARCEL_FLAG_ALLOW_CREATE_OBJECTS = 64;
        public const int PARCEL_FLAG_USE_ACCESS_GROUP = 256;
        public const int PARCEL_FLAG_USE_ACCESS_LIST = 512;
        public const int PARCEL_FLAG_USE_BAN_LIST = 1024;
        public const int PARCEL_FLAG_USE_LAND_PASS_LIST = 2048;
        public const int PARCEL_FLAG_LOCAL_SOUND_ONLY = 32768;
        public const int PARCEL_FLAG_RESTRICT_PUSHOBJECT = 2097152;
        public const int PARCEL_FLAG_ALLOW_GROUP_SCRIPTS = 33554432;
        public const int PARCEL_FLAG_ALLOW_CREATE_GROUP_OBJECTS = 67108864;
        public const int PARCEL_FLAG_ALLOW_ALL_OBJECT_ENTRY = 134217728;
        public const int PARCEL_FLAG_ALLOW_GROUP_OBJECT_ENTRY = 268435456;
        public const int REGION_FLAG_ALLOW_DAMAGE = 1;
        public const int REGION_FLAG_FIXED_SUN = 16;
        public const int REGION_FLAG_BLOCK_TERRAFORM = 64;
        public const int REGION_FLAG_SANDBOX = 256;
        public const int REGION_FLAG_DISABLE_COLLISIONS = 4096;
        public const int REGION_FLAG_DISABLE_PHYSICS = 16384;
        public const int REGION_FLAG_BLOCK_FLY = 524288;
        public const int REGION_FLAG_ALLOW_DIRECT_TELEPORT = 1048576;
        public const int REGION_FLAG_RESTRICT_PUSHOBJECT = 4194304;
        public const int ESTATE_ACCESS_ALLOWED_AGENT_ADD = 0;
        public const int ESTATE_ACCESS_ALLOWED_AGENT_REMOVE = 1;
        public const int ESTATE_ACCESS_ALLOWED_GROUP_ADD = 2;
        public const int ESTATE_ACCESS_ALLOWED_GROUP_REMOVE = 3;
        public const int ESTATE_ACCESS_BANNED_AGENT_ADD = 4;
        public const int ESTATE_ACCESS_BANNED_AGENT_REMOVE = 5;
        public static readonly LSL_Integer PAY_HIDE = new LSL_Integer(-1);
        public static readonly LSL_Integer PAY_DEFAULT = new LSL_Integer(-2);
        public const string NULL_KEY = "00000000-0000-0000-0000-000000000000";
        public const string EOF = "\n\n\n";
        public const double PI = 3.14159274101257;
        public const double TWO_PI = 6.28318548202515;
        public const double PI_BY_TWO = 1.57079637050629;
        public const double DEG_TO_RAD = 0.0174532923847437;
        public const double RAD_TO_DEG = 57.2957801818848;
        public const double SQRT2 = 1.41421353816986;
        public const int STRING_TRIM_HEAD = 1;
        public const int STRING_TRIM_TAIL = 2;
        public const int STRING_TRIM = 3;
        public const int LIST_STAT_RANGE = 0;
        public const int LIST_STAT_MIN = 1;
        public const int LIST_STAT_MAX = 2;
        public const int LIST_STAT_MEAN = 3;
        public const int LIST_STAT_MEDIAN = 4;
        public const int LIST_STAT_STD_DEV = 5;
        public const int LIST_STAT_SUM = 6;
        public const int LIST_STAT_SUM_SQUARES = 7;
        public const int LIST_STAT_NUM_COUNT = 8;
        public const int LIST_STAT_GEOMETRIC_MEAN = 9;
        public const int LIST_STAT_HARMONIC_MEAN = 100;
        public const int PARCEL_COUNT_TOTAL = 0;
        public const int PARCEL_COUNT_OWNER = 1;
        public const int PARCEL_COUNT_GROUP = 2;
        public const int PARCEL_COUNT_OTHER = 3;
        public const int PARCEL_COUNT_SELECTED = 4;
        public const int PARCEL_COUNT_TEMP = 5;
        public const int DEBUG_CHANNEL = 2147483647;
        public const int PUBLIC_CHANNEL = 0;
        public const int OBJECT_UNKNOWN_DETAIL = -1;
        public const int OBJECT_NAME = 1;
        public const int OBJECT_DESC = 2;
        public const int OBJECT_POS = 3;
        public const int OBJECT_ROT = 4;
        public const int OBJECT_VELOCITY = 5;
        public const int OBJECT_OWNER = 6;
        public const int OBJECT_GROUP = 7;
        public const int OBJECT_CREATOR = 8;
        public const int OBJECT_RUNNING_SCRIPT_COUNT = 9;
        public const int OBJECT_TOTAL_SCRIPT_COUNT = 10;
        public const int OBJECT_SCRIPT_MEMORY = 11;
        public const int OBJECT_SCRIPT_TIME = 12;
        public const int OBJECT_PRIM_EQUIVALENCE = 13;
        public const int OBJECT_SERVER_COST = 14;
        public const int OBJECT_STREAMING_COST = 15;
        public const int OBJECT_PHYSICS_COST = 16;
        public const int OBJECT_CHARACTER_TIME = 17;
        public const int OBJECT_ROOT = 18;
        public const int OBJECT_ATTACHED_POINT = 19;
        public const int OBJECT_PATHFINDING_TYPE = 20;
        public const int OBJECT_PHYSICS = 21;
        public const int OBJECT_PHANTOM = 22;
        public const int OBJECT_TEMP_ON_REZ = 23;
        public const int OBJECT_RENDER_WEIGHT = 24;
        public const int OBJECT_HOVER_HEIGHT = 25;
        public const int OBJECT_BODY_SHAPE_TYPE = 26;
        public const int OBJECT_LAST_OWNER_ID = 27;
        public const int OBJECT_CLICK_ACTION = 28;
        public const int OBJECT_OMEGA = 29;
        public const int OBJECT_PRIM_COUNT = 30;
        public const int OBJECT_TOTAL_INVENTORY_COUNT = 31;
        public const int OBJECT_REZZER_KEY = 32;
        public const int OBJECT_GROUP_TAG = 33;
        public const int OBJECT_TEMP_ATTACHED = 34;
        public const int OBJECT_ATTACHED_SLOTS_AVAILABLE = 35;
        public const int OPT_OTHER = -1;
        public const int OPT_LEGACY_LINKSET = 0;
        public const int OPT_AVATAR = 1;
        public const int OPT_CHARACTER = 2;
        public const int OPT_WALKABLE = 3;
        public const int OPT_STATIC_OBSTACLE = 4;
        public const int OPT_MATERIAL_VOLUME = 5;
        public const int OPT_EXCLUSION_VOLUME = 6;
        public const int AGENT_LIST_PARCEL = 1;
        public const int AGENT_LIST_PARCEL_OWNER = 2;
        public const int AGENT_LIST_REGION = 4;
        public const int AGENT_LIST_EXCLUDENPC = 67108864;
        public static readonly LSL_Vector ZERO_VECTOR = new LSL_Vector(0,0,0);
        public static readonly LSL_Rotation ZERO_ROTATION = new LSL_Rotation(0,0,0,1);
        public const int CAMERA_PITCH = 0;
        public const int CAMERA_FOCUS_OFFSET = 1;
        public const int CAMERA_FOCUS_OFFSET_X = 2;
        public const int CAMERA_FOCUS_OFFSET_Y = 3;
        public const int CAMERA_FOCUS_OFFSET_Z = 4;
        public const int CAMERA_POSITION_LAG = 5;
        public const int CAMERA_FOCUS_LAG = 6;
        public const int CAMERA_DISTANCE = 7;
        public const int CAMERA_BEHINDNESS_ANGLE = 8;
        public const int CAMERA_BEHINDNESS_LAG = 9;
        public const int CAMERA_POSITION_THRESHOLD = 10;
        public const int CAMERA_FOCUS_THRESHOLD = 11;
        public const int CAMERA_ACTIVE = 12;
        public const int CAMERA_POSITION = 13;
        public const int CAMERA_POSITION_X = 14;
        public const int CAMERA_POSITION_Y = 15;
        public const int CAMERA_POSITION_Z = 16;
        public const int CAMERA_FOCUS = 17;
        public const int CAMERA_FOCUS_X = 18;
        public const int CAMERA_FOCUS_Y = 19;
        public const int CAMERA_FOCUS_Z = 20;
        public const int CAMERA_POSITION_LOCKED = 21;
        public const int CAMERA_FOCUS_LOCKED = 22;
        public const int PARCEL_DETAILS_NAME = 0;
        public const int PARCEL_DETAILS_DESC = 1;
        public const int PARCEL_DETAILS_OWNER = 2;
        public const int PARCEL_DETAILS_GROUP = 3;
        public const int PARCEL_DETAILS_AREA = 4;
        public const int PARCEL_DETAILS_ID = 5;
        public const int PARCEL_DETAILS_SEE_AVATARS = 6;
        public const int PARCEL_DETAILS_ANY_AVATAR_SOUNDS = 7;
        public const int PARCEL_DETAILS_GROUP_SOUNDS = 8;
        public const int PARCEL_DETAILS_CLAIMDATE = 10;
        public const int CLICK_ACTION_NONE = 0;
        public const int CLICK_ACTION_TOUCH = 0;
        public const int CLICK_ACTION_SIT = 1;
        public const int CLICK_ACTION_BUY = 2;
        public const int CLICK_ACTION_PAY = 3;
        public const int CLICK_ACTION_OPEN = 4;
        public const int CLICK_ACTION_PLAY = 5;
        public const int CLICK_ACTION_OPEN_MEDIA = 6;
        public const int CLICK_ACTION_ZOOM = 7;
        public const int TOUCH_INVALID_FACE = -1;
        public static readonly LSL_Vector TOUCH_INVALID_TEXCOORD = new LSL_Vector(-1,-1,0);
        public static readonly LSL_Vector TOUCH_INVALID_VECTOR = new LSL_Vector(0,0,0);
        public const int PRIM_MEDIA_ALT_IMAGE_ENABLE = 0;
        public const int PRIM_MEDIA_CONTROLS = 1;
        public const int PRIM_MEDIA_CURRENT_URL = 2;
        public const int PRIM_MEDIA_HOME_URL = 3;
        public const int PRIM_MEDIA_AUTO_LOOP = 4;
        public const int PRIM_MEDIA_AUTO_PLAY = 5;
        public const int PRIM_MEDIA_AUTO_SCALE = 6;
        public const int PRIM_MEDIA_AUTO_ZOOM = 7;
        public const int PRIM_MEDIA_FIRST_CLICK_INTERACT = 8;
        public const int PRIM_MEDIA_WIDTH_PIXELS = 9;
        public const int PRIM_MEDIA_HEIGHT_PIXELS = 10;
        public const int PRIM_MEDIA_WHITELIST_ENABLE = 11;
        public const int PRIM_MEDIA_WHITELIST = 12;
        public const int PRIM_MEDIA_PERMS_INTERACT = 13;
        public const int PRIM_MEDIA_PERMS_CONTROL = 14;
        public const int PRIM_MEDIA_CONTROLS_STANDARD = 0;
        public const int PRIM_MEDIA_CONTROLS_MINI = 1;
        public const int PRIM_MEDIA_PERM_NONE = 0;
        public const int PRIM_MEDIA_PERM_OWNER = 1;
        public const int PRIM_MEDIA_PERM_GROUP = 2;
        public const int PRIM_MEDIA_PERM_ANYONE = 4;
        public const int DENSITY = 1;
        public const int FRICTION = 2;
        public const int RESTITUTION = 4;
        public const int GRAVITY_MULTIPLIER = 8;
        public static readonly LSL_Integer LSL_STATUS_OK = new LSL_Integer(0);
        public static readonly LSL_Integer LSL_STATUS_MALFORMED_PARAMS = new LSL_Integer(1000);
        public static readonly LSL_Integer LSL_STATUS_TYPE_MISMATCH = new LSL_Integer(1001);
        public static readonly LSL_Integer LSL_STATUS_BOUNDS_ERROR = new LSL_Integer(1002);
        public static readonly LSL_Integer LSL_STATUS_NOT_FOUND = new LSL_Integer(1003);
        public static readonly LSL_Integer LSL_STATUS_NOT_SUPPORTED = new LSL_Integer(1004);
        public static readonly LSL_Integer LSL_STATUS_INTERNAL_ERROR = new LSL_Integer(1999);
        public static readonly LSL_Integer LSL_STATUS_WHITELIST_FAILED = new LSL_Integer(2001);
        public const string TEXTURE_BLANK = "5748decc-f629-461c-9a36-a35a221fe21f";
        public const string TEXTURE_DEFAULT = "89556747-24cb-43ed-920b-47caed15465f";
        public const string TEXTURE_PLYWOOD = "89556747-24cb-43ed-920b-47caed15465f";
        public const string TEXTURE_TRANSPARENT = "8dcd4a48-2d37-4909-9f78-f7a9eb4ef903";
        public const string TEXTURE_MEDIA = "8b5fec65-8d8d-9dc5-cda8-8fdf2716e361";
        public const int STATS_TIME_DILATION = 0;
        public const int STATS_SIM_FPS = 1;
        public const int STATS_PHYSICS_FPS = 2;
        public const int STATS_AGENT_UPDATES = 3;
        public const int STATS_ROOT_AGENTS = 4;
        public const int STATS_CHILD_AGENTS = 5;
        public const int STATS_TOTAL_PRIMS = 6;
        public const int STATS_ACTIVE_PRIMS = 7;
        public const int STATS_FRAME_MS = 8;
        public const int STATS_NET_MS = 9;
        public const int STATS_PHYSICS_MS = 10;
        public const int STATS_IMAGE_MS = 11;
        public const int STATS_OTHER_MS = 12;
        public const int STATS_IN_PACKETS_PER_SECOND = 13;
        public const int STATS_OUT_PACKETS_PER_SECOND = 14;
        public const int STATS_UNACKED_BYTES = 15;
        public const int STATS_AGENT_MS = 16;
        public const int STATS_PENDING_DOWNLOADS = 17;
        public const int STATS_PENDING_UPLOADS = 18;
        public const int STATS_ACTIVE_SCRIPTS = 19;
        public const int STATS_SCRIPT_LPS = 20;
        public const int OS_NPC_FLY = 0;
        public const int OS_NPC_NO_FLY = 1;
        public const int OS_NPC_LAND_AT_TARGET = 2;
        public const int OS_NPC_RUNNING = 4;
        public const int OS_NPC_SIT_NOW = 0;
        public const int OS_NPC_CREATOR_OWNED = 1;
        public const int OS_NPC_NOT_OWNED = 2;
        public const int OS_NPC_SENSE_AS_AGENT = 4;
        public const int OS_NPC_OBJECT_GROUP = 8;
        public const string URL_REQUEST_GRANTED = "URL_REQUEST_GRANTED";
        public const string URL_REQUEST_DENIED = "URL_REQUEST_DENIED";
        public static readonly LSL_Integer RC_REJECT_TYPES = new LSL_Integer(0);
        public static readonly LSL_Integer RC_DETECT_PHANTOM = new LSL_Integer(1);
        public static readonly LSL_Integer RC_DATA_FLAGS = new LSL_Integer(2);
        public static readonly LSL_Integer RC_MAX_HITS = new LSL_Integer(3);
        public static readonly LSL_Integer RC_REJECT_AGENTS = new LSL_Integer(1);
        public static readonly LSL_Integer RC_REJECT_PHYSICAL = new LSL_Integer(2);
        public static readonly LSL_Integer RC_REJECT_NONPHYSICAL = new LSL_Integer(4);
        public static readonly LSL_Integer RC_REJECT_LAND = new LSL_Integer(8);
        public static readonly LSL_Integer RC_GET_NORMAL = new LSL_Integer(1);
        public static readonly LSL_Integer RC_GET_ROOT_KEY = new LSL_Integer(2);
        public static readonly LSL_Integer RC_GET_LINK_NUM = new LSL_Integer(4);
        public static readonly LSL_Integer RCERR_UNKNOWN = new LSL_Integer(-1);
        public static readonly LSL_Integer RCERR_SIM_PERF_LOW = new LSL_Integer(-2);
        public static readonly LSL_Integer RCERR_CAST_TIME_EXCEEDED = new LSL_Integer(-3);
        public const int KFM_MODE = 1;
        public const int KFM_LOOP = 1;
        public const int KFM_REVERSE = 3;
        public const int KFM_FORWARD = 0;
        public const int KFM_PING_PONG = 2;
        public const int KFM_DATA = 2;
        public const int KFM_TRANSLATION = 2;
        public const int KFM_ROTATION = 1;
        public const int KFM_COMMAND = 0;
        public const int KFM_CMD_PLAY = 0;
        public const int KFM_CMD_STOP = 1;
        public const int KFM_CMD_PAUSE = 2;
        public const string JSON_INVALID = "﷐";
        public const string JSON_OBJECT = "﷑";
        public const string JSON_ARRAY = "﷒";
        public const string JSON_NUMBER = "﷓";
        public const string JSON_STRING = "﷔";
        public const string JSON_NULL = "﷕";
        public const string JSON_TRUE = "﷖";
        public const string JSON_FALSE = "﷗";
        public const string JSON_DELETE = "﷘";
        public const string JSON_APPEND = "-1";
        public const int OS_LISTEN_REGEX_NAME = 1;
        public const int OS_LISTEN_REGEX_MESSAGE = 2;
        public const int OSTPOBJ_NONE = 0;
        public const int OSTPOBJ_STOPATTARGET = 1;
        public const int OSTPOBJ_STOPONFAIL = 2;
        public const int OSTPOBJ_SETROT = 4;
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
         * @brief Read return value from stdin.
         *        Must be in form <funcname>:<value>
         * @param name = name of function that the return value is for
         * @param type = script-visible return value type
         * @returns value read from stdin
         */
        private object ReadRetVal (string name, string type)
        {
            /*
             * Set up what input we are waiting for.
             */
            if (scriptRoot.iState != IState.RUNNING) throw new Exception ("bad iState " + scriptRoot.iState);
            scriptRoot.wfiName = name;
            scriptRoot.wfiType = type;
            scriptRoot.iState = IState.WAITINGFORINPUT;
            if ((uthread is ScriptUThread_MMR) || (uthread is ScriptUThread_Sys)) {
                /*
                 * Save this stack pointer and switch back to the stack pointer
                 * within StepScriptMMR().
                 */
                uthread.Hiber ();
                /*
                 * StepScriptMMR() called ResumeEx() to switch back to this
                 * stack pointer to tell us our input is ready.
                 */
            } else {
                /*
                 * Do a setjmp(0) that the input reader can use to resume us
                 * when it is ready to.
                 */
                if (((XMRInstance)this).scrstack.Store (0) == 0) {
                    /*
                     * Now do a longjmp(1) back to StepScriptOther().
                     * It will do a longjmp(1) back here when it has input.
                     */
                    ((XMRInstance)this).wfistack.Restore (1);
                }
                /*
                 * StepScriptOther() did a longjmp(1) here to tell us our input is ready.
                 */
            }
            /*
             * Our input should be ready now.
             */
            if (scriptRoot.iState != IState.GOTSOMEINPUT) throw new Exception ("bad iState " + scriptRoot.iState);
            scriptRoot.iState = IState.RUNNING;
            return scriptRoot.wfiValue;
        }
    }
    /*************************     *  Copied from OpenSim  *
*
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
    /**********************     *  Copied from Mono  *
*
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
            for (int i = 1; i < 55; i++) { //  [1, 55] is special (Knuth)
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
            if (++inext >= 56) inext = 1;
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
