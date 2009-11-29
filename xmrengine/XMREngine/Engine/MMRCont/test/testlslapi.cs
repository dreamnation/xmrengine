/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using MMR;
using Mono.Tasklets;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenSim.Region.ScriptEngine.Shared.Api.Interfaces;
using System;

using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;


public class TestLSLAPI : ILSL_Api, IScriptApi {

	/*
	 * Vars for testing
	 */
	public Token token;
	public ScriptWrapper scriptWrapper;

	private bool disposed = false;

	/***************************************\
	 * This function implements IScriptApi *
	\***************************************/

	public void Initialize (IScriptEngine engine, SceneObjectPart part, uint localID, OpenMetaverse.UUID item)
	{
		Console.WriteLine ("TestLSLAPI.Initialize*: call made!!!");
	}

	/**************************************\
	 * These functions implement ILSL_Api *
	\**************************************/

	public void state (string newState) {
		Console.WriteLine ("TestLSLAPI.state*: newState=" + newState);
	}

	/*
	 * Functions that when called, check against test tokens
	 * They 'hide' the implementations in ScriptBaseClass.
	 */
	[MMRContableAttribute ()]
	public LSL_Integer llAbs (int i) {
		return (int)Common (typeof (int), "llAbs", (object)i);
	}

	[MMRContableAttribute ()]
	public LSL_Float llAcos (double val) {
		return (float)Common (typeof (float), "llAcos", (object)val);
	}

	[MMRContableAttribute ()]
	public void llAddToLandBanList (string avatar, double hours) {
		Common (typeof (void), "llAddToLandBanList", (object)avatar, (object)hours);
	}

	[MMRContableAttribute ()]
	public void llAddToLandPassList (string avatar, double hours) {
		Common (typeof (void), "llAddToLandPassList", (object)avatar, (object)hours);
	}

	[MMRContableAttribute ()]
	public void llAdjustSoundVolume (double volume) {
		Common (typeof (void), "llAdjustSoundVolume", (object)volume);
	}

	[MMRContableAttribute ()]
	public void llAllowInventoryDrop (int add) {
		Common (typeof (void), "llAllowInventoryDrop", (object)add);
	}

	[MMRContableAttribute ()]
	public LSL_Float llAngleBetween (LSL_Rotation a, LSL_Rotation b) {
		return (float)Common (typeof (float), "llAngleBetween", (object)a, (object)b);
	}

	[MMRContableAttribute ()]
	public void llApplyImpulse (LSL_Vector force, int local) {
		Common (typeof (void), "llApplyImpulse", (object)force, (object)local);
	}

	[MMRContableAttribute ()]
	public void llApplyRotationalImpulse (LSL_Vector force, int local) {
		Common (typeof (void), "llApplyRotationalImpulse", (object)force, (object)local);
	}

	[MMRContableAttribute ()]
	public LSL_Float llAsin (double val) {
		return (float)Common (typeof (float), "llAsin", (object)val);
	}

	[MMRContableAttribute ()]
	public LSL_Float llAtan2 (double x, double y) {
		return (float)Common (typeof (float), "llAtan2", (object)x, (object)y);
	}

	[MMRContableAttribute ()]
	public void llAttachToAvatar (int attachment) {
		Common (typeof (void), "llAttachToAvatar", (object)attachment);
	}

	[MMRContableAttribute ()]
	public LSL_Key llAvatarOnSitTarget () {
		return (LSL_Key)Common (typeof (LSL_Key), "llAvatarOnSitTarget");
	}

	[MMRContableAttribute ()]
	public LSL_Rotation llAxes2Rot (LSL_Vector fwd, LSL_Vector left, LSL_Vector up) {
		return (LSL_Rotation)Common (typeof (LSL_Rotation), "llAxes2Rot", (object)fwd, (object)left, (object)up);
	}

