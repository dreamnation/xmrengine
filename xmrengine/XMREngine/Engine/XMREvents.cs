//////////////////////////////////////////////////////////////
//
// Copyright (c) 2009 Careminster Limited and Melanie Thielker
//
// All rights reserved
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.ScriptEngine.Shared;
using OpenSim.Region.ScriptEngine.Interfaces;
using log4net;

namespace OpenSim.Region.ScriptEngine.XMREngine
{
    /// <summary>
    /// Prepares events so they can be directly executed upon a script by EventQueueManager, then queues it.
    /// </summary>
    public class XMREvents
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private XMREngine myScriptEngine;

        public XMREvents(XMREngine _ScriptEngine)
        {
            myScriptEngine = _ScriptEngine;

            m_log.Info("[XMREngine] Hooking up to server events");
            myScriptEngine.World.EventManager.OnAttach += attach;
            myScriptEngine.World.EventManager.OnObjectGrab += touch_start;
            myScriptEngine.World.EventManager.OnObjectDeGrab += touch_end;
            myScriptEngine.World.EventManager.OnScriptChangedEvent += changed;
            myScriptEngine.World.EventManager.OnScriptAtTargetEvent += at_target;
            myScriptEngine.World.EventManager.OnScriptNotAtTargetEvent += not_at_target;
            myScriptEngine.World.EventManager.OnScriptControlEvent += control;
            myScriptEngine.World.EventManager.OnScriptColliderStart += collision_start;
            myScriptEngine.World.EventManager.OnScriptColliding += collision;
            myScriptEngine.World.EventManager.OnScriptCollidingEnd += collision_end;
            IMoneyModule money=myScriptEngine.World.RequestModuleInterface<IMoneyModule>();
            if (money != null)
            {
                money.OnObjectPaid+=HandleObjectPaid;
            }
        }

        /// <summary>
        /// When an object gets paid by an avatar and generates the paid event, 
        /// this will pipe it to the script engine
        /// </summary>
        /// <param name="objectID">Object ID that got paid</param>
        /// <param name="agentID">Agent Id that did the paying</param>
        /// <param name="amount">Amount paid</param>
        private void HandleObjectPaid(UUID objectID, UUID agentID,
                int amount)
        {
            // Since this is an event from a shared module, all scenes will
            // get it. But only one has the object in question. The others
            // just ignore it.
            //
            SceneObjectPart part =
                    myScriptEngine.World.GetSceneObjectPart(objectID);

            if (part == null)
                return;

            m_log.Debug("Paid: " + objectID + " from " + agentID + ", amount " + amount);
            if (part.ParentGroup != null)
                part = part.ParentGroup.RootPart;

            if (part != null)
            {
                money(part.LocalId, agentID, amount);
            }
        }

