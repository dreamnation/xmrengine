// Kunta's Dictionary/LinkedList implementation
// v1.5.0

xmroption advflowctl;
xmroption arrays;
xmroption objects;
xmroption trycatch;

partial class Kunta {

    public interface ICountable<T> : IEnumerable<T> {
        integer Count { get; }
    }

    public interface IEnumerable<T> {
        IEnumerator<T> GetEnumerator ();
    }

    public interface IEnumerator<T> {
        T Current { get; }
        integer MoveNext ();
        RemCurrent ();
        Reset ();
    }

    public class Dictionary<K,V> : ICountable<KVP> {
        typedef KVP KeyValuePair<K,V>;
        private integer count;
        private integer hashSize;
        private LinkedList<KVP>[] kvpss;
        private KeyList   keyList   = new KeyList   (this);
        private ValueList valueList = new ValueList (this);

        public constructor ()
        {
            this.hashSize = 47;
            InitKVPSS ();
        }

        public constructor (integer hashSize)
        {
            this.hashSize = hashSize;
            InitKVPSS ();
        }

        private InitKVPSS ()
        {
            this.kvpss = new LinkedList<KVP>[] (hashSize);
        }

        public KVP Add (K kee, V val)
        {
            if (this.GetByKey (kee) != undef) throw "duplicate key";
            integer index = xmrHashCode (kee) % hashSize;
            if (index < 0) index += hashSize;
            KVP kvp = new KVP ();
            kvp.kee = kee;
            kvp.value = val;
            LinkedList<KVP> kvps = this.kvpss[index];
            if (kvps == undef) {
                this.kvpss[index] = kvps = new LinkedList<KVP> ();
            }
            kvps.AddLast (kvp);
            count ++;
            return kvp;
        }

        public KVP GetByKey (K kee)
        {
            integer index = xmrHashCode (kee) % hashSize;
            if (index < 0) index += hashSize;
            LinkedList<KVP> kvps = this.kvpss[index];
            if (kvps == undef) return undef;
            for (IEnumerator<KVP> kvpenum = kvps.GetEnumerator (); kvpenum.MoveNext ();) {
                KVP kvp = kvpenum.Current;
                if (kvp.kee == kee) return kvp;
            }
            return undef;
        }

        public V [K kee] {
            get {
                KVP kvp = this.GetByKey (kee);
                if (kvp == undef) throw "key not found";
                return kvp.value;
            }
            set {
                KVP kvp = this.GetByKey (kee);
                if (kvp == undef) {
                    this.Add (kee, value);
                } else {
                    kvp.value = value;
                }
            }
        }

        public integer RemByKey (K kee)
        {
            integer index = xmrHashCode (kee) % hashSize;
            if (index < 0) index += hashSize;
            LinkedList<KVP> kvps = this.kvpss[index];
            if (kvps == undef) return 0;
            for (IEnumerator<KVP> kvpenum = kvps.GetEnumerator (); kvpenum.MoveNext ();) {
                KVP kvp = kvpenum.Current;
                if (kvp.kee == kee) {
                    kvpenum.RemCurrent ();
                    return 1;
                }
            }
            return 0;
        }

        public integer Count : ICountable<KVP>
        { get {
            return count;
        } }

        public ICountable<K> Keys
        { get {
            return keyList;
        } }
        public ICountable<V> Values
        { get {
            return valueList;
        } }

        // iterate through list of key-value pairs
        public IEnumerator<KVP> GetEnumerator () : IEnumerable<KVP>
        {
            return new Enumerator (this);
        }

        private class Enumerator : IEnumerator<KVP> {
            private Dictionary<K,V> thedict;
            private IEnumerator<KVP> listenum;
            private integer index;

            public constructor (Dictionary<K,V> thedict)
            {
                this.thedict = thedict;
                this.Reset ();
            }

            // get element currently pointed to
            public KVP Current : IEnumerator<KVP>
            { get {
                if (this.listenum == undef) throw "at end of list";
                return this.listenum.Current;
            } }

            // move to next element in list
            public integer MoveNext () : IEnumerator<KVP>
            {
                LinkedList<KVP> kvps;
                while (1) {
                    if (this.listenum == undef) jump done;
                    if (this.listenum.MoveNext ()) break;
                @done;
                    do {
                        if (this.index >= thedict.hashSize) return 0;
                        kvps = this.thedict.kvpss[this.index++];
                    } while (kvps == undef);
                    this.listenum = kvps.GetEnumerator ();
                }
                return 1;
            }

            // remove current element from list
            public RemCurrent () : IEnumerator<KVP>
            {
                if (this.listenum == undef) throw "at end of list";
                this.listenum.RemCurrent ();
            }

            // reset back to just before beginning of list
            public Reset () : IEnumerator<KVP>
            {
                this.index    = 0;
                this.listenum = undef;
            }
        }

        private class KeyList : ICountable<K> {
            private Dictionary<K,V> dict;

            public constructor (Dictionary<K,V> dict)
            {
                this.dict = dict;
            }
            public integer Count : ICountable<K>
            { get {
                return this.dict.Count;
            } }
            public IEnumerator<K> GetEnumerator () : IEnumerable<K>
            {
                return new Enumerator (this.dict);
            }

