xmroption arrays;
xmroption chars;
xmroption objects;
xmroption trycatch;

interface Intf1 { }
interface Intf2 { }
class Class0 { }
class Class1 : Class0, Intf1 { }
class Class2 : Class0, Intf2 { }

default {
    state_entry()
    {
        array     ar;
        Class0    c0;
        Class1    c1;
        Class2    c2;
        char      ch;
        exception ex;
        float     fl;
        Intf1     i1;
        Intf2     i2;
        integer   ii;
        list      li;
        object    ob;
        rotation  ro;
        string    st;
        vector    ve;

        ar = c0;
        ar = c1;
        ar = c2;
        ar = ch;
        ar = ex;
        ar = fl;
        ar = i1;
        ar = i2;
        ar = ii;
        ar = li;
        ar = ob;
        ar = ro;
        ar = st;
        ar = ve; 

        c0 = ar;
        c0 = c1;
        c0 = c2;
        c0 = ch;
        c0 = ex;
        c0 = fl;
        c0 = i1;
        c0 = i2;
        c0 = ii;
        c0 = li;
        c0 = ob;
        c0 = ro;
        c0 = st;
        c0 = ve;

        c1 = ar;
        c1 = c0;
        c1 = c2;
        c1 = ch;
        c1 = ex;
        c1 = fl;
        c1 = i1;
        c1 = i2;
        c1 = ii;
        c1 = li;
        c1 = ob;
        c1 = ro;
        c1 = st;
        c1 = ve;

        c2 = ar;
        c2 = c0;
        c2 = c1;
        c2 = ch;
        c2 = ex;
        c2 = fl;
        c2 = i1;
        c2 = i2;
        c2 = ii;
        c2 = li;
        c2 = ob;
        c2 = ro;
        c2 = st;
        c2 = ve;

        ch = ar;
        ch = c0;
        ch = c1;
        ch = c2;
        ch = ex;
        ch = fl;
        ch = i1;
        ch = i2;
        ch = ii;
        ch = li;
        ch = ob;
        ch = ro;
        ch = st;
        ch = ve;

        ex = ar;
        ex = c0;
        ex = c1;
        ex = c2;
        ex = ch;
        ex = fl;
        ex = i1;
        ex = i2;
        ex = ii;
        ex = li;
        ex = ob;
        ex = ro;
        ex = st;
        ex = ve;

        fl = ar;
        fl = c0;
        fl = c1;
        fl = c2;
        fl = ch;
        fl = ex;
        fl = i1;
        fl = i2;
        fl = ii;
        fl = li;
        fl = ob;
        fl = ro;
        fl = st;
        fl = ve;

        i1 = ar;
        i1 = c0;
        i1 = c1;
        i1 = c2;
        i1 = ch;
        i1 = ex;
        i1 = fl;
        i1 = i2;
        i1 = ii;
        i1 = li;
        i1 = ob;
        i1 = ro;
        i1 = st;
        i1 = ve;

        i2 = ar;
        i2 = c0;
        i2 = c1;
        i2 = c2;
        i2 = ch;
        i2 = ex;
        i2 = fl;
        i2 = i1;
        i2 = ii;
        i2 = li;
        i2 = ob;
        i2 = ro;
        i2 = st;
        i2 = ve;

        ii = ar;
        ii = c0;
        ii = c1;
        ii = c2;
        ii = ch;
        ii = ex;
        ii = fl;
        ii = i1;
        ii = i2;
        ii = li;
        ii = ob;
        ii = ro;
        ii = st;
        ii = ve;

        li = ar;
        li = c0;
        li = c1;
        li = c2;
        li = ch;
        li = ex;
        li = fl;
        li = i1;
        li = i2;
        li = ii;
        li = ob;
        li = ro;
        li = st;
        li = ve;

        ob = ar;
        ob = c0;
        ob = c1;
        ob = c2;
        ob = ch;
        ob = ex;
        ob = fl;
        ob = i1;
        ob = i2;
        ob = ii;
        ob = li;
        ob = ro;
        ob = st;
        ob = ve;

        ro = ar;
        ro = c0;
        ro = c1;
        ro = c2;
        ro = ch;
        ro = ex;
        ro = fl;
        ro = i1;
        ro = i2;
        ro = ii;
        ro = li;
        ro = ob;
        ro = st;
        ro = ve;

        st = ar;
        st = c0;
        st = c1;
        st = c2;
        st = ch;
        st = ex;
        st = fl;
        st = i1;
        st = i2;
        st = ii;
        st = li;
        st = ob;
        st = ro;
        st = ve;

        ve = ar;
        ve = c0;
        ve = c1;
        ve = c2;
        ve = ch;
        ve = ex;
        ve = fl;
        ve = i1;
        ve = i2;
        ve = ii;
        ve = li;
        ve = ob;
        ve = ro;
        ve = st;

        ar = undef;
        c0 = undef;
        c1 = undef;
        c2 = undef;
        ch = undef;
        ex = undef;
        fl = undef;
        i1 = undef;
        i2 = undef;
        ii = undef;
        li = undef;
        ob = undef;
        ro = undef;
        st = undef;
        ve = undef;

        ////////////////////////

        arCall (ch);
        arCall (ex);
        arCall (fl);
        arCall (ii);
        arCall (li);
        arCall (ob);
        arCall (ro);
        arCall (st);
        arCall (ve); 

        chCall (ar);
        chCall (ex);
        chCall (fl);
        chCall (ii);
        chCall (li);
        chCall (ob);
        chCall (ro);
        chCall (st);
        chCall (ve);

        exCall (ar);
        exCall (ch);
        exCall (fl);
        exCall (ii);
        exCall (li);
        exCall (ob);
        exCall (ro);
        exCall (st);
        exCall (ve);

        flCall (ar);
        flCall (ch);
        flCall (ex);
        flCall (ii);
        flCall (li);
        flCall (ob);
        flCall (ro);
        flCall (st);
        flCall (ve);

        iiCall (ar);
        iiCall (ch);
        iiCall (ex);
        iiCall (fl);
        iiCall (li);
        iiCall (ob);
        iiCall (ro);
        iiCall (st);
        iiCall (ve);

        liCall (ar);
        liCall (ch);
        liCall (ex);
        liCall (fl);
        liCall (ii);
        liCall (ob);
        liCall (ro);
        liCall (st);
        liCall (ve);

        obCall (ar);
        obCall (ch);
        obCall (ex);
        obCall (fl);
        obCall (ii);
        obCall (li);
        obCall (ro);
        obCall (st);
        obCall (ve);

        roCall (ar);
        roCall (ch);
        roCall (ex);
        roCall (fl);
        roCall (ii);
        roCall (li);
        roCall (ob);
        roCall (st);
        roCall (ve);

        stCall (ar);
        stCall (ch);
        stCall (ex);
        stCall (fl);
        stCall (ii);
        stCall (li);
        stCall (ob);
        stCall (ro);
        stCall (ve);

        veCall (ar);
        veCall (ch);
        veCall (ex);
        veCall (fl);
        veCall (ii);
        veCall (li);
        veCall (ob);
        veCall (ro);
        veCall (st);

        arCall (undef);
        chCall (undef);
        exCall (undef);
        flCall (undef);
        iiCall (undef);
        liCall (undef);
        obCall (undef);
        roCall (undef);
        stCall (undef);
        veCall (undef);
    }
}

arCall (array     ar) { }
chCall (char      ch) { }
exCall (exception ex) { }
flCall (float     fl) { }
iiCall (integer   ii) { }
liCall (list      li) { }
obCall (object    ob) { }
roCall (rotation  ro) { }
stCall (string    st) { }
veCall (vector    ve) { }

