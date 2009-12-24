/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using System;
using System.Collections.Generic;
using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;


namespace MMR {

	public class ScriptConst {

		public static LSL_Key      lslconst_NULL_KEY      = new LSL_Key ("00000000-0000-0000-0000-000000000000");
		public static LSL_Vector   lslconst_ZERO_VECTOR   = new LSL_Vector (0.0f, 0.0f, 0.0f);
		public static LSL_Rotation lslconst_ZERO_ROTATION = new LSL_Rotation (0.0f, 0.0f, 0.0f, 1.0f);

		private static Dictionary<string, ScriptConst> scriptConstants = null;

		/**
		 * @brief look up the value of a given built-in constant.
		 * @param name = name of constant
		 * @returns null: no constant by that name defined
		 *          else: pointer to ScriptConst struct
		 */
		public static ScriptConst Lookup (string name)
		{
			if (scriptConstants == null) {
				Init ();
			}
			//MB();
			if (scriptConstants.ContainsKey (name)) {
				return scriptConstants[name];
			}
			return null;
		}

		private static void Init ()
		{
			Dictionary<string, ScriptConst> sc = new Dictionary<string, ScriptConst> ();

			// http://lslwiki.net/lslwiki/wakka.php?wakka=LexFile
			new ScriptConst (sc, "TRUE", typeof (int), 1);
			new ScriptConst (sc, "FALSE", typeof (int), 0);
			new ScriptConst (sc, "STATUS_PHYSICS", typeof (int), 1);
			new ScriptConst (sc, "STATUS_ROTATE_X", typeof (int), 2);
			new ScriptConst (sc, "STATUS_ROTATE_Y", typeof (int), 4);
			new ScriptConst (sc, "STATUS_ROTATE_Z", typeof (int), 8);
			new ScriptConst (sc, "STATUS_PHANTOM", typeof (int), 16);
			new ScriptConst (sc, "STATUS_SANDBOX", typeof (int), 32);
			new ScriptConst (sc, "STATUS_BLOCK_GRAB", typeof (int), 64);
			new ScriptConst (sc, "STATUS_DIE_AT_EDGE", typeof (int), 128);
			new ScriptConst (sc, "STATUS_RETURN_AT_EDGE", typeof (int), 256);
			new ScriptConst (sc, "AGENT", typeof (int), 1);
			new ScriptConst (sc, "ACTIVE", typeof (int), 2);
			new ScriptConst (sc, "PASSIVE", typeof (int), 4);
			new ScriptConst (sc, "SCRIPTED", typeof (int), 8);
			new ScriptConst (sc, "CONTROL_FWD", typeof (int), 1);
			new ScriptConst (sc, "CONTROL_BACK", typeof (int), 2);
			new ScriptConst (sc, "CONTROL_LEFT", typeof (int), 4);
			new ScriptConst (sc, "CONTROL_RIGHT", typeof (int), 8);
			new ScriptConst (sc, "CONTROL_UP", typeof (int), 16);
			new ScriptConst (sc, "CONTROL_DOWN", typeof (int), 32);
			new ScriptConst (sc, "CONTROL_ROT_LEFT", typeof (int), 256);
			new ScriptConst (sc, "CONTROL_ROT_RIGHT", typeof (int), 512);
			new ScriptConst (sc, "CONTROL_LBUTTON", typeof (int), 268435456);
			new ScriptConst (sc, "CONTROL_ML_LBUTTON", typeof (int), 1073741824);
			new ScriptConst (sc, "PERMISSION_DEBIT", typeof (int), 2);
			new ScriptConst (sc, "PERMISSION_TAKE_CONTROLS", typeof (int), 4);
			new ScriptConst (sc, "PERMISSION_REMAP_CONTROLS", typeof (int), 8);
			new ScriptConst (sc, "PERMISSION_TRIGGER_ANIMATION", typeof (int), 16);
			new ScriptConst (sc, "PERMISSION_ATTACH", typeof (int), 32);
			new ScriptConst (sc, "PERMISSION_RELEASE_OWNERSHIP", typeof (int), 64);
			new ScriptConst (sc, "PERMISSION_CHANGE_LINKS", typeof (int), 128);
			new ScriptConst (sc, "PERMISSION_CHANGE_JOINTS", typeof (int), 256);
			new ScriptConst (sc, "PERMISSION_CHANGE_PERMISSIONS", typeof (int), 512);
			new ScriptConst (sc, "PERMISSION_TRACK_CAMERA", typeof (int), 1024);
			new ScriptConst (sc, "AGENT_FLYING", typeof (int), 1);
			new ScriptConst (sc, "AGENT_ATTACHMENTS", typeof (int), 2);
			new ScriptConst (sc, "AGENT_SCRIPTED", typeof (int), 4);
			new ScriptConst (sc, "AGENT_MOUSELOOK", typeof (int), 8);
			new ScriptConst (sc, "AGENT_SITTING", typeof (int), 16);
			new ScriptConst (sc, "AGENT_ON_OBJECT", typeof (int), 32);
			new ScriptConst (sc, "AGENT_AWAY", typeof (int), 64);
			new ScriptConst (sc, "AGENT_WALKING", typeof (int), 128);
			new ScriptConst (sc, "AGENT_IN_AIR", typeof (int), 256);
			new ScriptConst (sc, "AGENT_TYPING", typeof (int), 512);
			new ScriptConst (sc, "AGENT_CROUCHING", typeof (int), 1024);
			new ScriptConst (sc, "AGENT_BUSY", typeof (int), 2048);
			new ScriptConst (sc, "AGENT_ALWAYS_RUN", typeof (int), 4096);
			new ScriptConst (sc, "PSYS_PART_INTERP_COLOR_MASK", typeof (int), 1);
			new ScriptConst (sc, "PSYS_PART_INTERP_SCALE_MASK", typeof (int), 2);
			new ScriptConst (sc, "PSYS_PART_BOUNCE_MASK", typeof (int), 4);
			new ScriptConst (sc, "PSYS_PART_WIND_MASK", typeof (int), 8);
			new ScriptConst (sc, "PSYS_PART_FOLLOW_SRC_MASK", typeof (int), 16);
			new ScriptConst (sc, "PSYS_PART_FOLLOW_VELOCITY_MASK", typeof (int), 32);
			new ScriptConst (sc, "PSYS_PART_TARGET_POS_MASK", typeof (int), 64);
			new ScriptConst (sc, "PSYS_PART_TARGET_LINEAR_MASK", typeof (int), 128);
			new ScriptConst (sc, "PSYS_PART_EMISSIVE_MASK", typeof (int), 256);
			new ScriptConst (sc, "PSYS_PART_FLAGS", typeof (int), 0);
			new ScriptConst (sc, "PSYS_PART_START_COLOR", typeof (int), 1);
			new ScriptConst (sc, "PSYS_PART_START_ALPHA", typeof (int), 2);
			new ScriptConst (sc, "PSYS_PART_END_COLOR", typeof (int), 3);
			new ScriptConst (sc, "PSYS_PART_END_ALPHA", typeof (int), 4);
			new ScriptConst (sc, "PSYS_PART_START_SCALE", typeof (int), 5);
			new ScriptConst (sc, "PSYS_PART_END_SCALE", typeof (int), 6);
			new ScriptConst (sc, "PSYS_PART_MAX_AGE", typeof (int), 7);
			new ScriptConst (sc, "PSYS_SRC_ACCEL", typeof (int), 8);
			new ScriptConst (sc, "PSYS_SRC_PATTERN", typeof (int), 9);
			new ScriptConst (sc, "PSYS_SRC_INNERANGLE", typeof (int), 10);
			new ScriptConst (sc, "PSYS_SRC_OUTERANGLE", typeof (int), 11);
			new ScriptConst (sc, "PSYS_SRC_TEXTURE", typeof (int), 12);
			new ScriptConst (sc, "PSYS_SRC_BURST_RATE", typeof (int), 13);
			new ScriptConst (sc, "PSYS_SRC_BURST_PART_COUNT", typeof (int), 15);
			new ScriptConst (sc, "PSYS_SRC_BURST_RADIUS", typeof (int), 16);
			new ScriptConst (sc, "PSYS_SRC_BURST_SPEED_MIN", typeof (int), 17);
			new ScriptConst (sc, "PSYS_SRC_BURST_SPEED_MAX", typeof (int), 18);
			new ScriptConst (sc, "PSYS_SRC_MAX_AGE", typeof (int), 19);
			new ScriptConst (sc, "PSYS_SRC_TARGET_KEY", typeof (int), 20);
			new ScriptConst (sc, "PSYS_SRC_OMEGA", typeof (int), 21);
			new ScriptConst (sc, "PSYS_SRC_ANGLE_BEGIN", typeof (int), 22);
			new ScriptConst (sc, "PSYS_SRC_ANGLE_END", typeof (int), 23);
			new ScriptConst (sc, "PSYS_SRC_PATTERN_DROP", typeof (int), 1);
			new ScriptConst (sc, "PSYS_SRC_PATTERN_EXPLODE", typeof (int), 2);
			new ScriptConst (sc, "PSYS_SRC_PATTERN_ANGLE", typeof (int), 4);
			new ScriptConst (sc, "PSYS_SRC_PATTERN_ANGLE_CONE", typeof (int), 8);
			new ScriptConst (sc, "PSYS_SRC_PATTERN_ANGLE_CONE_EMPTY", typeof (int), 16);
			new ScriptConst (sc, "VEHICLE_TYPE_NONE", typeof (int), 0);
			new ScriptConst (sc, "VEHICLE_TYPE_SLED", typeof (int), 1);
			new ScriptConst (sc, "VEHICLE_TYPE_CAR", typeof (int), 2);
			new ScriptConst (sc, "VEHICLE_TYPE_BOAT", typeof (int), 3);
			new ScriptConst (sc, "VEHICLE_TYPE_AIRPLANE", typeof (int), 4);
			new ScriptConst (sc, "VEHICLE_TYPE_BALLOON", typeof (int), 5);
			new ScriptConst (sc, "VEHICLE_LINEAR_FRICTION_TIMESCALE", typeof (int), 16);
			new ScriptConst (sc, "VEHICLE_ANGULAR_FRICTION_TIMESCALE", typeof (int), 17);
			new ScriptConst (sc, "VEHICLE_LINEAR_MOTOR_DIRECTION", typeof (int), 18);
			new ScriptConst (sc, "VEHICLE_LINEAR_MOTOR_OFFSET", typeof (int), 20);
			new ScriptConst (sc, "VEHICLE_ANGULAR_MOTOR_DIRECTION", typeof (int), 19);
			new ScriptConst (sc, "VEHICLE_HOVER_HEIGHT", typeof (int), 24);
			new ScriptConst (sc, "VEHICLE_HOVER_EFFICIENCY", typeof (int), 25);
			new ScriptConst (sc, "VEHICLE_HOVER_TIMESCALE", typeof (int), 26);
			new ScriptConst (sc, "VEHICLE_BUOYANCY", typeof (int), 27);
			new ScriptConst (sc, "VEHICLE_LINEAR_DEFLECTION_EFFICIENCY", typeof (int), 28);
			new ScriptConst (sc, "VEHICLE_LINEAR_DEFLECTION_TIMESCALE", typeof (int), 29);
			new ScriptConst (sc, "VEHICLE_LINEAR_MOTOR_TIMESCALE", typeof (int), 30);
			new ScriptConst (sc, "VEHICLE_LINEAR_MOTOR_DECAY_TIMESCALE", typeof (int), 31);
			new ScriptConst (sc, "VEHICLE_ANGULAR_DEFLECTION_EFFICIENCY", typeof (int), 32);
			new ScriptConst (sc, "VEHICLE_ANGULAR_DEFLECTION_TIMESCALE", typeof (int), 33);
			new ScriptConst (sc, "VEHICLE_ANGULAR_MOTOR_TIMESCALE", typeof (int), 34);
			new ScriptConst (sc, "VEHICLE_ANGULAR_MOTOR_DECAY_TIMESCALE", typeof (int), 35);
			new ScriptConst (sc, "VEHICLE_VERTICAL_ATTRACTION_EFFICIENCY", typeof (int), 36);
			new ScriptConst (sc, "VEHICLE_VERTICAL_ATTRACTION_TIMESCALE", typeof (int), 37);
			new ScriptConst (sc, "VEHICLE_BANKING_EFFICIENCY", typeof (int), 38);
			new ScriptConst (sc, "VEHICLE_BANKING_MIX", typeof (int), 39);
			new ScriptConst (sc, "VEHICLE_BANKING_TIMESCALE", typeof (int), 40);
			new ScriptConst (sc, "VEHICLE_REFERENCE_FRAME", typeof (int), 44);
			new ScriptConst (sc, "VEHICLE_FLAG_NO_DEFLECTION_UP", typeof (int), 1);
			new ScriptConst (sc, "VEHICLE_FLAG_LIMIT_ROLL_ONLY", typeof (int), 2);
			new ScriptConst (sc, "VEHICLE_FLAG_HOVER_WATER_ONLY", typeof (int), 4);
			new ScriptConst (sc, "VEHICLE_FLAG_HOVER_TERRAIN_ONLY", typeof (int), 8);
			new ScriptConst (sc, "VEHICLE_FLAG_HOVER_GLOBAL_HEIGHT", typeof (int), 16);
			new ScriptConst (sc, "VEHICLE_FLAG_HOVER_UP_ONLY", typeof (int), 32);
			new ScriptConst (sc, "VEHICLE_FLAG_LIMIT_MOTOR_UP", typeof (int), 64);
			new ScriptConst (sc, "VEHICLE_FLAG_MOUSELOOK_STEER", typeof (int), 128);
			new ScriptConst (sc, "VEHICLE_FLAG_MOUSELOOK_BANK", typeof (int), 256);
			new ScriptConst (sc, "VEHICLE_FLAG_CAMERA_DECOUPLED", typeof (int), 512);
			new ScriptConst (sc, "INVENTORY_ALL", typeof (int), -1);
			new ScriptConst (sc, "INVENTORY_NONE", typeof (int), -1);
			new ScriptConst (sc, "INVENTORY_TEXTURE", typeof (int), 0);
			new ScriptConst (sc, "INVENTORY_SOUND", typeof (int), 1);
			new ScriptConst (sc, "INVENTORY_LANDMARK", typeof (int), 3);
			new ScriptConst (sc, "INVENTORY_CLOTHING", typeof (int), 5);
			new ScriptConst (sc, "INVENTORY_OBJECT", typeof (int), 6);
			new ScriptConst (sc, "INVENTORY_NOTECARD", typeof (int), 7);
			new ScriptConst (sc, "INVENTORY_SCRIPT", typeof (int), 10);
			new ScriptConst (sc, "INVENTORY_BODYPART", typeof (int), 13);
			new ScriptConst (sc, "INVENTORY_ANIMATION", typeof (int), 20);
			new ScriptConst (sc, "INVENTORY_GESTURE", typeof (int), 21);
			new ScriptConst (sc, "ATTACH_CHEST", typeof (int), 1);
			new ScriptConst (sc, "ATTACH_HEAD", typeof (int), 2);
			new ScriptConst (sc, "ATTACH_LSHOULDER", typeof (int), 3);
			new ScriptConst (sc, "ATTACH_RSHOULDER", typeof (int), 4);
			new ScriptConst (sc, "ATTACH_LHAND", typeof (int), 5);
			new ScriptConst (sc, "ATTACH_RHAND", typeof (int), 6);
			new ScriptConst (sc, "ATTACH_LFOOT", typeof (int), 7);
			new ScriptConst (sc, "ATTACH_RFOOT", typeof (int), 8);
			new ScriptConst (sc, "ATTACH_BACK", typeof (int), 9);
			new ScriptConst (sc, "ATTACH_PELVIS", typeof (int), 10);
			new ScriptConst (sc, "ATTACH_MOUTH", typeof (int), 11);
			new ScriptConst (sc, "ATTACH_CHIN", typeof (int), 12);
			new ScriptConst (sc, "ATTACH_LEAR", typeof (int), 13);
			new ScriptConst (sc, "ATTACH_REAR", typeof (int), 14);
			new ScriptConst (sc, "ATTACH_LEYE", typeof (int), 15);
			new ScriptConst (sc, "ATTACH_REYE", typeof (int), 16);
			new ScriptConst (sc, "ATTACH_NOSE", typeof (int), 17);
			new ScriptConst (sc, "ATTACH_RUARM", typeof (int), 18);
			new ScriptConst (sc, "ATTACH_RLARM", typeof (int), 19);
			new ScriptConst (sc, "ATTACH_LUARM", typeof (int), 20);
			new ScriptConst (sc, "ATTACH_LLARM", typeof (int), 21);
			new ScriptConst (sc, "ATTACH_RHIP", typeof (int), 22);
			new ScriptConst (sc, "ATTACH_RULEG", typeof (int), 23);
			new ScriptConst (sc, "ATTACH_RLLEG", typeof (int), 24);
			new ScriptConst (sc, "ATTACH_LHIP", typeof (int), 25);
			new ScriptConst (sc, "ATTACH_LULEG", typeof (int), 26);
			new ScriptConst (sc, "ATTACH_LLLEG", typeof (int), 27);
			new ScriptConst (sc, "ATTACH_BELLY", typeof (int), 28);
			new ScriptConst (sc, "ATTACH_RPEC", typeof (int), 29);
			new ScriptConst (sc, "ATTACH_LPEC", typeof (int), 30);
			new ScriptConst (sc, "LAND_LEVEL", typeof (int), 0);
			new ScriptConst (sc, "LAND_RAISE", typeof (int), 1);
			new ScriptConst (sc, "LAND_LOWER", typeof (int), 2);
			new ScriptConst (sc, "LAND_SMOOTH", typeof (int), 3);
			new ScriptConst (sc, "LAND_NOISE", typeof (int), 4);
			new ScriptConst (sc, "LAND_REVERT", typeof (int), 5);
			new ScriptConst (sc, "LAND_SMALL_BRUSH", typeof (int), 1);
			new ScriptConst (sc, "LAND_MEDIUM_BRUSH", typeof (int), 2);
			new ScriptConst (sc, "LAND_LARGE_BRUSH", typeof (int), 3);
			new ScriptConst (sc, "DATA_ONLINE", typeof (int), 1);
			new ScriptConst (sc, "DATA_NAME", typeof (int), 2);
			new ScriptConst (sc, "DATA_BORN", typeof (int), 3);
			new ScriptConst (sc, "DATA_RATING", typeof (int), 4);
			new ScriptConst (sc, "DATA_SIM_POS", typeof (int), 5);
			new ScriptConst (sc, "DATA_SIM_STATUS", typeof (int), 6);
			new ScriptConst (sc, "DATA_SIM_RATING", typeof (int), 7);
			new ScriptConst (sc, "ANIM_ON", typeof (int), 1);
			new ScriptConst (sc, "LOOP", typeof (int), 2);
			new ScriptConst (sc, "REVERSE", typeof (int), 4);
			new ScriptConst (sc, "PING_PONG", typeof (int), 8);
			new ScriptConst (sc, "SMOOTH", typeof (int), 16);
			new ScriptConst (sc, "ROTATE", typeof (int), 32);
			new ScriptConst (sc, "SCALE", typeof (int), 64);
			new ScriptConst (sc, "ALL_SIDES", typeof (int), -1);
			new ScriptConst (sc, "LINK_SET", typeof (int), -1);
			new ScriptConst (sc, "LINK_ROOT", typeof (int), 1);
			new ScriptConst (sc, "LINK_ALL_OTHERS", typeof (int), -2);
			new ScriptConst (sc, "LINK_ALL_CHILDREN", typeof (int), -3);
			new ScriptConst (sc, "LINK_THIS", typeof (int), -4);
			new ScriptConst (sc, "CHANGED_INVENTORY", typeof (int), 1);
			new ScriptConst (sc, "CHANGED_COLOR", typeof (int), 2);
			new ScriptConst (sc, "CHANGED_SHAPE", typeof (int), 4);
			new ScriptConst (sc, "CHANGED_SCALE", typeof (int), 8);
			new ScriptConst (sc, "CHANGED_TEXTURE", typeof (int), 16);
			new ScriptConst (sc, "CHANGED_LINK", typeof (int), 32);
			new ScriptConst (sc, "CHANGED_ALLOWED_DROP", typeof (int), 64);
			new ScriptConst (sc, "CHANGED_OWNER", typeof (int), 128);
			new ScriptConst (sc, "TYPE_INVALID", typeof (int), 0);
			new ScriptConst (sc, "TYPE_INTEGER", typeof (int), 1);
			new ScriptConst (sc, "TYPE_FLOAT", typeof (int), 2);
			new ScriptConst (sc, "TYPE_STRING", typeof (int), 3);
			new ScriptConst (sc, "TYPE_KEY", typeof (int), 4);
			new ScriptConst (sc, "TYPE_VECTOR", typeof (int), 5);
			new ScriptConst (sc, "TYPE_ROTATION", typeof (int), 6);
			new ScriptConst (sc, "REMOTE_DATA_CHANNEL", typeof (int), 1);
			new ScriptConst (sc, "REMOTE_DATA_REQUEST", typeof (int), 2);
			new ScriptConst (sc, "REMOTE_DATA_REPLY", typeof (int), 3);
			new ScriptConst (sc, "PRIM_MATERIAL", typeof (int), 2);
			new ScriptConst (sc, "PRIM_PHYSICS", typeof (int), 3);
			new ScriptConst (sc, "PRIM_TEMP_ON_REZ", typeof (int), 4);
			new ScriptConst (sc, "PRIM_PHANTOM", typeof (int), 5);
			new ScriptConst (sc, "PRIM_POSITION", typeof (int), 6);
			new ScriptConst (sc, "PRIM_SIZE", typeof (int), 7);
			new ScriptConst (sc, "PRIM_ROTATION", typeof (int), 8);
			new ScriptConst (sc, "PRIM_TYPE", typeof (int), 9);
			new ScriptConst (sc, "PRIM_TEXTURE", typeof (int), 17);
			new ScriptConst (sc, "PRIM_COLOR", typeof (int), 18);
			new ScriptConst (sc, "PRIM_BUMP_SHINY", typeof (int), 19);
			new ScriptConst (sc, "PRIM_FULLBRIGHT", typeof (int), 20);
			new ScriptConst (sc, "PRIM_FLEXIBLE", typeof (int), 21);
			new ScriptConst (sc, "PRIM_TEXGEN", typeof (int), 22);
			new ScriptConst (sc, "PRIM_POINT_LIGHT", typeof (int), 23);
			new ScriptConst (sc, "PRIM_CAST_SHADOWS", typeof (int), 24);
			new ScriptConst (sc, "PRIM_GLOW", typeof (int), 25);
			new ScriptConst (sc, "PRIM_TEXGEN_DEFAULT", typeof (int), 0);
			new ScriptConst (sc, "PRIM_TEXGEN_PLANAR", typeof (int), 1);
			new ScriptConst (sc, "PRIM_TYPE_BOX", typeof (int), 0);
			new ScriptConst (sc, "PRIM_TYPE_CYLINDER", typeof (int), 1);
			new ScriptConst (sc, "PRIM_TYPE_PRISM", typeof (int), 2);
			new ScriptConst (sc, "PRIM_TYPE_SPHERE", typeof (int), 3);
			new ScriptConst (sc, "PRIM_TYPE_TORUS", typeof (int), 4);
			new ScriptConst (sc, "PRIM_TYPE_TUBE", typeof (int), 5);
			new ScriptConst (sc, "PRIM_TYPE_RING", typeof (int), 6);
			new ScriptConst (sc, "PRIM_HOLE_DEFAULT", typeof (int), 0);
			new ScriptConst (sc, "PRIM_HOLE_CIRCLE", typeof (int), 16);
			new ScriptConst (sc, "PRIM_HOLE_SQUARE", typeof (int), 32);
			new ScriptConst (sc, "PRIM_HOLE_TRIANGLE", typeof (int), 48);
			new ScriptConst (sc, "PRIM_MATERIAL_STONE", typeof (int), 0);
			new ScriptConst (sc, "PRIM_MATERIAL_METAL", typeof (int), 1);
			new ScriptConst (sc, "PRIM_MATERIAL_GLASS", typeof (int), 2);
			new ScriptConst (sc, "PRIM_MATERIAL_WOOD", typeof (int), 3);
			new ScriptConst (sc, "PRIM_MATERIAL_FLESH", typeof (int), 4);
			new ScriptConst (sc, "PRIM_MATERIAL_PLASTIC", typeof (int), 5);
			new ScriptConst (sc, "PRIM_MATERIAL_RUBBER", typeof (int), 6);
			new ScriptConst (sc, "PRIM_MATERIAL_LIGHT", typeof (int), 7);
			new ScriptConst (sc, "PRIM_SHINY_NONE", typeof (int), 0);
			new ScriptConst (sc, "PRIM_SHINY_LOW", typeof (int), 1);
			new ScriptConst (sc, "PRIM_SHINY_MEDIUM", typeof (int), 2);
			new ScriptConst (sc, "PRIM_SHINY_HIGH", typeof (int), 3);
			new ScriptConst (sc, "PRIM_BUMP_NONE", typeof (int), 0);
			new ScriptConst (sc, "PRIM_BUMP_BRIGHT", typeof (int), 1);
			new ScriptConst (sc, "PRIM_BUMP_DARK", typeof (int), 2);
			new ScriptConst (sc, "PRIM_BUMP_WOOD", typeof (int), 3);
			new ScriptConst (sc, "PRIM_BUMP_BARK", typeof (int), 4);
			new ScriptConst (sc, "PRIM_BUMP_BRICKS", typeof (int), 5);
			new ScriptConst (sc, "PRIM_BUMP_CHECKER", typeof (int), 6);
			new ScriptConst (sc, "PRIM_BUMP_CONCRETE", typeof (int), 7);
			new ScriptConst (sc, "PRIM_BUMP_TILE", typeof (int), 8);
			new ScriptConst (sc, "PRIM_BUMP_STONE", typeof (int), 9);
			new ScriptConst (sc, "PRIM_BUMP_DISKS", typeof (int), 10);
			new ScriptConst (sc, "PRIM_BUMP_GRAVEL", typeof (int), 11);
			new ScriptConst (sc, "PRIM_BUMP_BLOBS", typeof (int), 12);
			new ScriptConst (sc, "PRIM_BUMP_SIDING", typeof (int), 13);
			new ScriptConst (sc, "PRIM_BUMP_LARGETILE", typeof (int), 14);
			new ScriptConst (sc, "PRIM_BUMP_STUCCO", typeof (int), 15);
			new ScriptConst (sc, "PRIM_BUMP_SUCTION", typeof (int), 16);
			new ScriptConst (sc, "PRIM_BUMP_WEAVE", typeof (int), 17);
			new ScriptConst (sc, "MASK_BASE", typeof (int), 0);
			new ScriptConst (sc, "MASK_OWNER", typeof (int), 1);
			new ScriptConst (sc, "MASK_GROUP", typeof (int), 2);
			new ScriptConst (sc, "MASK_EVERYONE", typeof (int), 3);
			new ScriptConst (sc, "MASK_NEXT", typeof (int), 4);
			new ScriptConst (sc, "PERM_TRANSFER", typeof (int), 8192);
			new ScriptConst (sc, "PERM_MODIFY", typeof (int), 16384);
			new ScriptConst (sc, "PERM_COPY", typeof (int), 32768);
			new ScriptConst (sc, "PERM_MOVE", typeof (int), 524288);
			new ScriptConst (sc, "PERM_ALL", typeof (int), 2147483647);
			new ScriptConst (sc, "PARCEL_MEDIA_COMMAND_STOP", typeof (int), 0);
			new ScriptConst (sc, "PARCEL_MEDIA_COMMAND_PAUSE", typeof (int), 1);
			new ScriptConst (sc, "PARCEL_MEDIA_COMMAND_PLAY", typeof (int), 2);
			new ScriptConst (sc, "PARCEL_MEDIA_COMMAND_LOOP", typeof (int), 3);
			new ScriptConst (sc, "PARCEL_MEDIA_COMMAND_TEXTURE", typeof (int), 4);
			new ScriptConst (sc, "PARCEL_MEDIA_COMMAND_URL", typeof (int), 5);
			new ScriptConst (sc, "PARCEL_MEDIA_COMMAND_TIME", typeof (int), 6);
			new ScriptConst (sc, "PARCEL_MEDIA_COMMAND_AGENT", typeof (int), 7);
			new ScriptConst (sc, "PARCEL_MEDIA_COMMAND_UNLOAD", typeof (int), 8);
			new ScriptConst (sc, "PARCEL_MEDIA_COMMAND_AUTO_ALIGN", typeof (int), 9);
			new ScriptConst (sc, "PAY_HIDE", typeof (int), -1);
			new ScriptConst (sc, "PAY_DEFAULT", typeof (int), -2);
			new ScriptConst (sc, "NULL_KEY", typeof (LSL_Key), ScriptConst.lslconst_NULL_KEY);
			new ScriptConst (sc, "EOF", typeof (string), "\n\n\n");
			new ScriptConst (sc, "PI", typeof (float), (float)3.14159265358979323846264338327950);
			new ScriptConst (sc, "TWO_PI", typeof (float), (float)6.28318530717958647692528676655900);
			new ScriptConst (sc, "PI_BY_TWO", typeof (float), (float)1.57079632679489661923132169163975);
			new ScriptConst (sc, "DEG_TO_RAD", typeof (float), (float)(3.14159265358979323846264338327950 / 180));
			new ScriptConst (sc, "RAD_TO_DEG", typeof (float), (float)(180 / 3.14159265358979323846264338327950));
			new ScriptConst (sc, "SQRT2", typeof (float), (float)1.4142135623730950488016887242097);
			new ScriptConst (sc, "ZERO_VECTOR", typeof (LSL_Vector), ScriptConst.lslconst_ZERO_VECTOR);
			new ScriptConst (sc, "ZERO_ROTATION", typeof (LSL_Rotation), ScriptConst.lslconst_ZERO_ROTATION);

			// http://wiki.secondlife.com/wiki/OBJECT_POS
			new ScriptConst (sc, "OBJECT_NAME", typeof (int), 1);
			new ScriptConst (sc, "OBJECT_DESC", typeof (int), 2);
			new ScriptConst (sc, "OBJECT_POS", typeof (int), 3);
			new ScriptConst (sc, "OBJECT_ROT", typeof (int), 4);
			new ScriptConst (sc, "OBJECT_VELOCITY", typeof (int), 5);
			new ScriptConst (sc, "OBJECT_OWNER", typeof (int), 6);
			new ScriptConst (sc, "OBJECT_GROUP", typeof (int), 7);
			new ScriptConst (sc, "OBJECT_CREATOR", typeof (int), 8);

			//MB();
			scriptConstants = sc;
		}

