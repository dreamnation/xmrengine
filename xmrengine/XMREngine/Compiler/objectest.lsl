xmroption advflowctl;
xmrOption arrayS;
xmroption objects;
xmroption trycatch;
xmrOPtion expIRYdays 5;

interface IEnumerable<T> {
    IEnumerator<T> GetEnumerator ();
}
interface IEnumerator<T> {
    T Current { get; }
    integer MoveNext ();
    Reset ();
}

class Dictionary<K,V> : IEnumerable<KeyValuePair<K,V>> {
    public List<KeyValuePair<K,V>> kvps;

    public constructor ()
    {
        this.kvps = new List<KeyValuePair<K,V>> ();
    }

    public KeyValuePair<K,V> Add (K kee, V val)
    {
        if (this.GetByKey (kee) != undef) throw "duplicate key";
        KeyValuePair<K,V> kvp = new KeyValuePair<K,V> ();
        kvp.kee = kee;
        kvp.value = val;
        this.kvps.Enqueue (kvp);
        return kvp;
    }

    public KeyValuePair<K,V> GetByKey (K kee)
    {
        for (IEnumerator<KeyValuePair<K,V>> kvpenum = this.GetEnumerator (); kvpenum.MoveNext ();) {
            KeyValuePair<K,V> kvp = kvpenum.Current;
            if (kvp.kee == kee) return kvp;
        }
        return undef;
    }

    // iterate through list of key-value pairs
    public IEnumerator<KeyValuePair<K,V>> GetEnumerator () : IEnumerable<KeyValuePair<K,V>>
    {
        return this.kvps.GetEnumerator ();
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
        if ((node.prev = this.last) == undef) {
            this.first = node;
        } else {
            this.last.next = node;
        }
        this.last = node;
        this.count ++;
    }

    // remove from beginning of list
    public T Dequeue ()
    {
        Enumerator.Node node = this.first;
        if ((this.first = node.next) == undef) {
            this.last = undef;
        }
        this.count --;
        return node.obj;
    }

    // remove from end of list
    public T Pop ()
    {
        Enumerator.Node node = this.last;
        if ((this.last = node.prev) == undef) {
            this.first = undef;
        }
        this.count --;
        return node.obj;
    }

    // see how many are in list
    public integer Count {
        get {
            return this.count;
        }
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
        public T Current : IEnumerator<T> {
            get
            {
                if (this.atend) throw "at end of list";
                return this.current.obj;
            }
        }

        // move to next element in list
        public integer MoveNext () : IEnumerator<T>
        {
            if (this.atend) return 0;
            if (this.current == undef) this.current = this.thelist.first;
                                  else this.current = this.current.next;
            this.atend = (this.current == undef);
            return !this.atend;
        }

        // reset back to just before beginning of list
        public Reset () : IEnumerator<T>
        {
            this.atend = 0;
            this.current = undef;
        }

        // list elements
        public class Node {
            public Node next;
            public Node prev;
            public T obj;
        }
    }
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
        llOwnerSay ("count=" + stuff.Count);
        integer first = 1;
        for (IEnumerator<string> stuffenum = stuff.GetEnumerator (); stuffenum.MoveNext ();) {
            if (first) llOwnerSay ("typeof (stuffenum) = " + xmrTypeName (stuffenum));
            llOwnerSay ("element=(" + xmrTypeName (stuffenum.Current) + ") " + stuffenum.Current);
            first = 0;
        }

        Dictionary<string,integer> s2i = new Dictionary<string,integer> ();
        s2i.Add ("one", 1);
        s2i.Add ("two", 2);
        s2i.Add ("three", 3);
        for (IEnumerator<KeyValuePair<string,integer>> kvpenum = s2i.GetEnumerator (); kvpenum.MoveNext ();) {
            KeyValuePair<string,integer> kvp = kvpenum.Current;
            llOwnerSay ("s2i: " + kvp.kee + " => " + kvp.value);
        }
    }
}
