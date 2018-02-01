// rummyboard

/*
 * The RummyBoard consists of these components:
 *   PlayerStatus<0..5>
 *   Discard
 *   Stock
 *   ResetButton
 *   StartButton
 *   HelpButton
 *   MeldPanel<0..15>
 *   MeldCardPanel<0..15><0..3>
 * Each has an instance of cardpanel.lsl script
 * which simply checks in with us on startup.
 * The cardpanel.lsl script will also send us a
 * message whenever an avatar touches it.  And
 * we can also set its texture and color by
 * sending it messages.
 *
 * The RummyHUD consists of 20 instances of
 *   PlayerCardPanel<0..19>
 * which are also cardpanel.lsl scripts.  This
 * script controls everything seen on the panel
 * and processes all the clicks.
 */

xmroption arrays;
xmroption advflowctl;
xmroption chars;
xmroption norighttoleft;
xmroption objects;
xmroption trycatch;


constant RUMMY_CHANNEL = -646830961;

constant canDiscardDraw = 1;  // 0: discard cannot be same card drawn from stock or discard pile
                              // 1: discard can be same card drawn from stock or discard pile

constant mustDiscardOut = 0;  // 0: melding can leave zero cards in player's hand
                              // 1: melding must leave at least one card in player's hand

integer fontsize = 14;

array allCardPanels;    // key     uuid   -> CardPanel
array melds;            // integer index  -> Meld
array playersByAvUUID;  // key     uuid   -> Player
array playersByIndex;   // integer index  -> PlayerStatus
Player currentPlayer;
integer phaseDrawing;
integer pingPlayerIndex;
list sortedCards = [ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,
                    26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51];

//                                     A 2 3 4 5 6 7 8 9 10 J  Q  K
integer[] cardpoints = new integer[] { 1,2,3,4,5,6,7,8,9,10,10,10,10 };


default {
    state_entry ()
    {
        llSay (PUBLIC_CHANNEL, "Kunta's RUMMY Game -- Ready to play!");
        llSay (PUBLIC_CHANNEL, "Click Help button for instructions.");

        allCardPanels.clear ();
        melds.clear ();
        pingPlayerIndex = 0;
        playersByAvUUID.clear ();
        playersByIndex.clear ();
        currentPlayer = undef;
        phaseDrawing = 0;

        /*
         * Start listening for incoming messages.
         */
        llListen (RUMMY_CHANNEL, "", "", "");

        /*
         * Tell anyone who cares that we have restarted.
         * Any objects should now re-register because we have forgotton about everything.
         */
        llRegionSay (RUMMY_CHANNEL, "rstr");

        /*
         * Ping timer to make sure card panels are still here.
         */
        llSetTimerEvent (5);
    }

    timer ()
    {
        if (playersByAvUUID.count > 0) {
            if (pingPlayerIndex >= playersByAvUUID.count) {
                pingPlayerIndex = 0;
            }
            Player p = (Player)playersByAvUUID.value (pingPlayerIndex);
            if (p.cardPanels.count > 0) {
                PlayerCardPanel pcp = (PlayerCardPanel)p.cardPanels.value (0);
                if (++ pcp.unansweredPings > 2) {
                    pcp.Detached ();
                    return;
                }
                llRegionSayTo (pcp.id, RUMMY_CHANNEL, "ping");
            }
            pingPlayerIndex ++;
        }
    }

    listen (integer channel, string name, key id, string message)
    {
        switch (xmrSubstring (message, 0, 4)) {

            /*
             * Card panel was touched.
             *   CPTC<avataruuidthattouchedcardpanel>
             */
            case "CPTC": {
                key byavuuid = xmrSubstring (message, 4);
                CardPanel cp = (CardPanel)allCardPanels[id];
                if (cp == undef) {
                    llOwnerSay ("unknown cardpanel " + id + " " + name);
                } else {
                    cp.unansweredPings = 0;
                    cp.Touched (byavuuid);
                }
                break;
            }

            case "DTCH": {
                CardPanel cp = (CardPanel)allCardPanels[id];
                if (cp != undef) {
                    cp.Detached ();
                }
                break;
            }

            /*
             * Responding to a ping.
             */
            case "PONG": {
                CardPanel cp = (CardPanel)allCardPanels[id];
                if (cp != undef) {
                    cp.unansweredPings = 0;
                }
                break;
            }

            /*
             * Response for RESET button confirmation dialog.
             */
            case "Rese": {
                if (message == "Reset") {
                    llSay (PUBLIC_CHANNEL, name + " has reset the game...");
                    llResetScript ();
                }
                return;
            }

            /*
             * Response to dialog indicating the user wants one of our HUDs.
             */
            case "I ne": {
                if (message == "I need HUD") {
                    llGiveInventory (id, "RummyHUD");
                }
                break;
            }

            /*
             * Register a card panel.
             *   RGCP<owneruuid>
             *     name=MeldCardPanel<meldno><cardno>
             *     name=PlayerCardPanel<index>
             *     name=PlayerStatus<playerno>
             */
            case "RGCP": {
                CardPanel cp = (CardPanel)allCardPanels[id];
                if (cp == undef) {
                    if (name == "Discard") {
                        cp = DiscardCardPanel.Construct (id);
                    } else if (name == "HelpButton") {
                        cp = HelpButton.Construct (id);
                    } else if (xmrSubstring (name, 0, 13) == "MeldCardPanel") {
                        integer code   = (integer)xmrSubstring (name, 13);
                        integer meldno = code / 10;  // -> 0..15
                        integer cardno = code % 10;  // -> 0..3
                        Meld meld = Meld.Construct ("", meldno);
                        cp = MeldCardPanel.Construct (id, meld, cardno);
                    } else if (xmrSubstring (name, 0, 9) == "MeldPanel") {
                        integer meldno = (integer)xmrSubstring (name, 9);
                        cp = Meld.Construct (id, meldno);
                    } else if (xmrSubstring (name, 0, 15) == "PlayerCardPanel") {
                        key avuuid = xmrSubstring (message, 4);
                        string avname = llKey2Name (avuuid);
                        if (avname == "") {
                            SendAvErrMsg (avuuid, "failed to capture avatar name, try detaching and reattaching HUD");
                        } else {
                            Player player = Player.Construct (avuuid, avname);
                            integer index = (integer)xmrSubstring (name, 15);
                            cp = PlayerCardPanel.Construct (id, player, index);
                        }
                    } else if (xmrSubstring (name, 0, 12) == "PlayerStatus") {
                        integer num = (integer)xmrSubstring (name, 12);
                        cp = PlayerStatus.Construct (id, num);
                    } else if (name == "ResetButton") {
                        cp = ResetButton.Construct (id);
                    } else if (name == "Stock") {
                        cp = StockCardPanel.Construct (id);
                    } else if (name == "StartButton") {
                        cp = StartButton.Construct (id);
                    } else {
                        llOwnerSay ("unknown card panel " + name);
                        cp = undef;
                    }
                    cp.unansweredPings = 0;
                    allCardPanels[id] = cp;
                }
                llRegionSayTo (id, RUMMY_CHANNEL, "rgcp");
                break;
            }

            /*
             * Response for START button confirmation dialog.
             */
            case "Shuf": {
                if (message == "Shuffle") {
                    llSay (PUBLIC_CHANNEL, name + " has started the game...");
                    StartGame ();
                }
                break;
            }
        }
    }
}

