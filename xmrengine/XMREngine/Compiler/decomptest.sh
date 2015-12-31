#!/bin/bash
#
#  Decompiler test script
#  Put .lsl files in zz subdirectory
#
#  [ -gt 'startwith' ]
#
function doone
{
    n=0
    while read lslfile
    do
        if grep -q -i xmroption zz/$lslfile
        then
            continue
        fi
        if grep -q '&&&' zz/$lslfile
        then
            continue
        fi
        if grep -q '|||' zz/$lslfile
        then
            continue
        fi
        n=$((n+1))
        echo $n: $lslfile
        grep -v '^[*][*][*][*]' zz/$lslfile > zz.lsl
        if mono --debug xmrengcomp_secret.exe -out zz.obj -asm zz.asm -src zz.src zz.lsl
        then
            if mono --debug xmrengcomp_secret.exe -out zz.obj -asm zz.asm zz.src
            then
                echo SUCCESS
            else
                ## this one failed so fix decompiler and retry
                echo FAILURE
            fi
        else
            ## original doesn't compile so don't bother with it again
            mv zz/$lslfile zz/$lslfile.badlsl
        fi
    done
}

set -e
export MONO_PATH=../../../bin_090
ls zz | grep '[.]lsl$' | truesort "$@" | doone