	[MMRContableAttribute ()]
	public LSL_Rotation llAxisAngle2Rot (LSL_Vector axis, double angle) {
		return (LSL_Rotation)Common (typeof (LSL_Rotation), "llAxisAngle2Rot", (object)axis, (object)angle);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llBase64ToInteger (string str) {
		return (int)Common (typeof (int), "llBase64ToInteger", (object)str);
	}

	[MMRContableAttribute ()]
	public LSL_String llBase64ToString (string str) {
		return (string)Common (typeof (string), "llBase64ToString", (object)str);
	}

	[MMRContableAttribute ()]
	public void llBreakAllLinks () {
		Common (typeof (void), "llBreakAllLinks");
	}

	[MMRContableAttribute ()]
	public void llBreakLink (int linknum) {
		Common (typeof (void), "llBreakLink", (object)linknum);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llCeil (double f) {
		return (int)Common (typeof (int), "llCeil", (object)f);
	}

	[MMRContableAttribute ()]
	public void llClearCameraParams () {
		Common (typeof (void), "llClearCameraParams");
	}

	[MMRContableAttribute ()]
	public void llCloseRemoteDataChannel (string channel) {
		Common (typeof (void), "llCloseRemoteDataChannel", (object)channel);
	}

	[MMRContableAttribute ()]
	public LSL_Float llCloud (LSL_Vector offset) {
		return (float)Common (typeof (float), "llCloud", (object)offset);
	}

	[MMRContableAttribute ()]
	public void llCollisionFilter (string name, string id, int accept) {
		Common (typeof (void), "llCollisionFilter", (object)name, (object)id, (object)accept);
	}

	[MMRContableAttribute ()]
	public void llCollisionSound (string impact_sound, double impact_volume) {
		Common (typeof (void), "llCollisionSound", (object)impact_sound, (object)impact_volume);
	}

	[MMRContableAttribute ()]
	public void llCollisionSprite (string impact_sprite) {
		Common (typeof (void), "llCollisionSprite", (object)impact_sprite);
	}

	[MMRContableAttribute ()]
	public LSL_Float llCos (double f) {
		return (float)Common (typeof (float), "llCos", (object)f);
	}

	[MMRContableAttribute ()]
	public void llCreateLink (string target, int parent) {
		Common (typeof (void), "llCreateLink", (object)target, (object)parent);
	}

	[MMRContableAttribute ()]
	public LSL_List llCSV2List (string src) {
		return (LSL_List)Common (typeof (LSL_List), "llCSV2List", (object)src);
	}

	[MMRContableAttribute ()]
	public LSL_List llDeleteSubList (LSL_List src, int start, int end) {
		return (LSL_List)Common (typeof (LSL_List), "llDeleteSubList", (object)src, (object)start, (object)end);
	}

	[MMRContableAttribute ()]
	public LSL_String llDeleteSubString (string src, int start, int end) {
		return (string)Common (typeof (string), "llDeleteSubString", (object)src, (object)start, (object)end);
	}

	[MMRContableAttribute ()]
	public void llDetachFromAvatar () {
		Common (typeof (void), "llDetachFromAvatar");
	}

	[MMRContableAttribute ()]
	public LSL_Vector llDetectedGrab (int number) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llDetectedGrab", (object)number);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llDetectedGroup (int number) {
		return (int)Common (typeof (int), "llDetectedGroup", (object)number);
	}

	[MMRContableAttribute ()]
	public LSL_Key llDetectedKey (int number) {
		return (LSL_Key)Common (typeof (LSL_Key), "llDetectedKey", (object)number);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llDetectedLinkNumber (int number) {
		return (int)Common (typeof (int), "llDetectedLinkNumber", (object)number);
	}

	[MMRContableAttribute ()]
	public LSL_String llDetectedName (int number) {
		return (string)Common (typeof (string), "llDetectedName", (object)number);
	}

	[MMRContableAttribute ()]
	public LSL_Key llDetectedOwner (int number) {
		return (LSL_Key)Common (typeof (LSL_Key), "llDetectedOwner", (object)number);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llDetectedPos (int number) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llDetectedPos", (object)number);
	}

	[MMRContableAttribute ()]
	public LSL_Rotation llDetectedRot (int number) {
		return (LSL_Rotation)Common (typeof (LSL_Rotation), "llDetectedRot", (object)number);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llDetectedType (int number) {
		return (int)Common (typeof (int), "llDetectedType", (object)number);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llDetectedTouchBinormal (int index) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llDetectedTouchBinormal", (object)index);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llDetectedTouchFace (int index) {
		return (int)Common (typeof (int), "llDetectedTouchFace", (object)index);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llDetectedTouchNormal (int index) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llDetectedTouchNormal", (object)index);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llDetectedTouchPos (int index) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llDetectedTouchPos", (object)index);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llDetectedTouchST (int index) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llDetectedTouchST", (object)index);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llDetectedTouchUV (int index) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llDetectedTouchUV", (object)index);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llDetectedVel (int number) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llDetectedVel", (object)number);
	}

	[MMRContableAttribute ()]
	public void llDialog (string avatar, string message, LSL_List buttons, int chat_channel) {
		Common (typeof (void), "llDialog", (object)avatar, (object)message, (object)buttons, (object)chat_channel);
	}

	[MMRContableAttribute ()]
	public void llDie () {
		Common (typeof (void), "llDie");
	}

	[MMRContableAttribute ()]
	public LSL_String llDumpList2String (LSL_List src, string seperator) {
		return (string)Common (typeof (string), "llDumpList2String", (object)src, (object)seperator);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llEdgeOfWorld (LSL_Vector pos, LSL_Vector dir) {
		return (int)Common (typeof (int), "llEdgeOfWorld", (object)pos, (object)dir);
	}

	[MMRContableAttribute ()]
	public void llEjectFromLand (string pest) {
		Common (typeof (void), "llEjectFromLand", (object)pest);
	}

	[MMRContableAttribute ()]
	public void llEmail (string address, string subject, string message) {
		Common (typeof (void), "llEmail", (object)address, (object)subject, (object)message);
	}

	[MMRContableAttribute ()]
	public LSL_String llEscapeURL (string url) {
		return (string)Common (typeof (string), "llEscapeURL", (object)url);
	}

	[MMRContableAttribute ()]
	public LSL_Rotation llEuler2Rot (LSL_Vector v) {
		return (LSL_Rotation)Common (typeof (LSL_Rotation), "llEuler2Rot", (object)v);
	}

	[MMRContableAttribute ()]
	public LSL_Float llFabs (double f) {
		return (float)Common (typeof (float), "llFabs", (object)f);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llFloor (double f) {
		return (int)Common (typeof (int), "llFloor", (object)f);
	}

	[MMRContableAttribute ()]
	public void llForceMouselook (int mouselook) {
		Common (typeof (void), "llForceMouselook", (object)mouselook);
	}

	[MMRContableAttribute ()]
	public LSL_Float llFrand (double mag) {
		return (float)Common (typeof (float), "llFrand", (object)mag);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetAccel () {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetAccel");
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetAgentInfo (string id) {
		return (int)Common (typeof (int), "llGetAgentInfo", (object)id);
	}

	[MMRContableAttribute ()]
	public LSL_String llGetAgentLanguage (string id) {
		return (string)Common (typeof (string), "llGetAgentLanguage", (object)id);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetAgentSize (string id) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetAgentSize", (object)id);
	}

	[MMRContableAttribute ()]
	public LSL_Float llGetAlpha (int face) {
		return (float)Common (typeof (float), "llGetAlpha", (object)face);
	}

	[MMRContableAttribute ()]
	public LSL_Float llGetAndResetTime () {
		return (float)Common (typeof (float), "llGetAndResetTime");
	}

	[MMRContableAttribute ()]
	public LSL_String llGetAnimation (string id) {
		return (string)Common (typeof (string), "llGetAnimation", (object)id);
	}

	[MMRContableAttribute ()]
	public LSL_List llGetAnimationList (string id) {
		return (LSL_List)Common (typeof (LSL_List), "llGetAnimationList", (object)id);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetAttached () {
		return (int)Common (typeof (int), "llGetAttached");
	}

	[MMRContableAttribute ()]
	public LSL_List llGetBoundingBox (string obj) {
		return (LSL_List)Common (typeof (LSL_List), "llGetBoundingBox", (object)obj);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetCameraPos () {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetCameraPos");
	}

	[MMRContableAttribute ()]
	public LSL_Rotation llGetCameraRot () {
		return (LSL_Rotation)Common (typeof (LSL_Rotation), "llGetCameraRot");
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetCenterOfMass () {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetCenterOfMass");
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetColor (int face) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetColor", (object)face);
	}

	[MMRContableAttribute ()]
	public LSL_String llGetCreator () {
		return (string)Common (typeof (string), "llGetCreator");
	}

	[MMRContableAttribute ()]
	public LSL_String llGetDate () {
		return (string)Common (typeof (string), "llGetDate");
	}

	[MMRContableAttribute ()]
	public LSL_Float llGetEnergy () {
		return (float)Common (typeof (float), "llGetEnergy");
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetForce () {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetForce");
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetFreeMemory () {
		return (int)Common (typeof (int), "llGetFreeMemory");
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetFreeURLs () {
		return (int)Common (typeof (int), "llGetFreeURLs");
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetGeometricCenter () {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetGeometricCenter");
	}

	[MMRContableAttribute ()]
	public LSL_Float llGetGMTclock () {
		return (float)Common (typeof (float), "llGetGMTclock");
	}

	[MMRContableAttribute ()]
	public LSL_String llGetHTTPHeader (LSL_Key request_id, string header) {
		return (string)Common (typeof (string), "llGetHTTPHeader", (object)request_id, (object)header);
	}

	[MMRContableAttribute ()]
	public LSL_Key llGetInventoryCreator (string item) {
		return (LSL_Key)Common (typeof (LSL_Key), "llGetInventoryCreator", (object)item);
	}

	[MMRContableAttribute ()]
	public LSL_Key llGetInventoryKey (string name) {
		return (LSL_Key)Common (typeof (LSL_Key), "llGetInventoryKey", (object)name);
	}

	[MMRContableAttribute ()]
	public LSL_String llGetInventoryName (int type, int number) {
		return (string)Common (typeof (string), "llGetInventoryName", (object)type, (object)number);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetInventoryNumber (int type) {
		return (int)Common (typeof (int), "llGetInventoryNumber", (object)type);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetInventoryPermMask (string item, int mask) {
		return (int)Common (typeof (int), "llGetInventoryPermMask", (object)item, (object)mask);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetInventoryType (string name) {
		return (int)Common (typeof (int), "llGetInventoryType", (object)name);
	}

	[MMRContableAttribute ()]
	public LSL_Key llGetKey () {
		return (LSL_Key)Common (typeof (LSL_Key), "llGetKey");
	}

	[MMRContableAttribute ()]
	public LSL_Key llGetLandOwnerAt (LSL_Vector pos) {
		return (LSL_Key)Common (typeof (LSL_Key), "llGetLandOwnerAt", (object)pos);
	}

	[MMRContableAttribute ()]
	public LSL_Key llGetLinkKey (int linknum) {
		return (LSL_Key)Common (typeof (LSL_Key), "llGetLinkKey", (object)linknum);
	}

	[MMRContableAttribute ()]
	public LSL_String llGetLinkName (int linknum) {
		return (string)Common (typeof (string), "llGetLinkName", (object)linknum);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetLinkNumber () {
		return (int)Common (typeof (int), "llGetLinkNumber");
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetListEntryType (LSL_List src, int index) {
		return (int)Common (typeof (int), "llGetListEntryType", (object)src, (object)index);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetListLength (LSL_List src) {
		return (int)Common (typeof (int), "llGetListLength", (object)src);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetLocalPos () {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetLocalPos");
	}

	[MMRContableAttribute ()]
	public LSL_Rotation llGetLocalRot () {
		return (LSL_Rotation)Common (typeof (LSL_Rotation), "llGetLocalRot");
	}

	[MMRContableAttribute ()]
	public LSL_Float llGetMass () {
		return (float)Common (typeof (float), "llGetMass"); ;
	}

	[MMRContableAttribute ()]
	public void llGetNextEmail (string address, string subject) {
		Common (typeof (void), "llGetNextEmail", (object)address, (object)subject);
	}

	[MMRContableAttribute ()]
	public LSL_String llGetNotecardLine (string name, int line) {
		return (string)Common (typeof (string), "llGetNotecardLine", (object)name, (object)line);
	}

	[MMRContableAttribute ()]
	public LSL_Key llGetNumberOfNotecardLines (string name) {
		return (LSL_Key)Common (typeof (LSL_Key), "llGetNumberOfNotecardLines", (object)name);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetNumberOfPrims () {
		return (int)Common (typeof (int), "llGetNumberOfPrims");
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetNumberOfSides () {
		return (int)Common (typeof (int), "llGetNumberOfSides");
	}

	[MMRContableAttribute ()]
	public LSL_String llGetObjectDesc () {
		return (string)Common (typeof (string), "llGetObjectDesc");
	}

	[MMRContableAttribute ()]
	public LSL_List llGetObjectDetails (string id, LSL_List args) {
		return (LSL_List)Common (typeof (LSL_List), "llGetObjectDetails", (object)id, (object)args);
	}

	[MMRContableAttribute ()]
	public LSL_Float llGetObjectMass (string id) {
		return (float)Common (typeof (float), "llGetObjectMass", (object)id);
	}

	[MMRContableAttribute ()]
	public LSL_String llGetObjectName () {
		return (string)Common (typeof (string), "llGetObjectName");
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetObjectPermMask (int mask) {
		return (int)Common (typeof (int), "llGetObjectPermMask", (object)mask);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetObjectPrimCount (string object_id) {
		return (int)Common (typeof (int), "llGetObjectPrimCount", (object)object_id);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetOmega () {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetOmega");
	}

	[MMRContableAttribute ()]
	public LSL_Key llGetOwner () {
		return (LSL_Key)Common (typeof (LSL_Key), "llGetOwner");
	}

	[MMRContableAttribute ()]
	public LSL_Key llGetOwnerKey (string id) {
		return (LSL_Key)Common (typeof (LSL_Key), "llGetOwnerKey", (object)id);
	}

	[MMRContableAttribute ()]
	public LSL_List llGetParcelDetails (LSL_Vector pos, LSL_List param) {
		return (LSL_List)Common (typeof (LSL_List), "llGetParcelDetails", (object)pos, (object)param);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetParcelFlags (LSL_Vector pos) {
		return (int)Common (typeof (int), "llGetParcelFlags", (object)pos);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetParcelMaxPrims (LSL_Vector pos, int sim_wide) {
		return (int)Common (typeof (int), "llGetParcelMaxPrims", (object)pos, (object)sim_wide);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetParcelPrimCount (LSL_Vector pos, int category, int sim_wide) {
		return (int)Common (typeof (int), "llGetParcelPrimCount", (object)pos, (object)category, (object)sim_wide);
	}

	[MMRContableAttribute ()]
	public LSL_List llGetParcelPrimOwners (LSL_Vector pos) {
		return (LSL_List)Common (typeof (LSL_List), "llGetParcelPrimOwners", (object)pos);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetPermissions () {
		return (int)Common (typeof (int), "llGetPermissions");
	}

	[MMRContableAttribute ()]
	public LSL_Key llGetPermissionsKey () {
		return (LSL_Key)Common (typeof (LSL_Key), "llGetPermissionsKey");
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetPos () {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetPos");
	}

	[MMRContableAttribute ()]
	public LSL_List llGetPrimitiveParams (LSL_List rules) {
		return (LSL_List)Common (typeof (LSL_List), "llGetPrimitiveParams", (object)rules);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetRegionAgentCount () {
		return (int)Common (typeof (int), "llGetRegionAgentCount");
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetRegionCorner () {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetRegionCorner");
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetRegionFlags () {
		return (int)Common (typeof (int), "llGetRegionFlags");
	}

	[MMRContableAttribute ()]
	public LSL_Float llGetRegionFPS () {
		return (float)Common (typeof (float), "llGetRegionFPS");
	}

	[MMRContableAttribute ()]
	public LSL_String llGetRegionName () {
		return (string)Common (typeof (string), "llGetRegionName");
	}

	[MMRContableAttribute ()]
	public LSL_Float llGetRegionTimeDilation () {
		return (float)Common (typeof (float), "llGetRegionTimeDilation");
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetRootPosition () {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetRootPosition");
	}

	[MMRContableAttribute ()]
	public LSL_Rotation llGetRootRotation () {
		return (LSL_Rotation)Common (typeof (LSL_Rotation), "llGetRootRotation");
	}

	[MMRContableAttribute ()]
	public LSL_Rotation llGetRot () {
		return (LSL_Rotation)Common (typeof (LSL_Rotation), "llGetRot");
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetScale () {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetScale");
	}

	[MMRContableAttribute ()]
	public LSL_String llGetScriptName () {
		return (string)Common (typeof (string), "llGetScriptName");
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetScriptState (string name) {
		return (int)Common (typeof (int), "llGetScriptState", (object)name);
	}

	[MMRContableAttribute ()]
	public LSL_String llGetSimulatorHostname () {
		return (string)Common (typeof (string), "llGetSimulatorHostname");
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetStartParameter () {
		return (int)Common (typeof (int), "llGetStartParameter");
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetStatus (int status) {
		return (int)Common (typeof (int), "llGetStatus", (object)status);
	}

	[MMRContableAttribute ()]
	public LSL_String llGetSubString (string src, int start, int end) {
		return (string)Common (typeof (string), "llGetSubString", (object)src, (object)start, (object)end);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetSunDirection () {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetSunDirection");
	}

	[MMRContableAttribute ()]
	public LSL_String llGetTexture (int face) {
		return (string)Common (typeof (string), "llGetTexture", (object)face);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetTextureOffset (int face) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetTextureOffset", (object)face);
	}

	[MMRContableAttribute ()]
	public LSL_Float llGetTextureRot (int side) {
		return (float)Common (typeof (float), "llGetTextureRot", (object)side);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetTextureScale (int side) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetTextureScale", (object)side);
	}

	[MMRContableAttribute ()]
	public LSL_Float llGetTime () {
		return (float)Common (typeof (float), "llGetTime");
	}

	[MMRContableAttribute ()]
	public LSL_Float llGetTimeOfDay () {
		return (float)Common (typeof (float), "llGetTimeOfDay");
	}

	[MMRContableAttribute ()]
	public LSL_String llGetTimestamp () {
		return (string)Common (typeof (string), "llGetTimestamp");
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetTorque () {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetTorque");
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGetUnixTime () {
		return (int)Common (typeof (int), "llGetUnixTime");
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGetVel () {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGetVel");
	}

	[MMRContableAttribute ()]
	public LSL_Float llGetWallclock () {
		return (float)Common (typeof (float), "llGetWallclock");
	}

	[MMRContableAttribute ()]
	public void llGiveInventory (string destination, string inventory) {
		Common (typeof (void), "llGiveInventory", (object)destination, (object)inventory);
	}

	[MMRContableAttribute ()]
	public void llGiveInventoryList (string destination, string category, LSL_List inventory) {
		Common (typeof (void), "llGiveInventoryList", (object)destination, (object)category, (object)inventory);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llGiveMoney (string destination, int amount) {
		return (int)Common (typeof (int), "llGiveMoney", (object)destination, (object)amount);
	}

	[MMRContableAttribute ()]
	public void llGodLikeRezObject (string inventory, LSL_Vector pos) {
		Common (typeof (void), "llGodLikeRezObject", (object)inventory, (object)pos);
	}

	[MMRContableAttribute ()]
	public LSL_Float llGround (LSL_Vector offset) {
		return (float)Common (typeof (float), "llGround", (object)offset);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGroundContour (LSL_Vector offset) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGroundContour", (object)offset);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGroundNormal (LSL_Vector offset) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGroundNormal", (object)offset);
	}

	[MMRContableAttribute ()]
	public void llGroundRepel (double height, int water, double tau) {
		Common (typeof (void), "llGroundRepel", (object)height, (object)water, (object)tau);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llGroundSlope (LSL_Vector offset) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llGroundSlope", (object)offset);
	}

	[MMRContableAttribute ()]
	public LSL_String llHTTPRequest (string url, LSL_List parameters, string body) {
		return (string)Common (typeof (string), "llHTTPRequest", (object)url, (object)parameters, (object)body);
	}

	[MMRContableAttribute ()]
	public void llHTTPResponse (LSL_Key request_id, int status, string body) {
		Common (typeof (string), "llHTTPResponse", (object)request_id, (object)status, (object)body);
	}

	[MMRContableAttribute ()]
	public LSL_String llInsertString (string dst, int position, string src) {
		return (string)Common (typeof (string), "llInsertString", (object)dst, (object)position, (object)src);
	}

	[MMRContableAttribute ()]
	public void llInstantMessage (string user, string message) {
		Common (typeof (void), "llInstantMessage", (object)user, (object)message);
	}

	[MMRContableAttribute ()]
	public LSL_String llIntegerToBase64 (int number) {
		return (string)Common (typeof (string), "llIntegerToBase64", (object)number);
	}

	[MMRContableAttribute ()]
	public LSL_String llKey2Name (string id) {
		return (string)Common (typeof (string), "llKey2Name", (object)id);
	}

	[MMRContableAttribute ()]
	public LSL_String llList2CSV (LSL_List src) {
		return (string)Common (typeof (string), "llList2CSV", (object)src);
	}

	[MMRContableAttribute ()]
	public LSL_Float llList2Float (LSL_List src, int index) {
		return (float)Common (typeof (float), "llList2Float", (object)src, (object)index);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llList2Integer (LSL_List src, int index) {
		return (int)Common (typeof (int), "llList2Integer", (object)src, (object)index);
	}

	[MMRContableAttribute ()]
	public LSL_Key llList2Key (LSL_List src, int index) {
		return (LSL_Key)Common (typeof (LSL_Key), "llList2Key", (object)src, (object)index);
	}

	[MMRContableAttribute ()]
	public LSL_List llList2List (LSL_List src, int start, int end) {
		return (LSL_List)Common (typeof (LSL_List), "llList2List", (object)src, (object)start, (object)end);
	}

	[MMRContableAttribute ()]
	public LSL_List llList2ListStrided (LSL_List src, int start, int end, int stride) {
		return (LSL_List)Common (typeof (LSL_List), "llList2ListStrided", (object)src, (object)start, (object)end, (object)stride);
	}

	[MMRContableAttribute ()]
	public LSL_Rotation llList2Rot (LSL_List src, int index) {
		return (LSL_Rotation)Common (typeof (LSL_Rotation), "llList2Rot", (object)src, (object)index);
	}

	[MMRContableAttribute ()]
	public LSL_String llList2String (LSL_List src, int index) {
		return (string)Common (typeof (string), "llList2String", (object)src, (object)index);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llList2Vector (LSL_List src, int index) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llList2Vector", (object)src, (object)index);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llListen (int channelID, string name, string ID, string msg) {
		return (int)Common (typeof (int), "llListen", (object)channelID, (object)name, (object)ID, (object)msg);
	}

	[MMRContableAttribute ()]
	public void llListenControl (int number, int active) {
		Common (typeof (void), "llListenControl", (object)number, (object)active);
	}

	[MMRContableAttribute ()]
	public void llListenRemove (int number) {
		Common (typeof (void), "llListenRemove", (object)number);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llListFindList (LSL_List src, LSL_List test) {
		return (int)Common (typeof (int), "llListFindList", (object)src, (object)test);
	}

	[MMRContableAttribute ()]
	public LSL_List llListInsertList (LSL_List dest, LSL_List src, int start) {
		return (LSL_List)Common (typeof (LSL_List), "llListInsertList", (object)dest, (object)src, (object)start);
	}

	[MMRContableAttribute ()]
	public LSL_List llListRandomize (LSL_List src, int stride) {
		return (LSL_List)Common (typeof (LSL_List), "llListRandomize", (object)src, (object)stride);
	}

	[MMRContableAttribute ()]
	public LSL_List llListReplaceList (LSL_List dest, LSL_List src, int start, int end) {
		return (LSL_List)Common (typeof (LSL_List), "llListReplaceList", (object)dest, (object)src, (object)start, (object)end);
	}

	[MMRContableAttribute ()]
	public LSL_List llListSort (LSL_List src, int stride, int ascending) {
		return (LSL_List)Common (typeof (LSL_List), "llListSort", (object)src, (object)stride, (object)ascending);
	}

	[MMRContableAttribute ()]
	public LSL_Float llListStatistics (int operation, LSL_List src) {
		return (float)Common (typeof (float), "llListStatistics", (object)operation, (object)src);
	}

	[MMRContableAttribute ()]
	public void llLoadURL (string avatar_id, string message, string url) {
		Common (typeof (void), "llLoadURL", (object)avatar_id, (object)message, (object)url);
	}

	[MMRContableAttribute ()]
	public LSL_Float llLog (double val) {
		return (float)Common (typeof (float), "llLog", (object)val);
	}

	[MMRContableAttribute ()]
	public LSL_Float llLog10 (double val) {
		return (float)Common (typeof (float), "llLog10", (object)val);
	}

	[MMRContableAttribute ()]
	public void llLookAt (LSL_Vector target, double strength, double damping) {
		Common (typeof (void), "llLookAt", (object)target, (object)strength, (object)damping);
	}

	[MMRContableAttribute ()]
	public void llLoopSound (string sound, double volume) {
		Common (typeof (void), "llLoopSound", (object)sound, (object)volume);
	}

	[MMRContableAttribute ()]
	public void llLoopSoundMaster (string sound, double volume) {
		Common (typeof (void), "llLoopSoundMaster", (object)sound, (object)volume);
	}

	[MMRContableAttribute ()]
	public void llLoopSoundSlave (string sound, double volume) {
		Common (typeof (void), "llLoopSoundSlave", (object)sound, (object)volume);
	}

	[MMRContableAttribute ()]
	public void llMakeExplosion (int particles, double scale, double vel, double lifetime, double arc, string texture, LSL_Vector offset) {
		Common (typeof (void), "llMakeExplosion", (object)particles, (object)scale, (object)vel, (object)lifetime, (object)arc, (object)texture, (object)offset);
	}

	[MMRContableAttribute ()]
	public void llMakeFire (int particles, double scale, double vel, double lifetime, double arc, string texture, LSL_Vector offset) {
		Common (typeof (void), "llMakeFire", (object)particles, (object)scale, (object)vel, (object)lifetime, (object)arc, (object)texture, (object)offset);
	}

	[MMRContableAttribute ()]
	public void llMakeFountain (int particles, double scale, double vel, double lifetime, double arc, int bounce, string texture, LSL_Vector offset, double bounce_offset) {
		Common (typeof (void), "llMakeFountain", (object)particles, (object)scale, (object)vel, (object)lifetime, (object)arc, (object)bounce, (object)texture, (object)offset, (object)bounce_offset);
	}

	[MMRContableAttribute ()]
	public void llMakeSmoke (int particles, double scale, double vel, double lifetime, double arc, string texture, LSL_Vector offset) {
		Common (typeof (void), "llMakeSmoke", (object)particles, (object)scale, (object)vel, (object)lifetime, (object)arc, (object)texture, (object)offset);
	}

	[MMRContableAttribute ()]
	public void llMapDestination (string simname, LSL_Vector pos, LSL_Vector look_at) {
		Common (typeof (void), "llMapDestination", (object)simname, (object)pos, (object)look_at);
	}

	[MMRContableAttribute ()]
	public LSL_String llMD5String (string src, int nonce) {
		return (string)Common (typeof (string), "llMD5String", (object)src, (object)nonce);
	}

	[MMRContableAttribute ()]
	public LSL_String llSHA1String (string src) {
		return (string)Common (typeof (string), "llSHA1String", (object)src);
	}

	[MMRContableAttribute ()]
	public void llMessageLinked (int linknum, int num, string str, string id) {
		Common (typeof (void), "llMessageLinked", (object)linknum, (object)num, (object)str, (object)id);
	}

	[MMRContableAttribute ()]
	public void llMinEventDelay (double delay) {
		Common (typeof (void), "llMinEventDelay", (object)delay);
	}

	[MMRContableAttribute ()]
	public void llModifyLand (int action, int brush) {
		Common (typeof (void), "llModifyLand", (object)action, (object)brush);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llModPow (int a, int b, int c) {
		return (int)Common (typeof (int), "llModPow", (object)a, (object)b, (object)c);
	}

	[MMRContableAttribute ()]
	public void llMoveToTarget (LSL_Vector target, double tau) {
		Common (typeof (void), "llMoveToTarget", (object)target, (object)tau);
	}

	[MMRContableAttribute ()]
	public void llOffsetTexture (double u, double v, int face) {
		Common (typeof (void), "llOffsetTexture", (object)u, (object)v, (object)face);
	}

	[MMRContableAttribute ()]
	public void llOpenRemoteDataChannel () {
		Common (typeof (void), "llOpenRemoteDataChannel");
	}

	[MMRContableAttribute ()]
	public LSL_Integer llOverMyLand (string id) {
		return (int)Common (typeof (int), "llOverMyLand", (object)id);
	}

	[MMRContableAttribute ()]
	public void llOwnerSay (string msg) {
		Common (typeof (void), "llOwnerSay", (object)msg);
	}

	[MMRContableAttribute ()]
	public void llParcelMediaCommandList (LSL_List commandList) {
		Common (typeof (void), "llParcelMediaCommandList", (object)commandList);
	}

	[MMRContableAttribute ()]
	public LSL_List llParcelMediaQuery (LSL_List aList) {
		return (LSL_List)Common (typeof (LSL_List), "llParcelMediaQuery", (object)aList);
	}

	[MMRContableAttribute ()]
	public LSL_List llParseString2List (string str, LSL_List separators, LSL_List spacers) {
		return (LSL_List)Common (typeof (LSL_List), "llParseString2List", (object)str, (object)separators, (object)spacers);
	}

	[MMRContableAttribute ()]
	public LSL_List llParseStringKeepNulls (string src, LSL_List seperators, LSL_List spacers) {
		return (LSL_List)Common (typeof (LSL_List), "llParseStringKeepNulls", (object)src, (object)seperators, (object)spacers);
	}

	[MMRContableAttribute ()]
	public void llParticleSystem (LSL_List rules) {
		Common (typeof (void), "llParticleSystem", (object)rules);
	}

	[MMRContableAttribute ()]
	public void llPassCollisions (int pass) {
		Common (typeof (void), "llPassCollisions", (object)pass);
	}

	[MMRContableAttribute ()]
	public void llPassTouches (int pass) {
		Common (typeof (void), "llPassTouches", (object)pass);
	}

	[MMRContableAttribute ()]
	public void llPlaySound (string sound, double volume) {
		Common (typeof (void), "llPlaySound", (object)sound, (object)volume);
	}

	[MMRContableAttribute ()]
	public void llPlaySoundSlave (string sound, double volume) {
		Common (typeof (void), "llPlaySoundSlave", (object)sound, (object)volume);
	}

	[MMRContableAttribute ()]
	public void llPointAt (LSL_Vector pos) {
		Common (typeof (void), "llPointAt", (object)pos);
	}

	[MMRContableAttribute ()]
	public LSL_Float llPow (double fbase, double fexponent) {
		return (float)Common (typeof (float), "llPow", (object)fbase, (object)fexponent);
	}

	[MMRContableAttribute ()]
	public void llPreloadSound (string sound) {
		Common (typeof (void), "llPreloadSound", (object)sound);
	}

	[MMRContableAttribute ()]
	public void llPushObject (string target, LSL_Vector impulse, LSL_Vector ang_impulse, int local) {
		Common (typeof (void), "llPushObject", (object)target, (object)impulse, (object)ang_impulse, (object)local);
	}

	[MMRContableAttribute ()]
	public void llRefreshPrimURL () {
		Common (typeof (void), "llRefreshPrimURL");
	}

	[MMRContableAttribute ()]
	public void llRegionSay (int channelID, string text) {
		Common (typeof (void), "llRegionSay", (object)channelID, (object)text);
	}

	[MMRContableAttribute ()]
	public void llReleaseCamera (string avatar) {
		Common (typeof (void), "llReleaseCamera", (object)avatar);
	}

	[MMRContableAttribute ()]
	public void llReleaseControls () {
		Common (typeof (void), "llReleaseControls");
	}

	[MMRContableAttribute ()]
	public void llReleaseURL (string url) {
		Common (typeof (void), "llReleaseURL", (object)url);
	}

	[MMRContableAttribute ()]
	public void llRemoteDataReply (string channel, string message_id, string sdata, int idata) {
		Common (typeof (void), "llRemoteDataReply", (object)channel, (object)message_id, (object)sdata, (object)idata);
	}

	[MMRContableAttribute ()]
	public void llRemoteDataSetRegion () {
		Common (typeof (void), "llRemoteDataSetRegion");
	}

	[MMRContableAttribute ()]
	public void llRemoteLoadScript (string target, string name, int running, int start_param) {
		Common (typeof (void), "llRemoteLoadScript", (object)target, (object)name, (object)running, (object)start_param);
	}

	[MMRContableAttribute ()]
	public void llRemoteLoadScriptPin (string target, string name, int pin, int running, int start_param) {
		Common (typeof (void), "llRemoteLoadScriptPin", (object)target, (object)name, (object)pin, (object)running, (object)start_param);
	}

	[MMRContableAttribute ()]
	public void llRemoveFromLandBanList (string avatar) {
		Common (typeof (void), "llRemoveFromLandBanList", (object)avatar);
	}

	[MMRContableAttribute ()]
	public void llRemoveFromLandPassList (string avatar) {
		Common (typeof (void), "llRemoveFromLandPassList", (object)avatar);
	}

	[MMRContableAttribute ()]
	public void llRemoveInventory (string item) {
		Common (typeof (void), "llRemoveInventory", (object)item);
	}

	[MMRContableAttribute ()]
	public void llRemoveVehicleFlags (int flags) {
		Common (typeof (void), "llRemoveVehicleFlags", (object)flags);
	}

	[MMRContableAttribute ()]
	public LSL_Key llRequestAgentData (string id, int data) {
		return (LSL_Key)Common (typeof (LSL_Key), "llRequestAgentData", (object)id, (object)data);
	}

	[MMRContableAttribute ()]
	public LSL_Key llRequestInventoryData (string name) {
		return (LSL_Key)Common (typeof (LSL_Key), "llRequestInventoryData", (object)name);
	}

	[MMRContableAttribute ()]
	public void llRequestPermissions (string agent, int perm) {
		Common (typeof (void), "llRequestPermissions", (object)agent, (object)perm);
	}

	[MMRContableAttribute ()]
	public LSL_Key llRequestSecureURL () {
		return (LSL_Key)Common (typeof (void), "llRequestSecureURL");
	}

	[MMRContableAttribute ()]
	public LSL_Key llRequestSimulatorData (string simulator, int data) {
		return (LSL_Key)Common (typeof (LSL_Key), "llRequestSimulatorData", (object)simulator, (object)data);
	}

	[MMRContableAttribute ()]
	public LSL_Key llRequestURL () {
		return (LSL_Key)Common (typeof (void), "llRequestURL");
	}

	[MMRContableAttribute ()]
	public void llResetLandBanList () {
		Common (typeof (void), "llResetLandBanList");
	}

	[MMRContableAttribute ()]
	public void llResetLandPassList () {
		Common (typeof (void), "llResetLandPassList");
	}

	[MMRContableAttribute ()]
	public void llResetOtherScript (string name) {
		Common (typeof (void), "llResetOtherScript", (object)name);
	}

	[MMRContableAttribute ()]
	public void llResetScript () {
		Common (typeof (void), "llResetScript");
	}

	[MMRContableAttribute ()]
	public void llResetTime () {
		Common (typeof (void), "llResetTime");
	}

	[MMRContableAttribute ()]
	public void llRezAtRoot (string inventory, LSL_Vector position, LSL_Vector velocity, LSL_Rotation rot, int param) {
		Common (typeof (void), "llRezAtRoot", (object)inventory, (object)position, (object)velocity, (object)rot, (object)param);
	}

	[MMRContableAttribute ()]
	public void llRezObject (string inventory, LSL_Vector pos, LSL_Vector vel, LSL_Rotation rot, int param) {
		Common (typeof (void), "llRezObject", (object)inventory, (object)pos, (object)vel, (object)rot, (object)param);
	}

	[MMRContableAttribute ()]
	public LSL_Float llRot2Angle (LSL_Rotation rot) {
		return (float)Common (typeof (float), "llRot2Angle", (object)rot);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llRot2Axis (LSL_Rotation rot) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llRot2Axis", (object)rot);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llRot2Euler (LSL_Rotation r) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llRot2Euler", (object)r);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llRot2Fwd (LSL_Rotation r) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llRot2Fwd", (object)r);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llRot2Left (LSL_Rotation r) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llRot2Left", (object)r);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llRot2Up (LSL_Rotation r) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llRot2Up", (object)r);
	}

	[MMRContableAttribute ()]
	public void llRotateTexture (double rotation, int face) {
		Common (typeof (void), "llRotateTexture", (object)rotation, (object)face);
	}

	[MMRContableAttribute ()]
	public LSL_Rotation llRotBetween (LSL_Vector start, LSL_Vector end) {
		return (LSL_Rotation)Common (typeof (LSL_Rotation), "llRotBetween", (object)start, (object)end);
	}

	[MMRContableAttribute ()]
	public void llRotLookAt (LSL_Rotation target, double strength, double damping) {
		Common (typeof (void), "llRotLookAt", (object)target, (object)strength, (object)damping);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llRotTarget (LSL_Rotation rot, double error) {
		return (int)Common (typeof (int), "llRotTarget", (object)rot, (object)error);
	}

	[MMRContableAttribute ()]
	public void llRotTargetRemove (int number) {
		Common (typeof (void), "llRotTargetRemove", (object)number);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llRound (double f) {
		return (int)Common (typeof (int), "llRound", (object)f);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llSameGroup (string agent) {
		return (int)Common (typeof (int), "llSameGroup", (object)agent);
	}

	[MMRContableAttribute ()]
	public void llSay (int channelID, string text) {
		Common (typeof (void), "llSay", (object)channelID, (object)text);
	}

	[MMRContableAttribute ()]
	public void llScaleTexture (double u, double v, int face) {
		Common (typeof (void), "llScaleTexture", (object)u, (object)v, (object)face);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llScriptDanger (LSL_Vector pos) {
		return (int)Common (typeof (int), "llScriptDanger", (object)pos);
	}

	[MMRContableAttribute ()]
	public LSL_Key llSendRemoteData (string channel, string dest, int idata, string sdata) {
		return (LSL_Key)Common (typeof (LSL_Key), "llSendRemoteData", (object)channel, (object)dest, (object)idata, (object)sdata);
	}

	[MMRContableAttribute ()]
	public void llSensor (string name, string id, int type, double range, double arc) {
		Common (typeof (void), "llSensor", (object)name, (object)id, (object)type, (object)range, (object)arc);
	}

	[MMRContableAttribute ()]
	public void llSensorRemove () {
		Common (typeof (void), "llSensorRemove");
	}

	[MMRContableAttribute ()]
	public void llSensorRepeat (string name, string id, int type, double range, double arc, double rate) {
		Common (typeof (void), "llSensorRepeat", (object)name, (object)id, (object)type, (object)range, (object)arc, (object)rate);
	}

	[MMRContableAttribute ()]
	public void llSetAlpha (double alpha, int face) {
		Common (typeof (void), "llSetAlpha", (object)alpha, (object)face);
	}

	[MMRContableAttribute ()]
	public void llSetBuoyancy (double buoyancy) {
		Common (typeof (void), "llSetBuoyancy", (object)buoyancy);
	}

	[MMRContableAttribute ()]
	public void llSetCameraAtOffset (LSL_Vector offset) {
		Common (typeof (void), "llSetCameraAtOffset", (object)offset);
	}

	[MMRContableAttribute ()]
	public void llSetCameraEyeOffset (LSL_Vector offset) {
		Common (typeof (void), "llSetCameraEyeOffset", (object)offset);
	}

	[MMRContableAttribute ()]
	public void llSetCameraParams (LSL_List rules) {
		Common (typeof (void), "llSetCameraParams", (object)rules);
	}

	[MMRContableAttribute ()]
	public void llSetClickAction (int action) {
		Common (typeof (void), "llSetClickAction", (object)action);
	}

	[MMRContableAttribute ()]
	public void llSetColor (LSL_Vector color, int face) {
		Common (typeof (void), "llSetColor", (object)color, (object)face);
	}

	[MMRContableAttribute ()]
	public void llSetDamage (double damage) {
		Common (typeof (void), "llSetDamage", (object)damage);
	}

	[MMRContableAttribute ()]
	public void llSetForce (LSL_Vector force, int local) {
		Common (typeof (void), "llSetForce", (object)force, (object)local);
	}

	[MMRContableAttribute ()]
	public void llSetForceAndTorque (LSL_Vector force, LSL_Vector torque, int local) {
		Common (typeof (void), "llSetForceAndTorque", (object)force, (object)torque, (object)local);
	}

	[MMRContableAttribute ()]
	public void llSetHoverHeight (double height, int water, double tau) {
		Common (typeof (void), "llSetHoverHeight", (object)height, (object)water, (object)tau);
	}

	[MMRContableAttribute ()]
	public void llSetInventoryPermMask (string item, int mask, int value) {
		Common (typeof (void), "llSetInventoryPermMask", (object)item, (object)mask, (object)value);
	}

	[MMRContableAttribute ()]
	public void llSetLinkAlpha (int linknumber, double alpha, int face) {
		Common (typeof (void), "llSetLinkAlpha", (object)linknumber, (object)alpha, (object)face);
	}

	[MMRContableAttribute ()]
	public void llSetLinkColor (int linknumber, LSL_Vector color, int face) {
		Common (typeof (void), "llSetLinkColor", (object)linknumber, (object)color, (object)face);
	}

	[MMRContableAttribute ()]
	public void llSetLinkPrimitiveParams (int linknumber, LSL_List rules) {
		Common (typeof (void), "llSetLinkPrimitiveParams", (object)linknumber, (object)rules);
	}

	[MMRContableAttribute ()]
	public void llSetLinkTexture (int linknumber, string texture, int face) {
		Common (typeof (void), "llSetLinkTexture", (object)linknumber, (object)texture, (object)face);
	}

	[MMRContableAttribute ()]
	public void llSetLocalRot (LSL_Rotation rot) {
		Common (typeof (void), "llSetLocalRot", (object)rot);
	}

	[MMRContableAttribute ()]
	public void llSetObjectDesc (string desc) {
		Common (typeof (void), "llSetObjectDesc", (object)desc);
	}

	[MMRContableAttribute ()]
	public void llSetObjectName (string name) {
		Common (typeof (void), "llSetObjectName", (object)name);
	}

	[MMRContableAttribute ()]
	public void llSetObjectPermMask (int mask, int value) {
		Common (typeof (void), "llSetObjectPermMask", (object)mask, (object)value);
	}

	[MMRContableAttribute ()]
	public void llSetParcelMusicURL (string url) {
		Common (typeof (void), "llSetParcelMusicURL", (object)url);
	}

	[MMRContableAttribute ()]
	public void llSetPayPrice (int price, LSL_List quick_pay_buttons) {
		Common (typeof (void), "llSetPayPrice", (object)price, (object)quick_pay_buttons);
	}

	[MMRContableAttribute ()]
	public void llSetPos (LSL_Vector pos) {
		Common (typeof (void), "llSetPos", (object)pos);
	}

	[MMRContableAttribute ()]
	public void llSetPrimitiveParams (LSL_List rules) {
		Common (typeof (void), "llSetPrimitiveParams", (object)rules);
	}

	[MMRContableAttribute ()]
	public void llSetPrimURL (string url) {
		Common (typeof (void), "llSetPrimURL", (object)url);
	}

	[MMRContableAttribute ()]
	public void llSetRemoteScriptAccessPin (int pin) {
		Common (typeof (void), "llSetRemoteScriptAccessPin", (object)pin);
	}

	[MMRContableAttribute ()]
	public void llSetRot (LSL_Rotation rot) {
		Common (typeof (void), "llSetRot", (object)rot);
	}

	[MMRContableAttribute ()]
	public void llSetScale (LSL_Vector scale) {
		Common (typeof (void), "llSetScale", (object)scale);
	}

	[MMRContableAttribute ()]
	public void llSetScriptState (string name, int run) {
		Common (typeof (void), "llSetScriptState", (object)name, (object)run);
	}

	[MMRContableAttribute ()]
	public void llSetSitText (string text) {
		Common (typeof (void), "llSetSitText", (object)text);
	}

	[MMRContableAttribute ()]
	public void llSetSoundQueueing (int queue) {
		Common (typeof (void), "llSetSoundQueueing", (object)queue);
	}

	[MMRContableAttribute ()]
	public void llSetSoundRadius (double radius) {
		Common (typeof (void), "llSetSoundRadius", (object)radius);
	}

	[MMRContableAttribute ()]
	public void llSetStatus (int status, int value) {
		Common (typeof (void), "llSetStatus", (object)status, (object)value);
	}

	[MMRContableAttribute ()]
	public void llSetText (string text, LSL_Vector color, double alpha) {
		Common (typeof (void), "llSetText", (object)text, (object)color, (object)alpha);
	}

	[MMRContableAttribute ()]
	public void llSetTexture (string texture, int face) {
		Common (typeof (void), "llSetTexture", (object)texture, (object)face);
	}

	[MMRContableAttribute ()]
	public void llSetTextureAnim (int mode, int face, int sizex, int sizey, double start, double length, double rate) {
		Common (typeof (void), "llSetTextureAnim", (object)mode, (object)face, (object)sizex, (object)sizey, (object)start, (object)length, (object)rate);
	}

	[MMRContableAttribute ()]
	public void llSetTimerEvent (double sec) {
		Common (typeof (void), "llSetTimerEvent", (object)sec);
	}

	[MMRContableAttribute ()]
	public void llSetTorque (LSL_Vector torque, int local) {
		Common (typeof (void), "llSetTorque", (object)torque, (object)local);
	}

	[MMRContableAttribute ()]
	public void llSetTouchText (string text) {
		Common (typeof (void), "llSetTouchText", (object)text);
	}

	[MMRContableAttribute ()]
	public void llSetVehicleFlags (int flags) {
		Common (typeof (void), "llSetVehicleFlags", (object)flags);
	}

	[MMRContableAttribute ()]
	public void llSetVehicleFloatParam (int param, LSL_Float value) {
		Common (typeof (void), "llSetVehicleFloatParam", (object)param, (object)value);
	}

	[MMRContableAttribute ()]
	public void llSetVehicleRotationParam (int param, LSL_Rotation rot) {
		Common (typeof (void), "llSetVehicleRotationParam", (object)param, (object)rot);
	}

	[MMRContableAttribute ()]
	public void llSetVehicleType (int type) {
		Common (typeof (void), "llSetVehicleType", (object)type);
	}

	[MMRContableAttribute ()]
	public void llSetVehicleVectorParam (int param, LSL_Vector vec) {
		Common (typeof (void), "llSetVehicleVectorParam", (object)param, (object)vec);
	}

	[MMRContableAttribute ()]
	public void llShout (int channelID, string text) {
		Common (typeof (void), "llShout", (object)channelID, (object)text);
	}

	[MMRContableAttribute ()]
	public LSL_Float llSin (double f) {
		return (float)Common (typeof (float), "llSin", (object)f);
	}

	[MMRContableAttribute ()]
	public void llSitTarget (LSL_Vector offset, LSL_Rotation rot) {
		Common (typeof (void), "llSitTarget", (object)offset, (object)rot);
	}

	[MMRContableAttribute ()]
	public void llSleep (double sec) {
		Common (typeof (void), "llSleep", (object)sec);
	}

	[MMRContableAttribute ()]
	public void llSound (string sound, double volume, int queue, int loop) {
		Common (typeof (void), "llSound", (object)sound, (object)volume, (object)queue, (object)loop);
	}

	[MMRContableAttribute ()]
	public void llSoundPreload (string sound) {
		Common (typeof (void), "llSoundPreload", (object)sound);
	}

	[MMRContableAttribute ()]
	public LSL_Float llSqrt (double f) {
		return (float)Common (typeof (float), "llSqrt", (object)f);
	}

	[MMRContableAttribute ()]
	public void llStartAnimation (string anim) {
		Common (typeof (void), "llStartAnimation", (object)anim);
	}

	[MMRContableAttribute ()]
	public void llStopAnimation (string anim) {
		Common (typeof (void), "llStopAnimation", (object)anim);
	}

	[MMRContableAttribute ()]
	public void llStopHover () {
		Common (typeof (void), "llStopHover");
	}

	[MMRContableAttribute ()]
	public void llStopLookAt () {
		Common (typeof (void), "llStopLookAt");
	}

	[MMRContableAttribute ()]
	public void llStopMoveToTarget () {
		Common (typeof (void), "llStopMoveToTarget");
	}

	[MMRContableAttribute ()]
	public void llStopPointAt () {
		Common (typeof (void), "llStopPointAt");
	}

	[MMRContableAttribute ()]
	public void llStopSound () {
		Common (typeof (void), "llStopSound");
	}

	[MMRContableAttribute ()]
	public LSL_Integer llStringLength (string str) {
		return (int)Common (typeof (int), "llStringLength", (object)str);
	}

	[MMRContableAttribute ()]
	public LSL_String llStringToBase64 (string str) {
		return (string)Common (typeof (string), "llStringToBase64", (object)str);
	}

	[MMRContableAttribute ()]
	public LSL_String llStringTrim (string src, int type) {
		return (string)Common (typeof (string), "llStringTrim", (object)src, (object)type);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llSubStringIndex (string source, string pattern) {
		return (int)Common (typeof (int), "llSubStringIndex", (object)source, (object)pattern);
	}

	[MMRContableAttribute ()]
	public void llTakeCamera (string avatar) {
		Common (typeof (void), "llTakeCamera", (object)avatar);
	}

	[MMRContableAttribute ()]
	public void llTakeControls (int controls, int accept, int pass_on) {
		Common (typeof (void), "llTakeControls", (object)controls, (object)accept, (object)pass_on);
	}

	[MMRContableAttribute ()]
	public LSL_Float llTan (double f) {
		return (float)Common (typeof (float), "llTan", (object)f);
	}

	[MMRContableAttribute ()]
	public LSL_Integer llTarget (LSL_Vector position, double range) {
		return (int)Common (typeof (int), "llTarget", (object)position, (object)range);
	}

	[MMRContableAttribute ()]
	public void llTargetOmega (LSL_Vector axis, double spinrate, double gain) {
		Common (typeof (void), "llTargetOmega", (object)axis, (object)spinrate, (object)gain);
	}

	[MMRContableAttribute ()]
	public void llTargetRemove (int number) {
		Common (typeof (void), "llTargetRemove", (object)number);
	}

	[MMRContableAttribute ()]
	public void llTeleportAgentHome (string agent) {
		Common (typeof (void), "llTeleportAgentHome", (object)agent);
	}

	[MMRContableAttribute ()]
	public void llTextBox (string avatar, string message, int chat_channel) {
		Common (typeof (void), "llTextBox", (object)avatar, (object)message, (object)chat_channel);
	}

	[MMRContableAttribute ()]
	public LSL_String llToLower (string source) {
		return (string)Common (typeof (string), "llToLower", (object)source);
	}

	[MMRContableAttribute ()]
	public LSL_String llToUpper (string source) {
		return (string)Common (typeof (string), "llToUpper", (object)source);
	}

	[MMRContableAttribute ()]
	public void llTriggerSound (string sound, double volume) {
		Common (typeof (void), "llTriggerSound", (object)sound, (object)volume);
	}

	[MMRContableAttribute ()]
	public void llTriggerSoundLimited (string sound, double volume, LSL_Vector top_north_east, LSL_Vector bottom_south_west) {
		Common (typeof (void), "llTriggerSoundLimited", (object)sound, (object)volume, (object)top_north_east, (object)bottom_south_west);
	}

	[MMRContableAttribute ()]
	public LSL_String llUnescapeURL (string url) {
		return (string)Common (typeof (string), "llUnescapeURL", (object)url);
	}

	[MMRContableAttribute ()]
	public void llUnSit (string id) {
		Common (typeof (void), "llUnSit", (object)id);
	}

	[MMRContableAttribute ()]
	public LSL_Float llVecDist (LSL_Vector a, LSL_Vector b) {
		return (float)Common (typeof (float), "llVecDist", (object)a, (object)b);
	}

	[MMRContableAttribute ()]
	public LSL_Float llVecMag (LSL_Vector v) {
		return (float)Common (typeof (float), "llVecMag", (object)v);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llVecNorm (LSL_Vector v) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llVecNorm", (object)v);
	}

	[MMRContableAttribute ()]
	public void llVolumeDetect (int detect) {
		Common (typeof (void), "llVolumeDetect", (object)detect);
	}

	[MMRContableAttribute ()]
	public LSL_Float llWater (LSL_Vector offset) {
		return (float)Common (typeof (float), "llWater", (object)offset);
	}

	[MMRContableAttribute ()]
	public void llWhisper (int channelID, string text) {
		Common (typeof (void), "llWhisper", (object)channelID, (object)text);
	}

	[MMRContableAttribute ()]
	public LSL_Vector llWind (LSL_Vector offset) {
		return (LSL_Vector)Common (typeof (LSL_Vector), "llWind", (object)offset);
	}

	[MMRContableAttribute ()]
	public LSL_String llXorBase64Strings (string str1, string str2) {
		return (string)Common (typeof (string), "llXorBase64Strings", (object)str1, (object)str2);
	}

	[MMRContableAttribute ()]
	public LSL_String llXorBase64StringsCorrect (string str1, string str2) {
		return (string)Common (typeof (string), "llXorBase64StringsCorrect", (object)str1, (object)str2);
	}

	private object[] args0 = new object[0];
	[MMRContableAttribute ()]
	private object Common (System.Type rettype, string name)
	{
		return CommonWork (rettype, name, args0);
	}

	private object[] args1 = new object[1];
	[MMRContableAttribute ()]
	private object Common (System.Type rettype, string name, object arg0)
	{
		args1[0] = arg0;
		return CommonWork (rettype, name, args1);
	}

	private object[] args2 = new object[2];
	[MMRContableAttribute ()]
	private object Common (System.Type rettype, string name, object arg0, object arg1)
	{
		args2[0] = arg0;
		args2[1] = arg1;
		return CommonWork (rettype, name, args2);
	}

	private object[] args3 = new object[3];
	[MMRContableAttribute ()]
	private object Common (System.Type rettype, string name, object arg0, object arg1, object arg2)
	{
		args3[0] = arg0;
		args3[1] = arg1;
		args3[2] = arg2;
		return CommonWork (rettype, name, args3);
	}

	private object[] args4 = new object[4];
	[MMRContableAttribute ()]
	private object Common (System.Type rettype, string name, object arg0, object arg1, object arg2, object arg3)
	{
		args4[0] = arg0;
		args4[1] = arg1;
		args4[2] = arg2;
		args4[3] = arg3;
		return CommonWork (rettype, name, args4);
	}

	private object[] args5 = new object[5];
	[MMRContableAttribute ()]
	private object Common (System.Type rettype, string name, object arg0, object arg1, object arg2, object arg3, object arg4)
	{
		args5[0] = arg0;
		args5[1] = arg1;
		args5[2] = arg2;
		args5[3] = arg3;
		args5[4] = arg4;
		return CommonWork (rettype, name, args5);
	}

	private object[] args6 = new object[6];
	[MMRContableAttribute ()]
	private object Common (System.Type rettype, string name, object arg0, object arg1, object arg2, object arg3, object arg4, object arg5)
	{
		args6[0] = arg0;
		args6[1] = arg1;
		args6[2] = arg2;
		args6[3] = arg3;
		args6[4] = arg4;
		args6[5] = arg5;
		return CommonWork (rettype, name, args6);
	}

	private object[] args7 = new object[7];
	[MMRContableAttribute ()]
	private object Common (System.Type rettype, string name, object arg0, object arg1, object arg2, object arg3, object arg4, object arg5, object arg6)
	{
		args7[0] = arg0;
		args7[1] = arg1;
		args7[2] = arg2;
		args7[3] = arg3;
		args7[4] = arg4;
		args7[5] = arg5;
		args7[6] = arg6;
		return CommonWork (rettype, name, args7);
	}

	/*
	private object[] args8 = new object[8];
	[MMRContableAttribute ()]
	private object Common (System.Type rettype, string name, object arg0, object arg1, object arg2, object arg3, object arg4, object arg5, object arg6, object arg7)
	{
		args8[0] = arg0;
		args8[1] = arg1;
		args8[2] = arg2;
		args8[3] = arg3;
		args8[4] = arg4;
		args8[5] = arg5;
		args8[6] = arg6;
		args8[7] = arg7;
		return CommonWork (rettype, name, args8);
	}
	 */

	private object[] args9 = new object[9];
	[MMRContableAttribute ()]
	private object Common (System.Type rettype, string name, object arg0, object arg1, object arg2, object arg3, object arg4, object arg5, object arg6, object arg7, object arg8)
	{
		args9[0] = arg0;
		args9[1] = arg1;
		args9[2] = arg2;
		args9[3] = arg3;
		args9[4] = arg4;
		args9[5] = arg5;
		args9[6] = arg6;
		args9[7] = arg7;
		args9[8] = arg8;
		return CommonWork (rettype, name, args9);
	}

	/*
	 * On entry, token points to either the next call name
	 * or the close brace if no more expected.
	 */
	[MMRContableAttribute ()]
	private object CommonWork (System.Type rettype, string name, object[] args)
	{
		if (this.disposed) {
			throw new Exception ("using a disposed LSLApi object");
		}

		/*
		 * Print out the call that we actually got.
		 * Leave output line open so we can print return value later.
		 */
		Console.Write ("{0}.{1}: call rcvd: (" + rettype.Name + ") " + name + " (", token.line, token.posn);
		for (int i = 0; i < args.Length; i ++) {
			if (i > 0) Console.Write (",");
			Console.Write (args[i].ToString ());
		}
		Console.Write (") ");

		/*
		 * See if the function name matches what the test script says.
		 */
		if (!(token is TokenName)) {
			token.ErrorMsg ("expected call name token");
			throw new Exception ("extra call received");
		}

		TokenName entryName = (TokenName)token;
		if (name != entryName.val) {
			entryName.ErrorMsg ("expected call to " + entryName.val);
			throw new Exception ("wrong call received");
		}

		/*
		 * And it should be followed by an open parenthesis.
		 */
		token = token.nextToken;
		if (!(token is TokenKwParOpen)) {
			token.ErrorMsg ("expected open paren");
			throw new Exception ("expected open paren");
		}
		token = token.nextToken;

		/*
		 * See if values given match those in the script.
		 */
		for (int i = 0; i < args.Length; i ++) {
			if (token is TokenKwParClose) {
				token.ErrorMsg ("script has too few args");
				throw new Exception ("script has too few args");
			}
			if (i > 0) {
				if (!(token is TokenKwComma)) {
					token.ErrorMsg ("expected comma");
					throw new Exception ("expected comma");
				}
				token = token.nextToken;
			}
			Token saveToken = token;
			object scriptval = GetTokenVal (ref token);
			if (scriptval == null) {
				throw new Exception ("bad script value");
			}
			object argval = args[i];
			if (!ValuesEqual (scriptval, argval)) {
				saveToken.ErrorMsg ("expected " + scriptval.ToString () + ", got " + args[i].ToString ());
				throw new Exception ("wrong value received");
			}
		}

		/*
		 * Make sure we got correct number of arguments.
		 */
		if (!(token is TokenKwParClose)) {
			token.ErrorMsg ("script has too many args");
			throw new Exception ("script has too many args");
		}
		token = token.nextToken;

		/*
		 * See what type of return value is then get corresponding token from script.
		 * If void, we return a null as it isn't used.
		 * Otherwise, for value types, we return a boxed value.
		 */
		object retvalu = null;

		if (rettype != typeof (void)) {
			Token t = token;
			retvalu = GetTokenVal (ref t);
			if (retvalu == null) {
				throw new Exception ("bad return value");
			}
			if ((rettype == typeof (float)) && (retvalu.GetType () == typeof (int))) {
				int ival = (int)retvalu;
				float fval = (float)ival;
				retvalu = fval;
			}
			if (rettype == typeof (LSL_Key)) {
				try {
					retvalu = new LSL_Key ((string)retvalu);
				}
				catch (ArgumentException ae) {
					token.ErrorMsg (ae.Message);
					throw ae;
				}
			}
			if (retvalu.GetType () != rettype) {
				token.ErrorMsg ("expected type " + rettype.ToString () + ", not " + retvalu.GetType ().ToString ());
				throw new Exception ("bad return type");
			}
			token = t;

			// output return value on end of line after call
			Console.WriteLine (retvalu);
		} else {
			Console.WriteLine ("");
		}

		/*
		 * Then there should be a semi-colon.
		 */
		if (!(token is TokenKwSemi)) {
			token.ErrorMsg ("expecting semi-colon");
			throw new Exception ("expecting semi-colon");
		}
		token = token.nextToken;

		/*
		 * Do a switch before returning for fun.
		 * COMMENTED OUT because script is compiled with a CheckRun()
		 * call after every ll API call so the ll API calls don't have
		 * to all be marked with MMContableAttribute().
		 */
		///this.scriptWrapper.continuation.CheckRun ();

		return retvalu;
	}

	/**
	 * @brief see if value passed to call is same as value in script.
	 * @param scrobj = object representing value from script
	 * @param argobj = object representing value from call
	 * @returns true iff they are equal
	 */
	private static bool ValuesEqual (object scrobj, object argobj)
	{
		/*
		 * Float just has to be equal enough.
		 */
		if ((scrobj is float) && (argobj is float || argobj is double)) {
			float sv  = (float)scrobj;
			float av  = (argobj is float) ? (float)argobj : (float)(double)argobj;
			float asv = Math.Abs (sv);
			float aav = Math.Abs (av);
			if ((asv < 1.0e-45f) && (aav < 1.0e-45f)) return true;
			if ((sv >= 0) ^ (av >= 0)) return false;
			float lsv = (float)Math.Log(asv);
			float lav = (float)Math.Log(aav);
			if (Math.Abs (lsv - lav) > 1.0f) return false;
			float ratio = sv / av;
			return Math.Abs (ratio - 1.0f) < 0.0001f;  // 4 digits of precision
		}

		/*
		 * Everything else must match exactly.
		 */
		return scrobj.ToString () == argobj.ToString ();
	}

	/**
	 * @brief This utility function gets a value from the test token stream.
	 * @param token = points to beginning of value in token stream
	 * @returns null: token not a valid value
	 *          else: object representing token's value (boxed if necessary)
	 *                token = advanced past value
	 */
	public static object GetTokenVal (ref Token token)
	{
		if (token is TokenKwSub) {
			Token t = token.nextToken;
			object posval = GetTokenVal (ref t);
			if (posval == null) return null;
			if (posval is float) {
				token = t;
				return (object)(-(float)posval);
			}
			if (posval is int) {
				token = t;
				return (object)(-(int)posval);
			}
			token.ErrorMsg ("can't negate a " + posval.GetType ().ToString ());
			return null;
		}
		if (token is TokenFloat) {
			float floatval = (float)((TokenFloat)token).val;
			token = token.nextToken;
			return floatval;
		}
		if (token is TokenInt) {
			int intval = ((TokenInt)token).val;
			token = token.nextToken;
			return intval;
		}
		if (token is TokenStr) {
			string strval = ((TokenStr)token).val;
			token = token.nextToken;
			return strval;
		}

		// vector or rotation
		if (token is TokenKwCmpLT) {
			Token t = token.nextToken;
			float val1 = GetTokenValFloat (ref t);
			if (t == null) return null;
			if (!(t is TokenKwComma)) goto vexcom;
			t = t.nextToken;
			float val2 = GetTokenValFloat (ref t);
			if (t == null) return null;
			if (!(t is TokenKwComma)) goto vexcom;
			t = t.nextToken;
			float val3 = GetTokenValFloat (ref t);
			if (t == null) return null;
			if (t is TokenKwCmpGT) {
				token = t.nextToken;
				return new LSL_Vector (val1, val2, val3);
			}
			if (!(t is TokenKwComma)) goto vexcom;
			t = t.nextToken;
			float val4 = GetTokenValFloat (ref t);
			if (t == null) return null;
			if (!(t is TokenKwCmpGT)) goto vexcom;
			token = t.nextToken;
			return new LSL_Rotation (val1, val2, val3, val4);
		vexcom:
			t.ErrorMsg ("expected comma or close angle-bracket");
			return null;
		}

		// list
		if (token is TokenKwBrkOpen) {
			Token t = token.nextToken;
			LSL_List list = new LSL_List ();
			while (!(t is TokenKwBrkClose)) {
				object element = GetTokenVal (ref t);
				if (element == null) return null;
				list.Add (element);
				if (t is TokenKwComma) {
					t = t.nextToken;
					continue;
				}
				if (!(t is TokenKwBrkClose)) {
					t.ErrorMsg ("expected comma or close bracket");
					return null;
				}
			}
		}

		// who knows what?
		token.ErrorMsg ("expected value");
		return null;
	}

	public static float GetTokenValFloat (ref Token token)
	{
		if (token is TokenFloat) {
			float floatval = (float)((TokenFloat)token).val;
			token = token.nextToken;
			return floatval;
		}
		if (token is TokenInt) {
			int intval = ((TokenInt)token).val;
			token = token.nextToken;
			return (float)intval;
		}
		token.ErrorMsg ("expected float (or integer)");
		token = null;
		return 0.0f;
	}

	/*
	 * Just for testing, no need for a real Clone() method.
	 */
	public TestLSLAPI Clone ()
	{
		TestLSLAPI lsltr = new TestLSLAPI ();
		lsltr.token = this.token;
		lsltr.scriptWrapper = this.scriptWrapper;
		return lsltr;
	}

	/*
	 * Just for testing, no need for a real Dispose() method.
	 */
	public void Dispose ()
	{
		if (this.disposed) {
			throw new Exception ("already disposed");
		}
		this.disposed = true;
	}
}
