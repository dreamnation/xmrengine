
xmroption advflowctl;
xmroption arrays;
xmroption objects;
xmroption trycatch;

xmroption include "http://dreamnation.net/dictionary.lsl";

typedef Dictionary<K,V>   Kunta.Dictionary<K,V>;
typedef IEnumerator<T>    Kunta.IEnumerator<T>;
typedef KeyValuePair<K,V> Kunta.KeyValuePair<K,V>;
typedef LinkedList<T>     Kunta.LinkedList<T>;

integer serverlink;
integer shuffled;
LinkedList<integer> hand;

default {
    state_entry ()
    {
        serverlink = -1;
        shuffled = 0;
        llMessageLinked (LINK_ALL_OTHERS, 0, "REGP", "");
        llSetTimerEvent (1);
    }

    timer ()
    {
        if (serverlink < 0) {
            llMessageLinked (LINK_ALL_OTHERS, 0, "REGP", "");
        }
    }

    touch_start (integer num)
    {
        if (serverlink >= 0) {
            if (!shuffled) {
                llMessageLinked (serverlink, 0, "SHUF", "");
            } else {
                llMessageLinked (serverlink, 0, "DEAL", "");
            }
        }
    }

    link_message (integer sender, integer num, string str, key id)
    {
        switch (xmrJSubstring (str, 0, 4)) {
            case "regp": {
                llSetTimerEvent (0);
                if (sender < 0) throw "server sender negative " + sender;
                serverlink = sender;
                break;
            }

            case "shuf": {
                shuffled = 1;
                hand = new LinkedList<integer> ();
                llOwnerSay ("Deck has been shuffled");
                break;
            }

            case "deal": {
                llOwnerSay ("Deal: " + xmrSubstring (str, 4));
                break;
            }

            case "hand": {
                llOwnerSay ("Hand: " + xmrSubstring (str, 4));
                break;
            }

            default: {
                llOwnerSay ("invalid message from " + sender + ": " + str);
                break;
            }
        }
    }
}
