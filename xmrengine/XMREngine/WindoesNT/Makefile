COMMITHASH  := $(strip $(shell git log -1 | grep ^commit | sed 's/commit//g'))
COMMITDATE  := $(strip $(shell git log -1 --date=iso | grep ^Date: | sed 's/Date://g'))
COMMITCLEAN := $(strip $(shell git status | grep -q modified: ; echo $$?))

DEFS := -DHAVE_MONOCONTS=0 -DHAVE_MMRUTHREADS=0 -DSCRIPTBASECLASS=ScriptBaseClass

KILLITALL := $(shell rm -f *.cs)

default: xmrengwincsfiles.zip

# ------------------------------------------------------------------------------
#
# Create ZIP file for Windows sources
#
xmrengwincsfiles.zip: \
		XMREngine.cs \
		IEventHandlers.cs      \
		MMRDelegateCommon.cs   \
		MMRInternalFuncDict.cs \
		MMRScriptBinOpStr.cs   \
		MMRScriptCodeGen.cs    \
		MMRScriptCollector.cs  \
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
		XMRScriptUThread.cs
	rm -f xmrengwincsfiles.zip
	zip xmrengwincsfiles.zip *.cs

XMREngine.cs: $(wildcard ../*/XMREngine.cx) $(wildcard ../*/*.h)
	cpp -I../Compiler $(DEFS) \
		-DGITCOMMITHASH='"$(COMMITHASH)"' \
		-DGITCOMMITDATE='"$(COMMITDATE)"' \
		-DGITCOMMITCLEAN=$(COMMITCLEAN) -C -P -nostdinc -o $@ $<

%.cs: $(wildcard ../*/$*.cx) $(wildcard ../*/*.h)
	cpp -I../Compiler $(DEFS) -C -P -nostdinc -o $@ $(wildcard ../*/$*.cx)

