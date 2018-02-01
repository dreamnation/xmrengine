xmroption norighttoleft;

default {
    state_entry ()
    {
        list ql = [ PARCEL_MEDIA_COMMAND_TEXTURE, 
                    PARCEL_MEDIA_COMMAND_URL, 
                    PARCEL_MEDIA_COMMAND_TYPE, 
                    PARCEL_MEDIA_COMMAND_SIZE, 
                    PARCEL_MEDIA_COMMAND_DESC ];
        list qr = llParcelMediaQuery (ql);
        llOwnerSay ((string)qr);

        list cl = [ PARCEL_MEDIA_COMMAND_STOP, 
                    PARCEL_MEDIA_COMMAND_PAUSE, 
                    PARCEL_MEDIA_COMMAND_PLAY, 
                    PARCEL_MEDIA_COMMAND_LOOP, 
                    PARCEL_MEDIA_COMMAND_TEXTURE,    "textureUUID", 
                    PARCEL_MEDIA_COMMAND_URL,        "urlString", 
                    PARCEL_MEDIA_COMMAND_TIME,       11.45, 
                    PARCEL_MEDIA_COMMAND_AGENT,      "agentUUID", 
                    PARCEL_MEDIA_COMMAND_UNLOAD, 
                    PARCEL_MEDIA_COMMAND_AUTO_ALIGN, 1, 
                    PARCEL_MEDIA_COMMAND_TYPE,       "mimeType", 
                    PARCEL_MEDIA_COMMAND_SIZE,       29, 92, 
                    PARCEL_MEDIA_COMMAND_DESC,       "descrip" ];
        llParcelMediaCommandList (cl);
    }
}
