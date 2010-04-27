CFLAGS := `pkg-config --cflags mono`
LFLAGS := `pkg-config --libs mono`

SAFILES = \
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
	MMRScriptTypeCast.cs \
	XMRArray.cs \
	XMRInstAbstract.cs

OPENSIMDLLDIR = ../../../bin
SAOPENSIMDLLS = \
	OpenSim.Region.ScriptEngine.Shared.dll \
	OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll

GCC := gcc $(CFLAGS) -Wall -O2 -g

default: csfiles xmrengcomp.zip xmrengcomp_secret.exe

# ------------------------------------------------------------------------------
#
# Create distributable xmrengcomp stand-alone compiler
#
xmrengcomp.zip: xmrengcomp.exe $(patsubst %,$(OPENSIMDLLDIR)/%,$(SAOPENSIMDLLS))
	$(foreach dll,$(SAOPENSIMDLLS),$(shell cp $(OPENSIMDLLDIR)/$(dll) $(dll)))
	rm -f xmrengcomp.zip
	zip -D xmrengcomp.zip xmrengcomp.exe $(SAOPENSIMDLLS)
	rm -f $(SAOPENSIMDLLS)

xmrengcomp.exe: $(SAFILES) MMRScriptCompile.cx $(patsubst %,$(OPENSIMDLLDIR)/%,$(SAOPENSIMDLLS))
	rm -f MMRScriptCompile_sa.cs
	cpp -C -P -nostdinc -o MMRScriptCompile_sa.cs -DSTANDALONE_COMPILER MMRScriptCompile.cx
	gmcs -debug -out:xmrengcomp.exe \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(SAOPENSIMDLLS)) \
		MMRScriptCompile_sa.cs \
		$(SAFILES)
	rm -f MMRScriptCompile_sa.cs

#
# Create internal-use-only xmrengcomp stand-alone compiler
# ...includes the -asm and -genkey command-line options
#
xmrengcomp_secret.exe: $(SAFILES) MMRScriptCompile.cx $(patsubst %,$(OPENSIMDLLDIR)/%,$(SAOPENSIMDLLS))
	rm -f MMRScriptCompile_sa.cs
	cpp -C -P -nostdinc -o MMRScriptCompile_sa.cs -DSTANDALONE_COMPILER -DSECRET_STUFF MMRScriptCompile.cx
	gmcs -debug -out:xmrengcomp_secret.exe \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(SAOPENSIMDLLS)) \
		MMRScriptCompile_sa.cs \
		$(SAFILES)
	rm -f MMRScriptCompile_sa.cs

# ------------------------------------------------------------------------------
#
# Create files needed for OpenSim
#
csfiles: $(patsubst %.cx,%.cs,$(wildcard *.cx))

%.cs: %.cx $(wildcard *.h)
	cpp -C -P -nostdinc -o $@ $<

# ------------------------------------------------------------------------------

xmrhelpers: ../../../../opensim/bin/xmrhelpers.so

../../../../opensim/bin/xmrhelpers.so: xmrhelpers.so
	cp xmrhelpers.so ../../../../opensim/bin/

xmrhelpers.so: xmrhelpers.o
	ld -share -g -o xmrhelpers.so xmrhelpers.o

xmrhelpers.o: xmrhelpers.c
	$(GCC) -c -o xmrhelpers.o xmrhelpers.c

# ------------------------------------------------------------------------------

