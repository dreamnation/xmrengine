default {
    state_entry ()
    {
        if ((key)"00000000-0000-0000-0000-000000000000") {
            llOwnerSay ("const not null");
        } else {
            llOwnerSay ("const is null");
        }

        list strs = [ "", "00000000-0000-0000-0000-000000000000", "12345678-1234-1234-1234-123456789abc" ];

        for (integer i = 0; i < 3; i ++) {
            string s = strs[i];
            llOwnerSay ("trying <" + s + ">");

            if (s) {
                llOwnerSay ("string var not null");
            } else {
                llOwnerSay ("string var is null");
            }

            key k = s;
            if (k) {
                llOwnerSay ("key var not null");
            } else {
                llOwnerSay ("key var is null");
            }

            if ((key)s) {
                llOwnerSay ("string var cast to key not null");
            } else {
                llOwnerSay ("string var cast to key is null");
            }

            if (llGetOwnerKey (s)) {
                llOwnerSay ("llGetOwnerKey not null");
            } else {
                llOwnerSay ("llGetOwnerKey is null");
            }
        }
    }
}