		/*
		 * Instance variables
		 */
		public string name;
		public CompRVal rVal;

		public Type type;
		public int valInt;
		public float valFloat;
		public string valString;
		public LSL_Rotation valRot;
		public LSL_Vector valVec;
		public LSL_Key valKey;

		private ScriptConst (Dictionary<string, ScriptConst> lc, string name, Type type, int val)
		{
			lc.Add (name, this);
			this.name = name;
			this.type = type;
			this.valInt = val;
			this.rVal = new CompRVal (TokenType.FromSysType (null, type), val.ToString ());
		}
		private ScriptConst (Dictionary<string, ScriptConst> lc, string name, Type type, float val)
		{
			lc.Add (name, this);
			this.name = name;
			this.type = type;
			this.valFloat = val;
			this.rVal = new CompRVal (TokenType.FromSysType (null, type), val.ToString ());
		}
		private ScriptConst (Dictionary<string, ScriptConst> lc, string name, Type type, string val)
		{
			lc.Add (name, this);
			this.name = name;
			this.type = type;
			this.valString = val;
			this.rVal = new CompRVal (TokenType.FromSysType (null, type), val);
		}
		private ScriptConst (Dictionary<string, ScriptConst> lc, string name, Type type, LSL_Rotation val)
		{
			lc.Add (name, this);
			this.name = name;
			this.type = type;
			this.valRot = val;
			this.rVal = new CompRVal (TokenType.FromSysType (null, type), "MMR.ScriptConst.lslconst_" + name);
		}
		private ScriptConst (Dictionary<string, ScriptConst> lc, string name, Type type, LSL_Vector val)
		{
			lc.Add (name, this);
			this.name = name;
			this.type = type;
			this.valVec = val;
			this.rVal = new CompRVal (TokenType.FromSysType (null, type), "MMR.ScriptConst.lslconst_" + name);
		}
		private ScriptConst (Dictionary<string, ScriptConst> lc, string name, Type type, LSL_Key val)
		{
			lc.Add (name, this);
			this.name = name;
			this.type = type;
			this.valKey = val;
			this.rVal = new CompRVal (TokenType.FromSysType (null, type), "MMR.ScriptConst.lslconst_" + name);
		}
	}
}
