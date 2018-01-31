xmroption advflowctl;
xmrOption arrayS;
xmroption norighttoleft;
xmroption objects;
xmroption trycatch;
xmrOPtion expIRYdays 5;

/*/ an idiot way to make a comment /*/

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
            default: llOwnerSay ("something big"); break;
        }
        switch ((string)i) {
            case "1": llOwnerSay ("One"); break;
            case "2" ... "3": llOwnerSay ("Two Or Tree"); break;
            case "4": llOwnerSay ("Four"); break;
            case "5": llOwnerSay ("Fife"); break;
            default: llOwnerSay ("Something Big"); return i;
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

        for (integer i = 0; i <= 16; i ++) {
            string x = (string)i;
            switch (x) {
                case  "1": llOwnerSay ("one"); break;
                case  "2": llOwnerSay ("two"); break;
                case  "3": llOwnerSay ("tree"); break;
                case  "4": llOwnerSay ("four"); break;
                case  "5": llOwnerSay ("fife"); break;
                case  "6": llOwnerSay ("six"); break;
                case  "7": llOwnerSay ("seven"); break;
                case  "8": llOwnerSay ("eight"); break;
                case  "9": llOwnerSay ("niner"); break;
                case "10": llOwnerSay ("ten"); break;
                case "11": llOwnerSay ("eleven"); break;
                case "12": llOwnerSay ("twelve"); break;
                case "13": llOwnerSay ("thirteen"); break;
                case "14": llOwnerSay ("fourteen"); break;
                case "15": llOwnerSay ("fifteen"); break;
                default: llOwnerSay ((string)x); break;
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
