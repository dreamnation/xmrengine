
xmroption advflowctl;
xmroption arrays;
xmroption norighttoleft;
xmroption objects;

xmroption include "dictionary.lsl";

typedef Dictionary<K,V>   Kunta.Dictionary<K,V>;
typedef IEnumerator<T>    Kunta.IEnumerator<T>;
typedef KeyValuePair<K,V> Kunta.KeyValuePair<K,V>;
typedef LinkedList<T>     Kunta.LinkedList<T>;

list sortedCards = [ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,
                    26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51];

list shuffledCards;
integer nextDealCardIndex;

class Player {
    public string uuid;
    public string name;
    public LinkedList<integer> hand = new LinkedList<integer> ();

    public integer HandValue ()
    {
        integer total = 0;
        for (IEnumerator<integer> henum = hand.GetEnumerator (); henum.MoveNext ();) {
            integer card = henum.Current;
            integer val = card % 13;
            switch (val) {
                case 0: {
                    total += 1001;
                    break;
                }
                case 1 ... 9: {
                    total += ++ val;
                    break;
                }
                case 10 ... 12: {
                    total += 10;
                    break;
                }
            }
        }
        return total;
    }

    public SendHand ()
    {
        string msg = "hand";
        for (IEnumerator<integer> henum = hand.GetEnumerator (); henum.MoveNext ();) {
            integer card = henum.Current;
            msg += card + ",";
        }
        llRegionSayTo (uuid, CHANNEL, msg);
    }
}

constant CHANNEL = -2135482309;

Dictionary<string,Player> players = new Dictionary<string,Player> ();

default {
    state_entry ()
    {
        for (integer i = 0; i < 5; i ++) {
            llOwnerSay ((string)i);
        }
        llListen (CHANNEL, "", "", "");
    }

    listen (integer channel, string name, key id, string message)
    {
        switch (xmrJSubstring (message, 0, 4)) {

            /*
             * Register new player.
             */
            case "REGP": {
                Player p;
                KeyValuePair<string,Player> kvp = players.GetByKey (id);
                if (kvp == undef) {
                    p = new Player ();
                    p.name = name;
                    p.uuid = id;
                    players.Add (p.uuid, p);
                } else {
                    p = kvp.value;
                }
                llRegionSayTo (id, CHANNEL, "regp");
                break;
            }

            /*
             * Shuffle the deck.
             */
            case "SHUF": {
                shuffledCards = llListRandomize (sortedCards, 1);
                nextDealCardIndex = 0;
                llRegionSayTo (id, CHANNEL, "shuf");
                for (IEnumerator<Player> penum = players.Values.GetEnumerator (); penum.MoveNext ();) {
                    Player p = penum.Current;
                    p.hand = new LinkedList<integer> ();
                    p.SendHand ();
                }
                break;
            }

            /*
             * Deal a card to the requesting player.
             */
            case "DEAL": {
                KeyValuePair<string,Player> kvp = players.GetByKey (id);
                if (kvp == undef) {
                    llOwnerSay ("invalid player uuid " + id);
                    break;
                }
                Player p = kvp.value;
                if (nextDealCardIndex >= llGetListLength (shuffledCards)) {
                    llRegionSayTo (p.uuid, CHANNEL, "deal-1");
                } else {
                    integer card = (integer)shuffledCards[nextDealCardIndex++];
                    p.hand.AddLast (card);
                    p.SendHand ();
                }
                break;
            }

            default: {
                llOwnerSay ("invalid message from " + name + ":" + id + ": " + message);
                break;
            }
        }
    }
}