/******************\
 *  Game Control  *
\******************/


/**
 * @brief Start a game.
 */
StartGame ()
{
    object v;

    /*
     * Clear off any existing melds.
     */
    foreach (,v in melds) {
        Meld m = (Meld)v;
        m.Clear ();
    }

    /*
     * Reset discard pile to be empty.
     */
    DiscardCardPanel.Clear ();

    /*
     * Shuffle deck.
     */
    StockCardPanel.Shuffle ();

    /*
     * Figure out how many cards each player gets initially.
     */
    integer nplayers = 0;
    foreach (,v in playersByIndex) {
        PlayerStatus ps = (PlayerStatus)v;
        if (ps.player != undef) nplayers ++;
    }
    if (nplayers == 0) {
        llSay (PUBLIC_CHANNEL, "no players registered; click on player status bar");
        return;
    }
    integer ncards;
    if (nplayers <= 2) ncards = 10;
    else if (nplayers <= 4) ncards = 7;
    else ncards = 6;

    /*
     * Deal that many cards to each player in order and
     * update all displays.
     */
    integer cardno;
    for (integer i = 0; i < ncards; i ++) {
        for (integer j = 0; j < playersByIndex.count; j ++) {
            PlayerStatus ps = (PlayerStatus)playersByIndex[j];
            Player p = ps.player;
            if (p != undef) {
                if (i == 0) p.ClearHand ();
                cardno = StockCardPanel.PopFromStockPile ();
                p.AddCardToHand (cardno);
            }
        }
    }
    for (integer j = 0; j < playersByIndex.count; j ++) {
        PlayerStatus ps = (PlayerStatus)playersByIndex[j];
        if (ps.player != undef) {
            ps.UpdateDisplay ();
        }
    }

    /*
     * Deal a card to the discard pile.
     */
    cardno = StockCardPanel.PopFromStockPile ();
    DiscardCardPanel.PushToDiscardPile (cardno);

    /*
     * Set current player to #0 and tell them it's their turn.
     */
    SetCurrentPlayer (0);
}


/**
 * @brief Tell the old player they are no longer current
 *        and tell the new player they are now current
 */
