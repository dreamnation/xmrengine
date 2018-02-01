xmroption norighttoleft;
xmroption objects;

partial class Abc<T> : Iface1, Marker {
    public T one;
    public IFace2() : Iface2 { llOwnerSay ("IFace2()"); }
}

default {
    state_entry ()
    {
        Abc<integer> abc = new Abc<integer> ();
        abc.one = 1;
        abc.two = 2;
        llOwnerSay ("one=" + abc.one);
        llOwnerSay ("two=" + abc.two);
        abc.IFace1();
        abc.IFace2();
    }
}

partial class Abc<T> : Iface2 {
    public T two;
    public IFace1() : Iface1 { llOwnerSay ("IFace1()"); }
}

interface Iface1 { IFace1(); }
interface Iface2 { IFace2(); }
interface Marker { }

