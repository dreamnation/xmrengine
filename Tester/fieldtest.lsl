default {
    state_entry ()
    {
        vector v = <1,2,3>;
        llOwnerSay ("v=" + (string)v);
        llOwnerSay ("x=" + v.x + " y=" + v.y + " z=" + v.z);
        v.y = 9;
        llOwnerSay ("v=" + (string)v);
        llOwnerSay ("x=" + v.x + " y=" + v.y + " z=" + v.z);

        list l1 = [ "a", "b" ];
        l1 = (l1 = []) + l1 + [ "c" ];
        llOwnerSay (llList2CSV (l1));

        list l2 = [ "a", "b" ];
        l2 = (l2 = []) + (l2 + [ "c" ]);
        llOwnerSay (llList2CSV (l2));

        list l3 = [ "a", "b" ];
        l3 = ((l3 = []) + l3) + [ "c" ];
        llOwnerSay (llList2CSV (l3));

        string s1 = "ab";
        s1 = (s1 = "") + s1 + "c";
        llOwnerSay (s1);

        string s2 = "ab";
        s2 = (s2 = "") + (s2 + "c");
        llOwnerSay (s2);

        string s3 = "ab";
        s3 = ((s3 = "") + s3) + "c";
        llOwnerSay (s3);

        integer i1 = 1;
        i1 = (i1 = 0) + i1 + 20;
        llOwnerSay (i1);

        integer i2 = 1;
        i2 = (i2 = 0) + (i2 + 20);
        llOwnerSay (i2);

        integer i3 = 1;
        i3 = ((i3 = 0) + i3) + 20;
        llOwnerSay (i3);
    }
}
