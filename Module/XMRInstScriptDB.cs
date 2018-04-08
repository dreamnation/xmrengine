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

// Provide script-accessible database
//  Entries are name/value pairs with object (group) level scope
//  sdb_key = (22-char base64 encoded obj uuid) concat (script-provided key)
//  sdb_val = script-provided value

// config entries (see XMREngine.cs)
//  # MySql database connection
//  ScriptDBConnection = "Data Source=hostname;Database=scriptdata;User ID=scriptdata;Password=????????;"
//  # maximum number of characters of data to cache
//  ScriptDBCacheSize = 10000000

// database should be created with:
//  create database scriptdata;
//  use scriptdata;
//  create table ScriptDBs (sdb_key varchar(255) primary key, sdb_val text);
//  create user 'scriptdata'@'??????.%' identified by '????????';
//  grant all privileges on scriptdata.* to 'scriptdata'@'??????.%';

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
    public class ScriptDBCacheEntry
    {
        public LinkedListNode<ScriptDBCacheEntry> links;
        public uint loaded;     // m_ScriptDBCacheSeq the entry was read from/written to database
        public string sdbkey;
        public string sdbval;   // null if record not found

        public ScriptDBCacheEntry ()
        {
            links = new LinkedListNode<ScriptDBCacheEntry> (this);
        }

        public int Size {
            get {
                int size = sdbkey.Length + 100;
                if (sdbval != null) size += sdbval.Length;
                return size;
            }
        }
    }

    public partial class XMRInstance
    {
        // get MySql command object and connection
        private MySqlCommand getCommand ()
        {
            MySqlCommand cmd = new MySqlCommand ();
            cmd.Connection = getConnection ();
            return cmd;
        }

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

        /**
         * Read single entry from the cache (and database if not in cache).
         *  Input:
         *   key = key with prefix
         *  Output:
         *   returns cache entry (sdbval == null if record not found)
         */
        private ScriptDBCacheEntry ReadCache (string key)
        {
            // remove from cache wherever it is
            // create new entry if we don't already have one
            ScriptDBCacheEntry entry;
            if (m_Engine.m_ScriptDBCacheEntries.TryGetValue (key, out entry)) {
                //?? optimization ??// if (entry.loaded == m_Engine.m_ScriptDBCacheSeq) return entry;
                m_Engine.RemFromScriptDBCache (entry);
            } else {
                entry = new ScriptDBCacheEntry ();
                entry.sdbkey = key;
            }

            // make sure it was created at or after when this script was instanced so we know data isn't stale
            if (entry.loaded < m_Instanced) {
                using (MySqlCommand cmd = getCommand ()) {
                    cmd.CommandText = "SELECT sdb_val FROM ScriptDBs WHERE sdb_key=?sdbkey";
                    cmd.Parameters.AddWithValue ("?sdbkey", key);
                    using (IDataReader rdr = cmd.ExecuteReader ()) {
                        entry.sdbval = rdr.Read () ? rdr["sdb_val"].ToString () : null;
                    }
                }
            }

            // add (back to) cache as the newest entry
            m_Engine.AddToScriptDBCache (entry);
            return entry;
        }

        /**
         * Write single entry to the cache and to the database.
         *  Input:
         *   key = key with prefix
         *   val = value string
         *  Output:
         *   entry written to database
         *   cache updated
         */
        private void WriteCache (string key, string val)
        {
            // write to database
            using (MySqlCommand cmd = getCommand ()) {
                string command = "INSERT INTO ScriptDBs SET sdb_key=?sdbkey,sdb_val=?sdbval";
                command += " ON DUPLICATE KEY UPDATE sdb_val=?sdbval";
                cmd.CommandText = command;
                cmd.Parameters.AddWithValue ("?sdbkey", key);
                cmd.Parameters.AddWithValue ("?sdbval", val);
                cmd.ExecuteNonQuery ();
            }

            // remove from cache wherever it is
            // make a new entry if not there already
            ScriptDBCacheEntry entry;
            if (m_Engine.m_ScriptDBCacheEntries.TryGetValue (key, out entry)) {
                m_Engine.RemFromScriptDBCache (entry);
            } else {
                entry = new ScriptDBCacheEntry ();
                entry.sdbkey = key;
            }

            // update cache value
            entry.sdbval = val;

            // re-insert into cache as newest entry
            m_Engine.AddToScriptDBCache (entry);
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
            lock (m_Engine.m_ScriptDBLock) {
                WriteCache (getPrefix () + key, value);
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
            lock (m_Engine.m_ScriptDBLock) {
                ScriptDBCacheEntry entry = ReadCache (getPrefix () + key);
                return (entry.sdbval == null) ? notfound : entry.sdbval;
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
            lock (m_Engine.m_ScriptDBLock) {
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
            lock (m_Engine.m_ScriptDBLock) {
                using (MySqlCommand cmd = getCommand ()) {
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
            lock (m_Engine.m_ScriptDBLock) {
                using (MySqlCommand cmd = getCommand ()) {
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
            lock (m_Engine.m_ScriptDBLock) {
                using (MySqlCommand cmd = getCommand ()) {
                    string pfxkeylike = getPrefix () + keylike;

                    if (pfxkeylike.Contains ("%") || pfxkeylike.Contains ("_")) {

                        // remove any matching entries from cache
                        LinkedList<ScriptDBCacheEntry> matches = new LinkedList<ScriptDBCacheEntry> ();
                        foreach (ScriptDBCacheEntry entry in m_Engine.m_ScriptDBCacheEntries.Values) {
                            if (MatchesLike (entry.sdbkey, pfxkeylike)) {
                                matches.AddLast (entry);
                            }
                        }
                        foreach (ScriptDBCacheEntry entry in matches) {
                            m_Engine.RemFromScriptDBCache (entry);
                        }

                        // remove any matching entries from database
                        cmd.CommandText = "DELETE FROM ScriptDBs WHERE sdb_key LIKE ?sdbkey";
                    } else {

                        // remove any matching entry from cache
                        ScriptDBCacheEntry entry;
                        m_Engine.m_ScriptDBCacheEntries.TryGetValue (pfxkeylike, out entry);
                        m_Engine.RemFromScriptDBCache (entry);

                        // remove any matching entry from database
                        cmd.CommandText = "DELETE FROM ScriptDBs WHERE sdb_key=?sdbkey";
                    }
                    cmd.Parameters.AddWithValue ("?sdbkey", pfxkeylike);
                    return cmd.ExecuteNonQuery ();
                }
            }
        }

        /**
         * See if key matches like.
         */
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

            // keep going as long as there are 'like' chars
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
}
