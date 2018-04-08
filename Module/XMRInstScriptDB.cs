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

using MySql.Data.MySqlClient;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.ScriptEngine.Shared;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;

namespace OpenSim.Region.ScriptEngine.XMREngine
{
    public partial class XMRInstance
    {
        // create database scriptdata;
        // use scriptdata;
        // create table ScriptDBs (sdb_key varchar(255) primary key, sdb_val text);
        // create user 'scriptdata'@'??????.%' identified by '????????????';
        // grant all privileges on scriptdata.* to 'scriptdata'@'???????.%';

        // get MySql server connection
        private MySqlConnection getConnection ()
        {
            if ((m_Engine.m_ScriptDBConnection != null) && !m_Engine.m_ScriptDBConnection.Ping ()) {
                m_log.Warn ("[XMREngine]: ScriptDB MySql server does not ping");
                m_Engine.m_ScriptDBConnection.Dispose ();
                m_Engine.m_ScriptDBConnection = null;
            }
            if (m_Engine.m_ScriptDBConnection == null) {
                m_log.Info ("[XMREngine]: ScriptDB MySql opening connection");
                MySqlConnection con = new MySqlConnection (m_Engine.m_ScriptDBConnectString);
                con.Open ();
                m_Engine.m_ScriptDBConnection = con;
            }
            return m_Engine.m_ScriptDBConnection;
        }

        // get 22-char groupid unique string that prefixes all the given keys
        private string prefix = null;
        private string getPrefix ()
        {
            if (prefix == null) {
                byte[] groupidbytes = m_Part.ParentGroup.GroupID.GetBytes ();
                prefix = System.Convert.ToBase64String (groupidbytes);
                if ((prefix.Length != 24) || !prefix.EndsWith ("==")) throw new ApplicationException ("bad uuid->base64");
                prefix = prefix.Substring (0, 22);
            }
            return prefix;
        }

        /*******************************\
         *  Operate on lists of lines  *
        \*******************************/

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

        /*************************************************\
         *  Operate on whole element as a single string  *
        \*************************************************/

        /**
         * Write element to database.
         *  Input:
         *   key = object unique key
         *   value = corresponding value to write
         */
        public override void xmrScriptDBWrite (string key, string value)
        {
            lock (m_Engine.m_ScriptDBConnectLock) {
                using (MySqlCommand cmd = new MySqlCommand ()) {
                    string command = "INSERT INTO ScriptDBs SET sdb_key=?sdbkey,sdb_val=?sdbval";
                    command += " ON DUPLICATE KEY UPDATE sdb_val=?sdbval";
                    cmd.Connection = getConnection ();
                    cmd.CommandText = command;
                    cmd.Parameters.AddWithValue ("?sdbkey", getPrefix () + key);
                    cmd.Parameters.AddWithValue ("?sdbval", value);
                    cmd.ExecuteNonQuery ();
                }
            }
        }

        /**
         * Read single element from database.
         *  Input:
         *   key = as given to xmrScriptDBWrite()
         *   notfound = value to return if not found
         *  Output:
         *   returns notfound: record not found
         *           else: value as given to xmrScriptDBWrite()
         */
        public override string xmrScriptDBReadOne (string key, string notfound)
        {
            lock (m_Engine.m_ScriptDBConnectLock) {
                using (MySqlCommand cmd = new MySqlCommand ()) {
                    cmd.Connection = getConnection ();
                    cmd.CommandText = "SELECT sdb_val FROM ScriptDBs WHERE sdb_key=?sdbkey";
                    cmd.Parameters.AddWithValue ("?sdbkey", getPrefix () + key);
                    using (IDataReader rdr = cmd.ExecuteReader ()) {
                        return rdr.Read () ? rdr["sdb_val"].ToString () : notfound;
                    }
                }
            }
        }