SelectNextPlayer ()
{
    currentPlayer.hasMeldedBeforeThisTurn = currentPlayer.hasEverMeldedThisHand;
    SetCurrentPlayer (currentPlayer.index + 1);
}
SetCurrentPlayer (integer n)
{
    if (currentPlayer != undef) {
        currentPlayer.UpdateDisplay ();
    }
    do {
        if (n == playersByIndex.count) n = 0;
        currentPlayer = ((PlayerStatus)playersByIndex[n++]).player;
    } while (currentPlayer == undef);
    llSay (PUBLIC_CHANNEL, "It is " + currentPlayer.avname + "'s turn");
    SendAvErrMsg (currentPlayer.avuuid, "it's your turn; draw from stock or from discard pile");
    phaseDrawing = 1;
}


/**
 * @brief The current player is now out so the game is over.
 */
PlayerIsOut ()
{
    integer factor = 1;
    string goneRummy = "";
    if (!currentPlayer.hasMeldedBeforeThisTurn) {
        factor = 2;
        goneRummy = " has GONE RUMMY";
    }

    llSay (PUBLIC_CHANNEL, "GAME OVER !!!  Hooray " + currentPlayer.avname + goneRummy + " !!!");

    integer score = 0;
    object v1;
    object v2;
    foreach (,v1 in playersByIndex) {
        PlayerStatus ps = (PlayerStatus)v1;
        Player player = ps.player;
        if (player != undef) {
            foreach (,v2 in player.cardPanels) {
                PlayerCardPanel pcp = (PlayerCardPanel)v2;
                if (pcp.cardno >= 0) {
                    score += cardpoints[pcp.cardno%13] * factor;
                    llSay (PUBLIC_CHANNEL, "... " + player.avname + " " + CardName (pcp.cardno) + " -> " + score);
                    pcp.RemoveFromHand ();
                }
            }
            ps.UpdateDisplay ();
        }
    }
    currentPlayer.score += score;
    currentPlayer.UpdateDisplay ();
    currentPlayer = undef;
}

/*************************\
 *  RummyBoard elements  *
\*************************/


/**
 * @brief Manage the undealt cards.
 */
class StockCardPanel : CardPanel {

    private static integer dealIndex;
    private static list shuffledCards;
    private static StockCardPanel singleton;

    /**
     * @brief Called when the StockPanel button box checks in.
     */
    public static StockCardPanel Construct (key id)
    {
        if (singleton == undef) {
            singleton = new StockCardPanel ();
        }
        singleton.id = id;

        string url = "http://www.outerworldapps.com/cards-classic/bluback.png";
        llRegionSayTo (id, RUMMY_CHANNEL, "sdtu" + url);

        return singleton;
    }

    private constructor () { }

    public static Shuffle ()
    {
        shuffledCards = llListRandomize (sortedCards, 1);
        dealIndex = 0;
    }

    /**
     * @brief Called when someone clicks on the stock pile box,
     *        presumably to draw a card at the beginning of their turn.
     */
    public override Touched (key byavuuid)
    {
        if ((currentPlayer != undef) &&& (currentPlayer.avuuid == byavuuid)) {
            if (phaseDrawing) {

                /*
                 * Player is drawing a card from the stock pile.
                 */
                integer cardno = PopFromStockPile ();
                currentPlayer.AddCardToHand (cardno);
                phaseDrawing = 0;
                SendAvErrMsg (currentPlayer.avuuid, "now do any melding you want then put one card in discard pile");
            } else {

                /*
                 * Player must discard exactly one card.
                 */
                SendAvErrMsg (byavuuid, "cannot draw from stock pile; meld and/or discard");
            }
        }
    }

    /**
     * @brief Draw (deal) a card from the stock pile.
     */
    public static integer PopFromStockPile ()
    {
        if (dealIndex >= llGetListLength (shuffledCards)) {
            llSay (PUBLIC_CHANNEL, "stock pile empty, reshuffling discards");
            list discards = DiscardCardPanel.PopAllFromDiscardPile ();
            shuffledCards = llListRandomize (discards, 1);
            dealIndex = 0;
        }
        return (integer)shuffledCards[dealIndex++];
    }
}


/**
 * @brief Keep track of discards.
 */
class DiscardCardPanel : CardPanel {

    private static array discardPile;
    private static DiscardCardPanel singleton;

    public static DiscardCardPanel Construct (key id)
    {
        if (singleton == undef) {
            singleton = new DiscardCardPanel ();
        }
        singleton.id = id;
        return singleton;
    }

    private constructor () { }

