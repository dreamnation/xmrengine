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
	MMRScriptReduce.cs \
	MMRScriptTokenize.cs \
	MMRScriptTypeCast.cs \
	MMRScriptVarDict.cs \
	XMRArray.cs \
	XMRInstAbstract.cs \
	XMRSDTypeClObj.cs

OPENSIMDLLDIR ?= ../../../bin
SAOPENSIMDLLS = \
	xmrengshare.dll \
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
	MMRScriptReduce_sa_secret.cs \
	MMRScriptTokenize_sa_secret.cs \
	MMRScriptTypeCast_sa_secret.cs \
	MMRScriptVarDict_sa_secret.cs \
	XMRArray_sa_secret.cs \
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
	MMRScriptReduce_tester.cs \
	MMRScriptTokenize_tester.cs \
	MMRScriptTypeCast_tester.cs \
	MMRScriptVarDict_tester.cs \
	XMRArray_tester.cs \
	xmrengtest_tester.cs \
	XMRInstAbstract_tester.cs \
	XMRSDTypeClObj_tester.cs

default: xmrengcomp.zip xmrengcomp_secret.exe xmrengtest.exe

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
	gmcs -debug -out:xmrengcomp.exe \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(SAOPENSIMDLLS)) \
		MMRScriptCompile_sa.cs

# ------------------------------------------------------------------------------
#
# Create internal-use-only xmrengcomp stand-alone compiler
# ...includes the -asm, -decode and -genkey command-line options
#
xmrengcomp_secret.exe: $(SECRETSRCFILES) $(wildcard *.h)
	gmcs -debug -out:$@ \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(filter-out xmrengshare.dll,$(SAOPENSIMDLLS))) \
		$(SECRETSRCFILES)

# ------------------------------------------------------------------------------
#
# Create xmrengtest.exe
#
xmrengtest.exe: $(TESTSRCFILES) $(wildcard *.h)
	gmcs -debug -out:$@ \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(filter-out xmrengshare.dll,$(SAOPENSIMDLLS))) \
		$(TESTSRCFILES)

# ------------------------------------------------------------------------------
#
# Create shareable that contains types common to both the compilers and the
# runtime engine, so the serialization/deserialization will have common types.
#
$(OPENSIMDLLDIR)/xmrengshare.dll: $(SADLLFILES) $(patsubst %,$(OPENSIMDLLDIR)/%,$(filter-out xmrengshare.dll,$(SAOPENSIMDLLS)))
	gmcs -debug -target:library -out:$(OPENSIMDLLDIR)/xmrengshare.dll \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(filter-out xmrengshare.dll,$(SAOPENSIMDLLS))) \
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

