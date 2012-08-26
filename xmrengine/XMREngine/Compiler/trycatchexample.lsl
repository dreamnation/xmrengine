
xmroption arrays;
xmroption trycatch;

default {
    state_entry ()
    {
        try {
            CallSomethingThatThrows ("try to catch this!");
        } finally {
            llOwnerSay ("finished one way or another");
        } catch (exception ex) {
            PrintOutException (ex);
        }

        try {
            llOwnerSay (((array)(object)undef).count);
        } catch (exception ex) {
            PrintOutException (ex);
        }
    }
}

CallSomethingThatThrows (string s)
{
    llOwnerSay ("say something so we don't get inlined");
    throw s;
}

PrintOutException (exception ex)
{
    llOwnerSay ("   typename: " + xmrExceptionTypeName (ex));
    llOwnerSay ("    message: " + xmrExceptionMessage (ex));
    try {
        object tv = xmrExceptionThrownValue (ex);
        llOwnerSay ("thrownvalue: " + tv);
    } catch (exception ex2) {
        if (xmrExceptionTypeName (ex2) != "InvalidCastException") throw;
    }
    llOwnerSay (" stacktrace:\n" + xmrExceptionStackTrace (ex));
}