    /**
     * @brief Someone clicked on the discard pile,
     *        presumably to either draw a card from the discard pile 
     *        or to move a card from their hand to the discard pile.
     */
    public override Touched (key byavuuid)
    {
        if ((currentPlayer != undef) &&& (currentPlayer.avuuid == byavuuid)) {
            if (phaseDrawing) {

                /*
                 * Current player is drawing a card from the discard pile.
                 */
                integer cardno = PopFromDiscardPile ();
                currentPlayer.AddCardToHand (cardno);
                phaseDrawing = 0;
                SendAvErrMsg (currentPlayer.avuuid, "now do any melding you want then put one card in discard pile");
            } else {
                array selectedcards = currentPlayer.GetSelectedCards ();
                if (selectedcards.count == 1) {

                    /*
                     * Current player is discarding the one card they have selected in their hand.
                     */
                    PlayerCardPanel pcp = (PlayerCardPanel)selectedcards.value (0);
                    if (!canDiscardDraw &&& (pcp.cardno == currentPlayer.lastCardAdded)) {
                        SendAvErrMsg (currentPlayer.avuuid, "cannot discard drawn card, select another");
                    } else {
                        PushToDiscardPile (pcp.cardno);
                        pcp.RemoveFromHand ();

                        /*
                         * End of game if player is out.
                         * Otherwise, on to next player.
                         */
                        if (currentPlayer.NumberCardsInHand () == 0) {
                            PlayerIsOut ();
                        } else {
                            SelectNextPlayer ();
                        }
                    }
                } else {

                    /*
                     * Player must discard exactly one card.
                     */
                    SendAvErrMsg (byavuuid, "select exactly one card to discard first");
                }
            }
        }
    }

    public static Clear ()
    {
        discardPile.clear ();
        singleton.DisplayCard (-1);
    }

    /**
     * @brief Push a card onto the discard pile and update display.
     */
    public static PushToDiscardPile (integer cardno)
    {
        discardPile[discardPile.count] = cardno;
        singleton.DisplayCard (cardno);
    }

    /**
     * @brief Pop a card from the discard pile and update display.
     */
    public static integer PopFromDiscardPile ()
    {
        integer n = discardPile.count;
        integer cardno = (integer)discardPile[--n];
        discardPile[n] = undef;
        if (discardPile.count > 0) {
            singleton.DisplayCard ((integer)discardPile[discardPile.count-1]);
        } else {
            singleton.DisplayCard (-1);
        }
        return cardno;
    }

    /**
     * @brief Pop all the cards from discard pile in a list.
     */
    public static list PopAllFromDiscardPile ()
    {
        integer i = discardPile.count;
        object[] objs = new object[] (i);
        i = 0;
        object v;
        foreach (,v in discardPile) {
            objs[i++] = v;
        }
        Clear ();
        return xmrArray2List (objs, 0, i);
    }
}


/**
 * @brief Help button gives them the help notecard.
 */
class HelpButton : CardPanel {
    private static string textextras = "width:128,height:64";

    private static HelpButton singleton;

    public static HelpButton Construct (key id)
    {
        if (singleton == undef) {
            singleton = new HelpButton ();
        }
        singleton.id = id;

        string data = "";
        data = osSetFontSize (data, fontsize);
        data = osDrawText (data, "HELP");
        string json = "{\"data\":" + JSONString (data) + ",\"extra\":" + JSONString (textextras) + "}";
        llRegionSayTo (id, RUMMY_CHANNEL, "sdtd" + json);

        return singleton;
    }

    private constructor () { }

    public override Touched (key byavuuid)
    {
        llGiveInventory (byavuuid, "RummyHelp");
    }
}


/**
 * @brief One of these per the six player status panels,
 *        whether there is a player registered there or not.
 */
class PlayerStatus : CardPanel {
    private static string textextras = "width:512,height:64";

    public integer index;  // index number 0..5
    public Player player;  // registered player (or undef if none)

    /**
     * @brief Catalog this player status panel so we can display stuff on it.
     */
    public static PlayerStatus Construct (key id, integer index)
    {
        PlayerStatus zhis = (PlayerStatus)playersByIndex[index];
        if (zhis == undef) {
            zhis = new PlayerStatus ();
            zhis.index = index;
            playersByIndex[index] = zhis;
        }
        zhis.id = id;
        zhis.UpdateDisplay ();
        return zhis;
    }

    private constructor () { }

    /**
     * @brief Someone just clicked on the player status bar.
     *        If no one is currently occupying that slot, put them there.
     *        Else, if they are the one occupying the slot, remove them.
     */
    private override Touched (key byavuuid)
    {
        if (currentPlayer != undef) {
            SendAvErrMsg (byavuuid, "cannot join or leave while a game is in progress");
            return;
        }
        if (player == undef) {
            player = (Player)playersByAvUUID[byavuuid];
            if (player == undef) {
                llDialog (byavuuid, "HUD not attached or try detaching and reattach HUD.  Do you need an HUD?", 
                          [ "I need HUD", "No thanks" ], RUMMY_CHANNEL);
                return;
            }
            if (player.index >= 0) {
                SendAvErrMsg (byavuuid, "you are already registered as player " + (player.index + 1));
                return;
            }
            player.index = index;
            UpdateDisplay ();
        } else if (player.avuuid == byavuuid) {
            player.index = -1;
            player = undef;
            UpdateDisplay ();
        } else {
            SendAvErrMsg (byavuuid, player.avname + " already registered in that slot, try another");
        }
    }

