SADLLFILES = \
	IEventHandlers.cs \
	MMRInternalFuncDict.cs \
	MMRScriptBinOpStr.cs \
	MMRScriptCodeGen.cs \
	MMRScriptCompValu.cs \
	MMRScriptConsts.cs \
	MMRScriptEventCode.cs \
	MMRScriptInlines.cs \
	MMRScriptMyILGen.cs \
	MMRScriptObjCode.cs \
	MMRScriptReduce.cs \
	MMRScriptTokenize.cs \
	MMRScriptTokenReadWrite.cs \
	MMRScriptTypeCast.cs \
	XMRArray.cs \
	XMRInstAbstract.cs

OPENSIMDLLDIR ?= ../../../bin
SAOPENSIMDLLS = \
	xmrengshare.dll \
	OpenSim.Region.ScriptEngine.Shared.dll \
	OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll

default: xmrengcomp.zip xmrengcomp_secret.exe

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
xmrengcomp_secret.exe: MMRScriptCompile.cx $(patsubst %,$(OPENSIMDLLDIR)/%,$(SAOPENSIMDLLS))
	rm -f MMRScriptCompile_sa_secret.cs
	cpp -C -P -nostdinc -o MMRScriptCompile_sa_secret.cs -DSTANDALONE_COMPILER -DSECRET_STUFF MMRScriptCompile.cx
	gmcs -debug -out:xmrengcomp_secret.exe \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(SAOPENSIMDLLS)) \
		MMRScriptCompile_sa_secret.cs

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
	cpp -C -P -nostdinc -o $@ $<

# ------------------------------------------------------------------------------

