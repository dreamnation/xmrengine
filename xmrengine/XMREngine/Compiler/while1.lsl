xmroption advflowctl;
xmrOption arrayS;
xmroption objects;
xmroption trycatch;
xmrOPtion expIRYdays 5;

integer SomethingToCall ()
{
    integer i = 0;
    while (1) {
        llOwnerSay ("something " + (++ i));
        switch (i) {
            case 1: llOwnerSay ("one"); break;
            case 2 ... 3: llOwnerSay ("two or tree"); break;
            case 4: llOwnerSay ("four"); break;
            case 5: llOwnerSay ("fife"); break;
            default: llOwnerSay ("something big"); return i;
        }
    }
}

default {
    state_entry ()
    {
        llOwnerSay ("done " + SomethingToCall ());
    }
}
