xmroption advflowctl;
xmrOption arrayS;
xmroption objects;
xmrOPtion expIRYdays 5;

DumpArray(integer shouldhave)
{
    integer n = zz.count;
    SaySomething("count=" + n);
    object l;
    object v;
    foreach (l,v in zz) {
        SaySomething("l=" + (string)l + ", v=" + (string)v);
    }
    for (integer i = 0; i < n; i ++) {
        l = zz.index(i);
        v = zz.value(i);
        SaySomething((string)i + ": zz[" + (string)l + "]=" + (string)v);
    }
    if ((shouldhave >= 0) && (n != shouldhave)) state error;
}

delegate DUMPARRAY(integer shouldHave);
delegate VERIFY(array,list s,string);

array zz;
DUMPARRAY da;

default
{
    state_entry()
    {
        VERIFY ver = Verify;
        AwfulSig(ZERO_ROTATION, ZERO_VECTOR, "", 0, []);

        SaySomething("existing array:");
        da(-1);
        zz.clear();
        SaySomething("arraytest");
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
        SaySomething("success!");

        SaySomething ("xmrHashCode(2992)  = " + xmrHashCode (2992));
        SaySomething ("xmrHashCode(29.92) = " + xmrHashCode (29.92) + " = " + xmrHashCode (TwoNinerNinerTwo ()));
        SaySomething ("xmrHashCode(Hello World) = " + xmrHashCode ("Hello World"));

        SaySomething ("test JSON parse:");
        JSONTest ("1234");
        JSONTest ("12.34e-5");
        JSONTest ("{ \"one\": \"first element\", \"two\": \"second element\", \"three\": [ 9, 8, 7 ]}");
    }
}

float TwoNinerNinerTwo ()
{
    return 29.92;
}

AwfulSig(rotation r, vector v, string s, integer i, list l)
{
    da = DumpArray;
}

Verify(array a, list s, string expect)
{
    string actual = (string)a[s];
    SaySomething("zz[" + (string)s +  "]=" + actual);
    if (actual != expect) {
        SaySomething("...but expect=" + expect);
        state error;
    }
}

JSONTest (string json)
{
    array ar;
    object k;
    object v;
    
    SaySomething (json + ":");
    ar = osParseJSON (json);
    foreach (k,v in ar) {
        SaySomething ("  " + (string)k + " = " + (string)v);
    }
}

SaySomething(string msg)
{
    llOwnerSay (msg);
}
