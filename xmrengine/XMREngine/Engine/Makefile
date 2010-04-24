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

SAOPENSIMDLLS = \
	../../../../opensim/bin/OpenSim.Region.ScriptEngine.Shared.dll \
	../../../../opensim/bin/OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll \

GCC := gcc $(CFLAGS) -Wall -O2 -g

default: xmrengcomp.zip csfiles

# ------------------------------------------------------------------------------

xmrengcomp.zip: xmrengcomp.exe $(SAOPENSIMDLLS)
	rm -f OpenSim.Region.ScriptEngine.Shared.dll
	rm -f OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll
	ln -s ../../../../opensim/bin/OpenSim.Region.ScriptEngine.Shared.dll
	ln -s ../../../../opensim/bin/OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll
	rm -f xmrengcomp.zip
	zip -D xmrengcomp.zip xmrengcomp.exe OpenSim.Region.ScriptEngine.Shared.dll OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll
	rm -f OpenSim.Region.ScriptEngine.Shared.dll
	rm -f OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll

xmrengcomp.exe: $(SAFILES) $(SAOPENSIMDLLS) MMRScriptCompile.cx
	rm -f MMRScriptCompile_sa.cs
	cpp -C -P -nostdinc -o MMRScriptCompile_sa.cs -DSTANDALONE_COMPILER MMRScriptCompile.cx
	gmcs -debug -out:xmrengcomp.exe \
		-reference:../../../../opensim/bin/OpenSim.Region.ScriptEngine.Shared.dll \
		-reference:../../../../opensim/bin/OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll \
		MMRScriptCompile_sa.cs \
		$(SAFILES)
	rm -f MMRScriptCompile_sa.cs

# ------------------------------------------------------------------------------

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

