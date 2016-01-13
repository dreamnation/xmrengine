#!/bin/bash
#
#  See if Mono.Tasklets.dll contains the given class definition
#
#    $1 = monobindir
#    $2 = tasklets dll filename
#    $3 = class name
#
if [ ! -x $1/monodis ]
then
    echo 0
    exit
fi
if [ ! -f $2 ]
then
    echo 0
    exit
fi
$1/monodis --typedef $2 | grep -q Mono.Tasklets.$3
echo $(( $? == 0 ))
