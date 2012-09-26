default {
    state_entry ()
    {
        llListen (-9, "", "", "");
    }

    listen (integer channel, string name, key id, string message)
    {
        llOwnerSay ("heard channel=" + channel + " name=" + name + " id=" + id + " message=" + message);
    }
}
