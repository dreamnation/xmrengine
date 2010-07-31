XMROption advFlowCtl;

constant C_ZERO = 99-90-9;
constant C_42 = 21*2;
constant C_ONE = 5-4;
constant C_M42 = -C_42;

default {
    state_entry()
    {
        llOwnerSay("touch to start!");
    }

    touch_start(integer n)
    {
        llOwnerSay("counting...");
        for (integer i = -50; i <= 50; i ++) {
            PrintSomething(i);
            //llSleep(0.5);
        }
        llOwnerSay("all done!");
    }
}

PrintSomething(integer x)
{
    switch (x) {
    case 46:
        llOwnerSay((string)x + ": forty-six/ft zero");
    case C_ZERO:
        llOwnerSay((string)x + ": zero");
        break;
    case C_42:
        llOwnerSay((string)x + ": forty-two");
        break;
    case 13 ... 13+6:
        llOwnerSay((string)x + ": the teens");
        break;
    case 43:
        llOwnerSay((string)x + ": forty-three/ft 44");
    case 44:
        llOwnerSay((string)x + ": forty-four");
        break;
    case C_M42:
        llOwnerSay((string)x + ": minus forty-two");
        break;
    case C_ONE:
        llOwnerSay((string)x + ": one/ft default");
    default:
        llOwnerSay((string)x + ": default: " + x);
    }
}
