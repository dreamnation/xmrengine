xmroption trycatch;

default {
    state_entry ()
    {
        integer x = 0;

        try {
            x = 1;
            llOwnerSay ("just set x = 1");
            llOwnerSay ("after CheckRun() in try block");
            throw "get out of here";
        } catch (string s) {
            llOwnerSay ("catch x=" + x);
            llOwnerSay ("after CheckRun() in catch block");
        }

        try {
            x = 2;
            llOwnerSay ("just set x = 2");
            llOwnerSay ("after CheckRun() in try block");
            jump done1;
        } finally {
            llOwnerSay ("finally x=" + x);
            llOwnerSay ("after CheckRun() in finally block");
        }
    @done1;

        try {
            llOwnerSay ("inside try { }");
            throw "something";
        } catch (string s) {
            llOwnerSay ("inside first catch { } with " + TrimException (s));
            throw;
        } finally {
            llOwnerSay ("inside first finally { }");
        } catch (string s) {
            llOwnerSay ("inside second catch { } with " + TrimException (s));
        } finally {
            llOwnerSay ("inside second finally { }");
        }

        llOwnerSay ("generate first call to checkrun");
        llOwnerSay ("generate second call to checkrun");

        try {
            llOwnerSay ("generate first call to checkrun inside try");
            llOwnerSay ("generate second call to checkrun inside try");
            llOwnerSay ("throwing up");
            throw "some exception";
        } catch (string ex) {
            llOwnerSay ("caught " + TrimException (ex));
        }

        llOwnerSay ("multiple finallies via jump test");
        try {
            try {
                try {
                    llOwnerSay ("multiple jump finallies 1a");
                    llOwnerSay ("multiple jump finallies 1b");
                    jump done2;
                } finally {
                    llOwnerSay ("multiple jump finallies 2a");
                    llOwnerSay ("multiple jump finallies 2b");
                }
                llOwnerSay ("dont see this");
            } finally {
                llOwnerSay ("multiple jump finallies 3a");
                llOwnerSay ("multiple jump finallies 3b");
            }
            llOwnerSay ("dont see this");
        } finally {
            llOwnerSay ("multiple jump finallies 4a");
            llOwnerSay ("multiple jump finallies 4b");
        }
    @done2;

        llOwnerSay ("multiple finallies via throw test");
        try {
            try {
                try {
                    llOwnerSay ("multiple throw finallies 1a");
                    llOwnerSay ("multiple throw finallies 1b");
                    throw "get me out!";
                } finally {
                    llOwnerSay ("multiple throw finallies 2a");
                    llOwnerSay ("multiple throw finallies 2b");
                }
                llOwnerSay ("dont see this");
            } finally {
                llOwnerSay ("multiple throw finallies 3a");
                llOwnerSay ("multiple throw finallies 3b");
            }
            llOwnerSay ("dont see this");
        } catch (string imout) {
            llOwnerSay ("multiple throw finallies 4a");
            llOwnerSay ("multiple throw finallies 4b");
        }
        llOwnerSay ("all done");
    }
}

string TrimException (string ex)
{
    integer i = llSubStringIndex (ex, "\n");
    if (i > 0) ex = llGetSubString (ex, 0, i - 1);
    return ex;
}
