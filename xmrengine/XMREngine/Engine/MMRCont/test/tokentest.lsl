//OPEN SOURCE POSEBALL SCRIPT BY QD MUGGINS
//PLEASE LEAVE AS FULL PERM =) <3 xx

key gotperms=NULL_KEY;
key requesting=NULL_KEY;

vector offset=<0.0,0.0,0.0>;

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

/**TEST
state_entry() {
   llSitTarget(<0,0,0>, <0,0,0,1>);
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

} : default
TEST**/
