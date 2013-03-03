default {
    state_entry ()
    {
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
