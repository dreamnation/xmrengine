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
                comms.RegisterScriptInvocation (this, "commsTestVoidPart1");
                comms.RegisterScriptInvocation (this, "commsTestVoidPart2");
                comms.RegisterScriptInvocation (this, "commsTestVoidPart3");
                comms.RegisterScriptInvocation (this, "commsTestVoidPart4");
                comms.RegisterScriptInvocation (this, "commsTestFloat");
                comms.RegisterScriptInvocation (this, "commsTestInteger");
                comms.RegisterScriptInvocation (this, "commsTestKey");
                comms.RegisterScriptInvocation (this, "commsTestList");
                comms.RegisterScriptInvocation (this, "commsTestRotation");
                comms.RegisterScriptInvocation (this, "commsTestString");
                comms.RegisterScriptInvocation (this, "commsTestVector");

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
        public int commsTestVoidPart1 (UUID host, UUID script, float flt, int itr)
        {
            Vals vals = GetVals (host, script);
            vals.flt  = flt;
            vals.itr  = itr;
            return 0;
        }
        public int commsTestVoidPart2 (UUID host, UUID script, UUID kee, object[] lis)
        {
            Vals vals = GetVals (host, script);
            vals.kee  = kee;
            vals.lis  = lis;
            return 0;
        }
        public int commsTestVoidPart3 (UUID host, UUID script, OpenMetaverse.Quaternion rot, string str)
        {
            Vals vals = GetVals (host, script);
            vals.rot  = rot;
            vals.str  = str;
            return 0;
        }
        public int commsTestVoidPart4 (UUID host, UUID script, OpenMetaverse.Vector3 vec)
        {
            Vals vals = GetVals (host, script);
            vals.vec  = vec;
            return 0;
        }

        // the rest return the values set by the above
        public float commsTestFloat (UUID host, UUID script)
        {
            Vals vals = GetVals (host, script);
            return vals.flt;
        }
        public int commsTestInteger (UUID host, UUID script)
        {
            Vals vals = GetVals (host, script);
            return vals.itr;
        }
        public UUID commsTestKey (UUID host, UUID script)
        {
            Vals vals = GetVals (host, script);
            return vals.kee;
        }
        public object[] commsTestList (UUID host, UUID script)
        {
            Vals vals = GetVals (host, script);
            return vals.lis;
        }
        public OpenMetaverse.Quaternion commsTestRotation (UUID host, UUID script)
        {
            Vals vals = GetVals (host, script);
            return vals.rot;
        }
        public string commsTestString (UUID host, UUID script)
        {
            Vals vals = GetVals (host, script);
            return vals.str;
        }
        public OpenMetaverse.Vector3 commsTestVector (UUID host, UUID script)
        {
            Vals vals = GetVals (host, script);
            return vals.vec;
        }
    }
}
