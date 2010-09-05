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
using System.Timers;
using System.Data;
using MySql.Data.MySqlClient;
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
        protected Timer m_DwellTimer = new Timer();

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

            m_DwellTimer.AutoReset = true;
            m_DwellTimer.Interval = 600000; // 10 minutes
            m_DwellTimer.Elapsed += OnDwellTimer;
            m_DwellTimer.Start();
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
            XDwellData[] dwell = m_DwellTable.Get("ParcelID", parcelID.ToString());
            if (dwell.Length < 1)
                return 0;

            return Convert.ToInt32(dwell[0].Data["AggregatedDwell"]);
        }

        private void OnDwellTimer(object sender, ElapsedEventArgs args)
        {
            List<ILandObject> landObjects = m_Scene.LandChannel.AllParcels();

            if (landObjects == null || landObjects.Count == 0)
                return;

            List<string> parcelIDs = new List<string>();
            Dictionary<UUID,int> counts = new Dictionary<UUID,int>();

            foreach (ILandObject land in landObjects)
            {
                parcelIDs.Add(land.LandData.GlobalID.ToString());
                counts[land.LandData.GlobalID] = 0;
            }

            string where = "ParcelID not in ('" + string.Join("', '", parcelIDs.ToArray()) + "')";
            m_DwellTable.Delete(where);

            m_Scene.ForEachScenePresence(delegate (ScenePresence p)
                    {
                        if (p.IsChildAgent)
                            return;
                        Vector3 pos = p.AbsolutePosition;
                        ILandObject land = m_Scene.LandChannel.GetLandObject(
                                pos.X, pos.Y);
                        if (land == null)
                            return;
                        if (counts.ContainsKey(land.LandData.GlobalID))
                            counts[land.LandData.GlobalID]++;
                    });
            foreach (KeyValuePair<UUID,int> kvp in counts)
            {
                if (kvp.Value == 0)
                    continue;

                XDwellData[] data = m_DwellTable.Get("ParcelID", kvp.Key.ToString());
                if (data.Length == 0)
                {
                    data = new XDwellData[1];
                    data[0] = new XDwellData();
                    data[0].Data = new Dictionary<string, string>();

                    data[0].ParcelID = kvp.Key;
                    data[0].RegionID = m_Scene.RegionInfo.RegionID;
                    data[0].Data["Dwell"] = "0";
                    data[0].Data["AggregatedDwell"] = "0";
                }

                int dwell = Convert.ToInt32(data[0].Data["Dwell"]);
                dwell += kvp.Value;
                data[0].Data["Dwell"] = dwell.ToString();

                m_DwellTable.Store(data[0]);
            }
        }
    }

    public class DwellTableHandler : MySQLGenericTableHandler<XDwellData>
    {
        public DwellTableHandler(string conn, string realm, string migration) :
                base(conn, realm, migration)
        {
        }

        public void Delete(string where)
        {
            MySqlCommand cmd = new MySqlCommand();

            cmd.CommandText = "delete from " + m_Realm + " where " + where;
            
            ExecuteNonQuery(cmd);

            cmd.Dispose();
        }
    }
}
