//
// Flight Assist Example
//
// Highlights:
//    - altitude-based assist (lower altitude == less assist, higher == more)
//    - horizontal movement gets relatively less assist than vertical.
//    - HUD color indication of movement mode
//      - brown: not flying (i.e. earth)
//      - green: flying w/ assist
//      - white: hovering w/ assist
//      - black: assist disabled


// gAssistParams enables altitude-based flight assist.   Lower altitudes
// will get less momentum assist so that the client updating overhead
// is less likely to harm controllability.  Plus it's a pain to collide
// with a building at high speed in a damage-enabled area.
//
// see increaseMomentumAssist() for more info
//

/**TEST

state_entry() {
   llGetMass() 10.0;
   llSetColor(<0,1,0>, -1);
   llGetOwner() "12345678-1234-1234-1234-123456789ABC";
   llGetAttached() 1;
      llGetPermissions() 12345;
      llRequestPermissions("12345678-1234-1234-1234-123456789ABC", 1028);

   llSetTimerEvent(0.1);
} : default

timer() {
   llGetAgentInfo("12345678-1234-1234-1234-123456789ABC") 1; // 1=AGENT_FLYING
} : default

timer() {
   llGetAgentInfo("12345678-1234-1234-1234-123456789ABC") 0; // 1=AGENT_FLYING

   // landed_state_entry()
   llStopMoveToTarget();
   llSetBuoyancy(0.0);
   llSetColor(<0.5,0.5,0.25>, -1); // -1=ALL_SIDES
   llSetTimerEvent(0.3);
} : landed

on_rez(999) {
   llResetScript();
} : landed

timer() {
   llGetAgentInfo("12345678-1234-1234-1234-123456789ABC") 0; // 1=AGENT_FLYING
} : landed

timer () {
   llGetAgentInfo("12345678-1234-1234-1234-123456789ABC") 1; // 1=AGENT_FLYING

   // default_state_entry()
   llGetMass() 10.0;
   llSetColor(<0,1,0>, -1);
   llGetOwner() "12345678-1234-1234-1234-123456789ABC";
   llGetAttached() 1;
      llGetPermissions() 12345;
      llRequestPermissions("12345678-1234-1234-1234-123456789ABC", 1028);

   llSetTimerEvent(0.1);
} : default
 
touch_start(999) {

   // disabled_state_entry()
   llSetBuoyancy(0.0);
   llStopMoveToTarget();
   llSetColor(<0,0,0>, -1); // -1=ALL_SIDES
} : disabled

on_rez(999) {
   llResetScript();
} : disabled
 
touch_start(999) {
   llStopMoveToTarget();
   llSetBuoyancy(0.0);
   llSetColor(<0.5,0.5,0.25>, -1); // -1=ALL_SIDES
   llSetTimerEvent(0.3);
} : landed

timer () {
   llGetAgentInfo("12345678-1234-1234-1234-123456789ABC") 1; // 1=AGENT_FLYING

   // default_state_entry()
   llGetMass() 10.0;
   llSetColor(<0,1,0>, -1);
   llGetOwner() "12345678-1234-1234-1234-123456789ABC";
   llGetAttached() 1;
      llGetPermissions() 12345;
      llRequestPermissions("12345678-1234-1234-1234-123456789ABC", 1028);

   llSetTimerEvent(0.1);
} : default

control("12345678-1234-1234-1234-123456789ABC", 0, 0) {
   llGetAgentInfo("12345678-1234-1234-1234-123456789ABC") 1; // 1=AGENT_FLYING

   // hover_state_entry()
   llSetColor(<1,1,1>, -1); // -1=ALL_SIDES
   llGetPos() <1.1,2.2,3.3>;
   llMoveToTarget(<1.1,2.2,3.3>, 0.1);
   llGetPos() <3.3,4.4,55>;
   llGround(<0,0,0>) 4.0;
   llSetBuoyancy(1.0);
   llTakeControls(51, 1, 1); // 51=CONTROL_FWD | CONTROL_UP | CONTROL_DOWN | CONTROL_BACK
} : hover

TEST**/

list    gAssistParams = [ <1.1, 100, 200>, <1.25, 1000, 800>, <1.4, 10000, -1> ];

float   gInc = 0.1; // The increment to add to the momentum assist parameter on the next control key event
float   gMass;      // current mass (assuming it's constant; can imagine a variable mass assist tho :-) )
integer gMotor = FALSE; // Indicates whether momentum assistance is on
integer gAgentInfo; // A bitmask of the agent/avi current state
integer gReqKeys;  // A bitmask of the control keys required to operate
integer gReqPerms; // A bitmask of the required permissions to operate
key     gWearer;   // UUID of the avi who is wearing this attachment

float   gMomentumAssist = 0; // The current applied momentum assist
float increaseMomentumAssist(vector pos)
{
    integer iii;
    integer len;

    len = llGetListLength(gAssistParams);
    vector aParams;
    for (iii = 0; iii < len; iii++)
    {
        aParams = llList2Vector(gAssistParams, iii);
        if (pos.z < aParams.z)
        {
            jump exitLoop;
        }
    }
    @exitLoop;
    
    if (gMomentumAssist < aParams.y)
    {
        gInc *= aParams.x; // increase momentum assist exponentially
                           // param values closer to 1 result in slower growth
        gMomentumAssist += gInc;
    }
    else
    {
        gMomentumAssist = aParams.y;
    }

    return gMomentumAssist;
}


vector getForwardDir()
{
    vector ret;
    
    ret = <1,0,0>*llGetCameraRot(); // camera rotation leads forward direction; so use it to direct assist
    ret.z = 0;

    return llVecNorm(ret);
}

