default {
    touch_start (integer n)
    {
        llOwnerSay (xmrTypeName (COMMSTEST_F1) + " COMMSTEST_F1 = " + (string)COMMSTEST_F1);
        llOwnerSay (xmrTypeName (COMMSTEST_F2) + " COMMSTEST_F2 = " + (string)COMMSTEST_F2);
        llOwnerSay (xmrTypeName (COMMSTEST_F3) + " COMMSTEST_F3 = " + (string)COMMSTEST_F3);
        llOwnerSay (xmrTypeName (COMMSTEST_I1) + " COMMSTEST_I1 = " + (string)COMMSTEST_I1);
        llOwnerSay (xmrTypeName (COMMSTEST_I2) + " COMMSTEST_I2 = " + (string)COMMSTEST_I2);
        llOwnerSay (xmrTypeName (COMMSTEST_K1) + " COMMSTEST_K1 = " + (string)COMMSTEST_K1);
        llOwnerSay (xmrTypeName (COMMSTEST_R1) + " COMMSTEST_R1 = " + (string)COMMSTEST_R1);
        llOwnerSay (xmrTypeName (COMMSTEST_R2) + " COMMSTEST_R2 = " + (string)COMMSTEST_R2);
        llOwnerSay (xmrTypeName (COMMSTEST_S1) + " COMMSTEST_S1 = " + (string)COMMSTEST_S1);
        llOwnerSay (xmrTypeName (COMMSTEST_S2) + " COMMSTEST_S2 = " + (string)COMMSTEST_S2);
        llOwnerSay (xmrTypeName (COMMSTEST_V1) + " COMMSTEST_V1 = " + (string)COMMSTEST_V1);
        llOwnerSay (xmrTypeName (COMMSTEST_V2) + " COMMSTEST_V2 = " + (string)COMMSTEST_V2);

        commsTestVoidPart1 (1.5, 47);
        commsTestVoidPart2 (
            (key)"12345678-abcd-4321-8765-123456789abc",
            [ 1.6, 48, (key)"11111111-abcd-4321-8765-123456789ccc", <5,4,3,2>, <8,7,6> ]
        );
        commsTestVoidPart3 (<1,2,3,4>, "try passing this back");
        commsTestVoidPart4 (<5,6,7>);

        float    flt = commsTestFloat ();
        llOwnerSay ((string)flt);

        integer  itr = commsTestInteger ();
        llOwnerSay ((string)itr);

        key      kee = commsTestKey ();
        llOwnerSay ((string)kee);

        list     lis = commsTestList ();
        llOwnerSay (llList2CSV (lis));

        rotation rot = commsTestRotation ();
        llOwnerSay ((string)rot);

        string   str = commsTestString ();
        llOwnerSay (str);

        vector   vec = commsTestVector ();
        llOwnerSay ((string)vec);
    }
}