        /// <summary>
        /// Handles piping the proper stuff to The script engine for touching
        /// Including DetectedParams
        /// </summary>
        /// <param name="localID"></param>
        /// <param name="originalID"></param>
        /// <param name="offsetPos"></param>
        /// <param name="remoteClient"></param>
        /// <param name="surfaceArgs"></param>
        public void touch_start(uint localID, uint originalID, Vector3 offsetPos,
                IClientAPI remoteClient, SurfaceTouchEventArgs surfaceArgs)
        {
            // Add to queue for all scripts in ObjectID object
            DetectParams[] det = new DetectParams[1];
            det[0] = new DetectParams();
            det[0].Key = remoteClient.AgentId;
            det[0].Populate(myScriptEngine.World);

            if (originalID == 0)
            {
                SceneObjectPart part = myScriptEngine.World.GetSceneObjectPart(localID);
                if (part == null)
                    return;

                det[0].LinkNum = part.LinkNum;
            }
            else
            {
                SceneObjectPart originalPart = myScriptEngine.World.GetSceneObjectPart(originalID);
                det[0].LinkNum = originalPart.LinkNum;
            }

            if (surfaceArgs != null)
            {
                det[0].SurfaceTouchArgs = surfaceArgs;
            }

            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "touch_start", new Object[] { 1 },
                    det));
        }

        public void touch(uint localID, uint originalID, Vector3 offsetPos,
                IClientAPI remoteClient)
        {
            // Add to queue for all scripts in ObjectID object
            DetectParams[] det = new DetectParams[1];
            det[0] = new DetectParams();
            det[0].Key = remoteClient.AgentId;
            det[0].Populate(myScriptEngine.World);
            det[0].OffsetPos = new LSL_Types.Vector3(offsetPos.X,
                                                     offsetPos.Y,
                                                     offsetPos.Z);

            if (originalID == 0)
            {
                SceneObjectPart part = myScriptEngine.World.GetSceneObjectPart(localID);
                if (part == null)
                    return;

                det[0].LinkNum = part.LinkNum;
            }
            else
            {
                SceneObjectPart originalPart = myScriptEngine.World.GetSceneObjectPart(originalID);
                det[0].LinkNum = originalPart.LinkNum;
            }

            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "touch", new Object[] { 1 },
                    det));
        }

        public void touch_end(uint localID, uint originalID, IClientAPI remoteClient,
                              SurfaceTouchEventArgs surfaceArgs)
        {
            // Add to queue for all scripts in ObjectID object
            DetectParams[] det = new DetectParams[1];
            det[0] = new DetectParams();
            det[0].Key = remoteClient.AgentId;
            det[0].Populate(myScriptEngine.World);

            if (originalID == 0)
            {
                SceneObjectPart part = myScriptEngine.World.GetSceneObjectPart(localID);
                if (part == null)
                    return;

                det[0].LinkNum = part.LinkNum;
            }
            else
            {
                SceneObjectPart originalPart = myScriptEngine.World.GetSceneObjectPart(originalID);
                det[0].LinkNum = originalPart.LinkNum;
            }

            if (surfaceArgs != null)
            {
                det[0].SurfaceTouchArgs = surfaceArgs;
            }

            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "touch_end", new Object[] { 1 },
                    det));
        }

        public void changed(uint localID, uint change)
        {
            // Add to queue for all scripts in localID, Object pass change.
            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "changed",new object[] { change },
                    new DetectParams[0]));
        }

        // state_entry: not processed here
        // state_exit: not processed here

        public void money(uint localID, UUID agentID, int amount)
        {
            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "money", new object[] {
                    agentID.ToString(),
                    amount },
                    new DetectParams[0]));
        }

        public void collision_start(uint localID, ColliderArgs col)
        {
            // Add to queue for all scripts in ObjectID object
            List<DetectParams> det = new List<DetectParams>();

            foreach (DetectedObject detobj in col.Colliders)
            {
                DetectParams d = new DetectParams();
                d.Key =detobj.keyUUID;
                d.Populate(myScriptEngine.World);
                det.Add(d);
            }

            if (det.Count > 0)
                myScriptEngine.PostObjectEvent(localID, new EventParams(
                        "collision_start",
                        new Object[] { det.Count },
                        det.ToArray()));
        }

        public void collision(uint localID, ColliderArgs col)
        {
            // Add to queue for all scripts in ObjectID object
            List<DetectParams> det = new List<DetectParams>();

            foreach (DetectedObject detobj in col.Colliders)
            {
                DetectParams d = new DetectParams();
                d.Key =detobj.keyUUID;
                d.Populate(myScriptEngine.World);
                det.Add(d);
            }

            if (det.Count > 0)
                myScriptEngine.PostObjectEvent(localID, new EventParams(
                        "collision", new Object[] { det.Count },
                        det.ToArray()));
        }

        public void collision_end(uint localID, ColliderArgs col)
        {
            // Add to queue for all scripts in ObjectID object
            List<DetectParams> det = new List<DetectParams>();

            foreach (DetectedObject detobj in col.Colliders)
            {
                DetectParams d = new DetectParams();
                d.Key =detobj.keyUUID;
                d.Populate(myScriptEngine.World);
                det.Add(d);
            }

            if (det.Count > 0)
                myScriptEngine.PostObjectEvent(localID, new EventParams(
                        "collision_end",
                        new Object[] { det.Count },
                        det.ToArray()));
        }

        public void land_collision_start(uint localID, UUID itemID)
        {
            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "land_collision_start",
                    new object[0],
                    new DetectParams[0]));
        }

        public void land_collision(uint localID, UUID itemID)
        {
            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "land_collision",
                    new object[0],
                    new DetectParams[0]));
        }

        public void land_collision_end(uint localID, UUID itemID)
        {
            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "land_collision_end",
                    new object[0],
                    new DetectParams[0]));
        }

        // timer: not handled here
        // listen: not handled here

        public void control(uint localID, UUID itemID, UUID agentID, uint held, uint change)
        {
            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "control",new object[] {
                    agentID.ToString(),
                    held,
                    change},
                    new DetectParams[0]));
        }

        public void email(uint localID, UUID itemID, string timeSent,
                string address, string subject, string message, int numLeft)
        {
            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "email",new object[] {
                    timeSent,
                    address,
                    subject,
                    message,
                    numLeft},
                    new DetectParams[0]));
        }

        public void at_target(uint localID, uint handle, Vector3 targetpos,
                Vector3 atpos)
        {
            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "at_target", new object[] {
                    handle,
                    new LSL_Types.Vector3(targetpos.X,targetpos.Y,targetpos.Z),
                    new LSL_Types.Vector3(atpos.X,atpos.Y,atpos.Z) },
                    new DetectParams[0]));
        }

        public void not_at_target(uint localID)
        {
            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "not_at_target",new object[0],
                    new DetectParams[0]));
        }

        public void at_rot_target(uint localID, UUID itemID)
        {
            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "at_rot_target",new object[0],
                    new DetectParams[0]));
        }

        public void not_at_rot_target(uint localID, UUID itemID)
        {
            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "not_at_rot_target",new object[0],
                    new DetectParams[0]));
        }

        // run_time_permissions: not handled here

        public void attach(uint localID, UUID itemID, UUID avatar)
        {
            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "attach",new object[] {
                    avatar.ToString() },
                    new DetectParams[0]));
        }

        // dataserver: not handled here
        // link_message: not handled here

        public void moving_start(uint localID, UUID itemID)
        {
            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "moving_start",new object[0],
                    new DetectParams[0]));
        }

        public void moving_end(uint localID, UUID itemID)
        {
            myScriptEngine.PostObjectEvent(localID, new EventParams(
                    "moving_end",new object[0],
                    new DetectParams[0]));
        }

        // object_rez: not handled here
        // remote_data: not handled here
        // http_response: not handled here
    }
}
