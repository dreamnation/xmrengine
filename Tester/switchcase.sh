export MONO_PATH=../../../bin
mono --debug xmrengcomp_secret.exe -out switchcase.xmrbin -asm switchcase.xmrasm switchcase.lsl
mono --debug xmrengcomp_secret.exe -decode -asm out.xmrasm -src out.lsl switchcase.xmrbin
scp switchcase.xmrbin mrieker@ws.nii.net:www.o3one.org/docs/stuff/switchcase.xmrbin
