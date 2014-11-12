// make sure everything that gets added to a list
// gets added as the lsl-wrapped type not the
// unwrapped system type

// test using llListSort() as llListSort() only
// sorts lsl-wrapped types

default {
    state_entry ()
    {
        list after;
        list before;

        before  = (list)1;
        before += [ 3 ];
        before += 0;
        before += 4;
        before += (list)2;
        before += [ 7 ];
        after   = llListSort (before, 2, TRUE);
        llOwnerSay ("before=" + llList2CSV (before));
        llOwnerSay (" after=" + llList2CSV (after));

        before  = (list)1.0;
        before += 3.0;
        before += [ 0.0 ];
        before += [ 4.0 ];
        before += (list)2.0;
        before += 7.0;
        after   = llListSort (before, 2, TRUE);
        llOwnerSay ("before=" + llList2CSV (before));
        llOwnerSay (" after=" + llList2CSV (after));

        before  = "1 one";
        before += (list)"3 three";
        before += (list)"0 zero";
        before += (list)"4 four";
        before += [ "2 two" ];
        before += (list)"7 seven";
        after   = llListSort (before, 2, TRUE);
        llOwnerSay ("before=" + llList2CSV (before));
        llOwnerSay (" after=" + llList2CSV (after));

        // also check bool->list type conversion
        list flist = (list)(1 == 2);
        llOwnerSay ("flist=" + flist);
        list tlist = (list)(2 == 2);
        llOwnerSay ("tlist=" + tlist);
    }
}