            private class Enumerator : IEnumerator<K> {
                public IEnumerator<KVP> listenum;

                public constructor (Dictionary<K,V> thedict)
                {
                    listenum = thedict.GetEnumerator ();
                }

                // get key element currently pointed to
                public K Current : IEnumerator<K>
                { get {
                    return this.listenum.Current.kee;
                } }

                // move to next element in list
                public integer MoveNext () : IEnumerator<K>
                {
                    return this.listenum.MoveNext ();
                }

                // remove current element from list
                public RemCurrent () : IEnumerator<K>
                {
                    this.listenum.RemCurrent ();
                }

                // reset back to just before beginning of list
                public Reset () : IEnumerator<K>
                {
                    this.listenum.Reset ();
                }
            }
        }

        private class ValueList : ICountable<V> {
            private Dictionary<K,V> dict;

            public constructor (Dictionary<K,V> dict)
            {
                this.dict = dict;
            }
            public integer Count : ICountable<V>
            { get {
                return this.dict.Count;
            } }
            public IEnumerator<V> GetEnumerator () : IEnumerable<V>
            {
                return new Enumerator (this.dict);
            }

            private class Enumerator : IEnumerator<V> {
                public IEnumerator<KVP> listenum;

                public constructor (Dictionary<K,V> thedict)
                {
                    listenum = thedict.GetEnumerator ();
                }

                // get value element currently pointed to
                public V Current : IEnumerator<V>
                { get {
                    return this.listenum.Current.value;
                } }

                // move to next element in list
                public integer MoveNext () : IEnumerator<V>
                {
                    return this.listenum.MoveNext ();
                }

                // remove current element from list
                public RemCurrent () : IEnumerator<V>
                {
                    this.listenum.RemCurrent ();
                }

                // reset back to just before beginning of list
                public Reset () : IEnumerator<V>
                {
                    this.listenum.Reset ();
                }
            }
        }
    }

    public class KeyValuePair<K,V> {
        public K kee;
        public V value;
    }

    public class LinkedList<T> : Node, ICountable<T> {
        private integer count;

        public constructor ()
        {
            this.next = this;
            this.prev = this;
        }

        // add to beginning of list
        public Node AddFirst (T obj)
        {
            return AddAfter (this, obj);
        }

        // add to end of list
        public Node AddLast (T obj)
        {
            return AddBefore (this, obj);
        }

        // add after an arbitrary node
        public Node AddAfter (Node other, T obj)
        {
            Node node = new Node ();
            node.obj = obj;

            node.next = other.next;
            node.prev = other;

            node.next.prev = node;
            node.prev.next = node;

            count ++;

            return node;
        }

        // add before an arbitrary node
        public Node AddBefore (Node other, T obj)
        {
            Node node = new Node ();
            node.obj = obj;

            node.next = other;
            node.prev = other.prev;

            node.next.prev = node;
            node.prev.next = node;

            count ++;

            return node;
        }

        // get first and last node
        public Node First
        { get {
            if (this.next == this) return undef;
            return this.next;
        } }
        public Node Last
        { get {
            if (this.prev == this) return undef;
            return this.prev;
        } }

        // peek at beginning of list
        public T PeekFirst ()
        {
            Node node = this.next;
            if (node == this) throw "list is empty";
            return node.obj;
        }

        // peek at end of list
        public T PeekLast ()
        {
            Node node = this.prev;
            if (node == this) throw "list is empty";
            return node.obj;
        }

        // remove from beginning of list
        public T RemFirst ()
        {
            Node node = this.next;
            if (node == this) throw "list is empty";
            return RemNode (node);
        }

        // remove from end of list
        public T RemLast ()
        {
            Node node = this.prev;
            if (node == this) throw "list is empty";
            return RemNode (node);
        }

        // see how many are in list
        public integer Count : ICountable<T>
        { get {
            return count;
        } }

        // remove node from list
        public T RemNode (Node node)
        {
            node.prev.next = node.next;
            node.next.prev = node.prev;
            count --;
            return node.obj;
        }

        // iterate through list
        public IEnumerator<T> GetEnumerator () : IEnumerable<T>
        {
            return new Enumerator (this);
        }

        // list elements
        public class Node {
            public Node next;
            public Node prev;
            public T obj;
        }

        private class Enumerator : IEnumerator<T> {
            private integer atend;
            private integer removed;
            private LinkedList<T> thelist;
            private Node current;

            public constructor (LinkedList<T> thelist)
            {
                this.thelist = thelist;
                this.current = thelist;
            }

            // get element currently pointed to
            public T Current : IEnumerator<T>
            { get {
                if (atend) throw "at end of list";
                if (removed) throw "has been removed";
                return current.obj;
            } }

            // move to next element in list
            public integer MoveNext () : IEnumerator<T>
            {
                if (atend) return 0;
                current = current.next;
                atend   = (current == thelist);
                removed = 0;
                return !atend;
            }

            // remove current element from list
            public RemCurrent () : IEnumerator<T>
            {
                if (atend) throw "at end of list";
                if (!removed) {
                    thelist.RemNode (current);
                    removed = 1;
                }
            }

            // reset back to just before beginning of list
            public Reset () : IEnumerator<T>
            {
                atend   = 0;
                removed = 0;
                current = thelist;
            }
        }
    }
}
