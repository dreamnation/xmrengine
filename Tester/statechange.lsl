XMROption noRightToLeft;
XMROption tryCatch;

// Just waiting to be touched
default
{
    state_entry()
    {
        llOwnerSay("#100: touch to start");
    }
    touch_start()
    {
        state a;
    }
    state_exit()
    {
        llOwnerSay("#110");
    }
}

// Four timer messages
// And list of any avatars in range
integer ntimers;
state a
{
    state_entry()
    {
        ntimers = 4;
        llOwnerSay("#200");
        llSensorRepeat("", "", AGENT, 96.0, PI, 0.5);
        llSetTimerEvent(1.0);
    }
    sensor(integer numDetected)
    {
        for (integer agentNum = 0; agentNum < numDetected; agentNum ++) {
            string thisAgent = llDetectedName(agentNum);
            llOwnerSay("#205: " + thisAgent);
        }
    }
    timer()
    {
        llOwnerSay("#210:" + (-- ntimers));
        if (ntimers == 0) state b;
    }
    state_exit()
    {
        llOwnerSay("#220");
    }
}

// Echo any messages on channel zero
// Touch advances on to next state
state b
{
    state_entry()
    {
        llOwnerSay("#300: listening on channel 0");
        llListen(0, "", "", "");
    }
    touch_start()
    {
        state c;
    }
    // llSensorRepeat() should be cancelled
    sensor()
    {
        throw "sensor() should be cancelled";
    }
    // timers persist on state change
    timer()
    {
        string msg = (string)(ntimers ++);
        llOwnerSay("#310: " + msg);
    }
    listen(integer channel, string name, key id, string msg)
    {
        llOwnerSay("#320: " + channel + ": " + msg);
    }
    state_exit()
    {
        llOwnerSay("#330");
    }
}

// Shouldn't echo any channel 0 messages
// Just click to return to default state
state c
{
    state_entry()
    {
        llOwnerSay("#400: listening chan 0 should be cancelled");
    }
    touch_start()
    {
        state default;
    }
    // llSensorRepeat() should be cancelled
    sensor()
    {
        throw "sensor() should be cancelled";
    }
    // timers persist on state change
    timer()
    {
        string msg = (string)(ntimers ++);
        llOwnerSay("#410: " + msg);
    }
    listen(integer channel, string name, key id, string msg)
    {
        throw "listen() should be cancelled";
    }
    state_exit()
    {
        llOwnerSay("#430");
    }
}
