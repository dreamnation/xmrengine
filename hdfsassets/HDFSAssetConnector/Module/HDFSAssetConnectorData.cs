using System;
using System.Reflection;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Server.Base;
using OpenSim.Services.Base;
using OpenSim.Services.Interfaces;
using Nini.Config;
using log4net;
using MySql.Data.MySqlClient;
using System.Data;
using OpenMetaverse;

namespace Careminster
{
    public delegate string StoreDelegate(AssetBase asset, bool force);

    public class HDFSAssetConnectorData
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        protected MySqlConnection m_Connection = null;
        protected string m_ConnectionString;

        public HDFSAssetConnectorData(string connectionString)
        {
            m_ConnectionString = connectionString;

            OpenDatabase();
        }

        private bool OpenDatabase()
        {
            try
            {
                m_Connection = new MySqlConnection(m_ConnectionString);

                m_Connection.Open();
            }
            catch (MySqlException e)
            {
                m_log.ErrorFormat("[HDFSASSETS]: Can't connect to database: {0}",
                        e.Message.ToString());

                return false;
            }

            return true;
        }

        private IDataReader ExecuteReader(MySqlCommand c)
        {
            IDataReader r = null;
            MySqlConnection connection = (MySqlConnection) ((ICloneable)m_Connection).Clone();
            connection.Open();
            c.Connection = connection;

            r = c.ExecuteReader();

            return r;
        }

        private void ExecuteNonQuery(MySqlCommand c)
        {
            IDataReader r = null;
            MySqlConnection connection = (MySqlConnection) ((ICloneable)m_Connection).Clone();
            connection.Open();
            c.Connection = connection;

            c.ExecuteNonQuery();
        }

        public AssetMetadata Get(string id, out string hash)
        {
            hash = String.Empty;

            MySqlCommand cmd = new MySqlCommand();

            cmd.CommandText = "select id, name, description, type, hash, create_time from hdfsassets where id = ?id";
            cmd.Parameters.AddWithValue("?id", id);

            IDataReader reader = ExecuteReader(cmd);

            if (!reader.Read())
            {
                reader.Close();
                cmd.Dispose();
                return null;
            }
            
            AssetMetadata meta = new AssetMetadata();

            hash = reader["hash"].ToString();

            meta.ID = id;
            meta.FullID = new UUID(id);

            meta.Name = reader["name"].ToString();
            meta.Description = reader["description"].ToString();
            meta.Type = (sbyte)Convert.ToInt32(reader["type"]);
            meta.ContentType = ServerUtils.SLAssetTypeToContentType(meta.Type);
            meta.CreationDate = Util.ToDateTime(Convert.ToInt32(reader["create_time"]));

            reader.Close();

            cmd.CommandText = "update hdfsassets set access_time = UNIX_TIMESTAMP() where id = ?id";

            ExecuteNonQuery(cmd);

            cmd.Connection.Close();
            cmd.Dispose();

            return meta;
        }

        public void Store(AssetMetadata meta, string hash)
        {
            string oldhash;
            AssetMetadata existingAsset = Get(meta.ID, out oldhash);

            MySqlCommand cmd = m_Connection.CreateCommand();

            cmd.Parameters.AddWithValue("?id", meta.ID);
            cmd.Parameters.AddWithValue("?name", meta.Name);
            cmd.Parameters.AddWithValue("?description", meta.Description);
            cmd.Parameters.AddWithValue("?type", meta.Type.ToString());
            cmd.Parameters.AddWithValue("?hash", hash);

            if (existingAsset == null)
            {
                cmd.CommandText = "insert into hdfsassets (id, name, description, type, hash, create_time, access_time) values ( ?id, ?name, ?description, ?type, ?hash, UNIX_TIMESTAMP(), UNIX_TIMESTAMP())";

                ExecuteNonQuery(cmd);

                cmd.Connection.Close();
                cmd.Dispose();

                return;
            }

            cmd.CommandText = "update hdfsassets set hash = ?hash, access_time = UNIX_TIMESTAMP() where id = ?id";

            ExecuteNonQuery(cmd);

            cmd.Dispose();
            cmd.Connection.Close();
        }

        public int Count()
        {
            MySqlCommand cmd = m_Connection.CreateCommand();

            cmd.CommandText = "select count(*) as count from hdfsassets";

            IDataReader reader = ExecuteReader(cmd);

            reader.Read();

            int count = Convert.ToInt32(reader["count"]);

            reader.Close();
            cmd.Dispose();

            return count;
        }

        public void Delete(string id)
        {
            MySqlCommand cmd = m_Connection.CreateCommand();

            cmd.CommandText = "delete from hdfsassets where id = ?id";

            cmd.Parameters.AddWithValue("?id", id);

            ExecuteNonQuery(cmd);

            cmd.Dispose();
            cmd.Connection.Close();
        }

        public void Import(string conn, string table, int start, int count, bool force, StoreDelegate store)
        {
            MySqlConnection importConn;

            try
            {
                importConn = new MySqlConnection(conn);

                importConn.Open();
            }
            catch (MySqlException e)
            {
                m_log.ErrorFormat("[HDFSASSETS]: Can't connect to database: {0}",
                        e.Message.ToString());

                return;
            }

            int imported = 0;

            MySqlCommand cmd = importConn.CreateCommand();

            string limit = String.Empty;
            if (count != -1)
            {
                limit = String.Format(" limit {0},{1}", start, count);
            }
                
            cmd.CommandText = String.Format("select * from {0}{1}", table, limit);

            MainConsole.Instance.Output("Querying database");
            IDataReader reader = cmd.ExecuteReader();

            MainConsole.Instance.Output("Reading data");

            while (reader.Read())
            {
                if ((imported % 100) == 0)
                {
                    MainConsole.Instance.Output(String.Format("{0} assets imported so far", imported));
                }
    
                AssetBase asset = new AssetBase();
                AssetMetadata meta = new AssetMetadata();

                meta.ID = reader["id"].ToString();
                meta.FullID = new UUID(meta.ID);

                meta.Name = reader["name"].ToString();
                meta.Description = reader["description"].ToString();
                meta.Type = (sbyte)Convert.ToInt32(reader["assetType"]);
                meta.ContentType = ServerUtils.SLAssetTypeToContentType(meta.Type);
                meta.CreationDate = Util.ToDateTime(Convert.ToInt32(reader["create_time"]));

                asset.Metadata = meta;
                asset.Data = (byte[])reader["data"];

                store(asset, force);

                imported++;
            }

            reader.Close();
            cmd.Dispose();
            importConn.Close();

            MainConsole.Instance.Output(String.Format("Import done, {0} assets imported", imported));
        }
    }
}
