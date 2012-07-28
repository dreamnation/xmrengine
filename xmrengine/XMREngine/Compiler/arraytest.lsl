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

delegate void DUMPARRAY(integer shouldHave);
delegate void VERIFY(array,list s,string);

array zz;
DUMPARRAY da;

constant c0 = 99;
constant c1 = Klass.c2 + 12;

class KlassOne : Klass, Printable {
    void Print () : Printable.Print
    {
        SaySomething ("KlassOne.Printable");
    }
    override string ToString () : Printable.ToString
    {
        return "zhis is KlassOne";
    }
}

class Klass : Printable {
    constant c2 = c3 + 34;
    constant c6 = c0 + c1 + c2 + c3 + c4 + c5;
    constant c4 = 56;
    constant c5 = c0 + 98;

    integer x;
    static integer y;

    constructor ()
    {
        this.x = 99;
    }

    static void SayIt (string x)
    {
        SaySomething ("SayIt: " + x);
    }

    void Print () : Printable
    {
        SayIt ("this.x=" + this.x);
        SayIt ("Klass.y=" + Klass.y);
    }
    PrintTwice ()
    {
        this.Print ();
        this.Print ();
    }
    virtual string ToString () : Printable
    {
        return "zhis is Klass";
    }
}

interface Printable {
    Print ();
    string ToString ();
}

constant c3 = 45 + Klass.c4;

default
{
    touch_start(integer num)
    {
        SaySomething ("c0 = " + c0);
        SaySomething ("c1 = " + c1);
        SaySomething ("c2 = " + Klass.c2);
        SaySomething ("c3 = " + c3);
        SaySomething ("c4 = " + Klass.c4);
        SaySomething ("c5 = " + Klass.c5);
        SaySomething ("c6 = " + Klass.c6);

        VERIFY ver = Verify;
        AwfulSig(ZERO_ROTATION, ZERO_VECTOR, "", 0, []);

        Klass k = new Klass ();
        k.PrintTwice ();
        SaySomething ("k string " + k.ToString ());

        KlassOne k1 = new KlassOne ();
        k1.PrintTwice ();
        SaySomething ("k1 string " + k1.ToString ());

        Printable pr = k;
        pr.Print();
        SaySomething ("printable k string " + pr.ToString ());

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
