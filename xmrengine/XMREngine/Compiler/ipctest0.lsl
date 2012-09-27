default {
    state_entry ()
    {
        for (integer i = 0; i < 5; i ++) {
            llOwnerSay ((string)i);
        }
    }
    touch_start (integer num)
    {
        llSay (-9, "was just touched!");
    }
}
