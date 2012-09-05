xmroption advflowctl;
xmroption arrays;
xmroption objects;
xmroption trycatch;

CallSomethingThatCallsSomethingThatThrows (string msg)
{
    llOwnerSay ("CallSomethingThatCallsSomethingThatThrows or we get optimized out");
    CallSomethingThatThrows (msg);
}

CallSomethingThatThrows (string msg)
{
    llOwnerSay ("CallSomethingThatThrows or we get optimized out");
    throw msg;
}

default {
    state_entry ()
    {
        integer x = 0;

        try {
            x = 1;
            llOwnerSay ("just set x = 1");
            llOwnerSay ("after CheckRun() in try block");
            throw "get out of here";
        } catch (exception s) {
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
        } catch (exception s) {
            llOwnerSay ("inside first catch { } with " + xmrExceptionMessage (s));
            throw;
        } finally {
            llOwnerSay ("inside first finally { }");
        } catch (exception s) {
            llOwnerSay ("inside second catch { } with " + xmrExceptionMessage (s));
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
        } catch (exception ex) {
            llOwnerSay ("caught " + xmrExceptionMessage (ex));
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
                    CallSomethingThatCallsSomethingThatThrows ("get me out!");
                } catch (exception ex) {
                    llOwnerSay ("inner catch: " + xmrExceptionMessage (ex));
                    llOwnerSay ("stack trace: " + xmrExceptionStackTrace (ex));
                    throw;
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
        } catch (exception imout) {
            llOwnerSay ("multiple throw finallies 4a");
            llOwnerSay ("multiple throw finallies 4b");
        }

        // ScriptRestoreCatchException test
        llOwnerSay ("check stack trace capture/restore");
        try {
            throw "can you see me now?";
        } catch (exception ex) {
            llOwnerSay ("    argtype: " + xmrTypeName (ex));
            llOwnerSay ("     extype: " + xmrExceptionTypeName (ex));
            llOwnerSay ("whole thing: " + (string)ex);
            llOwnerSay ("    message: " + xmrExceptionMessage (ex));
            llOwnerSay ("stack trace: " + xmrExceptionStackTrace (ex));
        }

        // Filter the type
        llOwnerSay ("catch filtering...");
        list values = [ 1, "two", 3.0, <4,4,4>, <5,5,5,5> ];
        for (integer i = 0; i < 5; i ++) {
            try {
                throw values[i];
            } catch (exception ex) {
                if (!(xmrExceptionThrownValue (ex) is integer)) throw;
                llOwnerSay ("caught integer " + (integer)xmrExceptionThrownValue (ex));
            } catch (exception ex) {
                if (!(xmrExceptionThrownValue (ex) is string)) throw;
                llOwnerSay ("caught string " + (string)xmrExceptionThrownValue (ex));
            } catch (exception ex) {
                if (!(xmrExceptionThrownValue (ex) is float)) throw;
                llOwnerSay ("caught float " + (float)xmrExceptionThrownValue (ex));
            } catch (exception ex) {
                if (!(xmrExceptionThrownValue (ex) is vector)) throw;
                llOwnerSay ("caught vector " + (vector)xmrExceptionThrownValue (ex));
            } catch (exception ex) {
                llOwnerSay ("caught unknown " + xmrTypeName (xmrExceptionThrownValue (ex)) + " " + xmrExceptionThrownValue (ex));
            }
        }

        Vase vase = new VaseOne ();
        try {
            throw vase;
        } catch (exception ex) {
            if (!(xmrExceptionThrownValue (ex) is Vase)) throw;
            Vase v = (Vase)xmrExceptionThrownValue (ex);
            llOwnerSay ("* caught vase " + v.ToString ());
        } catch (exception ex) {
            if (!(xmrExceptionThrownValue (ex) is VaseOne)) throw;
            VaseOne v1 = (VaseOne)xmrExceptionThrownValue (ex);
            llOwnerSay ("  caught vaseone " + v1.ToString ());
        }
        try {
            throw vase;
        } catch (exception ex) {
            if (!(xmrExceptionThrownValue (ex) is VaseOne)) throw;
            VaseOne v1 = (VaseOne)xmrExceptionThrownValue (ex);
            llOwnerSay ("* caught vaseone " + v1.ToString ());
        } catch (exception ex) {
            if (!(xmrExceptionThrownValue (ex) is Vase)) throw;
            Vase v = (Vase)xmrExceptionThrownValue (ex);
            llOwnerSay ("  caught vase " + v.ToString ());
        }

       for (integer z = 0; z < 10; z ++) {
            try {
                if (z & 1) continue;
                if (z > 7) break;
            } finally {
                llOwnerSay ("finally " + z);
            }
            llOwnerSay ("normal " + z);
            try {
                llOwnerSay ((string)(1000 / z));
                continue;
            } catch (exception e) {
                llOwnerSay (xmrExceptionTypeName (e));
            }
            llOwnerSay ("next...");
        }

        llOwnerSay ("all done");
    }
}

class Vase {
    public virtual string ToString ()
    {
        return "this is a Vase";
    }
}
class VaseOne : Vase {
    public override string ToString ()
    {
        return "this is a VaseOne";
    }
}
