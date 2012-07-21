xmrOption arrayS;
xmroption objects;

array zz;

default
{
    touch_start(integer num)
    {
        delegate void(integer) da;
        delegate void(array,list,string) ver = Verify;

        da = DumpArray;

        llSay(0, "existing array:");
        da(-1);
        zz.clear();
        llSay(0, "arraytest");
        zz[0] = "the cat is fat";
        zz["pig"] = "dog";
        da(2);
        zz["rat"] = "snake";
        da(3);
        zz[0] = undef;
        da(2);
        zz["pig"] = "schwein";
        da(2);
        zz[0,1] = "zero,one";
        zz[1,2] = "one,two";
        zz["two",3] = "t.w.o,three";
        zz[4,5,6,7] = "four,five,six,seven";
        da(6);
        ver(zz, [0,1], "zero,one");
        ver(zz, [1,2], "one,two");
        ver(zz, ["two",3], "t.w.o,three");
        ver(zz, [4,5,6,7], "four,five,six,seven");
        llSay(0, "success!");
    }
}

DumpArray(integer shouldhave)
{
    integer n = zz.count;
    llSay(0, "count=" + n);
    object l;
    object v;
    foreach (l,v in zz) {
        llSay(0, "l=" + (string)l + ", v=" + (string)v);
    }
    for (integer i = 0; i < n; i ++) {
        l = zz.index(i);
        v = zz.value(i);
        llSay(0, (string)i + ": zz[" + (string)l + "]=" + (string)v);
    }
    if ((shouldhave >= 0) && (n != shouldhave)) state error;
}

Verify(array a, list s, string expect)
{
    string actual = (string)a[s];
    llSay(0, "zz[" + (string)s +  "]=" + actual);
    if (actual != expect) {
        llSay(0, "...but expect=" + expect);
        state error;
    }
}
