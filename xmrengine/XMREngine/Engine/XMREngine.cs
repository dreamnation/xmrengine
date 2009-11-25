/////////////////////////////////////////////////////////////
//
// Copyright (c)2009 Careminster Limited and Melanie Thielker
//
// All rights reserved
//


using System;
using System.Runtime.Remoting;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.ScriptEngine.Interfaces;

namespace OpenSim.Region.ScriptEngine.XMREngine
{
    public class XMREngine : INonSharedRegionModule
    {
        XMREngine()
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

        public void Initialise(IConfig config)
        {
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
    }
}
