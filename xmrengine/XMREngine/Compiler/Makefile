MONODIR       ?= /home/kunta/mono64.2109
MONOBINDIR    ?= $(MONODIR)/bin
MONODLLDIR    ?= $(MONODIR)/lib/mono/4.0
OPENSIMDLLDIR ?= ../../../bin

COMMITHASH  := $(strip $(shell git log -1 | grep ^commit | sed 's/commit//g'))
COMMITDATE  := $(strip $(shell git log -1 --date=iso | grep ^Date: | sed 's/Date://g'))
COMMITCLEAN := $(strip $(shell git status | grep -q modified: ; echo $$?))

SOURCEFILES := \
	IEventHandlers.cx \
	MMRDelegateCommon.cx \
	MMRInternalFuncDict.cx \
	MMRScriptBinOpStr.cx \
	MMRScriptCodeGen.cx \
	MMRScriptCollector.cx \
	MMRScriptCompValu.cx \
	MMRScriptConsts.cx \
	MMRScriptEventCode.cx \
	MMRScriptInlines.cx \
	MMRScriptMyILGen.cx \
	MMRScriptObjCode.cx \
	MMRScriptObjWriter.cx \
	MMRScriptReduce.cx \
	MMRScriptTokenize.cx \
	MMRScriptTypeCast.cx \
	MMRScriptVarDict.cx \
	XMRArray.cx \
	XMRHeapTracker.cx \
	XMRInstAbstract.cx \
	XMRSDTypeClObj.cx

TESTERCSFILES := \
	xmrengtest_tester.cs \
	$(SOURCEFILES:.cx=_tester.cs) \
	MMRScriptCompCommit_$(COMMITHASH)_$(COMMITCLEAN).cs \
	XMRScriptUThread_tester.cs

SHARECSFILES := \
	$(SOURCEFILES:.cx=_shared.cs) \
	MMRScriptCompCommit_$(COMMITHASH)_$(COMMITCLEAN).cs

REFDOPENSIMDLLS := \
	OpenMetaverseTypes.dll \
	OpenSim.Region.ScriptEngine.Shared.dll \
	OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll

default: xmrengtest.zip xmrengshare.dll

# ------------------------------------------------------------------------------
#
# Create xmrengtest.exe
#
xmrengtest.zip: xmrengtest.exe $(patsubst %,$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS))
	$(foreach dll,$(REFDOPENSIMDLLS),$(shell cp $(OPENSIMDLLDIR)/$(dll) $(dll)))
	rm -f xmrengtest.zip
	zip -D xmrengtest.zip xmrengtest.exe $(REFDOPENSIMDLLS)
	rm -f $(REFDOPENSIMDLLS)

xmrengtest.exe: $(TESTERCSFILES) $(wildcard *.h)
	$(MONOBINDIR)/dmcs -debug -out:$@ \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS)) \
		-reference:$(MONODLLDIR)/Mono.Tasklets.dll \
		-reference:$(MONODLLDIR)/System.Drawing.dll \
		$(TESTERCSFILES)

xmrengtest_tester.cs: xmrengtest.cx makexmrengtestilslapi.inc $(wildcard *.h)
	cpp -C -P -nostdinc -DSCRIPTBASECLASS=TestScriptBaseClass -o $@ $<

%_tester.cs: %.cx $(wildcard *.h)
	cpp -C -P -nostdinc -DSCRIPTBASECLASS=TestScriptBaseClass -o $@ $<

XMRScriptUThread_tester.cs: ../Engine/XMRScriptUThread.cx $(wildcard *.h)
	cpp -C -P -nostdinc -DHAVE_MONOCONTS=1 -DHAVE_MMRUTHREADS=1 -DSCRIPTBASECLASS=TestScriptBaseClass -o $@ $<

makexmrengtestilslapi.inc: makexmrengtestilslapi.exe
	MONO_PATH=../../../bin mono --debug makexmrengtestilslapi.exe > makexmrengtestilslapi.tmp
	mv -f makexmrengtestilslapi.tmp makexmrengtestilslapi.inc

makexmrengtestilslapi.exe: makexmrengtestilslapi.cs
	$(MONOBINDIR)/dmcs -debug -out:$@ \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS)) \
		$<

makexmrengtestilslapi.cs: makexmrengtestilslapi.cx $(wildcard *.h)
	cpp -C -P -nostdinc $(DEFS) -DSCRIPTBASECLASS=ScriptBaseClass -o $@ $<

# ------------------------------------------------------------------------------
#
# Create shareable that contains types common to both the compilers and the
# runtime engine, so the serialization/deserialization will have common types.
#
xmrengshare.dll: $(SHARECSFILES) $(patsubst %,$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS))
	rm -rf xmrengshare.dll*
	$(MONOBINDIR)/dmcs -debug -target:library -out:xmrengshare.dll \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS)) \
		-reference:$(MONODLLDIR)/System.Drawing.dll \
		$(SHARECSFILES)

%_shared.cs: %.cx $(wildcard *.h)
	cpp -C -P -nostdinc -DSCRIPTBASECLASS=ScriptBaseClass -o $@ $<

# ------------------------------------------------------------------------------

MMRScriptCompCommit_$(COMMITHASH)_$(COMMITCLEAN).cs: MMRScriptCompCommit.cx
	rm -f MMRScriptCompCommit_*.cs
	cpp -C -P -nostdinc \
		-DGITCOMMITHASH='"$(COMMITHASH)"' \
		-DGITCOMMITDATE='"$(COMMITDATE)"' \
		-DGITCOMMITCLEAN=$(COMMITCLEAN)   \
		-o $@ $<

# ------------------------------------------------------------------------------
