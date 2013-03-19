xmroption advflowctl;
xmroption arrays;
xmroption objects;
xmroption trycatch;
xmroption norighttoleft;

constant c0 = 99;
constant c1 = Klass.c2 + 12;

integer _plusFortyTwo;
integer plusFortyTwo { get { return _plusFortyTwo + 42; } 
                       set { _plusFortyTwo = value; }
                     }

class KlassOne : Klass, Printable {
    public integer k1Prop { get { SaySomething ("it's a sandard day"); return 2992; } 
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

integer _gblPropCheckRun;
integer gblPropCheckRun { get { SaySomething ("gblPropCheckRun: getting " + _gblPropCheckRun); return _gblPropCheckRun; }
                          set { SaySomething ("gblPropCheckRun: setting " + value); _gblPropCheckRun = value; } }

default {
    state_entry ()
    {
        SaySomething ("c0 = " + c0);
        SaySomething ("c1 = " + c1);
        SaySomething ("c2 = " + Klass.c2);
        SaySomething ("c3 = " + c3);
        SaySomething ("c4 = " + Klass.c4);
        SaySomething ("c5 = " + Klass.c5);
        SaySomething ("c6 = " + Klass.c6);

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

        plusFortyTwo = 12345;
        SaySomething ("plusFortyTwo = " + plusFortyTwo);
        plusFortyTwo ++;
        SaySomething ("plusFortyTwo = " + plusFortyTwo);

        SaySomething ("Value is: " + gblPropCheckRun);
        integer kk = 9 + gblPropCheckRun;
        SaySomething ("Nine plus: " + kk);
        SaySomething ("I say: " + ((string)gblPropCheckRun != "0"));
        PrintFourInts (11, ((gblPropCheckRun = 12), gblPropCheckRun), 45, ((gblPropCheckRun = 47), gblPropCheckRun));
    }
}

PrintFourInts (integer one, integer two, integer three, integer four)
{
    SaySomething (one);
    SaySomething (two);
    SaySomething (three);
    SaySomething (four);
}

SaySomething (string what)
{
    llOwnerSay (what);
}
