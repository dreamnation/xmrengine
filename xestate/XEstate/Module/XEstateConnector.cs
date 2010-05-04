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

using System;
using System.Collections.Generic;
using System.Reflection;

using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using OpenSim.Server.Base;
using OpenSim.Framework.Servers.HttpServer;

using OpenMetaverse;
using log4net;

namespace Careminster.Modules.XEstate
{
    public class EstateConnector
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

//        public bool FriendshipTerminated(GridRegion region, UUID userID, UUID friendID)
//        {
//            Dictionary<string, object> sendData = new Dictionary<string, object>();
//            sendData["METHOD"] = "friendship_terminated";
//
//            sendData["FromID"] = userID.ToString();
//            sendData["ToID"] = friendID.ToString();
//
//            return Call(region, sendData);
//        }

        private bool Call(GridRegion region, Dictionary<string, object> sendData)
        {
            string reqString = ServerUtils.BuildQueryString(sendData);
            // m_log.DebugFormat("[XESTATE CONNECTOR]: queryString = {0}", reqString);
            try
            {
                string url = "http://" + region.ExternalHostName + ":" + region.HttpPort;
                string reply = SynchronousRestFormsRequester.MakeRequest("POST",
                        url + "/friends",
                        reqString);
                if (reply != string.Empty)
                {
                    Dictionary<string, object> replyData = ServerUtils.ParseXmlResponse(reply);

                    if (replyData.ContainsKey("RESULT"))
                    {
                        if (replyData["RESULT"].ToString().ToLower() == "true")
                            return true;
                        else
                            return false;
                    }
                    else
                        m_log.DebugFormat("[XESTATE CONNECTOR]: reply data does not contain result field");

                }
                else
                    m_log.DebugFormat("[XESTATE CONNECTOR]: received empty reply");
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[XESTATE CONNECTOR]: Exception when contacting remote sim: {0}", e.Message);
            }

            return false;
        }
    }
}
