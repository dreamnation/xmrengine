
xmroption advflowctl;
xmroption arrays;
xmroption objects;

xmroption include "http://dreamnation.net/dictionary.lsl";

typedef Dictionary<K,V>   Kunta.Dictionary<K,V>;
typedef IEnumerator<T>    Kunta.IEnumerator<T>;
typedef KeyValuePair<K,V> Kunta.KeyValuePair<K,V>;
typedef LinkedList<T>     Kunta.LinkedList<T>;

list sortedCards = [ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,
                    26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51];

list shuffledCards;
integer nextDealCardIndex;

class Player {
    public integer link;
    public key uuid;
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
        llMessageLinked (link, 0, msg, uuid);
    }
}

Dictionary<integer,Player> players = new Dictionary<integer,Player> ();

default {
    link_message (integer sender, integer num, string str, key id)
    {
        switch (xmrJSubstring (str, 0, 4)) {

            /*
             * Register new player.
             */
            case "REGP": {
                Player p;
                KeyValuePair<integer,Player> kvp = players.GetByKey (sender);
                if (kvp == undef) {
                    p = new Player ();
                    p.link = sender;
                    p.uuid = id;
                    players.Add (p.link, p);
                } else {
                    p = kvp.value;
                }
                llMessageLinked (p.link, 0, "regp", p.uuid);
                break;
            }

            /*
             * Shuffle the deck.
             */
            case "SHUF": {
                shuffledCards = llListRandomize (sortedCards, 1);
                nextDealCardIndex = 0;
                llMessageLinked (sender, 0, "shuf", id);
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
                KeyValuePair<integer,Player> kvp = players.GetByKey (sender);
                if (kvp == undef) {
                    llOwnerSay ("invalid player " + sender);
                    break;
                }
                Player p = kvp.value;
                if (nextDealCardIndex >= llGetListLength (shuffledCards)) {
                    llMessageLinked (p.link, 0, "deal-1", p.uuid);
                } else {
                    integer card = (integer)shuffledCards[nextDealCardIndex++];
                    p.hand.AddLast (card);
                    p.SendHand ();
                }
                break;
            }

            default: {
                llOwnerSay ("invalid message from " + sender + ": " + str);
                break;
            }
        }
    }
}
