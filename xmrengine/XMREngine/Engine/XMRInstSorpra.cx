/***************************************************\
 *  COPYRIGHT 2013, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.ScriptEngine.Shared;
using System;

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
        /**
         * @brief If RegionCrossing trapping is enabled, any attempt to move the object
         *        outside its current region will cause the event to fire and the object
         *        will remain in its current region.
         */
        public override void xmrTrapRegionCrossing (int en)
        { }

        /**
         * @brief Move object to new position and rotation asynchronously.
         *        Can move object across region boundary.
         * @param pos     = new position within current region (same coords as llGetPos())
         * @param rot     = new rotation within current region (same coords as llGetRot())
         * @param options = not used
         * @param evcode  = not used
         * @param evargs  = arguments to pass to event handler
         * @returns false: completed synchronously, no event will be queued
         */
        public const double Sorpra_MIN_CROSS  = 1.0 / 512.0;  // ie, ~2mm
        public const int    Sorpra_TIMEOUT_MS = 30000;        // ie, 30sec
        public override bool xmrSetObjRegPosRotAsync (LSL_Vector pos, LSL_Rotation rot, int options, int evcode, LSL_List evargs)
        {
            // do the move
            SceneObjectGroup sog = m_Part.ParentGroup;
            sog.UpdateGroupRotationPR (pos, rot);

            // it is always synchronous
            return false;
        }
    }
}
