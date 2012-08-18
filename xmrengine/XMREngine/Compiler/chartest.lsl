xmroption advflowctl;
xmroption chars;
xmroption objects;

char gblchar;

default {
    state_entry ()
    {
        char varchar;
        varchar = 'c';
        llOwnerSay ("varchar=" + varchar);
        varchar += 5;
        llOwnerSay ("varchar=" + varchar);

        char[] icat = new char[] { 'I', 'c', 'a', 't' };
        string line = "";
        for (integer i = 0; i < icat.Length; i ++) {
            char ch = icat[i];
            switch (ch) {
                case 2 + (integer)'a': {
                    line += " see";
                    break;
                }
                case 't': {
                    line += " cat";
                    break;
                }
                default: {
                    line += " " + ch;
                    break;
                }
            }
        }
        llOwnerSay (line);
        llOwnerSay (xmrChars2String (icat, 0, 4));
        char[] outchars = new char[] (32);
        xmrString2Chars ("the cat is fat", 0, outchars, 3, 14);
        string outstr = xmrChars2String (outchars, 3, 14);
        llOwnerSay (outstr);
        for (integer i = 0; i < 14; i ++) {
            llOwnerSay ("outstr[" + i + "]=" + outstr[i]);
        }
        llOwnerSay ("c > a ? " + (string)('c' > (char)0x61));
        llOwnerSay ("c < a ? " + (string)('c' < 'a'));
    }
}
