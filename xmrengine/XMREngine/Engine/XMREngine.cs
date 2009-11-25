/////////////////////////////////////////////////////////////
//
// Copyright (c)2009 Careminster Limited and Melanie Thielker
//
// All rights reserved
//


using System;
using System.IO;
using System.Runtime.Remoting;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.ScriptEngine.Interfaces;
using Nini.Config;
using Mono.Addins;
using MMR;
using OpenMetaverse;

[assembly: Addin("XMREngine", "0.1")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace OpenSim.Region.ScriptEngine.XMREngine
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "XMREngine")]
    public class XMREngine : INonSharedRegionModule
    {
        public XMREngine()
        {
        }

        public string Name
        {
            get { return "XMREngine"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void Initialise(IConfigSource config)
        {
            MainConsole.Instance.Commands.AddCommand("xmr", false,
                    "xmr test",
                    "xmr test",
                    "Run current xmr test",
                    RunTest);
        }

        public void AddRegion(Scene scene)
        {
        }

        public void RemoveRegion(Scene scene)
        {
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public void Close()
        {
        }

        private void RunTest(string module, string[] args)
        {
            if (args.Length < 3)
                return;

            string source = args[2];

            MainConsole.Instance.Output(String.Format("Compiling {0}", source));

            FileStream fs = new FileStream(source, FileMode.Open, FileAccess.Read);
            StreamReader tr = new StreamReader(fs);

            string text = tr.ReadToEnd();

            tr.Close();
            fs.Close();

            ScriptCompile.Compile(text, "/tmp/assem.dll", UUID.Zero.ToString(),
                    null);
        }
    }
}
