using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;

namespace MMR
{
    public interface IEventHandlers {
        void at_rot_target (int tnum, LSL_Rotation targetrot, LSL_Rotation ourrot);
        void at_target (int tnum, LSL_Vector targetpos, LSL_Vector ourpos);
        void attach (LSL_Key id);
        void changed (int change);
        void collision (int num_detected);
        void collision_end (int num_detected);
        void collision_start (int num_detected);
        void control (LSL_Key id, int held, int change);
        void dataserver (LSL_Key queryid, string data);
        void email (string time, string address, string subj, string message, int num_left);
        void http_response (LSL_Key request_id, int status, LSL_List metadata, string body);
        void land_collision (LSL_Vector pos);
        void land_collision_end (LSL_Vector pos);
        void land_collision_start (LSL_Vector pos);
        void link_message (int sender_num, int num, string str, LSL_Key id);
        void listen (int channel, string name, LSL_Key id, string message);
        void money (LSL_Key id, int amount);
        void moving_end ();
        void moving_start ();
        void no_sensor ();
        void not_at_rot_target ();
        void not_at_target ();
        void object_rez (LSL_Key id);
        void on_rez (int start_param);
        void remote_data (int event_type, LSL_Key channel, LSL_Key message_id, string sender, int idata, string sdata);
        void run_time_permissions (int perm);
        void sensor (int num_detected);
        void state_entry ();
        void state_exit ();
        void timer ();
        void touch (int num_detected);
        void touch_start (int num_detected);
        void touch_end (int num_detected);
    }
}
