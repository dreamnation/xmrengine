// http://wiki.secondlife.com/wiki/LSL_Language_Test

integer gTestsPassed = 0;
integer gTestsFailed = 0;
 
testPassed(string description, string actual, string expected)
{
    ++gTestsPassed;
    //llSay(0, description);
}
 
testFailed(string description, string actual, string expected)
{
    ++gTestsFailed;
    llSay(0, "FAILED!: " + description + " (" + actual + " expected " + expected + ")");
}
 
ensureTrue(string description, integer actual)
{
    if(actual)
    {
        testPassed(description, (string) actual, (string) TRUE);
    }
    else
    {
        testFailed(description, (string) actual, (string) TRUE);
    }
}
 
ensureFalse(string description, integer actual)
{
    if(actual)
    {
        testFailed(description, (string) actual, (string) FALSE);
    }
    else
    {
        testPassed(description, (string) actual, (string) FALSE);
    }
}
 
ensureIntegerEqual(string description, integer actual, integer expected)
{
    if(actual == expected)
    {
        testPassed(description, (string) actual, (string) expected);
    }
    else
    {
        testFailed(description, (string) actual, (string) expected);
    }
}
 
integer floatEqual(float actual, float expected)
{
    float error = llFabs(expected - actual);
    float epsilon = 0.001;
    if(error > epsilon)
    {
        llSay(0,"Float equality delta " + (string)error);
        return FALSE;
    }
    return TRUE;
}
 
ensureFloatEqual(string description, float actual, float expected)
{
    if(floatEqual(actual, expected))
    {
        testPassed(description, (string) actual, (string) expected);
    }
    else
    {
        testFailed(description, (string) actual, (string) expected);
    }
}
 
ensureStringEqual(string description, string actual, string expected)
{
    if(actual == expected)
    {
        testPassed(description, (string) actual, (string) expected);
    }
    else
    {
        testFailed(description, (string) actual, (string) expected);
    }
}
 
ensureVectorEqual(string description, vector actual, vector expected)
{
    if(floatEqual(actual.x, expected.x) &&
        floatEqual(actual.y, expected.y) &&
        floatEqual(actual.z, expected.z))
    {
        testPassed(description, (string) actual, (string) expected);
    }
    else
    {
        testFailed(description, (string) actual, (string) expected);
    }
}
 
ensureRotationEqual(string description, rotation actual, rotation expected)
{
    if(floatEqual(actual.x, expected.x) &&
        floatEqual(actual.y, expected.y) &&
        floatEqual(actual.z, expected.z) &&
        floatEqual(actual.s, expected.s))
    {
        testPassed(description, (string) actual, (string) expected);
    }
    else
    {
        testFailed(description, (string) actual, (string) expected);
    }
}
 
ensureListEqual(string description, list actual, list expected)
{
    if(actual == expected)
    {
        testPassed(description, (string) actual, (string) expected);
    }
    else
    {
        testFailed(description, (string) actual, (string) expected);
    }
}
 
integer gInteger = 5;
float gFloat = 1.5;
string gString = "foo";
vector gVector = <1, 2, 3>;
rotation gRot = <1, 2, 3, 4>;
list gList = [1, 2, 3];
 
integer testReturn()
{
    return 1;
}
 
float testReturnFloat()
{
    return 1.0;
}
 
string testReturnString()
{
    return "Test string";
}
 
list testReturnList()
{
    return [1,2,3];
}
 
vector testReturnVector()
{
    return <1,2,3>;
}
 
rotation testReturnRotation()
{
    return <1,2,3,4>;
}
 
vector testReturnVectorNested()
{
    return testReturnVector();
}
 
vector testReturnVectorWithLibraryCall()
{
    llSin(0);
    return <1,2,3>;
}
 
rotation testReturnRotationWithLibraryCall()
{
    llSin(0);
    return <1,2,3,4>;
}
 