    /**
     * @brief Update the display to match what we think the state is.
     */
    private string lastdisplayed = "";
    public UpdateDisplay ()
    {
        if (player == undef) {
            if (lastdisplayed != "") {
                llRegionSayTo (id, RUMMY_CHANNEL, "txur" + TEXTURE_BLANK);
                lastdisplayed = "";
            }
        } else {
            string data = "";
            data = osSetFontSize (data, fontsize);
            data = osDrawText (data, (string)player.NumberCardsInHand () + " : " + player.avname + " : " + (string)player.score);
            string json = "{\"data\":" + JSONString (data) + ",\"extra\":" + JSONString (textextras) + "}";
            if (lastdisplayed != json) {
                llRegionSayTo (id, RUMMY_CHANNEL, "sdtd" + json);
                lastdisplayed = json;
            }
        }
    }
}


/**
 * @brief Reset button restarts the script.
 */
class ResetButton : CardPanel {
    private static string textextras = "width:128,height:64";

    private static ResetButton singleton;

    public static ResetButton Construct (key id)
    {
        if (singleton == undef) {
            singleton = new ResetButton ();
        }
        singleton.id = id;

        string data = "";
        data = osSetFontSize (data, fontsize);
        data = osDrawText (data, "RESET");
        string json = "{\"data\":" + JSONString (data) + ",\"extra\":" + JSONString (textextras) + "}";
        llRegionSayTo (id, RUMMY_CHANNEL, "sdtd" + json);

        return singleton;
    }

    private constructor () { }

    public override Touched (key byavuuid)
    {
        ResetDialog (byavuuid);
    }

    public static ResetDialog (key byavuuid)
    {
        llDialog (byavuuid, "Are you REALLY SURE you want to factory reset the game?  " + 
                            "Any hands and scores will be lost and players will have to re-register.", 
                  [ "Reset", "Never mind" ], RUMMY_CHANNEL);
    }
}


/**
 * @brief Start button shuffles the deck, deals initial cards and starts first player.
 */
class StartButton : CardPanel {
    private static string textextras = "width:128,height:64";

    private static StartButton singleton;

    public static StartButton Construct (key id)
    {
        if (singleton == undef) {
            singleton = new StartButton ();
        }
        singleton.id = id;

        string data = "";
        data = osSetFontSize (data, fontsize);
        data = osDrawText (data, "START");
        string json = "{\"data\":" + JSONString (data) + ",\"extra\":" + JSONString (textextras) + "}";
        llRegionSayTo (id, RUMMY_CHANNEL, "sdtd" + json);

        return singleton;
    }

    private constructor () { }

    public override Touched (key byavuuid)
    {
        string msg = "Are you REALLY SURE you want to start the game?";
        if (currentPlayer != undef) {
            msg += "  All hands will be lost and deck will be reshuffled.";
        }
        llDialog (byavuuid, msg, [ "Shuffle", "Never mind" ], RUMMY_CHANNEL);
    }
}


/**
 * @brief One of these per meld.  It has four MeldCardPanels for displaying the 
 *        melded cards, but it does not display anything itself as such.
 *        It is a CardPanel though so we can sense a touch meaning that the 
 *        current player wishes to meld his selected cards with this meld panel.
 */
class Meld : CardPanel {
    public array melded;         // cardno -> cardno (so it's sorted by cardno)
    public integer index;        // which meld panel we are (0..n)
    public integer setTypeMeld;  // 0=run; 1=set
    public array cardPanels;     // integer -> MeldCardPanel : each card panel on the meld panel

    public static Meld Construct (key id, integer index)
    {
        Meld zhis = (Meld)melds[index];
        if (zhis == undef) {
            zhis = new Meld ();
            zhis.index = index;
            melds[index] = zhis;
        }
        if (id != "") zhis.id = id;
        return zhis;
    }

    private constructor () { }

    public Clear ()
    {
        melded.clear ();
        UpdateDisplay ();
    }

    /**
     * @brief Try to meld the currently selected cards into this meld.
     *        Update the status board and the player's hand.
     */
    public override Touched (key byavuuid)
    {
        if (currentPlayer == undef) return;
        if (currentPlayer.avuuid != byavuuid) return;

        /*
         * Get list of cards in their hand they are trying to meld.
         */
        array melding = currentPlayer.GetSelectedCards ();
        if (mustDiscardOut && (melding.count >= currentPlayer.NumberCardsInHand ())) {
            SendAvErrMsg (currentPlayer.avuuid, "cannot meld all cards, must retain one card to discard");
            return;
        }

        /*
         * Try to add them to this meld.
         * If successful, remove the melded cards from the player's hand.
         */
        integer rc = AddToMeld (melding);
        if (rc) {
            string cardnames = "";
            object v;
            foreach (,v in melding) {
                PlayerCardPanel pcp = (PlayerCardPanel)v;
                if (cardnames != "") cardnames += ", ";
                cardnames += CardName (pcp.cardno);
                pcp.RemoveFromHand ();
            }
            llSay (0, currentPlayer.avname + " just melded " + cardnames);
            currentPlayer.hasEverMeldedThisHand = 1;
            if (currentPlayer.NumberCardsInHand () == 0) {
                PlayerIsOut ();
            } else {
                SendAvErrMsg (currentPlayer.avuuid, "you may meld again or put one card in discard pile");
            }
        } else {
            SendAvErrMsg (currentPlayer.avuuid, "those cards are not meldable (to the selected slot anyway)");
        }
    }

