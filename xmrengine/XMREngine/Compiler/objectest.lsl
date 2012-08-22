xmroption advflowctl;
xmrOption arrayS;
xmroption objects;
xmroption trycatch;
xmrOPtion expIRYdays 5;

xmroption include "dictionary.lsl";

integer _gblPropTrivial;
integer gblPropTrivial { get { return _gblPropTrivial; } set { _gblPropTrivial = value; } }
integer _gblPropCheckRun;
integer gblPropCheckRun { get { llOwnerSay ("gblPropCheckRun: getting " + _gblPropCheckRun); return _gblPropCheckRun; }
                          set { llOwnerSay ("gblPropCheckRun: setting " + value); _gblPropCheckRun = value; } }

float[,] MatMul (float[,] x, float[,] y)
{
    integer jMax = x.Length (0);  // rows of X
    integer kMax = x.Length (1);  // columns of X = rows of Y
    if (y.Length (0) != kMax) throw "size mismatch";
    integer iMax = y.Length (1);  // columns of Y

    float[,] z = new float[,](jMax,iMax);

    for (integer j = 0; j < jMax; j ++) {          // select row of Z to compute
        for (integer i = 0; i < iMax; i ++) {      // select column in that row
            float s = 0;
            for (integer k = 0; k < kMax; k ++) {
                s += x[j,k] * y[k,i];              // row J of X dot col I of Y
            }
            z[j,i] = s;
        }
    }
    return z;
}

integer nontriv = NonTrivInit ();

integer NonTrivInit ()
{
    llOwnerSay ("test non-trivial global init");
    return 99;
}

SaySomething(string s)
{
    llOwnerSay (s);
}

