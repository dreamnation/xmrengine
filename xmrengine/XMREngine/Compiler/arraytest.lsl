xmrOption arrayS;
xmroption objects;

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

delegate void DUMPARRAY(integer shouldHave);
delegate void VERIFY(array,list s,string);

array zz;
DUMPARRAY da;

class Klass {
    integer x;
    static integer y;
    void Print ()
    {
        SaySomething ("this.x=" + this.x);
        SaySomething ("Klass.y=" + Klass.y);
    }
    void PrintTwice ()
    {
        this.Print ();
        this.Print ();
    }
}

default
{
    touch_start(integer num)
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
    }
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

SaySomething(string msg)
{
    llSay(0, msg);
}
