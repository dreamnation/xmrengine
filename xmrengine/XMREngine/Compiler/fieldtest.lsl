default {
    state_entry ()
    {
        vector v = <1,2,3>;
        llOwnerSay ("v=" + v);
        llOwnerSay ("x=" + v.x + " y=" + v.y + " z=" + v.z);
        v.y = 9;
        llOwnerSay ("x=" + v.x + " y=" + v.y + " z=" + v.z);
    }
}
