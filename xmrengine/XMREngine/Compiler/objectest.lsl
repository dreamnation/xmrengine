xmroption advflowctl;
xmrOption arrayS;
xmroption objects;
xmroption trycatch;
xmrOPtion expIRYdays 5;

interface IComparable {
    integer CompareTo (object that);
}
interface IEnumerable {
    IEnumerator GetEnumerator ();
}
interface IEnumerator {
    object Current { get; }
    integer MoveNext ();
    Reset ();
}
interface IEquality {
    integer Equals (object that);
    integer HashCode { get; }
}

class List : IEnumerable {
    private Enumerator.Node first;
    private Enumerator.Node last;
    private integer count;

    public Enqueue (object obj)
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

    public object Dequeue ()
    {
        Enumerator.Node node = this.first;
        if ((this.first = node.next) == undef) {
            this.last = undef;
        }
        this.count --;
        return node.obj;
    }

    public object Pop ()
    {
        Enumerator.Node node = this.last;
        if ((this.last = node.prev) == undef) {
            this.first = undef;
        }
        this.count --;
        return node.obj;
    }

    public integer Count {
        get {
            return this.count;
        }
    }

    public IEnumerator GetEnumerator () : IEnumerable
    {
        return new Enumerator (this);
    }

    private class Enumerator : IEnumerator {
        public List thelist;
        public integer atend;
        private Node current;

        public constructor (List thelist)
        {
            this.thelist = thelist;
        }

        public object Current : IEnumerator {
            get
            {
                if (this.atend) throw "at end of list";
                return this.current;
            }
        }

        public integer MoveNext () : IEnumerator
        {
            if (this.atend) return 0;
            if (this.current == undef) this.current = this.thelist.first;
                                  else this.current = this.current.next;
            this.atend = (this.current == undef);
            return !this.atend;
        }

        public Reset () : IEnumerator
        {
            this.atend = 0;
            this.current = undef;
        }

        public class Node {
            public Node next;
            public Node prev;
            public object obj;
        }
    }
}

default {
    state_entry ()
    {
        List stuff = new List ();
        stuff.Enqueue ("abcdef");
        stuff.Enqueue (1);
        stuff.Enqueue ([2,3,4]);
        stuff.Enqueue (<5,6,7>);
        llOwnerSay ("count=" + stuff.Count);
        for (IEnumerator stuffenum = stuff.GetEnumerator (); stuffenum.MoveNext ();) {
            llOwnerSay ("element=" + (string)stuffenum.Current);
        }
    }
}