    /**
     * @brief Try to add the given list of cards to the meld
     * @param melding = integer cardno -> PlayerCardPanel
     * @returns 0: can't add to meld
     *          1: successfully added to meld
     */
    public integer AddToMeld (array melding)
    {
        object v;

        if (melded.count == 0) {

            /*
             * Making new meld, must have at least 3 cards.
             */
            if (melding.count < 3) return 0;

            /*
             * Check for run-type meld, ie, all same suit and in sequence.
             */
            integer locardno = 999999999;
            integer hicardno = -1;
            foreach (v, in melding) {
                integer cardno = (integer)v;
                if (locardno > cardno) locardno = cardno;
                if (hicardno < cardno) hicardno = cardno;
            }
            if ((locardno / 13 != hicardno / 13) ||| (hicardno + 1 - locardno != melding.count)) {

                /*
                 * Check for set-type meld, ie, all same rank (but different suits).
                 */
                integer rank = -1;
                foreach (v, in melding) {
                    integer cardno = (integer)v;
                    if (rank < 0) rank = cardno % 13;
                    else if (cardno % 13 != rank) return 0;
                }
                setTypeMeld = 1;
            }
        } else if (setTypeMeld) {

            /*
             * Adding to set-type meld, new card must be same rank.
             */
            integer basecardno = (integer)melded.value (0) % 13;
            foreach (v, in melding) {
                integer cardno = (integer)v;
                if (cardno % 13 != basecardno) return 0;
            }
        } else {

            /*
             * Run-type meld, new cards must be same suit and 
             * sequential rank building off of existing meld.
             */
            integer lomeldedcardno = 999999999;  // lowest cardno in existing meld
            integer himeldedcardno = -1;         // highest cardno in existing meld
            foreach (,v in melded) {
                integer meldedcardno = (integer)v;
                if (lomeldedcardno > meldedcardno) lomeldedcardno = meldedcardno;
                if (himeldedcardno < meldedcardno) himeldedcardno = meldedcardno;
            }
            array yettomeld;                   // copy list of cards to be added
            foreach (v, in melding) {
                yettomeld[v] = v;
            }
        @scan;
            if (yettomeld.count > 0) {
                foreach (,v in yettomeld) {    // scan through unmelded cards
                    integer meldcardno = (integer)v;
                    if (lomeldedcardno - 1 == meldcardno) {
                        lomeldedcardno --;       // it tacks on lower end
                        yettomeld[v] = undef;
                        jump scan;
                    }
                    if (himeldedcardno + 1 == meldcardno) {
                        himeldedcardno ++;       // it tacks on  higher end
                        yettomeld[v] = undef;
                        jump scan;
                    }
                }
                return 0;                      // not even one card melded, fail
            }
            if (lomeldedcardno / 13 !=         // make sure suit didn't wrap around
                himeldedcardno / 13) return 0; // eg, can't meld AceClubs and KingSpades
        }

        /*
         * Meld is legal, add new card(s) to existing melded array and update display.
         */
        foreach (v, in melding) {
            melded[v] = v;
        }
        UpdateDisplay ();

        return 1;
    }

    /**
     * @brief Update the meld's card panels with melded cards.
     */
    public UpdateDisplay ()
    {
        integer ci = 0;
        integer cn;
        object v;

        if (melded.count <= 4) {
            foreach (,v in melded) {
                cn = (integer)v;
                DisplayCard (ci++, cn);
            }
            while (ci < 4) {
                DisplayCard (ci++, -1);
            }
        } else {
            foreach (,v in melded) {
                cn = (integer)v;
                if (ci < 2) {
                    DisplayCard (ci++, cn);
                }
            }
            DisplayCard (2, -1);
            DisplayCard (3, cn);
        }
    }

    /**
     * @brief Display a texture on the given card sub-panel
     * @param ci = 0..3 giving which card sub-panel
     * @param cn < 0: blank
     *          else: card number
     */
    private DisplayCard (integer ci, integer cn)
    {
        if (cardPanels[ci] != undef) {
            ((MeldCardPanel)cardPanels[ci]).DisplayCard (cn);
        }
    }
}


/**
 * @brief One of these per card panel in the meld piles,
 *        whether there is a card there or not.
 */
class MeldCardPanel : CardPanel {
    public integer cardno;  // -1: empty; 0..51: occupied
    public integer index;   // 0..3
    public Meld meld;       // which meld we are part of