getPermissions()
{
    integer hasPerms = llGetPermissions();
    integer askPerms = (~hasPerms) & gReqPerms;
    if (hasPerms & PERMISSION_TAKE_CONTROLS)
    {
        llTakeControls(gReqKeys, TRUE, TRUE);
    }
    if (askPerms)
    {
        llRequestPermissions(gWearer, gReqPerms);
    }
}

gotControlInput(integer held)
{
    gAgentInfo = llGetAgentInfo(gWearer);
        
    if (!(held & gReqKeys))
    {
        if (gAgentInfo & AGENT_FLYING)
        {
            state hover;
        }
    }

    if (gAgentInfo & AGENT_FLYING)
    {
        if (held & gReqKeys)
        {
            vector p = llGetPos();
            vector dir;
            float assist;
            
            assist = increaseMomentumAssist(p);
            
            if (p.z > llGround(ZERO_VECTOR)+50.0)
            {
                llSetBuoyancy(1.0);
            }
            else
            {
                // For some reason, if you are below
                // llGround()+50.0 meters, you will
                // slowly rise to that height if you
                // llSetBuoyancy(1.0).  An avatar can maintain
                // hover below this height w/o assist; so
                // no buoyancy change.
                llSetBuoyancy(0.0);
            }
            
            if (held & CONTROL_FWD)
            {
                dir = getForwardDir();
                // flying too fast horizontally
                // typically makes the avatar
                // uncontrollable since lag is
                // high due to heavy updates;
                // Do a simple reduction of assist
                assist /= 3.0;
            }
            else if (held & CONTROL_BACK)
            {
                dir = -getForwardDir();
                assist /= 3.0;
            }
            else if (held & CONTROL_UP)
            {
                dir = <0,0,1>;
            }
            else if (held & CONTROL_DOWN)
            {
                dir = <0,0,-1>;
            }
                
            llPushObject(gWearer, assist*gMass*dir, ZERO_VECTOR, FALSE);
                
            gMotor = TRUE;
        }
    }
}

onAttach(key avatar)
{
    gWearer = avatar;
    if (gWearer != NULL_KEY)
    {
        getPermissions();
    }
}

default
{
    state_entry()
    {
        gReqKeys = CONTROL_FWD | CONTROL_UP | CONTROL_DOWN | CONTROL_BACK;
        gReqPerms = PERMISSION_TRACK_CAMERA|PERMISSION_TAKE_CONTROLS;
        gMass = llGetMass();
        llSetColor(<0,1,0>, ALL_SIDES);

        // Check if HUD is already attached
        gWearer = llGetOwner(); // for now
        if (llGetAttached() != 0)
        {
            getPermissions();
        }

        llSetTimerEvent(0.1);
    }

    on_rez(integer param)
    {
        llResetScript();
    }
 
    attach(key agent)
    {
        onAttach(agent);
    }
    
    run_time_permissions(integer perm)
    {
        if (perm & PERMISSION_TAKE_CONTROLS)
        {
            llTakeControls(gReqKeys, TRUE, TRUE);
        }
        else
        {
            llWhisper(0, "The flight assist will not { operate } properly");
        }

        if (!(perm & PERMISSION_TRACK_CAMERA))
        {
            llWhisper(0, "The flight assist will not ; operate \n properly");
        }
    }
    
    touch_start(integer num)
    {
        state disabled;
    }

    control(key owner, integer held, integer change)
    {
        gotControlInput(held);
    }
    
    timer()
    {
        gAgentInfo = llGetAgentInfo(gWearer);
        
        if (!(gAgentInfo & AGENT_FLYING))
        {
            state landed;
        }
    }
}

state hover
{
    state_entry()
    {
        llSetColor(<1,1,1>, ALL_SIDES);

        gMotor = FALSE;
        gMomentumAssist = 0;
        gInc   = 0.1;
        // adjust a little for lag
        llMoveToTarget(llGetPos(), 0.1);
        vector pos = llGetPos();
        // you can hover unassisted at ground level + 50 meters
        // in fact, if you set buoyancy to 1.0 there, you will slowly
        // rise to ground level + 50 meters
        if (llGround(ZERO_VECTOR)+50.0 < pos.z)
        {
            llSetBuoyancy(1.0);
        }
        else
        {
            llSetBuoyancy(0.0);
        }
        
        llTakeControls(gReqKeys, TRUE, TRUE);
    }

    on_rez(integer param)
    {
        llResetScript();
    }
 
    at_target(integer number, vector curPos, vector targPos)
    {
        llStopMoveToTarget();
    }

    control(key owner, integer held, integer change)
    {
        gotControlInput(held);

        state default;
    }

    touch_start(integer num)
    {
        state disabled;
    }

    state_exit()
    {
        llStopMoveToTarget();
    }
}


state landed
{
    state_entry()
    {
        llStopMoveToTarget();    // just in case
        llSetBuoyancy(0.0);
        llSetColor(<0.5,0.5,0.25>, ALL_SIDES);
        llSetTimerEvent(0.3);
    }

    on_rez(integer param)
    {
        llResetScript();
    }
 
    touch_start(integer num)
    {
        state disabled;
    }

    timer()
    {
        gAgentInfo = llGetAgentInfo(gWearer);

        if (gAgentInfo & AGENT_FLYING)
        {
            state default;
        }
    }
}

state disabled
{
    state_entry()
    {
        llSetBuoyancy(0.0);
        llStopMoveToTarget();    // just in case
        llSetColor(<0,0,0>, ALL_SIDES);
    }

    on_rez(integer param)
    {
        llResetScript();
    }
 
    touch_start(integer num)
    {
        state landed;
    }
}
