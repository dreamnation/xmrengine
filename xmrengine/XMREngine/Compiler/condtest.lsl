
list somekeys = [ "12345678-1234-1234-1234-123456789abc", "", "00000000-0000-0000-0000-000000000000" ];

default {
    state_entry ()
    {
        llOwnerSay (TRUE ? 1 : 2);
        llOwnerSay (FALSE ? 0.33 : 0.67);

        for (integer tfA = 0; tfA < 2; tfA ++) {
            for (integer tfB = 0; tfB < 2; tfB ++) {
                llOwnerSay (tfA ? 0 : tfB ? 1 : 2);
            }
        }

        for (integer i = 0; i < 3; i ++) {
            key akey = (key)somekeys[i];
            llOwnerSay ("<" + akey + "> " + (akey ? "NOT" : "IS") + " null");
        }
    }
}
