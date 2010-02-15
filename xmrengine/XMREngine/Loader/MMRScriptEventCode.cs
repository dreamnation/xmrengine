/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

namespace OpenSim.Region.ScriptEngine.XMREngine.Loader {

	/**
	 * @brief List of event codes that can be passed to StartEventHandler().
	 *        Must have same name as corresponding event handler name, so
	 *        the compiler will know what column in the seht to put the
	 *        event handler entrypoint in.
	 */
	public enum ScriptEventCode : int {

		// used by ScriptWrapper to indicate no event being processed
		None                 = -1,

		// must be bit numbers of equivalent values in ...
		// OpenSim.Region.ScriptEngine.Shared.ScriptBase.scriptEvents
		// ... so they can be passed to m_Part.SetScriptEvents().
		attach               =  0,
		state_exit           =  1,
		timer                =  2,
		touch                =  3,
		collision            =  4,
		collision_end        =  5,
		collision_start      =  6,
		control              =  7,
		dataserver           =  8,
		email                =  9,
		http_response        = 10,
		land_collision       = 11,
		land_collision_end   = 12,
		land_collision_start = 13,
		at_target            = 14,
		listen               = 15,
		money                = 16,
		moving_end           = 17,
		moving_start         = 18,
		not_at_rot_target    = 19,
		not_at_target        = 20,
		touch_start          = 21,
		object_rez           = 22,
		remote_data          = 23,
		run_time_permissions = 28,
		touch_end            = 29,
		state_entry          = 30,

		// events not passed to m_Part.SetScriptEvents().
		at_rot_target        = 32,
		changed              = 33,
		link_message         = 34,
		no_sensor            = 35,
		on_rez               = 36,
		sensor               = 37,

		// marks highest numbered event, ie, number of columns in seht.
		Size                 = 38,
		Garbage        = 12345678
	}
}
