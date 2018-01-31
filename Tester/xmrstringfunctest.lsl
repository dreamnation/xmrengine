
xmroption trycatch;

default {
    state_entry ()
    {
        string str = "abc,def,ghij,klmno,pqrs";
        llOwnerSay (xmrStringIndexOf (str, ","));
        llOwnerSay (xmrStringIndexOf (str, ",", 4));
        llOwnerSay (xmrStringLastIndexOf (str, ","));
        llOwnerSay (xmrStringLastIndexOf (str, ",", 17));

        llOwnerSay (xmrFloat2String (1.2345, ""));
        llOwnerSay (xmrInteger2String (12345, "X8"));
        llOwnerSay (xmrRotation2String (<1,2,3,4>, "0.00"));
        llOwnerSay (xmrVector2String (<1.1,2.2,3.3>, "0.00"));

        try { llOwnerSay ((string)xmrString2Float ("12.34"));                           } catch (exception e) { llOwnerSay (xmrExceptionMessage (e)); }
        try { llOwnerSay ((string)xmrString2Float ("ab.cd"));                           } catch (exception e) { llOwnerSay (xmrExceptionMessage (e)); }
        try { llOwnerSay ((string)xmrString2Integer ("987"));                           } catch (exception e) { llOwnerSay (xmrExceptionMessage (e)); }
        try { llOwnerSay ((string)xmrString2Integer ("12345678901"));                   } catch (exception e) { llOwnerSay (xmrExceptionMessage (e)); }
        try { llOwnerSay ((string)xmrString2Integer ("  0x1145  "));                    } catch (exception e) { llOwnerSay (xmrExceptionMessage (e)); }
        try { llOwnerSay ((string)xmrString2Rotation (" <  9.9 , 88, -7.7, 0.66 >  ")); } catch (exception e) { llOwnerSay (xmrExceptionMessage (e)); }
        try { llOwnerSay ((string)xmrString2Vector (" <  9.9 , 88, -7.7, 0.66 >  "));   } catch (exception e) { llOwnerSay (xmrExceptionMessage (e)); }
        try { llOwnerSay ((string)xmrString2Vector (" <  9.9 , 88, >  "));              } catch (exception e) { llOwnerSay (xmrExceptionMessage (e)); }
        try { llOwnerSay ((string)xmrString2Vector (" <  9.9 , 88 >  "));               } catch (exception e) { llOwnerSay (xmrExceptionMessage (e)); }
        try { llOwnerSay ((string)xmrString2Vector (" <  0.75, 9.9 , -7.7>  "));        } catch (exception e) { llOwnerSay (xmrExceptionMessage (e)); }
    }
}
