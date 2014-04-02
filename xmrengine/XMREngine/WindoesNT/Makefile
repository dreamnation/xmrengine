COMMITHASH  := $(strip $(shell git log -1 | grep ^commit | sed 's/commit//g'))
COMMITDATE  := $(strip $(shell git log -1 --date=iso | grep ^Date: | sed 's/Date://g'))
COMMITCLEAN := $(strip $(shell git status | grep -q modified: ; echo $$?))

EXTHASHCODE := 538609101 ## WindoesNT hash different than mono ## $(shell cd ../Engine ; make -s -f makestrhashcode STR=world.dreamnation.net)

DEFS := -DEXTERNAL=$(EXTHASHCODE) -DHAVE_MONOCONTS=0 -DHAVE_MMRUTHREADS=0 -DSCRIPTBASECLASS=ScriptBaseClass

KILLITALL := $(shell rm -f *.cs)

default: xmrengwincsfiles.zip

# ------------------------------------------------------------------------------
#
# Create ZIP file for Windows sources
#
xmrengwincsfiles.zip: \
		XMREngine.cs           \
		XMREngCommit.cs        \
		IEventHandlers.cs      \
		MMRDelegateCommon.cs   \
		MMRInternalFuncDict.cs \
		MMRScriptBinOpStr.cs   \
		MMRScriptCodeGen.cs    \
		MMRScriptCollector.cs  \
		MMRScriptCompCommit.cs \
		MMRScriptCompValu.cs   \
		MMRScriptCompile.cs    \
		MMRScriptConsts.cs     \
		MMRScriptEventCode.cs  \
		MMRScriptInlines.cs    \
		MMRScriptMyILGen.cs    \
		MMRScriptObjCode.cs    \
		MMRScriptObjWriter.cs  \
		MMRScriptReduce.cs     \
		MMRScriptTokenize.cs   \
		MMRScriptTypeCast.cs   \
		MMRScriptVarDict.cs    \
		MMRWebRequest.cs       \
		XMRArray.cs            \
		XMREngXmrTestLs.cs     \
		XMREvents.cs           \
		XMRHeapTracker.cs      \
		XMRInstAbstract.cs     \
		XMRInstBackend.cs      \
		XMRInstCapture.cs      \
		XMRInstCtor.cs         \
		XMRInstMain.cs         \
		XMRInstMisc.cs         \
		XMRInstQueue.cs        \
		XMRInstRun.cs          \
		XMRInstSorpra.cs       \
		XMRSDTypeClObj.cs      \
		XMRScriptThread.cs     \
		XMRScriptUThread.cs    \
		xmrengine.csproj
	rm -f xmrengwincsfiles.zip
	zip xmrengwincsfiles.zip $^

MMRScriptCompCommit.cs: ../Compiler/MMRScriptCompCommit_$(COMMITHASH)_$(COMMITCLEAN).cs
	cp -a $< $@

XMREngCommit.cs: ../Engine/XMREngCommit_$(COMMITHASH)_$(COMMITCLEAN).cs
	cp -a $< $@

../Compiler/MMRScriptCompCommit_$(COMMITHASH)_$(COMMITCLEAN).cs: ../Compiler/MMRScriptCompCommit.cx
	cd ../Compiler ; make MMRScriptCompCommit_$(COMMITHASH)_$(COMMITCLEAN).cs

../Engine/XMREngCommit_$(COMMITHASH)_$(COMMITCLEAN).cs: ../Engine/XMREngCommit.cx
	cd ../Engine ; make XMREngCommit_$(COMMITHASH)_$(COMMITCLEAN).cs

%.cs: $(wildcard ../*/$*.cx) $(wildcard ../*/*.h)
	cpp -I../Compiler $(DEFS) -C -P -nostdinc -o $@ $(wildcard ../*/$*.cx)

