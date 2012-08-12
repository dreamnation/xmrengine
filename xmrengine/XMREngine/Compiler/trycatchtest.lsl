xmroption trycatch;

default {
    state_entry ()
    {
        integer x = 0;
        try {
            x = 1;
            llOwnerSay ("maybe wipe x out");
            llOwnerSay ("after CheckRun() in try block");
            jump done;
        } finally {
            llOwnerSay ("finally x=" + x);
            llOwnerSay ("after CheckRun() in finally block");
        }
    @done;
        llOwnerSay ("all done");
    }
}