        /**
         * Get count of matching elements from database.
         *  Input:
         *   keylike = as given to xmrScriptDBWrite()
         *  Output:
         *   returns number of matching elements from database
         */
        public override int xmrScriptDBCount (string keylike)
        {
            lock (m_Engine.m_ScriptDBConnectLock) {
                using (MySqlCommand cmd = new MySqlCommand ()) {
                    cmd.Connection = getConnection ();
                    cmd.CommandText = "SELECT COUNT(sdb_key) AS count FROM ScriptDBs WHERE sdb_key LIKE ?sdbkey";
                    cmd.Parameters.AddWithValue ("?sdbkey", getPrefix () + keylike);
                    using (IDataReader rdr = cmd.ExecuteReader ()) {
                        if (!rdr.Read ()) throw new ApplicationException ("failed to read count");
                        return int.Parse (rdr["count"].ToString ());
                    }
                }
            }
        }

        /**
         * List matching elements from database.
         *  Input:
         *   keylike = as given to xmrScriptDBWrite()
         *   limit = maximum number of elements to return
         *   offset = skip over this many elements
         *  Output:
         *   returns list of keys matching keylike
         */
        public override LSL_List xmrScriptDBList (string keylike, int limit, int offset)
        {
            lock (m_Engine.m_ScriptDBConnectLock) {
                using (MySqlCommand cmd = new MySqlCommand ()) {
                    cmd.Connection = getConnection ();
                    cmd.CommandText = "SELECT sdb_key FROM ScriptDBs WHERE sdb_key LIKE ?sdbkey ORDER BY sdb_key LIMIT " + limit + " OFFSET " + offset;
                    cmd.Parameters.AddWithValue ("?sdbkey", getPrefix () + keylike);
                    using (IDataReader rdr = cmd.ExecuteReader ()) {
                        LinkedList<object> list = new LinkedList<object> ();
                        while (rdr.Read ()) {
                            string key = rdr["sdb_key"].ToString ().Substring (prefix.Length);
                            list.AddLast (new LSL_String (key));
                        }
                        object[] array = new object[list.Count];
                        int i = 0;
                        foreach (Object obj in list) array[i++] = obj;
                        return new LSL_List (array);
                    }
                }
            }
        }

        /**
         * Read many matching elements from database.
         *  Input:
         *   keylike = as given to xmrScriptDBWrite()
         *   limit   = maximum number of elements to return
         *   offset  = skip over this many elements
         *  Output:
         *   returns array of key=>val of elements matching keylike
         */
        public override XMR_Array xmrScriptDBReadMany (string keylike, int limit, int offset)
        {
            lock (m_Engine.m_ScriptDBConnectLock) {
                using (MySqlCommand cmd = new MySqlCommand ()) {
                    cmd.Connection = getConnection ();
                    cmd.CommandText = "SELECT sdb_key,sdb_val FROM ScriptDBs WHERE sdb_key LIKE ?sdbkey ORDER BY sdb_key LIMIT " + limit + " OFFSET " + offset;
                    cmd.Parameters.AddWithValue ("?sdbkey", getPrefix () + keylike);
                    using (IDataReader rdr = cmd.ExecuteReader ()) {
                        XMR_Array array = new XMR_Array (this);
                        while (rdr.Read ()) {
                            string key = rdr["sdb_key"].ToString ().Substring (prefix.Length);
                            string val = rdr["sdb_val"].ToString ();
                            array.SetByKey (key, val);
                        }
                        return array;
                    }
                }
            }
        }

        /**
         * Delete matching elements from database.
         *  Input:
         *   keylike = as given to xmrScriptDBWrite()
         *  Output:
         *   returns number of rows deleted
         */
        public override int xmrScriptDBDelete (string keylike)
        {
            lock (m_Engine.m_ScriptDBConnectLock) {
                using (MySqlCommand cmd = new MySqlCommand ()) {
                    cmd.Connection = getConnection ();
                    cmd.CommandText = "DELETE FROM ScriptDBs WHERE sdb_key LIKE ?sdbkey";
                    cmd.Parameters.AddWithValue ("?sdbkey", getPrefix () + keylike);
                    return cmd.ExecuteNonQuery ();
                }
            }
        }
    }
}
