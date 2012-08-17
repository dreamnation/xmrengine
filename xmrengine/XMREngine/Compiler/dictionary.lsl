// Kunta's Dictionary/List implementation
// v1.2.0

xmroption advflowctl;
xmroption arrays;
xmroption objects;

class Kunta {

    public interface ICountable<T> : IEnumerable<T> {
        integer GetCount ();
    }

    public interface IEnumerable<T> {
        IEnumerator<T> GetEnumerator ();
    }

    public interface IEnumerator<T> {
        T GetCurrent ();
        integer MoveNext ();
        Reset ();
    }

    public class Dictionary<K,V> : ICountable<KeyValuePair<K,V>> {
        private integer count;
        private integer hashSize;
        private List<KeyValuePair<K,V>>[] kvpss;
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
            this.kvpss = new List<KeyValuePair<K,V>>[] (hashSize);
        }

        public KeyValuePair<K,V> Add (K kee, V val)
        {
            if (this.GetByKey (kee) != undef) throw "duplicate key";
            integer index = xmrHashCode (kee) % hashSize;
            if (index < 0) index += hashSize;
            KeyValuePair<K,V> kvp = new KeyValuePair<K,V> ();
            kvp.kee = kee;
            kvp.value = val;
            List<KeyValuePair<K,V>> kvps = this.kvpss[index];
            if (kvps == undef) {
                this.kvpss[index] = kvps = new List<KeyValuePair<K,V>> ();
            }
            kvps.Enqueue (kvp);
            count ++;
            return kvp;
        }

        public KeyValuePair<K,V> GetByKey (K kee)
        {
            integer index = xmrHashCode (kee) % hashSize;
            if (index < 0) index += hashSize;
            List<KeyValuePair<K,V>> kvps = this.kvpss[index];
            if (kvps == undef) return undef;
            for (IEnumerator<KeyValuePair<K,V>> kvpenum = kvps.GetEnumerator (); kvpenum.MoveNext ();) {
                KeyValuePair<K,V> kvp = kvpenum.GetCurrent ();
                if (kvp.kee == kee) return kvp;
            }
            return undef;
        }

        public integer GetCount () : ICountable<KeyValuePair<K,V>>
        {
            return count;
        }

        public ICountable<K> GetKeyList ()
        {
            return keyList;
        }
        public ICountable<V> GetValueList ()
        {
            return valueList;
        }

        // iterate through list of key-value pairs
        public IEnumerator<KeyValuePair<K,V>> GetEnumerator () : IEnumerable<KeyValuePair<K,V>>
        {
            return new Enumerator (this);
        }

        private class Enumerator : IEnumerator<KeyValuePair<K,V>> {
            private Dictionary<K,V> thedict;
            private IEnumerator<KeyValuePair<K,V>> listenum;
            private integer index;

            public constructor (Dictionary<K,V> thedict)
            {
                this.thedict = thedict;
                this.Reset ();
            }

            // get element currently pointed to
            public KeyValuePair<K,V> GetCurrent () : IEnumerator<KeyValuePair<K,V>>
            {
                if (this.listenum == undef) throw "at end of list";
                return this.listenum.GetCurrent ();
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
                        if (this.index >= thedict.hashSize) return 0;
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

        private class KeyList : ICountable<K> {
            private Dictionary<K,V> dict;

            public constructor (Dictionary<K,V> dict)
            {
                this.dict = dict;
            }
            public integer GetCount () : ICountable<K>
            {
                return this.dict.GetCount ();
            }
            public IEnumerator<K> GetEnumerator () : IEnumerable<K>
            {
                return new Enumerator (this.dict);
            }

            private class Enumerator : IEnumerator<K> {
                public IEnumerator<KeyValuePair<K,V>> listenum;

                public constructor (Dictionary<K,V> thedict)
                {
                    listenum = thedict.GetEnumerator ();
                }

                // get key element currently pointed to
                public K GetCurrent () : IEnumerator<K>
                {
                    return this.listenum.GetCurrent ().kee;
                }

                // move to next element in list
                public integer MoveNext () : IEnumerator<K>
                {
                    return listenum.MoveNext ();
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
            public integer GetCount () : ICountable<V>
            {
                return this.dict.GetCount ();
            }
            public IEnumerator<V> GetEnumerator () : IEnumerable<V>
            {
                return new Enumerator (this.dict);
            }

            private class Enumerator : IEnumerator<V> {
                public IEnumerator<KeyValuePair<K,V>> listenum;

                public constructor (Dictionary<K,V> thedict)
                {
                    listenum = thedict.GetEnumerator ();
                }

                // get value element currently pointed to
                public V GetCurrent () : IEnumerator<V>
                {
                    return this.listenum.GetCurrent ().value;
                }

                // move to next element in list
                public integer MoveNext () : IEnumerator<V>
                {
                    return listenum.MoveNext ();
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

    public class List<T> : ICountable<T> {
        private Enumerator.Node first;
        private Enumerator.Node last;
        private integer count;

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
        public integer GetCount () : ICountable<T>
        {
            return count;
        }

        // iterate through list
        public IEnumerator<T> GetEnumerator () : IEnumerable<T>
        {
            return new Enumerator (this);
        }

        private class Enumerator : IEnumerator<T> {
            private List<T> thelist;
            private integer atend;
            private Node current;

            public constructor (List<T> thelist)
            {
                this.thelist = thelist;
            }

            // get element currently pointed to
            public T GetCurrent () : IEnumerator<T>
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
}
