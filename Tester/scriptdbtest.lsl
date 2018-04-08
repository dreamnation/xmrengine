
xmroption advflowctl;
xmroption arrays;

default
{
    state_entry()
    {
        object k;
        object v;
        integer ln = llGetLinkNumber ();
        llSay(0, "Script running");
        
        xmrScriptDBWrite (ln + ".one", "won value");
        xmrScriptDBWrite (ln + ".two", llGetDate ());
        llSay (0, xmrScriptDBReadOne (ln + ".one", "**notfound**"));
        
        llSay (0, "count=" + xmrScriptDBCount ("%"));
        list keys = xmrScriptDBList ("%", 100, 0);
        for (integer i = 0; i < llGetListLength (keys); i ++) {
            llSay (0, i + ": " + keys[i]);
        }
        array all = xmrScriptDBReadMany ("%", 100, 0);
        foreach (k, v in all) {
            llSay (0, k + ": " + v);
        }
        
        llSay (0, ln + ".count=" + xmrScriptDBCount (ln + ".%"));
        list keys = xmrScriptDBList (ln + ".%", 100, 0);
        for (integer i = 0; i < llGetListLength (keys); i ++) {
            llSay (0, i + ": " + keys[i]);
        }
        array all = xmrScriptDBReadMany (ln + ".%", 100, 0);
        foreach (k, v in all) {
            llSay (0, k + ": " + v);
        }

        list writelines = [ "first line", "second line", "third line", "fourth line", "fifth line", "last line" ];
        xmrScriptDBWriteLines ("nc", writelines);
        llOwnerSay (xmrScriptDBReadOne ("nc", "**notfound**"));
        integer nlines = xmrScriptDBNumLines ("nc");
        llOwnerSay ("nlines=" + nlines);
        for (integer i = 0; i < nlines; i ++) {
            llOwnerSay ("  " + i + ":" + xmrScriptDBReadLine ("nc", i, "**notfound**", "**endoffile**"));
        }
        list readlines = xmrScriptDBReadLines ("nc", [ "**notfound**" ]);
        for (integer i = 0; i < llGetListLength (readlines); i ++) {
            llOwnerSay ("  [" + i + "] " + readlines[i]);
        }
    }
}
