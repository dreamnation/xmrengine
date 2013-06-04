COMMITHASH  := $(strip $(shell git log -1 | grep ^commit | sed 's/commit//g'))
COMMITDATE  := $(strip $(shell git log -1 --date=iso | grep ^Date: | sed 's/Date://g'))
COMMITCLEAN := $(strip $(shell git status | grep -q modified: ; echo $$?))

MONODIR    ?= /home/kunta/mono64.2109
MONOBINDIR ?= $(MONODIR)/bin
MONODLLDIR ?= $(MONODIR)/lib/mono/2.0

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

OPENSIMDLLDIR ?= ../../../bin
SAOPENSIMDLLS = \
	xmrengshare.dll \
	OpenMetaverseTypes.dll \
	OpenSim.Region.ScriptEngine.Shared.dll \
	OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll

TESTOSIMDLLS = \
	OpenMetaverseTypes.dll \
	OpenSim.Region.ScriptEngine.Shared.dll \
	OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll

SECRETSRCFILES = \
	IEventHandlers_sa_secret.cs \
	MMRDelegateCommon_sa_secret.cs \
	MMRInternalFuncDict_sa_secret.cs \
	MMRScriptBinOpStr_sa_secret.cs \
	MMRScriptCodeGen_sa_secret.cs \
	MMRScriptCollector_sa_secret.cs \
	MMRScriptCompile_sa_secret.cs \
	MMRScriptCompValu_sa_secret.cs \
	MMRScriptConsts_sa_secret.cs \
	MMRScriptEventCode_sa_secret.cs \
	MMRScriptInlines_sa_secret.cs \
	MMRScriptMyILGen_sa_secret.cs \
	MMRScriptObjCode_sa_secret.cs \
	MMRScriptObjWriter_sa_secret.cs \
	MMRScriptReduce_sa_secret.cs \
	MMRScriptTokenize_sa_secret.cs \
	MMRScriptTypeCast_sa_secret.cs \
	MMRScriptVarDict_sa_secret.cs \
	XMRArray_sa_secret.cs \
	XMRHeapTracker_sa_secret.cs \
	XMRInstAbstract_sa_secret.cs \
	XMRSDTypeClObj_sa_secret.cs

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

default: xmrengcomp.zip xmrengcomp_secret.exe xmrengtest.zip

# ------------------------------------------------------------------------------
#
# Create distributable xmrengcomp stand-alone compiler
#
xmrengcomp.zip: xmrengcomp.exe $(patsubst %,$(OPENSIMDLLDIR)/%,$(SAOPENSIMDLLS))
	$(foreach dll,$(SAOPENSIMDLLS),$(shell cp $(OPENSIMDLLDIR)/$(dll) $(dll)))
	rm -f xmrengcomp.zip
	zip -D xmrengcomp.zip xmrengcomp.exe $(SAOPENSIMDLLS)
	rm -f $(SAOPENSIMDLLS)

xmrengcomp.exe: MMRScriptCompile.cx $(patsubst %,$(OPENSIMDLLDIR)/%,$(SAOPENSIMDLLS))
	rm -f MMRScriptCompile_sa.cs
	cpp -C -P -nostdinc -o MMRScriptCompile_sa.cs -DSTANDALONE_COMPILER MMRScriptCompile.cx
	$(MONOBINDIR)/gmcs -debug -out:xmrengcomp.exe \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(SAOPENSIMDLLS)) \
		-reference:$(MONODLLDIR)/System.Drawing.dll \
		MMRScriptCompile_sa.cs

# ------------------------------------------------------------------------------
#
# Create internal-use-only xmrengcomp stand-alone compiler
# ...includes the -asm, -decode and -genkey command-line options
#
xmrengcomp_secret.exe: $(SECRETSRCFILES) $(wildcard *.h)
	$(MONOBINDIR)/gmcs -debug -out:$@ \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(filter-out xmrengshare.dll,$(SAOPENSIMDLLS))) \
		-reference:$(MONODLLDIR)/System.Drawing.dll \
		$(SECRETSRCFILES)

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
	$(MONOBINDIR)/gmcs -debug -out:$@ \
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
	$(MONOBINDIR)/gmcs -debug -out:$@ \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(filter-out xmrengshare.dll,$(TESTOSIMDLLS))) \
		$<

# ------------------------------------------------------------------------------
#
# Create shareable that contains types common to both the compilers and the
# runtime engine, so the serialization/deserialization will have common types.
#
$(OPENSIMDLLDIR)/xmrengshare.dll: $(SADLLFILES) $(patsubst %,$(OPENSIMDLLDIR)/%,$(filter-out xmrengshare.dll,$(SAOPENSIMDLLS)))
	$(MONOBINDIR)/gmcs -debug -target:library -out:$(OPENSIMDLLDIR)/xmrengshare.dll \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(filter-out xmrengshare.dll,$(SAOPENSIMDLLS))) \
		-reference:$(MONODLLDIR)/System.Drawing.dll \
		$(SADLLFILES)

# ------------------------------------------------------------------------------
#
# Create .cs files from the .cx files
#
%.cs: %.cx $(wildcard *.h)
	cpp -C -P -nostdinc -DSCRIPTBASECLASS=ScriptBaseClass -o $@ $<

%_sa_secret.cs: %.cx $(wildcard *.h)
	cpp -C -P -nostdinc -DSCRIPTBASECLASS=ScriptBaseClass -DSTANDALONE_COMPILER -DSECRET_STUFF -o $@ $<

%_tester.cs: %.cx $(wildcard *.h)
	cpp -C -P -nostdinc -DSCRIPTBASECLASS=TestScriptBaseClass -o $@ $<

# ------------------------------------------------------------------------------

