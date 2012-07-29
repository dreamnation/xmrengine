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

class ListNode {
    public ListNode next;
    public ListNode prev;
    public object obj;
}

class List : IEnumerable {
    public ListNode first;
    public ListNode last;

    public AddLast (object obj)
    {
        ListNode node = new ListNode ();
        node.obj = obj;
        this.AddLast (node);
    }
    public AddLast (ListNode node)
    {
        node.next = undef;
        if ((node.prev = this.last) == undef) {
            this.first = node;
        } else {
            this.last.next = node;
        }
        this.last = node;
    }

    public IEnumerator GetEnumerator () : IEnumerable
    {
        return new ListEnumerator (this);
    }
}

class ListEnumerator : IEnumerator {
    public List thelist;
    public integer atend;
    private ListNode current;

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
}

default {
}
