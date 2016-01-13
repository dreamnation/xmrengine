MONODIR       ?= /usr
MONOBINDIR    ?= $(MONODIR)/bin
MONODLLDIR    ?= $(MONODIR)/lib/mono/4.5
OPENSIMDLLDIR ?= ../../../bin_090
TASKLETSDLL   ?= $(MONODLLDIR)/Mono.Tasklets.dll

COMMITHASH  := $(strip $(shell git log -1 | grep ^commit | sed 's/commit//g'))
COMMITDATE  := $(strip $(shell git log -1 --date=iso | grep ^Date: | sed 's/Date://g'))
COMMITCLEAN := $(strip $(shell git status | grep -q modified: ; echo $$?))

HAVE_MONOCONTS   ?= $(shell ../Compiler/findmonotasklet.sh $(MONOBINDIR) $(TASKLETSDLL) Continuation)
HAVE_MMRUTHREADS ?= $(shell ../Compiler/findmonotasklet.sh $(MONOBINDIR) $(TASKLETSDLL) MMRUThread)

DEFS  := -DHAVE_MONOCONTS=$(HAVE_MONOCONTS) -DHAVE_MMRUTHREADS=$(HAVE_MMRUTHREADS)
DEFS0 := -DHAVE_MONOCONTS=0 -DHAVE_MMRUTHREADS=0

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
	MMRWebRequest.cx \
	XMRArray.cx \
	XMRHeapTracker.cx \
	XMRInstAbstract.cx \
	XMRObjectTokens.cx \
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
	OpenSim.Framework.dll \
	OpenSim.Region.ScriptEngine.Shared.dll \
	OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll

default: xmrengcomp.zip xmrengcomp_secret.exe xmrengshare.dll xmrengtest.zip

# ------------------------------------------------------------------------------
#
# Create xmrengcomp.exe
#
xmrengcomp.zip: xmrengcomp.exe $(patsubst %,$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS))
	$(foreach dll,$(REFDOPENSIMDLLS),$(shell cp $(OPENSIMDLLDIR)/$(dll) $(dll)))
	rm -f xmrengcomp.zip
	zip -D xmrengcomp.zip xmrengcomp.exe xmrengcomp.exe.mdb $(REFDOPENSIMDLLS)
	rm -f $(REFDOPENSIMDLLS)

xmrengcomp.exe: xmrengcomp.cs $(wildcard *.h) xmrengshare.dll
	$(MONOBINDIR)/mcs -debug -out:$@ \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS)) \
		-reference:xmrengshare.dll \
		-reference:$(TASKLETSDLL) \
		-reference:$(MONODLLDIR)/System.Drawing.dll \
		xmrengcomp.cs

xmrengcomp.cs: xmrengcomp.cx $(wildcard *.h)
	cpp -C -P -nostdinc $(DEFS) -o $@ $<

xmrengcomp_secret.exe: xmrengcomp_secret.cs $(wildcard *.h) xmrengshare.dll
	$(MONOBINDIR)/mcs -debug -out:$@ \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS)) \
		-reference:xmrengshare.dll \
		-reference:$(TASKLETSDLL) \
		-reference:$(MONODLLDIR)/System.Drawing.dll \
		xmrengcomp_secret.cs

xmrengcomp_secret.cs: xmrengcomp.cx $(wildcard *.h)
	cpp -C -P -nostdinc -DSECRET_STUFF $(DEFS) -o $@ $<

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
	$(MONOBINDIR)/mcs -debug -out:$@ \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS)) \
		-reference:$(MONODLLDIR)/Mono.Tasklets.dll \
		-reference:$(MONODLLDIR)/System.Drawing.dll \
		$(TESTERCSFILES)

xmrengtest_tester.cs: xmrengtest.cx makexmrengtestilslapi.inc $(wildcard *.h)
	cpp -C -P -nostdinc $(DEFS) -DSCRIPTBASECLASS=TestScriptBaseClass -o $@ $<

%_tester.cs: %.cx $(wildcard *.h)
	cpp -C -P -nostdinc $(DEFS) -DSCRIPTBASECLASS=TestScriptBaseClass -o $@ $<

XMRScriptUThread_tester.cs: ../Engine/XMRScriptUThread.cx $(wildcard *.h)
	cpp -C -P -nostdinc $(DEFS) -DSCRIPTBASECLASS=TestScriptBaseClass -o $@ $<

makexmrengtestilslapi.inc: makexmrengtestilslapi.exe $(patsubst %,$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS))
	MONO_PATH=$(OPENSIMDLLDIR) mono --debug makexmrengtestilslapi.exe > makexmrengtestilslapi.tmp
	mv -f makexmrengtestilslapi.tmp makexmrengtestilslapi.inc

makexmrengtestilslapi.exe: makexmrengtestilslapi.cs
	$(MONOBINDIR)/mcs -debug -out:$@ \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS)) \
		$<

makexmrengtestilslapi.cs: makexmrengtestilslapi.cx $(wildcard *.h)
	cpp -C -P -nostdinc $(DEFS0) -DSCRIPTBASECLASS=ScriptBaseClass -o $@ $<

# ------------------------------------------------------------------------------
#
# Create shareable that contains types common to both the compilers and the
# runtime engine, so the serialization/deserialization will have common types.
#
xmrengshare.dll: $(SHARECSFILES) $(patsubst %,$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS))
	rm -rf xmrengshare.dll*
	$(MONOBINDIR)/mcs -debug -target:library -out:xmrengshare.dll \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS)) \
		-reference:$(MONODLLDIR)/System.Drawing.dll \
		$(SHARECSFILES)

%_shared.cs: %.cx $(wildcard *.h)
	cpp -C -P -nostdinc $(DEFS) -DSCRIPTBASECLASS=ScriptBaseClass -o $@ $<

# ------------------------------------------------------------------------------

MMRScriptCompCommit_$(COMMITHASH)_$(COMMITCLEAN).cs: MMRScriptCompCommit.cx
	rm -f MMRScriptCompCommit_*.cs
	cpp -C -P -nostdinc $(DEFS) \
		-DGITCOMMITHASH='"$(COMMITHASH)"' \
		-DGITCOMMITDATE='"$(COMMITDATE)"' \
		-DGITCOMMITCLEAN=$(COMMITCLEAN)   \
		-o $@ $<

# ------------------------------------------------------------------------------