    public static MeldCardPanel Construct (key id, Meld meld, integer index)
    {
        MeldCardPanel zhis = (MeldCardPanel)meld.cardPanels[index];
        if (zhis == undef) {
            zhis = new MeldCardPanel ();
            zhis.index = index;
            meld.cardPanels[index] = zhis;
            zhis.meld  = meld;
        }
        zhis.id = id;
        return zhis;
    }

    private constructor () { }

    /**
     * @brief Someone clicked on a card in the meld pile.
     *        Just pass it along to the meld panel this card belongs to.
     */
    public override Touched (key byavuuid)
    {
        meld.Touched (byavuuid);
    }
}

/***********************\
 *  RummyHUD Elements  *
\***********************/


/**
 * @brief One of these per HUD attached in this region.
 */
class Player {
    public array cardPanels;        // integer 0..19 -> PlayerCardPanel
    public integer hasMeldedBeforeThisTurn;
    public integer hasEverMeldedThisHand;
    public integer index = -1;      // 0..5 (or -1 if not playing)
    public integer lastCardAdded;   // last card added to hand (ie, last card drawn)
    public integer score;           // accumulated score for all hands
    public key avuuid;              // this avatar's uuid
    public string avname;           // this avatar's name

    public static Player Construct (key avuuid, string avname)
    {
        Player zhis = (Player)playersByAvUUID[avuuid];
        if (zhis == undef) {
            llOwnerSay ("detected player " + avname);
            zhis = new Player ();
            zhis.avuuid = avuuid;
            playersByAvUUID[avuuid] = zhis;
        }
        zhis.avname = avname;
        return zhis;
    }

    private constructor () { }

    /**
     * @brief Remove all cards from player's hand and update display.
     */
    public ClearHand ()
    {
        object v;
        foreach (,v in cardPanels) {
            PlayerCardPanel pcp = (PlayerCardPanel)v;
            pcp.RemoveFromHand ();
        }
        hasMeldedBeforeThisTurn = 0;
        hasEverMeldedThisHand   = 0;
    }

    /**
     * @brief Add a card to player's hand and update display.
     */
    public AddCardToHand (integer cardno)
    {
        lastCardAdded = cardno;
        SendAvErrMsg (avuuid, "... " + CardName (cardno));
        object v;
        foreach (,v in cardPanels) {
            PlayerCardPanel pcp = (PlayerCardPanel)v;
            if (pcp.cardno < 0) {
                pcp.AddCardToHand (cardno);
                break;
            }
        }
    }

    /**
     * @brief Count number of cards in player's hand.
     */
    public integer NumberCardsInHand ()
    {
        integer n = 0;
        object v;
        foreach (,v in cardPanels) {
            PlayerCardPanel pcp = (PlayerCardPanel)v;
            n += (integer)(pcp.cardno >= 0);
        }
        return n;
    }

    /**
     * @brief Get an sorted array of selected card numbers.
     * @returns array of cardno->PlayerCardPanel
     */
    public array GetSelectedCards ()
    {
        array seldcards;
        object v;
        foreach (,v in cardPanels) {
            PlayerCardPanel pcp = (PlayerCardPanel)v;
            if ((pcp.cardno >= 0) && pcp.selected) {
                seldcards[pcp.cardno] = pcp;
            }
        }
        return seldcards;
    }

    /**
     * @brief Haven't heard from the player in too long.
     */
    public UnheardFrom ()
    {
        SendAvErrMsg (avuuid, "hud inactive");

        playersByAvUUID[avuuid] = undef;
        if (index >= 0) {
            PlayerStatus ps = (PlayerStatus)playersByIndex[index];
            ps.player = undef;
            ps.UpdateDisplay ();
            index = -1;
        }

        object v;
        foreach (,v in cardPanels) {
            PlayerCardPanel pcp = (PlayerCardPanel)v;
            allCardPanels[pcp.id] = undef;
            if ((currentPlayer != undef) && (pcp.cardno >= 0)) {
                DiscardCardPanel.PushToDiscardPile (pcp.cardno);
            }
        }
    }

    /**
     * @brief Update corresponding player status bar.
     */
    public UpdateDisplay ()
    {
        if (index >= 0) {
            ((PlayerStatus)playersByIndex[index]).UpdateDisplay ();
        }
    }
}


/**
 * @brief One of these per slot in the player's hand (HUD).
 *        Includes both slots that have a card and those that are empty.
 */
class PlayerCardPanel : CardPanel {
    public integer cardno = -1; // -1 if nothing; else: card number 0..51
    public integer selected;    // set/cleared by touching
                                // - used to select cards for melding / discard
    private Player player;

    public static PlayerCardPanel Construct (key id, Player player, integer index)
    {
        PlayerCardPanel zhis = (PlayerCardPanel)player.cardPanels[index];
        if (zhis == undef) {
            zhis = new PlayerCardPanel (id);
            player.cardPanels[index] = zhis;
            zhis.player = player;
        }
        zhis.id = id;
        zhis.UpdateDisplay ();
        return zhis;
    }

    private constructor (key id) { }

