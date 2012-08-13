xmroption trycatch;

default {
    state_entry ()
    {
        integer x = 0;
        try {
            x = 1;
            llOwnerSay ("maybe wipe x out");
            llOwnerSay ("after CheckRun() in try block");
            jump done1;
        } finally {
            llOwnerSay ("finally x=" + x);
            llOwnerSay ("after CheckRun() in finally block");
        }
    @done1;

        llOwnerSay ("generate first call to checkrun");
        llOwnerSay ("generate second call to checkrun");

        try {
            llOwnerSay ("generate first call to checkrun inside try");
            llOwnerSay ("generate second call to checkrun inside try");
            llOwnerSay ("throwing up");
            throw "some exception";
        } catch (string ex) {
            integer i = llSubStringIndex (ex, "\n");
            if (i > 0) ex = llGetSubString (ex, 0, i - 1);
            llOwnerSay ("caught " + ex);
        }

        llOwnerSay ("all done");
    }
}
