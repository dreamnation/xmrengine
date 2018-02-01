
xmroption advflowctl;
xmroption arrays;
xmroption norighttoleft;
xmroption objects;

xmroption include "dictionary.lsl";

typedef Dictionary<K,V>   Kunta.Dictionary<K,V>;
typedef IEnumerator<T>    Kunta.IEnumerator<T>;
typedef KeyValuePair<K,V> Kunta.KeyValuePair<K,V>;
typedef LinkedList<T>     Kunta.LinkedList<T>;

integer registered;
string  serveruuid;
integer shuffled;
LinkedList<integer> hand;
string myuuid;

constant CHANNEL = -2135482309;

default {
    state_entry ()
    {
        for (integer i = 0; i < 5; i ++) {
            llOwnerSay ((string)i);
        }
        myuuid = llGetKey ();
        registered = 0;
        shuffled = 0;
        llListen (CHANNEL, "", "", "");
        llRegionSay (CHANNEL, "REGP");
        llSetTimerEvent (1);
    }

    timer ()
    {
        if (!registered) {
            llRegionSay (CHANNEL, "REGP");
        }
    }

    touch_start (integer num)
    {
        if (!shuffled) {
            llRegionSayTo (serveruuid, CHANNEL, "SHUF");
        } else {
            llRegionSayTo (serveruuid, CHANNEL, "DEAL");
        }
    }

    listen (integer channel, string name, key id, string message)
    {
        switch (xmrJSubstring (message, 0, 4)) {
            case "regp": {
                llSetTimerEvent (0);
                registered = 1;
                serveruuid = id;
                break;
            }

            case "shuf": {
                shuffled = 1;
                hand = new LinkedList<integer> ();
                llOwnerSay ("Deck has been shuffled");
                break;
            }

            case "deal": {
                llOwnerSay ("Deal: " + xmrSubstring (message, 4));
                break;
            }

            case "hand": {
                llOwnerSay ("Hand: " + xmrSubstring (message, 4));
                break;
            }

            default: {
                llOwnerSay ("invalid message from " + name + ":" + id + ": " + message);
                break;
            }
        }
    }
}