    /**
     * @brief Called to remove the card from the slot,
     *        such as when the card was discarded or
     *        melded.
     */
    public RemoveFromHand ()
    {
        cardno = -1;
        selected = 0;
        UpdateDisplay ();
    }

    /**
     * @brief Called to add the card to the slot,
     *        such as when drawn from the stock pile.
     */
    public AddCardToHand (integer cardno)
    {
        this.cardno = cardno;
        this.selected = 0;
        this.UpdateDisplay ();
    }

    /**
     * @brief Player has clicked on a card in his hand (HUD),
     *        presumably to either select or deselect it.
     *        But if they click on an empty slot with just one other 
     *        card selected, we move that card to this slot.
     */
    public override Touched (key byavuuid)
    {
        if (byavuuid == player.avuuid) {
            if (cardno >= 0) {
                SendAvErrMsg (byavuuid, "... [ " + CardName (cardno) + " ]");
                selected = !selected;
                UpdateDisplay ();
            } else {
                array selectedcards = player.GetSelectedCards ();
                if (selectedcards.count == 1) {
                    PlayerCardPanel oldslot = (PlayerCardPanel)selectedcards.value (0);
                    integer cardno = oldslot.cardno;
                    oldslot.RemoveFromHand ();
                    this.AddCardToHand (cardno);
                }
            }
        }
    }

    /**
     * @brief HUD was detached.
     */
    public override Detached ()
    {
        if (player != undef) player.UnheardFrom ();
    }

    /**
     * @brief Update display to reflect current object state.
     */
    public UpdateDisplay ()
    {
        vector color = <0,0,0>;                     // black
        if (cardno >= 0) {
            color = <1,1,1>;                        // white
            if (selected) color = <0.75,0.75,1.0>;  // sky blue
        }
        llRegionSayTo (id, RUMMY_CHANNEL, "colr" + color);
        DisplayCard (cardno);
    }
}

/***************\
 *  Utilities  *
\***************/


/**
 * @brief Common for all clickable boxes
 */
class CardPanel {
    public key id;                           // uuid of box element
    public integer unansweredPings;
    public abstract Touched (key byavuuid);  // called when element touched
    public virtual Detached () { }           // called when element detached

    private integer lastdisplayedcardno = -99;
    public DisplayCard (integer cardno)      // utility to display card in box
    {
        if (cardno != lastdisplayedcardno) {
            if (cardno < 0) {
                llRegionSayTo (id, RUMMY_CHANNEL, "txur" + TEXTURE_BLANK);
            } else {
                string url = "http://www.outerworldapps.com/cards-classic/";
                url += "a23456789tjqk"[cardno%13];
                url += "schd"[cardno/13];
                url += ".png";
                llRegionSayTo (id, RUMMY_CHANNEL, "sdtu" + url);
            }
            lastdisplayedcardno = cardno;
        }
    }
}


/**
 * @brief Send the given avatar a message, one way or another.
 */
SendAvErrMsg (key avuuid, string message)
{
    try {
        llRegionSayTo (avuuid, PUBLIC_CHANNEL, message);
    } catch (exception e1) {
        try {
            llInstantMessage (avuuid, message);
        } catch (exception e2) {
            string avname = llKey2Name (avuuid);
            if (avname == "") avname = avuuid;
            llSay (PUBLIC_CHANNEL, avname + ": " + message);
        }
    }
}


/**
 * @brief Get card name string for a given card number.
 */
list ranks = [ "Ace", "Two", "Tree", "Four", "Fife", "Six", "Seven", "Eight", "Niner", "Ten", "Jack", "Queen", "King" ];
list suits = [ "Spades", "Clubs", "Hearts", "Diamonds" ];
string CardName (integer cardno)
{
    if (cardno < 0) return "(none)";
    return ranks[cardno%13] + " of " + suits[cardno/13];
}


/**
 * @brief Insert needed escapes and enclose string in quotes suitable for JSON.
 */
string JSONString (string str)
{
    integer len = llStringLength (str);
    char[] out = new char[] (len * 2 + 2);
    integer j = 0;
    out[j++] = '"';
    for (integer i = 0; i < len; i ++) {
        char c = str[i];
        switch (c) {
            case  8: {
                out[j++] = '\\';
                out[j++] = 'b';
                break;
            }
            case  9: {
                out[j++] = '\\';
                out[j++] = 't';
                break;
            }
            case 10: {
                out[j++] = '\\';
                out[j++] = 'n';
                break;
            }
            case 12: {
                out[j++] = '\\';
                out[j++] = 'f';
                break;
            }
            case 13: {
                out[j++] = '\\';
                out[j++] = 'r';
                break;
            }
            case '"': {
                out[j++] = '\\';
                out[j++] = '"';
                break;
            }
            case '\\': {
                out[j++] = '\\';
                out[j++] = '\\';
                break;
            }
            default: {
                out[j++] = c;
                break;
            }
        }
    }
    out[j++] = '"';
    return xmrChars2String (out, 0, j);
}
