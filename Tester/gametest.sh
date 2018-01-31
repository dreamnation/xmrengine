#!/bin/bash -v
export MONO_PATH=../../../bin
mono --debug xmrengtest.exe -eventio \
	-linknum 1 controller.lsl \
	-linknum 2 player.lsl << EOF
1)touch_start(0)
0)llListRandomize:69827238
1)touch_start(0)
1)touch_start(0)
1)touch_start(0)
EOF
