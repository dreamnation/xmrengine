///////////////////////////////////////////////////////////////////
//
// (c) 2010 Melanie Thielker and Careminster, Limited
//
// All rights reserved
//
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Data;
using OpenSim.Data.MySQL;
using OpenSim.Framework.Communications;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using Mono.Addins;

[assembly: Addin("XDwell.Module", "1.0")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace Careminster.Modules.XDwell
{
    public class XDwellData
    {
        public UUID ParcelID;
        public UUID RegionID;
        public Dictionary<string,string> Data;
    }

    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "Dwell")]
    public class XDwellModule : INonSharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected DwellTableHandler m_DwellTable;

        protected Scene m_Scene;
        protected string m_DatabaseConnect;

        public void Initialise(IConfigSource config)
        {
            IConfig dwellConfig = config.Configs["Dwell"];
            if (dwellConfig == null)
                return;

            if (dwellConfig.GetString("Module", String.Empty) != Name)
                return;

            m_DatabaseConnect = dwellConfig.GetString("DatabaseConnect", String.Empty);
            if (m_DatabaseConnect == String.Empty)
            {
                m_log.Error("[XDWELL]: No DatabaseConnect in section Dwell");
                return;
            }

            m_DwellTable = new DwellTableHandler("", "XDwell", String.Empty);
            m_log.Info("[XDwell]: Dwell module active");
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
            m_Scene = scene;

            m_Scene.EventManager.OnNewClient += OnNewClient;
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public void RemoveRegion(Scene scene)
        {
            m_Scene.EventManager.OnNewClient -= OnNewClient;
        }

        public string Name
        {
            get { return "XDwellModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        private void OnNewClient(IClientAPI client)
        {
            client.OnParcelDwellRequest += OnParcelDwellRequest;
        }

        private void OnParcelDwellRequest(int localID, IClientAPI client)
        {
            ILandObject land = m_Scene.LandChannel.GetLandObject(localID);

            int dwell = GetDwell(land.LandData.GlobalID);

            client.SendParcelDwellReply(localID, land.LandData.GlobalID, (float)dwell);
        }

        public int GetDwell(UUID parcelID)
        {
            return 0;
        }
    }

    public class DwellTableHandler : MySQLGenericTableHandler<XDwellData>
    {
        public DwellTableHandler(string conn, string realm, string migration) :
                base(conn, realm, migration)
        {
        }
    }
}
