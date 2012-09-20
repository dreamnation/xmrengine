xmroption advflowctl;
xmrOption arrayS;
xmroption objects;
xmroption trycatch;
xmrOPtion expIRYdays 5;

integer SomethingToCall ()
{
    integer i = 0;
    while (1) {
        llOwnerSay ("something " + (++ i));
        switch (i) {
            case 1: llOwnerSay ("one"); break;
            case 2 ... 3: llOwnerSay ("two or tree"); break;
            case 4: llOwnerSay ("four"); break;
            case 5: llOwnerSay ("fife"); break;
            default: llOwnerSay ("something big"); return i;
        }
    }
}

default {
    state_entry ()
    {
        llOwnerSay ("done " + SomethingToCall ());

        for (integer i = 0; i < 2; i ++) {
            for (integer j = 0; j < 2; j ++) {
                for (integer m = 0; m < 2; m ++) {
                    for (integer n = 0; n < 2; n ++) {
                        llOwnerSay ((string)i + " &&& " + (string)j + " ||| " + (string)m + " &&& " + (string)n + " = " + SSTest (i,j,m,n));
                    }
                }
            }
        }
    }
}

integer SSTest (integer i, integer j, integer m, integer n)
{
    return SSPrint ("i", i) &&& SSPrint ("j", j) ||| SSPrint ("m", m) &&& SSPrint ("n", n);
}

integer SSPrint (string name, integer x)
{
    llOwnerSay (name + "=" + x);
    return x;
}