default {
    state_entry ()
    {
        llOwnerSay ("nontriv 99 = " + nontriv);
        Kunta.List<string> stuff = new Kunta.List<string> ();
        llOwnerSay ("typeof(stuff) = " + xmrTypeName (stuff));
        stuff.Enqueue ("abcdef");
        stuff.Enqueue (1);
        stuff.Enqueue ((string)[2,3,4]);
        stuff.Enqueue (<5,6,7>);
        llOwnerSay ("count=" + stuff.Count);
        integer first = 1;
        for (Kunta.IEnumerator<string> stuffenum = stuff.GetEnumerator (); stuffenum.MoveNext ();) {
            if (first) llOwnerSay ("typeof (stuffenum) = " + xmrTypeName (stuffenum));
            llOwnerSay ("element=(" + xmrTypeName (stuffenum.GetCurrent ()) + ") " + stuffenum.GetCurrent ());
            first = 0;
        }

        gblPropTrivial = 987;
        llOwnerSay ("gblPropTrivial=" + gblPropTrivial);
        gblPropCheckRun = 789;
        gblPropCheckRun ++;
        SaySomething ("gblPropCheckRun=" + gblPropCheckRun);

        typedef Kunta.Dictionary<string,integer> MyDict;
        typedef Kunta.KeyValuePair KVP;
        typedef Kunta.ICountable<KVP<string,integer>> MyCountable;

        MyDict s2i = new MyDict (23);
        s2i.Add ("one", 1);
        s2i.Add ("two", 2);
        s2i.Add ("three", 3);
        MyCountable s2iCountable = (MyCountable) s2i;
        for (Kunta.IEnumerator<KVP<string,integer>> kvpenum = s2iCountable.GetEnumerator (); kvpenum.MoveNext ();) {
            KVP<string,integer> kvp = kvpenum.GetCurrent ();
            llOwnerSay ("s2i: " + kvp.kee + " => " + kvp.value);
        }
        for (Kunta.IEnumerator<string> keyenum = s2i.Keys.GetEnumerator (); keyenum.MoveNext ();) {
            llOwnerSay ("s2i.key = " + keyenum.GetCurrent ());
        }
        for (Kunta.IEnumerator<integer> valenum = s2i.Values.GetEnumerator (); valenum.MoveNext ();) {
            llOwnerSay ("s2i.value = " + valenum.GetCurrent ());
        }

        float[,] x = new float[,](2,3);  // 2 rows, 3 columns
        float[,] y = new float[,](3,4);  // 3 rows, 4 columns
        llOwnerSay ("x.Length = " + x.Length);
        llOwnerSay ("y.Length = " + y.Length);
        for (integer i = 0; i < 2; i ++) {
            for (integer j = 0; j < 3; j ++) {
                x[i,j] = i + j + 1;
            }
        }
        for (integer i = 0; i < 3; i ++) {
            for (integer j = 0; j < 4; j ++) {
                y[i,j] = i + j + 2;
            }
        }
        float[,] z = MatMul (x, y);
        for (integer i = 0; i < 2; i ++) {
            string line = "";
            for (integer j = 0; j < 4; j ++) {
                line += "  ";
                line += (string)z[i,j];
            }
            string expect;
            if (i == 0) {
                expect = "  20.000000  26.000000  32.000000  38.000000";
            } else {
                expect = "  29.000000  38.000000  47.000000  56.000000";
            }
            if (line == expect) line += "  -- good";
                           else line += "  -- BAD";
            llOwnerSay (line);
        }

        string[,][] jagged = new string[,][] (3,4);
        llOwnerSay ("typeof jagged = " + xmrTypeName (jagged));
        for (integer i = 0; i < 3; i ++) {
            for (integer j = 0; j < 4; j ++) {
                jagged[i,j] = new string[] (4+i+j);
                for (integer k = 0; k < 4 + i + j; k ++) {
                    jagged[i,j][k] = i + "," + j + "," + k;
                }
            }
        }
        for (integer i = 0; i < 3; i ++) {
            for (integer j = 0; j < 4; j ++) {
                integer len = jagged[i,j].Length;
                string msg = "jagged[" + i + "," + j + "].Length=" + jagged[i,j].Length;
                for (integer k = 0; k < len; k ++) {
                    msg += " : " + jagged[i,j][k];
                }
                llOwnerSay (msg);
            }
        }

        llOwnerSay ("doing array copy:");
        xmrArrayCopy ((object)jagged, jagged.Index(1,0), (object)jagged, jagged.Index(0,0), 4);
        for (integer i = 0; i < 3; i ++) {
            for (integer j = 0; j < 4; j ++) {
                integer len = jagged[i,j].Length;
                string msg = "jagged[" + i + "," + j + "].Length=" + jagged[i,j].Length;
                for (integer k = 0; k < len; k ++) {
                    msg += " : " + jagged[i,j][k];
                }
                llOwnerSay (msg);
            }
        }

        llOwnerSay ("jagged array initializing");
        integer[,][] jagint = new integer[,][] 
            { { { 5,6,7 },, { 1,2 } }, 
              { , { 9 }, { 8,9 }, { 3,4,5,6 }, } };

        for (integer i = 0; i < jagint.Length (0); i ++) {
            string line = "        ";
            if (i == 0) line += "{ {";
                   else line += "  {";
            for (integer j = 0; j < jagint.Length (1); j ++) {
                if (j > 0) line += ",";
                integer[] jagintel = jagint[i,j];
                if (jagintel == undef) {
                    line += " undef";
                } else {
                    line += " { ";
                    for (integer k = 0; k < jagintel.Length (0); k ++) {
                        if (k > 0) line += ",";
                        line += (string)jagintel[k];
                    }
                    line += " }";
                }
            }
            line += " }";
            if (i + 1 == jagint.Length (0)) line += " }";
                                       else line += ",";
            llOwnerSay (line);
        }

        for (integer i = 0; i < jagint.Length (0); i ++) {
            for (integer j = 0; j < jagint.Length (1); j ++) {
                integer[] jagintel = jagint[i,j];
                if (jagintel == undef) continue;
                integer numel = jagintel.Length (0);
                integer skip = 0;
                if (numel > 2) skip = numel - 2;
                list lis = xmrArray2List (jagintel, skip, numel - skip);
                string line = "   [" + i + "," + j + "]=" + (string)lis + " => ";
                string[] copy = new string[] (numel);
                xmrList2Array (lis, 0, copy, skip, numel - skip);
                for (integer k = 0; k < numel; k ++) {
                    if (k > 0) line += ",";
                    line += (string)copy[k];
                }
                llOwnerSay (line);
            }
        }

        Vase vase = new Vase ();
        llOwnerSay (vase.Meth0 (2999));        // Vase.Meth0: 2999
        llOwnerSay (vase.Meth0 ("kaybek"));    // Vase.Meth0: kaybek

        VaseOver vaseover = new VaseOver ();
        llOwnerSay (vaseover.Meth0 (2995));       // VaseOver.Meth0: 2995
        llOwnerSay (vaseover.Meth0 ("whiskey"));  // VaseOvwe.Meth0: whiskey

        Vase vaseovervase = vaseover;
        llOwnerSay (xmrTypeName (vaseovervase) + "=" + vaseovervase.Meth0 (2993));      // VaseOver=VaseOver.Meth0: 2003
        llOwnerSay (xmrTypeName (vaseovervase) + "=" + vaseovervase.Meth0 ("victor"));  // VaseOver=Vase.Meth0: victor

        IFace1 vaseoveriface1 = (Vase)vaseover;
        llOwnerSay (xmrTypeName (vaseoveriface1) + "=" + vaseoveriface1.IFace1A (42));           // VaseOver=VaseOver.Meth0: 42
        llOwnerSay (xmrTypeName (vaseoveriface1) + "=" + vaseoveriface1.IFace1B ("farenheit"));  // VaseOver=Vase.Meth0: farenheit

        IFace2 vaseoveriface2 = vaseover;
        llOwnerSay (xmrTypeName (vaseoveriface2) + "+" + vaseoveriface2.IFace2A (11, 45));                   // VaseOver=VaseOver.IFace2A: 1145
        llOwnerSay (xmrTypeName (vaseoveriface2) + "+" + vaseoveriface2.IFace2B ("eleven-", "forty-five"));  // VaseOver=VaseOver.IFace2B: eleven-forty-five
    }
}

interface IFace1 {
    string IFace1A (integer i);
    string IFace1B (string s);
}

interface IFace2 {
    string IFace2A (integer i, integer j);
    string IFace2B (string s, string t);
}

class Vase : IFace1 {
    private static string vasemeth0 = "Vase.Meth0: ";
    public virtual string Meth0 (integer i) : IFace1.IFace1A { return vasemeth0 + i; }
    public virtual string Meth0 (string  s) : IFace1.IFace1B { return vasemeth0 + s; }
}

class VaseOver : Vase, IFace2 {
    private constant vaseovermeth0 = "VaseOver.Meth0: ";
    public override string Meth0 (integer i) { return vaseovermeth0 + i; }
    public new virtual string Meth0 (string s) { return vaseovermeth0 + s; }
    private string IFace2A (integer i, integer j) : IFace2 { return "VaseOver.IFace2A: " + i + j; }
    private string IFace2B (string  s, string  t) : IFace2 { return "VaseOver.IFace2B: " + s + t; }
}

