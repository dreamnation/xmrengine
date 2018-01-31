
default {
    state_entry ()
    {
        integer y = 0;
        integer x = (2 | (y > 0));
        llOwnerSay (x);
    }
}

