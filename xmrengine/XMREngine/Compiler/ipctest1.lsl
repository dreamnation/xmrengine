default {
    state_entry ()
    {
        llListen (-9, "", "", "");
        for (integer i = 0; i < 5; i ++) {
            llOwnerSay ((string)i);
        }
    }

    listen (integer channel, string name, key id, string message)
    {
        llOwnerSay ("heard channel=" + channel + " name=" + name + " id=" + id + " message=" + message);
    }
}
