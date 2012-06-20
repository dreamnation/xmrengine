CFLAGS := `pkg-config --cflags mono`
LFLAGS := `pkg-config --libs mono`

GCC := gcc $(CFLAGS) -Wall -O2 -g

OPENSIMDLLDIR ?= ../../../bin

default: csfiles $(OPENSIMDLLDIR)/xmrengshare.dll $(OPENSIMDLLDIR)/xmrhelpers.so $(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.XMREngine.dll

# ------------------------------------------------------------------------------
#
# Create files needed for OpenSim
#
$(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.XMREngine.dll: $(OPENSIMDLLDIR)/xmrengshare.dll csfiles ../Compiler/MMRScriptCompile.cs
	/home/kunta/mono32/bin/gmcs -debug -target:library -out:$(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.XMREngine.dll \
		XMREngine.cs \
		XMREvents.cs \
		XMRInstBackend.cs \
		XMRInstCapture.cs \
		XMRInstCtor.cs \
		XMRInstMain.cs \
		XMRInstMisc.cs \
		XMRInstQueue.cs \
		XMRInstRun.cs \
		XMRScriptUThread.cs \
		XMREngXmrTestLs.cs \
		../Compiler/MMRScriptCompile.cs \
		-reference:$(OPENSIMDLLDIR)/xmrengshare.dll \
		-reference:$(OPENSIMDLLDIR)/log4net.dll \
		-reference:$(OPENSIMDLLDIR)/Nini.dll \
		-reference:$(OPENSIMDLLDIR)/Mono.Addins.dll \
		-reference:$(OPENSIMDLLDIR)/OpenMetaverse.dll \
		-reference:$(OPENSIMDLLDIR)/OpenMetaverse.StructuredData.dll \
		-reference:$(OPENSIMDLLDIR)/OpenMetaverseTypes.dll \
		-reference:$(OPENSIMDLLDIR)/OpenSim.Framework.dll \
		-reference:$(OPENSIMDLLDIR)/OpenSim.Framework.Console.dll \
		-reference:$(OPENSIMDLLDIR)/OpenSim.Region.ClientStack.LindenCaps.dll \
		-reference:$(OPENSIMDLLDIR)/OpenSim.Region.CoreModules.dll \
		-reference:$(OPENSIMDLLDIR)/OpenSim.Region.Framework.dll \
		-reference:$(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.Shared.dll \
		-reference:$(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.Shared.Api.dll \
		-reference:$(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll \
		-reference:/home/kunta/mono32/lib/mono/2.0/Mono.Tasklets.dll

$(OPENSIMDLLDIR)/xmrengshare.dll: $(wildcard ../*/*.cx) $(wildcard ../*/*.h)
	make -C ../Compiler $(OPENSIMDLLDIR)/xmrengshare.dll

csfiles: $(patsubst %.cx,%.cs,$(wildcard *.cx)) MMRScriptCompile.cs

MMRScriptCompile.cs: ../Compiler/MMRScriptCompile.cx
	cpp -I../Compiler -C -P -nostdinc -o $@ $<

%.cs: %.cx $(wildcard ../*/*.h)
	cpp -I../Compiler -C -P -nostdinc -o $@ $<

##../Compiler/MMRScriptCompile.cs: ../Compiler/MMRScriptCompile.cx $(wildcard ../*/*.h)
##	cpp -I../Compiler -C -P -nostdinc -o $@ $<

# ------------------------------------------------------------------------------

xmrhelpers: $(OPENSIMDLLDIR)/xmrhelpers.so

$(OPENSIMDLLDIR)/xmrhelpers.so: xmrhelpers.o
	ld -m elf_i386 -share -g -o $(OPENSIMDLLDIR)/xmrhelpers.so xmrhelpers.o

xmrhelpers.o: xmrhelpers.c
	$(GCC) -m32 -c -o xmrhelpers.o xmrhelpers.c

# ------------------------------------------------------------------------------

