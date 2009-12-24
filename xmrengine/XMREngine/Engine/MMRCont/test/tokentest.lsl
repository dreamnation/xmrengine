//OPEN SOURCE POSEBALL SCRIPT BY QD MUGGINS
//PLEASE LEAVE AS FULL PERM =) <3 xx

key gotperms=NULL_KEY;
key requesting=NULL_KEY;

vector offset=<0.1,0.1,0.1>;

show(integer on) {
 
    if (on) {
    
        llSetAlpha(1.0,ALL_SIDES);    
        
    } else {
        
        llSetAlpha(0.0,ALL_SIDES);
        
    }
    
}

animate(integer start) {
    integer u=llGetInventoryNumber(INVENTORY_ANIMATION);
    if (u>0) {
        if (start==TRUE) {
            llStartAnimation(llGetInventoryName(INVENTORY_ANIMATION,0));
            //Hack for opensim
            llSleep(0.5);
            llStopAnimation(llGetInventoryName(INVENTORY_ANIMATION,0));
            llStartAnimation(llGetInventoryName(INVENTORY_ANIMATION,0));
        } else {
            
            llStopAnimation(llGetInventoryName(INVENTORY_ANIMATION,0));
        }
    } else {
        if (start==TRUE) {
            llInstantMessage(gotperms,"Sorry, this pose ball doesn't seem to contain an animation. Gosh darn it!"); 
        }   
    }
    
    
}

integer change_to_dead_state ()
{
   llSay (0, "changing to dead state");
   state dead;
   llSay (1, "never executes this");
   return 99;
}

XMROption arrays;

default
{
  state_entry() {
      
    llSitTarget(offset,ZERO_ROTATION);   
    llListen( 1, "", NULL_KEY, "" );  

    integer i;
    for (i = 0; i < 5; i ++) {
       llSay (0, (string)i);
    }
    while (i) {
       llSay (i, "never say die");
       -- i;
    }

    array ar;
    ar[0] = 5;
    ar[1] = "astring";
    ar[2] = <1,2,3>;
    ar["phony"] = "bologna";
    ar[3] = <4,5,6,7>;
    ar[4] = 3.5;

    llSay (99, "count is now " + (string)ar.count);
    for (i = 0; i < 5; i ++) {
        llSay (i, "ar[i]=" + (string)ar[i]);
    }

    ar[3] = undef;
    llSay (99, "count is now " + (string)ar.count);

    object k;
    object v;

    foreach (k,v in ar) {
        llSay (0, (string)k + " => " + (string)v);
        if (v is float)    llSay (1, "float");
        if (v is integer)  llSay (1, "integer");
     ///   if (v is key)      llSay (1, "key");
        if (v is rotation) llSay (1, "rotation");
        if (v is string)   llSay (1, "string");
        if (v is undef)    llSay (1, "undef");
        if (v is vector)   llSay (1, "vector");
    }

    llSay (2, (string)(ar[3] is undef));
    llSay (2, (string)(ar[4] is undef));
    llSay (2, (string)(ar[5] is undef));

    string st;
    integer zz = 1;

    for (i = 0;; i ++, zz *= 2) {
        k = ar.index (i);
        v = ar.value (i);
        if (k is undef) jump done;
        llSay (3, (string)k + " => " + (string)v);
        st = (st = "") + st + "," + (string)v;
    }
@done;
    llSay (4, "st=" + st);
    llSay (4, "zz=" + (string)zz);

    float inlineTest = llPow(3.0,4);
    llSay (5, "inlineTest=" + (string)inlineTest);
    llSleep (1);

    integer j = change_to_dead_state ();
    llSay (j, "I say, this doesn't ever execute!");

  }
  run_time_permissions(integer p) {
    
        if (p & PERMISSION_TRIGGER_ANIMATION) {
            if (requesting!=NULL_KEY) {
                llStopAnimation("sit");
                gotperms=requesting;
                animate(TRUE);
                   
                    
            }    
                
        }   else {
            
            gotperms=NULL_KEY;     
        }  
      
  }
  listen(integer channel, string name, key id, string message)
 {
    
        if (message=="show") {
            show(TRUE);
        } else if (message=="hide") {
            show(FALSE);    
        }
      
  }
  touch_start(integer h) {
  
      if (llGetAlpha(0)==1.0) {
          show(FALSE);
     } else {
            show(TRUE);    
    }
      
  }
   changed( integer c) {
       
       if (c & CHANGED_LINK) { 
           key sat=llAvatarOnSitTarget();
            if (sat!=NULL_KEY) {
                        show(FALSE);            
                    if (gotperms == sat) {
                        
                        animate(TRUE);    
                    } else {
                        requesting=sat;
                        llRequestPermissions(sat,PERMISSION_TRIGGER_ANIMATION);    
                    }
                
            } else {
                show(TRUE);
                requesting=NULL_KEY;
                if (gotperms!=NULL_KEY) {
                    //Is target still here?
                    if (llGetListLength(llGetObjectDetails(gotperms,[OBJECT_POS]))>0) {
                        animate(FALSE);   
                    }
                }
                gotperms=NULL_KEY;    
            }
               
        }
       
    }
}

state dead {
   state_entry()
   {
      llSay (0, "we're dead!");
   }
}


/**TEST
state_entry() {
   llSitTarget(<.1,.1,.1>, <0,0,0,1>);
   llListen(1, "", "00000000-0000-0000-0000-000000000000", "") 0;

   llSay (0, "0");
   llSay (0, "1");
   llSay (0, "2");
   llSay (0, "3");
   llSay (0, "4");

   llSay (5, "never say die");
   llSay (4, "never say die");
   llSay (3, "never say die");
   llSay (2, "never say die");
   llSay (1, "never say die");

   llSay (99, "count is now 6");
   llSay (0, "ar[i]=5");
   llSay (1, "ar[i]=astring");
   llSay (2, "ar[i]=<1.000000,2.000000,3.000000>");
   llSay (3, "ar[i]=<4.000000,5.000000,6.000000,7.000000>");
   llSay (4, "ar[i]=3.5");

   llSay (99, "count is now 5");
   llSay (0, "0 => 5");
   llSay (1, "integer");
   llSay (0, "1 => astring");
   llSay (1, "string");
   llSay (0, "2 => <1.000000,2.000000,3.000000>");
   llSay (1, "vector");
   llSay (0, "phony => bologna");
   llSay (1, "string");
   llSay (0, "4 => 3.5");
   llSay (1, "float");

   llSay (2, "true");
   llSay (2, "false");
   llSay (2, "true");

   llSay (3, "0 => 5");
   llSay (3, "1 => astring");
   llSay (3, "2 => <1.000000,2.000000,3.000000>");
   llSay (3, "phony => bologna");
   llSay (3, "4 => 3.5");

   llSay (4, "st=,5,astring,<1.000000,2.000000,3.000000>,bologna,3.5");
   llSay (4, "zz=32");
   llSay (5, "inlineTest=81");
   llSleep (1);

   llSay (0, "changing to dead state");
   llSay (0, "we're dead!");

} : dead
TEST**/
