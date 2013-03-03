#!/bin/bash -v
BIN=../../../bin
gmcs -debug -target:library -out:$BIN/CommsTest.dll CommsTest.cs \
	-reference:$BIN/OpenMetaverse.dll \
	-reference:$BIN/OpenMetaverseTypes.dll \
	-reference:$BIN/Nini.dll \
	-reference:$BIN/log4net.dll \
	-reference:$BIN/OpenSim.Framework.dll \
	-reference:$BIN/OpenSim.Region.Framework.dll \
	-reference:$BIN/OpenSim.Region.ScriptEngine.Shared.dll \
	-reference:/home/kunta/Mono.Addins-binary-1.0/Mono.Addins.dll
