xmroption advflowctl;
xmrOption arrayS;
xmroption objects;
xmroption trycatch;
xmrOPtion expIRYdays 5;

interface IEnumerable<T> {
    IEnumerator<T> GetEnumerator ();
}
interface IEnumerator<T> {
    T Current ();
    integer MoveNext ();
    Reset ();
}

class Dictionary<K,V> : IEnumerable<KeyValuePair<K,V>> {
    public constant HASHSIZE = 23;
    public List<KeyValuePair<K,V>>[] kvpss = InitKVPSS ();

    private List<KeyValuePair<K,V>>[] InitKVPSS ()
    {
        List<KeyValuePair<K,V>>[] it = new List<KeyValuePair<K,V>>[](HASHSIZE);
        return it;
    }

    public KeyValuePair<K,V> Add (K kee, V val)
    {
        if (this.GetByKey (kee) != undef) throw "duplicate key";
        integer index = xmrHashCode (kee) % HASHSIZE;
        if (index < 0) index += HASHSIZE;
        KeyValuePair<K,V> kvp = new KeyValuePair<K,V> ();
        kvp.kee = kee;
        kvp.value = val;
        List<KeyValuePair<K,V>> kvps = this.kvpss[index];
        if (kvps == undef) {
            this.kvpss[index] = kvps = new List<KeyValuePair<K,V>> ();
        }
        kvps.Enqueue (kvp);
        return kvp;
    }

    public KeyValuePair<K,V> GetByKey (K kee)
    {
        integer index = xmrHashCode (kee) % HASHSIZE;
        if (index < 0) index += HASHSIZE;
        List<KeyValuePair<K,V>> kvps = this.kvpss[index];
        if (kvps == undef) return undef;
        for (IEnumerator<KeyValuePair<K,V>> kvpenum = kvps.GetEnumerator (); kvpenum.MoveNext ();) {
            KeyValuePair<K,V> kvp = kvpenum.Current ();
            if (kvp.kee == kee) return kvp;
        }
        return undef;
    }

    // iterate through list of key-value pairs
    public IEnumerator<KeyValuePair<K,V>> GetEnumerator () : IEnumerable<KeyValuePair<K,V>>
    {
        return new Enumerator (this);
    }

    public class Enumerator : IEnumerator<KeyValuePair<K,V>> {
        public Dictionary<K,V> thedict;
        public IEnumerator<KeyValuePair<K,V>> listenum;
        public integer index;

        public constructor (Dictionary<K,V> thedict)
        {
            this.thedict = thedict;
            this.Reset ();
        }

        // get element currently pointed to
        public KeyValuePair<K,V> Current () : IEnumerator<KeyValuePair<K,V>>
        {
            if (this.listenum == undef) throw "at end of list";
            return this.listenum.Current ();
        }

        // move to next element in list
        public integer MoveNext () : IEnumerator<KeyValuePair<K,V>>
        {
            List<KeyValuePair<K,V>> kvps;
            while (1) {
                if (this.listenum == undef) jump done;
                if (this.listenum.MoveNext ()) break;
            @done;
                do {
                    if (this.index >= HASHSIZE) return 0;
                    kvps = this.thedict.kvpss[this.index++];
                } while (kvps == undef);
                this.listenum = kvps.GetEnumerator ();
            }
            return 1;
        }

        // reset back to just before beginning of list
        public Reset () : IEnumerator<KeyValuePair<K,V>>
        {
            this.index    = 0;
            this.listenum = undef;
        }
    }
}

class KeyValuePair<K,V> {
    public K kee;
    public V value;
}

class List<T> : IEnumerable<T> {
    public Enumerator.Node first;
    public Enumerator.Node last;
    public integer count;

    // add to end of list
    public Enqueue (T obj)
    {
        Enumerator.Node node = new Enumerator.Node ();
        node.obj = obj;

        node.next = undef;
        if ((node.prev = last) == undef) {
            first = node;
        } else {
            last.next = node;
        }
        last = node;
        count ++;
    }

    // remove from beginning of list
    public T Dequeue ()
    {
        Enumerator.Node node = first;
        if ((first = node.next) == undef) {
            last = undef;
        }
        count --;
        return node.obj;
    }

    // remove from end of list
    public T Pop ()
    {
        Enumerator.Node node = last;
        if ((last = node.prev) == undef) {
            first = undef;
        }
        count --;
        return node.obj;
    }

    // see how many are in list
    public integer Count ()
    {
        return count;
    }

    // iterate through list
    public IEnumerator<T> GetEnumerator () : IEnumerable<T>
    {
        return new Enumerator (this);
    }

    public class Enumerator : IEnumerator<T> {
        public List<T> thelist;
        public integer atend;
        public Node current;

        public constructor (List<T> thelist)
        {
            this.thelist = thelist;
        }

        // get element currently pointed to
        public T Current () : IEnumerator<T>
        {
            if (atend) throw "at end of list";
            return current.obj;
        }

        // move to next element in list
        public integer MoveNext () : IEnumerator<T>
        {
            if (atend) return 0;
            if (current == undef) current = thelist.first;
                             else current = current.next;
            atend = (current == undef);
            return !atend;
        }

        // reset back to just before beginning of list
        public Reset () : IEnumerator<T>
        {
            atend = 0;
            current = undef;
        }

        // list elements
        public class Node {
            public Node next;
            public Node prev;
            public T obj;
        }
    }
}

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

default {
    state_entry ()
    {
        List<string> stuff = new List<string> ();
        llOwnerSay ("typeof(stuff) = " + xmrTypeName (stuff));
        stuff.Enqueue ("abcdef");
        stuff.Enqueue (1);
        stuff.Enqueue ((string)[2,3,4]);
        stuff.Enqueue (<5,6,7>);
        llOwnerSay ("count=" + stuff.Count ());
        integer first = 1;
        for (IEnumerator<string> stuffenum = stuff.GetEnumerator (); stuffenum.MoveNext ();) {
            if (first) llOwnerSay ("typeof (stuffenum) = " + xmrTypeName (stuffenum));
            llOwnerSay ("element=(" + xmrTypeName (stuffenum.Current ()) + ") " + stuffenum.Current ());
            first = 0;
        }

        Dictionary<string,integer> s2i = new Dictionary<string,integer> ();
        s2i.Add ("one", 1);
        s2i.Add ("two", 2);
        s2i.Add ("three", 3);
        for (IEnumerator<KeyValuePair<string,integer>> kvpenum = s2i.GetEnumerator (); kvpenum.MoveNext ();) {
            KeyValuePair<string,integer> kvp = kvpenum.Current ();
            llOwnerSay ("s2i: " + kvp.kee + " => " + kvp.value);
        }

        float[,] x = new float[,](2,3);  // 2 rows, 3 columns
        float[,] y = new float[,](3,4);  // 3 rows, 4 columns
        llOwnerSay ("x.Length = " + x.Length ());
        llOwnerSay ("y.Length = " + y.Length ());
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
                integer len = jagged[i,j].Length ();
                string msg = "jagged[" + i + "," + j + "].Length=" + jagged[i,j].Length ();
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
                integer len = jagged[i,j].Length ();
                string msg = "jagged[" + i + "," + j + "].Length=" + jagged[i,j].Length ();
                for (integer k = 0; k < len; k ++) {
                    msg += " : " + jagged[i,j][k];
                }
                llOwnerSay (msg);
            }
        }
    }
}
