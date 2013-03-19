// cardpanel
// - displays a texture on all sides of the object
// - also sends a message when touched

xmroption advflowctl;
xmroption arrays;
xmroption norighttoleft;

constant RUMMY_CHANNEL = -646830961;

array knownImages;
array knownTexts;
key serveruuid;

default {
    state_entry ()
    {
        Restart ();
    }

    attach (key id)
    {
        if ((serveruuid != "") && (id == NULL_KEY)) {
            llRegionSayTo (serveruuid, RUMMY_CHANNEL, "DTCH");
        }
    }

    timer ()
    {
        if (serveruuid == "") {
            llRegionSay (RUMMY_CHANNEL, "RGCP" + llGetOwner ());
        }
    }

    listen (integer channel, string name, key id, string message)
    {
        switch (xmrSubstring (message, 0, 4)) {

            // set tinting color
            case "colr": {
                llSetColor ((vector)xmrSubstring (message, 4), ALL_SIDES);
                break;
            }

            // server wants to know if we are still here
            case "ping": {
                llRegionSayTo (id, RUMMY_CHANNEL, "PONG");
                break;
            }

            // RummyBoard acknowledgement so stop trying to register
            case "rgcp": {
                serveruuid = id;
                llSetTimerEvent (0);
                break;
            }

            // RummyBoard is resetting so restart to re-register
            case "rstr": {
                Restart ();
                break;
            }

            // display some text
            case "sdtd": {
                string text = xmrSubstring (message, 4);
                string uuid = (string)knownTexts[text];
                if (uuid == undef) {
                    array json   = osParseJSON (xmrSubstring (message, 4));
                    string data  = (string)json["data"];
                    string extra = (string)json["extra"];
                    if (extra == undef) extra = "";
                    uuid = osSetDynamicTextureData ("", "vector", data, extra, 0);
                    // knownTexts[text] = uuid; // caching does not work - get blank text
                } else {
                    llSetTexture (uuid, ALL_SIDES);
                }
                break;
            }

            // display an image
            case "sdtu": {
                string url  = xmrSubstring (message, 4);
                string uuid = (string)knownImages[url];
                if (uuid == undef) {
                    uuid = osSetDynamicTextureURL ("", "image", xmrSubstring (message, 4), "", 0);
                    // knownImages[url] = uuid; // caching does not work - get blank image
                } else {
                    llSetTexture (uuid, ALL_SIDES);
                }
                break;
            }

            // display a texture given its uuid (eg, TEXTURE_BLANK)
            case "txur": {
                llSetTexture (xmrSubstring (message, 4), ALL_SIDES);
                break;
            }
        }
    }

    // when touched, just send a message to RummyBoard and let it handle it
    touch_start ()
    {
        if (serveruuid != "") {
            llRegionSayTo (serveruuid, RUMMY_CHANNEL, "CPTC" + llDetectedKey (0));
        }
    }
}


Restart ()
{
    serveruuid = "";
    llSetTexture (TEXTURE_BLANK, ALL_SIDES);
    llListen (RUMMY_CHANNEL, "", "", "");
    llRegionSay (RUMMY_CHANNEL, "RGCP" + llGetOwner ());
    llSetTimerEvent (0.25);
}
