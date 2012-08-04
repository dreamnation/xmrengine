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

constant c0 = 99;
constant c1 = Klass.c2 + 12;

integer _globalProperty;
integer globalProperty { get { return _globalProperty + 42; } 
                         set { _globalProperty = value; }
                       }

class KlassOne : Klass, Printable {
    public integer k1Prop { get { return 2992; } 
                            set { SaySomething ("please don't set k1Prop = " + value); }
                          }

    public override Print () : Printable.Print
    {
        SaySomething ("KlassOne.Printable");
        this.k1Prop = 2999;
    }
    public override string ToString () : Printable.ToString
    {
        return "zhis is KlassOne viss k1Prop=" + this.k1Prop;
    }
}

class Klass : Printable {
    public constant c2 = c3 + 34;
    public constant c6 = c0 + c1 + c2 + c3 + c4 + c5;
    public constant c4 = 56;
    public constant c5 = c0 + 98;

    public integer x;
    public static integer y;

    public constructor ()
    {
        this.x = 99;
    }

    public static SayIt (string x)
    {
        SaySomething ("SayIt: " + x);
    }

    public virtual Print () : Printable
    {
        SayIt ("this.x=" + this.x);
        SayIt ("Klass.y=" + Klass.y);
    }
    public PrintTwice ()
    {
        this.Print ();
        this.Print ();
    }
    public virtual string ToString () : Printable
    {
        return "zhis is Klass";
    }

    public integer == (Klass that)
    {
        return this.x == that.x;
    }
    public integer -= (integer that)
    {
        return this.x -= that;
    }
}

interface Printable {
    Print ();
    string ToString ();
}

constant c3 = 45 + Klass.c4;

default
{
    state_entry()
    {
        SaySomething ("c0 = " + c0);
        SaySomething ("c1 = " + c1);
        SaySomething ("c2 = " + Klass.c2);
        SaySomething ("c3 = " + c3);
        SaySomething ("c4 = " + Klass.c4);
        SaySomething ("c5 = " + Klass.c5);
        SaySomething ("c6 = " + Klass.c6);

        globalProperty = 12345;
        SaySomething ("globalProperty = " + globalProperty);

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

        Klass k99 = new Klass ();
        Klass k55 = new Klass ();
        SaySomething ("two different Klass both with 99: " + (k99 == k55));
        k55.x -= 44;
        SaySomething ("k55.x = " + k55.x);
        SaySomething ("k99.x = " + k99.x);
        SaySomething ("now the values are different: " + (k99 == k55));

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
        SaySomething ("xmrHashCode([1,2]) = " + xmrHashCode ([1,2]));
        SaySomething ("xmrHashCode(Hello World) = " + xmrHashCode ("Hello World"));
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

SaySomething(string msg)
{
    llOwnerSay(msg);
}
