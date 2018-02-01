
list buttons = [ 1, 2, 3 ];

x ()
{
    // the llList2List (buttons, 0, 1) refers to the global buttons
    // because the VarDict stored in the TokenLValName when buttons
    // is parsed is frozen, so it doesn't contain the local definition
    // of buttons, even after the local buttons is added to the
    // current definition frame

    list buttons = llList2List (buttons, 0, 1);

    llOwnerSay (llList2CSV (buttons));
}

y ()
{
    integer a = a + 987;  // forward ref to global 'a' is ok
    {
        integer a = a + 765;  // backward ref to outer 'a' is ok
        llOwnerSay ("one thousand seven hundred sixty four = " + a);
    }
    llOwnerSay ("niner hundred ninety niner = " + a);
}

integer a = 12;

default {
    state_entry ()
    {
        x ();
        y ();
    }
}

