MONODIR    ?= /home/kunta/mono64.2109
MONOBINDIR ?= $(MONODIR)/bin
MONODLLDIR ?= $(MONODIR)/lib/mono/4.0
OPENSIMDLLDIR ?= ../../../bin

COMMITHASH  := $(strip $(shell git log -1 | grep ^commit | sed 's/commit//g'))
COMMITDATE  := $(strip $(shell git log -1 --date=iso | grep ^Date: | sed 's/Date://g'))
COMMITCLEAN := $(strip $(shell git status | grep -q modified: ; echo $$?))

SADLLFILES = \
	IEventHandlers.cs \
	MMRDelegateCommon.cs \
	MMRInternalFuncDict.cs \
	MMRScriptBinOpStr.cs \
	MMRScriptCodeGen.cs \
	MMRScriptCollector.cs \
	MMRScriptCompValu.cs \
	MMRScriptConsts.cs \
	MMRScriptEventCode.cs \
	MMRScriptInlines.cs \
	MMRScriptMyILGen.cs \
	MMRScriptObjCode.cs \
	MMRScriptObjWriter.cs \
	MMRScriptReduce.cs \
	MMRScriptTokenize.cs \
	MMRScriptTypeCast.cs \
	MMRScriptVarDict.cs \
	XMRArray.cs \
	XMRHeapTracker.cs \
	XMRInstAbstract.cs \
	XMRSDTypeClObj.cs

SAOPENSIMDLLS = \
	xmrengshare.dll \
	OpenMetaverseTypes.dll \
	OpenSim.Region.ScriptEngine.Shared.dll \
	OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll

TESTOSIMDLLS = \
	OpenMetaverseTypes.dll \
	OpenSim.Region.ScriptEngine.Shared.dll \
	OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll

TESTSRCFILES = \
	IEventHandlers_tester.cs \
	MMRDelegateCommon_tester.cs \
	MMRInternalFuncDict_tester.cs \
	MMRScriptBinOpStr_tester.cs \
	MMRScriptCodeGen_tester.cs \
	MMRScriptCollector_tester.cs \
	MMRScriptCompValu_tester.cs \
	MMRScriptConsts_tester.cs \
	MMRScriptEventCode_tester.cs \
	MMRScriptInlines_tester.cs \
	MMRScriptMyILGen_tester.cs \
	MMRScriptObjCode_tester.cs \
	MMRScriptObjWriter_tester.cs \
	MMRScriptReduce_tester.cs \
	MMRScriptTokenize_tester.cs \
	MMRScriptTypeCast_tester.cs \
	MMRScriptVarDict_tester.cs \
	XMRArray_tester.cs \
	xmrengtest_$(COMMITHASH)_$(COMMITCLEAN)_tester.cs \
	XMRHeapTracker_tester.cs \
	XMRInstAbstract_tester.cs \
	XMRSDTypeClObj_tester.cs

default: $(OPENSIMDLLDIR)/xmrengshare.dll xmrengtest.zip

# ------------------------------------------------------------------------------
#
# Create xmrengtest.exe
#
xmrengtest.zip: xmrengtest.exe $(patsubst %,$(OPENSIMDLLDIR)/%,$(TESTOSIMDLLS))
	$(foreach dll,$(TESTOSIMDLLS),$(shell cp $(OPENSIMDLLDIR)/$(dll) $(dll)))
	rm -f xmrengtest.zip
	zip -D xmrengtest.zip xmrengtest.exe $(TESTOSIMDLLS)
	rm -f $(TESTOSIMDLLS)

xmrengtest.exe: $(TESTSRCFILES) $(wildcard *.h)
	$(MONOBINDIR)/dmcs -debug -out:$@ \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(filter-out xmrengshare.dll,$(TESTOSIMDLLS))) \
		-reference:$(MONODLLDIR)/System.Drawing.dll \
		$(TESTSRCFILES)

xmrengtest_$(COMMITHASH)_$(COMMITCLEAN)_tester.cs: xmrengtest.cx makexmrengtestilslapi.inc $(wildcard *.h)
	rm -f xmrengtest_*_tester.cs
	cpp -C -P -nostdinc -DSCRIPTBASECLASS=TestScriptBaseClass \
		-DGITCOMMITHASH='"$(COMMITHASH)"' \
		-DGITCOMMITDATE='"$(COMMITDATE)"' \
		-DGITCOMMITCLEAN=$(COMMITCLEAN)   \
		-o $@ $<

makexmrengtestilslapi.inc: makexmrengtestilslapi.exe
	MONO_PATH=../../../bin mono --debug makexmrengtestilslapi.exe > makexmrengtestilslapi.tmp
	mv -f makexmrengtestilslapi.tmp makexmrengtestilslapi.inc

makexmrengtestilslapi.exe: makexmrengtestilslapi.cs
	$(MONOBINDIR)/dmcs -debug -out:$@ \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(filter-out xmrengshare.dll,$(TESTOSIMDLLS))) \
		$<

# ------------------------------------------------------------------------------
#
# Create shareable that contains types common to both the compilers and the
# runtime engine, so the serialization/deserialization will have common types.
#
$(OPENSIMDLLDIR)/xmrengshare.dll: $(SADLLFILES) $(patsubst %,$(OPENSIMDLLDIR)/%,$(filter-out xmrengshare.dll,$(SAOPENSIMDLLS)))
	$(MONOBINDIR)/dmcs -debug -target:library -out:$(OPENSIMDLLDIR)/xmrengshare.dll \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(filter-out xmrengshare.dll,$(SAOPENSIMDLLS))) \
		-reference:$(MONODLLDIR)/System.Drawing.dll \
		$(SADLLFILES)

# ------------------------------------------------------------------------------
#
# Create .cs files from the .cx files
#
%.cs: %.cx $(wildcard *.h)
	cpp -C -P -nostdinc -DSCRIPTBASECLASS=ScriptBaseClass -o $@ $<

%_tester.cs: %.cx $(wildcard *.h)
	cpp -C -P -nostdinc -DSCRIPTBASECLASS=TestScriptBaseClass -o $@ $<

# ------------------------------------------------------------------------------