integer testParameters(integer param)
{
    param = param + 1;
    return param;
}
 
integer testRecursion(integer param)
{
    if(param <= 0)
    {
        return 0;
    }
    else
    {
        return testRecursion(param - 1);
    }
}
 
string testExpressionLists(list l)
{
    return "foo" + (string)l;
}
 
tests()
{
    // truth
    ensureIntegerEqual("TRUE", TRUE, TRUE);
    ensureIntegerEqual("FALSE", FALSE, FALSE);
 
    if(0.0)
    {
        testFailed("if(0.0)", "TRUE", "FALSE");
    }
    else
    {
        testPassed("if(0.0)", "TRUE", "TRUE");
    }
    if(0.000001)
    {
        testPassed("if(0.000001)", "TRUE", "TRUE");
    }
    else
    {
        testFailed("if(0.000001)", "TRUE", "FALSE");
    }
    if(0.9)
    {
        testPassed("if(0.9)", "TRUE", "TRUE");
    }
    else
    {
        testFailed("if(0.9)", "TRUE", "FALSE");
    }
    if(ZERO_VECTOR)
    {
        testFailed("if(ZERO_VECTOR)", "TRUE", "FALSE");
    }
    else
    {
        testPassed("if(ZERO_VECTOR)", "TRUE", "TRUE");
    }
    if(ZERO_ROTATION)
    {
        testFailed("if(ZERO_ROTATION)", "TRUE", "FALSE");
    }
    else
    {
        testPassed("if(ZERO_ROTATION)", "TRUE", "TRUE");
    }
 
    if(NULL_KEY)
    {
        testPassed("if(NULL_KEY)", "TRUE", "TRUE");
    }
    else
    {
        testFailed("if(NULL_KEY)", "TRUE", "FALSE");
    }
    if((key)NULL_KEY)
    {
        testFailed("if((key)NULL_KEY)", "TRUE", "FALSE");
    }
    else
    {
        testPassed("if((key)NULL_KEY)", "TRUE", "TRUE");
    }
 
    if("")
    {
        testFailed("if(\"\")", "TRUE", "FALSE");
    }
    else
    {
        testPassed("if(\"\")", "TRUE", "TRUE");
    }
 
    if([])
    {
        testFailed("if([])", "TRUE", "FALSE");
    }
    else
    {
        testPassed("if([])", "TRUE", "TRUE");
    }
 
    // equality
    ensureIntegerEqual("(TRUE == TRUE)", (TRUE == TRUE), TRUE);
    ensureIntegerEqual("(TRUE == FALSE)", (TRUE == FALSE), FALSE);
    ensureIntegerEqual("(FALSE == TRUE)", (FALSE == TRUE), FALSE);
    ensureIntegerEqual("(FALSE == FALSE)", (FALSE == FALSE), TRUE);
 
    // inequality
    ensureIntegerEqual("(TRUE != TRUE)", (TRUE != TRUE), FALSE);
    ensureIntegerEqual("(TRUE != FALSE)", (TRUE != FALSE), TRUE);
    ensureIntegerEqual("(FALSE != TRUE)", (FALSE != TRUE), TRUE);
    ensureIntegerEqual("(FALSE != FALSE)", (FALSE != FALSE), FALSE);
 
    // and
    ensureIntegerEqual("(TRUE && TRUE)", (TRUE && TRUE), TRUE);
    ensureIntegerEqual("(TRUE && FALSE)", (TRUE && FALSE), FALSE);
    ensureIntegerEqual("(FALSE && TRUE)", (FALSE && TRUE), FALSE);
    ensureIntegerEqual("(FALSE && FALSE)", (FALSE && FALSE), FALSE);
 
    // and
    ensureIntegerEqual("(1 && 2)", (1 && 2), TRUE);
    ensureIntegerEqual("(1 && 0)", (1 && 0), FALSE);
    ensureIntegerEqual("(0 && 2)", (0 && 2), FALSE);
    ensureIntegerEqual("(0 && 0)", (0 && 0), FALSE);
 
 
    // or
    ensureIntegerEqual("(TRUE || TRUE)", (TRUE || TRUE), TRUE);
    ensureIntegerEqual("(TRUE || FALSE)", (TRUE || FALSE), TRUE);
    ensureIntegerEqual("(FALSE || TRUE)", (FALSE || TRUE), TRUE);
    ensureIntegerEqual("(FALSE || FALSE)", (FALSE || FALSE), FALSE);
 
    // or
    ensureIntegerEqual("(1 || 2)", (1 || 2), TRUE);
    ensureIntegerEqual("(1 || 0)", (1 || 0), TRUE);
    ensureIntegerEqual("(0 || 2)", (0 || 2), TRUE);
    ensureIntegerEqual("(0 || 0)", (0 || 0), FALSE);
 
    // not
    ensureIntegerEqual("(! TRUE)", (! TRUE), FALSE);
    ensureIntegerEqual("(! FALSE)", (! FALSE), TRUE);
 
    // not 
    ensureIntegerEqual("(! 2)", (! 2), FALSE);
    ensureIntegerEqual("(! 0)", (! 0), TRUE);
 
 
    // greater than
    ensureIntegerEqual("(1 > 0)", (1 > 0), TRUE);
    ensureIntegerEqual("(0 > 1)", (0 > 1), FALSE);
    ensureIntegerEqual("(1 > 1)", (1 > 1), FALSE);
 
    // less than
    ensureIntegerEqual("(0 < 1)", (0 < 1), TRUE);
    ensureIntegerEqual("(1 < 0)", (1 < 0), FALSE);
    ensureIntegerEqual("(1 < 1)", (1 < 1), FALSE);
 
    // greater than or equal
    ensureIntegerEqual("(1 >= 0)", (1 >= 0), TRUE);
    ensureIntegerEqual("(0 >= 1)", (0 >= 1), FALSE);
    ensureIntegerEqual("(1 >= 1)", (1 >= 1), TRUE);
 
    // tess than or equal
    ensureIntegerEqual("(0 <= 1)", (0 <= 1), TRUE);
    ensureIntegerEqual("(1 <= 0)", (1 <= 0), FALSE);
    ensureIntegerEqual("(1 <= 1)", (1 <= 1), TRUE);
 
    // bitwise and
    ensureIntegerEqual("(10 & 25)", (10 & 25), 8);
 
    // bitwise or
    ensureIntegerEqual("(10 | 25)", (10 | 25), 27);
 
    // bitwise not
    ensureIntegerEqual("~10", ~10, -11);
 
    // xor
    ensureIntegerEqual("(10 ^ 25)", (10 ^ 25), 19);
 
    // right shift
    ensureIntegerEqual("(523 >> 2)", (523 >> 2), 130);
 
    // left shift
    ensureIntegerEqual("(523 << 2)", (523 << 2), 2092);
 
    // addition
    ensureIntegerEqual("(1 + 1)", (1 + 1), 2);
    ensureFloatEqual("(1 + 1.1)", (1 + 1.1), 2.1);
    ensureFloatEqual("(1.1 + 1)", (1.1 + 1), 2.1);
    ensureFloatEqual("(1.1 + 1.1)", (1.1 + 1.1), 2.2);
    ensureStringEqual("\"foo\" + \"bar\"", "foo" + "bar", "foobar");
    ensureVectorEqual("(<1.1, 2.2, 3.3> + <4.4, 5.5, 6.6>)", (<1.1, 2.2, 3.3> + <4.4, 5.5, 6.6>), <5.5, 7.7, 9.9>);
    ensureRotationEqual("(<1.1, 2.2, 3.3, 4.4> + <4.4, 5.5, 6.6, 3.3>)", (<1.1, 2.2, 3.3, 4.4> + <4.4, 5.5, 6.6, 3.3>), <5.5, 7.7, 9.9, 7.7>);
    ensureListEqual("([1] + 2)", ([1] + 2), [1,2]);
    ensureListEqual("([] + 1.5)", ([] + 1.5), [1.5]);
    ensureListEqual("([\"foo\"] + \"bar\")", (["foo"] + "bar"), ["foo", "bar"]);
    ensureListEqual("([] + <1,2,3>)", ([] + <1,2,3>), [<1,2,3>]);
    ensureListEqual("([] + <1,2,3,4>)", ([] + <1,2,3,4>), [<1,2,3,4>]);
 
    ensureListEqual("(1 + [2])", (1 + [2]), [1,2]);
    ensureListEqual("(1.0 + [2])", (1.0 + [2]), [1.0,2]);
    ensureListEqual("(1 + [2])", ("one" + [2]), ["one",2]);
    ensureListEqual("(<1.0,1.0,1.0,1.0> + [2])", (<1.0,1.0,1.0,1.0> + [2]), [<1.0,1.0,1.0,1.0>,2]);
    ensureListEqual("(<1.0,1.0,1.0> + [2])", (<1.0,1.0,1.0> + [2]), [<1.0,1.0,1.0>,2]);
 
    // list concatenation value semantics
    list a = [];
    list b = a;
    a += ["foo"];
    ensureListEqual("list a = []; list b = a; a += [\"foo\"]; a == [\"foo\"]", a, ["foo"]);
    ensureListEqual("list a = []; list b = a; a += [\"foo\"]; b == []", b, []);
 
    // list concatenation value semantics
    a = ["a"];
    b = ["b"];
    list c = a + b;
    ensureListEqual("a = [\"a\"]; b = [\"b\"]; list c = a + b; a == [\"a\"];", a, ["a"]);
    ensureListEqual("a = [\"a\"]; b = [\"b\"]; list c = a + b; b == [\"b\"];", b, ["b"]); 
    ensureListEqual("a = [\"a\"]; b = [\"b\"]; list c = a + b; c == [\"a\", \"b\"];", c, ["a", "b"]); 
 
    // subtraction
    ensureIntegerEqual("(1 - 1)", (1 - 1), 0);
    ensureFloatEqual("(1 - 0.5)", (1 - 0.5), 0.5);
    ensureFloatEqual("(1.5 - 1)", (1.5 - 1), 0.5);
    ensureFloatEqual("(2.2 - 1.1)", (2.2 - 1.1), 1.1);
    ensureVectorEqual("(<1.5, 2.5, 3.5> - <4.5, 5.5, 6.5>)", (<1.5, 2.5, 3.5> - <4.5, 5.5, 6.5>), <-3.0, -3.0, -3.0>);
    ensureRotationEqual("(<1.5, 2.5, 3.5, 4.5> - <4.5, 5.5, 6.5, 7.5>)", (<1.5, 2.5, 3.5, 4.5> - <4.5, 5.5, 6.5, 7.5>), <-3.0, -3.0, -3.0, -3.0>);
 
    // multiplication
    ensureIntegerEqual("(2 * 3)", (2 * 3), 6);
    ensureFloatEqual("(2 * 3.5)", (2 * 3.5), 7.0);
    ensureFloatEqual("(2.5 * 3)", (2.5 * 3), 7.5);
    ensureFloatEqual("(2.5 * 3.5)", (2.5 * 3.5), 8.75);
    ensureVectorEqual("(<1.1, 2.2, 3.3> * 2)", (<1.1, 2.2, 3.3> * 2), <2.2, 4.4, 6.6>);
    ensureVectorEqual("(2 * <1.1, 2.2, 3.3>)", (<1.1, 2.2, 3.3> * 2), (2 * <1.1, 2.2, 3.3>));
    ensureVectorEqual("(<2.2, 4.4, 6.6> * 2.0)", (<2.2, 4.4, 6.6> * 2.0), <4.4, 8.8, 13.2>);
    ensureVectorEqual("(2.0 * <2.2, 4.4, 6.6>)", (<2.2, 4.4, 6.6> * 2.0), (2.0 * <2.2, 4.4, 6.6>));
    ensureFloatEqual("<1,3,-5> * <4,-2,-1>", <1,3,-5> * <4,-2,-1>, 3.0);
    ensureVectorEqual("<-1,0,0> * <0, 0, 0.707, 0.707>", <-1,0,0> * <0, 0, 0.707, 0.707>, <0,-1, 0>);
    ensureRotationEqual("(<1.0, 2.0, 3.0, 4.0> * <5.0, 6.0, 7.0, 8.0>)", (<1.0, 2.0, 3.0, 4.0> * <5.0, 6.0, 7.0, 8.0>), <32.0, 32.0, 56.0, -6.0>);
 
 
    // division
    ensureIntegerEqual("(2 / 2)", (2 / 2), 1);
    ensureFloatEqual("(2.2 / 2)", (2.2 / 2), 1.1);
    ensureFloatEqual("(3 / 1.5)", (3 / 1.5), 2.0);
    ensureFloatEqual("(2.2 / 2.0)", (2.2 / 2.0), 1.1);
    ensureVectorEqual("(<1.0, 2.0, 3.0> / 2)", (<1.0, 2.0, 3.0> / 2), <0.5, 1.0, 1.5>);
    ensureVectorEqual("(<3.0, 6.0, 9.0> / 1.5)", (<3.0, 6.0, 9.0> / 1.5), <2.0, 4.0, 6.0>);
    ensureVectorEqual("<-1,0,0> / <0, 0, 0.707, 0.707>", <-1,0,0> / <0, 0, 0.707, 0.707>, <0,1, 0>);
    ensureRotationEqual("(<1.0, 2.0, 3.0, 4.0> / <5.0, 6.0, 7.0, 8.0>)", (<1.0, 2.0, 3.0, 4.0> / <5.0, 6.0, 7.0, 8.0>), <-16.0, 0.0, -8.0, 70.0>);
 
    // modulo
    ensureIntegerEqual("(3 % 1)", (3 % 1), 0);
    ensureVectorEqual("(<1.0, 2.0, 3.0> % <4.0, 5.0, 6.0>)", (<1.0, 2.0, 3.0> % <4.0, 5.0, 6.0>), <-3.0, 6.0, -3.0>);
 
    // assignment
    integer i = 1;
    ensureIntegerEqual("i = 1;", i, 1);
 
    // addition assignment
    i = 1;
    i += 1;
    ensureIntegerEqual("i = 1; i += 1;", i, 2);
 
    // subtraction assignment
    i = 1;
    i -= 1;
    ensureIntegerEqual("i = 1; i -= 1;", i, 0);
 
    // multiplication assignment integer *= integer
    i = 2;
    i *= 2;
    ensureIntegerEqual("i = 2; i *= 2;", i, 4);
 
    // multiplication assignment integer *= float
    i = 1;
    i *= 0.5;
    ensureIntegerEqual("i = 1; i *= 0.5;", i, 0);
 
    // division assignment
    i = 2;
    i /= 2;
    ensureIntegerEqual("i = 2; i /= 2;", i, 1);
 
    // check overflow behaviour
    i = 0x80000000 / -1;
    ensureIntegerEqual("i = 0x80000000 / -1;", i, -2147483648);
 
    // modulo assignment
    i = 3;
    i %= 1;
    ensureIntegerEqual("i = 3; i %= 1;", i, 0);
 
    // check overflow behaviour
    i = 0x80000000 % -1;
    ensureIntegerEqual("i = 0x80000000 & -1;", i, 0);
 
    // post increment.
    i = 1;
    ensureIntegerEqual("i = 1; (i == 2) && (i++ == 1)", (i == 2) && (i++ == 1), TRUE);
 
    // pre increment.
    i = 1;
    ensureIntegerEqual("i = 1; (i == 2) && (++i == 2)", (i == 2) && (++i == 2), TRUE);
 
    // post decrement.
    i = 2;
    ensureIntegerEqual("i = 2; (i == 1) && (i-- == 2)", (i == 1) && (i-- == 2), TRUE);
 
    // pre decrement.
    i = 2;
    ensureIntegerEqual("i = 2; (i == 1) && (--i == 1)", (i == 1) && (--i == 1), TRUE);
    i = 2; --i;
    ensureIntegerEqual("i = 2; --i", i, 1);
 
    // casting
    ensureFloatEqual("((float)2)", ((float)2), 2.0);
    ensureStringEqual("((string)2)", ((string)2), "2");
    ensureIntegerEqual("((integer) 1.5)", ((integer) 1.5), 1);
    ensureStringEqual("((string) 1.5)", ((string) 1.5), "1.500000");
    ensureIntegerEqual("((integer) \"0xF\")", ((integer) "0xF"), 15);
    ensureIntegerEqual("((integer) \"2\")", ((integer) "2"), 2);
    ensureFloatEqual("((float) \"1.5\")", ((float) "1.5"), 1.5);
    ensureVectorEqual("((vector) \"<1,2,3>\")", ((vector) "<1,2,3>"), <1,2,3>);
    ensureRotationEqual("((quaternion) \"<1,2,3,4>\")", ((quaternion) "<1,2,3,4>"), <1,2,3,4>);
    ensureStringEqual("((string) <1,2,3>)", ((string) <1,2,3>), "<1.00000, 2.00000, 3.00000>");
    ensureStringEqual("((string) <1,2,3,4>)", ((string) <1,2,3,4>), "<1.00000, 2.00000, 3.00000, 4.00000>");
    ensureStringEqual("((string) [1,2.5,<1,2,3>])", ((string) [1,2.5,<1,2,3>]), "12.500000<1.000000, 2.000000, 3.000000>");
 
    // while
    i = 0;
    while(i < 10) ++i;
    ensureIntegerEqual("i = 0; while(i < 10) ++i", i, 10);
 
    // do while
    i = 0;
    do {++i;} while(i < 10);
    ensureIntegerEqual("i = 0; do {++i;} while(i < 10);", i, 10);
 
    // for
    for(i = 0; i < 10; ++i);
    ensureIntegerEqual("for(i = 0; i < 10; ++i);", i, 10);
 
    // jump
    i = 1;
    jump SkipAssign;
    llSetText("Error", <1,0,0>, 1); // Inserting this library call causes a save point to be added.
    i = 2;
    @SkipAssign;
    ensureIntegerEqual("i = 1; jump SkipAssign; i = 2; @SkipAssign;", i, 1);
 
    // return
    ensureIntegerEqual("testReturn()", testReturn(), 1);
    ensureFloatEqual("testReturnFloat()", testReturnFloat(), 1.0);
    ensureStringEqual("testReturnString()", testReturnString(), "Test string");
    ensureVectorEqual("testReturnVector()", testReturnVector(), <1,2,3>);
    ensureRotationEqual("testReturnRotation()", testReturnRotation(), <1,2,3,4>);
    ensureVectorEqual("testReturnVectorNested()", testReturnVectorNested(), <1,2,3>);
    ensureVectorEqual("testReturnVectorWithLibraryCall()", testReturnVectorWithLibraryCall(), <1,2,3>);
    ensureRotationEqual("testReturnRotationWithLibraryCall()", testReturnRotationWithLibraryCall(), <1,2,3,4>);
 
    // parameters
    ensureIntegerEqual("testParameters(1)", testParameters(1), 2);
 
    // variable parameters
    i = 1;
    ensureIntegerEqual("i = 1; testParameters(i)", testParameters(i), 2);
 
    // recursion
    ensureIntegerEqual("testRecursion(10)", testRecursion(10), 0);
 
    // globals
    ensureIntegerEqual("gInteger", gInteger, 5);
    ensureFloatEqual("gFloat", gFloat, 1.5);
    ensureStringEqual("gString", gString, "foo");
    ensureVectorEqual("gVector", gVector, <1, 2, 3>);
    ensureRotationEqual("gRot", gRot, <1, 2, 3, 4>);
    ensureListEqual("gList", gList, [1, 2, 3]);
 
    // global assignment
    gInteger = 1;
    ensureIntegerEqual("gInteger = 1", gInteger, 1);
 
    gFloat = 0.5;
    ensureFloatEqual("gFloat = 0.5", gFloat, 0.5);
 
    gString = "bar";
    ensureStringEqual("gString = \"bar\"", gString, "bar");
 
    gVector = <3,3,3>;
    ensureVectorEqual("gVector = <3,3,3>", gVector, <3,3,3>);
 
    gRot = <3,3,3,3>;
    ensureRotationEqual("gRot = <3,3,3,3>", gRot, <3,3,3,3>);
 
    gList = [4,5,6];
    ensureListEqual("gList = [4,5,6]", gList, [4,5,6]);
 
    // negation
    gVector = <3,3,3>;
    ensureVectorEqual("-gVector = <-3,-3,-3>", -gVector, <-3,-3,-3>);
 
    gRot = <3,3,3,3>;
    ensureRotationEqual("-gRot = <-3,-3,-3,-3>", -gRot, <-3,-3,-3,-3>);
 
 
    // vector accessor
    vector v;
    v.x = 3;
    ensureFloatEqual("v.x", v.x, 3);
 
    // rotation accessor
    rotation q;
    q.s = 5;
    ensureFloatEqual("q.s", q.s, 5);
 
    // global vector accessor
    gVector.y = 17.5;
    ensureFloatEqual("gVector.y = 17.5", gVector.y, 17.5);
 
    // global rotation accessor
    gRot.z = 19.5;
    ensureFloatEqual("gRot.z = 19.5", gRot.z, 19.5);
 
    // list equality
    list l = (list) 5;
    list l2 = (list) 5;
    ensureListEqual("list l = (list) 5; list l2 = (list) 5", l, l2);
    ensureListEqual("list l = (list) 5", l, [5]);
    ensureListEqual("[1.5, 6, <1,2,3>, <1,2,3,4>]", [1.5, 6, <1,2,3>, <1,2,3,4>], [1.5, 6, <1,2,3>, <1,2,3,4>]);
 
    // String escaping
    ensureIntegerEqual("llStringLength(\"\\\") == 1", llStringLength("\\"), 1);
    ensureIntegerEqual("llStringLength(\"\\t\") == 4", llStringLength("\t"), 4);
    ensureIntegerEqual("llStringLength(\"\\n\") == 1", llStringLength("\n"), 1);
    ensureIntegerEqual("llStringLength(\"\\\"\") == 1", llStringLength("\""), 1);
 
    // Nested expression lists
    ensureStringEqual("testExpressionLists([testExpressionLists([]), \"bar\"]) == \"foofoobar\"", testExpressionLists([testExpressionLists([]), "bar"]), "foofoobar");
 
}
 
runTests()
{
    llResetTime();
    tests();
    llSay(0, "Ran " + (string)(gTestsPassed + gTestsFailed) + " tests in " + (string) llGetTime() + " seconds with " + (string)gTestsFailed + " failures");
 
    // reset globals  
    gInteger = 5;
    gFloat = 1.5;
    gString = "foo";
    gVector = <1, 2, 3>;
    gRot = <1, 2, 3, 4>;
    gList = [1, 2, 3];
    gTestsPassed = 0;
    gTestsFailed = 0;
}
 
default
{
    state_entry()
    {
        runTests();
    }
 
    touch_start(integer total_number)
    {
        runTests();
    }
}
