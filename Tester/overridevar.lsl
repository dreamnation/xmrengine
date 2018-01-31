say(string str)
{
    {
        llSay(0, str);
    }

    string str = "Else";
    llSay(0, str);
}

default
{
    state_entry()
    {
        say("Test");
    }
}
