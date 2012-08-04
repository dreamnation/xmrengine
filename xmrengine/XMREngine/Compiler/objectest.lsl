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

class List<T> : IEnumerable<T> {
    private Enumerator.Node first;
    private Enumerator.Node last;
    private integer count;

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

    private class Enumerator : IEnumerator<T> {
        public List<T> thelist;
        public integer atend;
        private Node current;

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
        stuff.Enqueue ([2,3,4]);
        stuff.Enqueue (<5,6,7>);
        llOwnerSay ("count=" + stuff.Count);
        integer first = 1;
        for (IEnumerator<string> stuffenum = stuff.GetEnumerator (); stuffenum.MoveNext ();) {
            if (first) llOwnerSay ("typeof (stuffenum) = " + xmrTypeName (stuffenum));
            llOwnerSay ("element=(" + xmrTypeName (stuffenum.Current) + ") " + stuffenum.Current);
            first = 0;
        }
    }
}
