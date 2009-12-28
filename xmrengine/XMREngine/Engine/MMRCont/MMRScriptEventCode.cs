/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

namespace MMR {

	/**
	 * @brief List of event codes that can be passed to StartEventHandler().
	 */
	public enum ScriptEventCode {
		None,

		at_rot_target,
		at_target,
		attach,
		changed,
		collision,
		collision_end,
		collision_start,
		control,
		dataserver,
		email,
		http_response,
		land_collision,
		land_collision_end,
		land_collision_start,
		link_message,
		listen,
		money,
		moving_end,
		moving_start,
		no_sensor,
		not_at_rot_target,
		not_at_target,
		object_rez,
		on_rez,
		remote_data,
		run_time_permissions,
		sensor,
		state_entry,
		state_exit,
		timer,
		touch,
		touch_start,
		touch_end,

		Size,
		Garbage = 12345678
	}
}
