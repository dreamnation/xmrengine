/*
    dmcs -debug -target:library -out:CommsTest.dll CommsTest.cs \
        -reference:../../../bin/OpenMetaverse.dll \
        -reference:../../../bin/OpenMetaverseTypes.dll \
        -reference:../../../bin/Nini.dll \
        -reference:../../../bin/log4net.dll \
        -reference:../../../bin/OpenSim.Framework.dll \
        -reference:../../../bin/OpenSim.Region.Framework.dll \
        -reference:../../../bin/OpenSim.Region.ScriptEngine.Shared.dll \
        -reference:/home/kunta/Mono.Addins-binary-1.0/Mono.Addins.dll
*/

using System;
using System.Collections;
using System.Collections.Generic;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Client;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using Mono.Addins;

using LSL_Float    = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer  = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_List     = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String   = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector   = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;

[assembly: Addin("CommsTest.Module", "1.0")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace Dreamnation.Modules.CommsTest
{
    [Extension (Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "CommsTest")]
    public class CommsTestModule : INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger("CommsTest");

        public void Initialise (IConfigSource source)
        { }

        public void AddRegion (Scene scene)
        { }

        public void RemoveRegion (Scene scene)
        { }

        public void RegionLoaded (Scene scene)
        {
            IScriptModuleComms comms = scene.RequestModuleInterface<IScriptModuleComms> ();
            if (comms != null) {
                comms.RegisterConstants (this);
                comms.RegisterScriptInvocations (this);
                m_log.Debug ("[CommsTest]: enabled");
            } else {
                m_log.Error ("[CommsTest]: disabled");
            }

        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public string Name
        {
            get { return "CommsTestModule"; }
        }

        public void Close ()
        { }

        [ScriptConstant] public static readonly double                   COMMSTEST_F1 = 1.5;
        [ScriptConstant] public static readonly float                    COMMSTEST_F2 = 2.5f;
        [ScriptConstant] public static readonly LSL_Float                COMMSTEST_F3 = new LSL_Float (3.5);
        [ScriptConstant] public static readonly int                      COMMSTEST_I1 = 11;
        [ScriptConstant] public static readonly LSL_Integer              COMMSTEST_I2 = new LSL_Integer (12);
        [ScriptConstant] public static readonly OpenMetaverse.UUID       COMMSTEST_K1 = new OpenMetaverse.UUID ("12345678-1234-5678-9abc-cba987654321");
        [ScriptConstant] public static readonly LSL_Rotation             COMMSTEST_R1 = new LSL_Rotation (1,2,3,4);
        [ScriptConstant] public static readonly OpenMetaverse.Quaternion COMMSTEST_R2 = new OpenMetaverse.Quaternion (4,3,2,1);
        [ScriptConstant] public static readonly string                   COMMSTEST_S1 = "bare string";
        [ScriptConstant] public static readonly LSL_String               COMMSTEST_S2 = "wrapped string";
        [ScriptConstant] public static readonly LSL_Vector               COMMSTEST_V1 = new LSL_Vector (7,8,9);
        [ScriptConstant] public static readonly OpenMetaverse.Vector3    COMMSTEST_V2 = new OpenMetaverse.Vector3 (9,8,7);

        private class Vals {
            public float                    flt;
            public int                      itr;
            public UUID                     kee;
            public object[]                 lis;
            public OpenMetaverse.Quaternion rot;
            public string                   str;
            public OpenMetaverse.Vector3    vec;
        }

        private Dictionary<string,Vals> valsDict = new Dictionary<string,Vals> ();

        private Vals GetVals (UUID host, UUID script)
        {
            string k = host.ToString () + script.ToString ();
            Vals v;
            lock (valsDict) {
                if (!valsDict.TryGetValue (k, out v)) {
                    v = new Vals ();
                    valsDict.Add (k, v);
                }
            }
            return v;
        }

        // this one sets the values returned by all the others
        // the numbskull interface doesn't allow void type so just return int 0.
        [ScriptInvocation]
        public int commsTestVoidPart1 (UUID host, UUID script, float flt, int itr)
        {
            Vals vals = GetVals (host, script);
            vals.flt  = flt;
            vals.itr  = itr;
            return 0;
        }
        [ScriptInvocation]
        public int commsTestVoidPart2 (UUID host, UUID script, UUID kee, object[] lis)
        {
            Vals vals = GetVals (host, script);
            vals.kee  = kee;
            vals.lis  = lis;
            return 0;
        }
        [ScriptInvocation]
        public int commsTestVoidPart3 (UUID host, UUID script, OpenMetaverse.Quaternion rot, string str)
        {
            Vals vals = GetVals (host, script);
            vals.rot  = rot;
            vals.str  = str;
            return 0;
        }
        [ScriptInvocation]
        public int commsTestVoidPart4 (UUID host, UUID script, OpenMetaverse.Vector3 vec)
        {
            Vals vals = GetVals (host, script);
            vals.vec  = vec;
            return 0;
        }

        // the rest return the values set by the above
        [ScriptInvocation]
        public float commsTestFloat (UUID host, UUID script)
        {
            Vals vals = GetVals (host, script);
            return vals.flt;
        }
        [ScriptInvocation]
        public int commsTestInteger (UUID host, UUID script)
        {
            Vals vals = GetVals (host, script);
            return vals.itr;
        }
        [ScriptInvocation]
        public UUID commsTestKey (UUID host, UUID script)
        {
            Vals vals = GetVals (host, script);
            return vals.kee;
        }
        [ScriptInvocation]
        public object[] commsTestList (UUID host, UUID script)
        {
            Vals vals = GetVals (host, script);
            return vals.lis;
        }
        [ScriptInvocation]
        public OpenMetaverse.Quaternion commsTestRotation (UUID host, UUID script)
        {
            Vals vals = GetVals (host, script);
            return vals.rot;
        }
        [ScriptInvocation]
        public string commsTestString (UUID host, UUID script)
        {
            Vals vals = GetVals (host, script);
            return vals.str;
        }
        [ScriptInvocation]
        public OpenMetaverse.Vector3 commsTestVector (UUID host, UUID script)
        {
            Vals vals = GetVals (host, script);
            return vals.vec;
        }
    }
}
