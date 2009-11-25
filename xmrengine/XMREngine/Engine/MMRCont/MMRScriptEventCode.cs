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
		run_time_permissions,
		sensor,
		state_entry,
		state_exit,
		timer,
		touch,
		touch_end,
		touch_start,
		Size,
		Garbage = 12345678
	}
}
