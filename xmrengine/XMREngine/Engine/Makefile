CFLAGS := `pkg-config --cflags mono`
LFLAGS := `pkg-config --libs mono`

GCC := gcc $(CFLAGS) -Wall -O2 -g

OPENSIMDLLDIR ?= ../../../bin

ENGINECSFILES = XMREngine.cs \
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
                MMRScriptCompile.cs

REFERENCED = $(OPENSIMDLLDIR)/xmrengshare.dll \
             $(OPENSIMDLLDIR)/log4net.dll \
             $(OPENSIMDLLDIR)/Nini.dll \
             $(OPENSIMDLLDIR)/Mono.Addins.dll \
             $(OPENSIMDLLDIR)/OpenMetaverse.dll \
             $(OPENSIMDLLDIR)/OpenMetaverse.StructuredData.dll \
             $(OPENSIMDLLDIR)/OpenMetaverseTypes.dll \
             $(OPENSIMDLLDIR)/OpenSim.Framework.dll \
             $(OPENSIMDLLDIR)/OpenSim.Framework.Console.dll \
             $(OPENSIMDLLDIR)/OpenSim.Region.ClientStack.LindenCaps.dll \
             $(OPENSIMDLLDIR)/OpenSim.Region.CoreModules.dll \
             $(OPENSIMDLLDIR)/OpenSim.Region.Framework.dll \
             $(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.Shared.dll \
             $(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.Shared.Api.dll \
             $(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll \
             /home/kunta/mono32.267/lib/mono/2.0/Mono.Tasklets.dll

default: \
	$(OPENSIMDLLDIR)/xmrengshare.dll \
	$(OPENSIMDLLDIR)/xmrhelpers.so \
	$(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.XMREngine.dll

# ------------------------------------------------------------------------------
#
# Create files needed for OpenSim
#
$(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.XMREngine.dll: $(ENGINECSFILES) $(REFERENCED)
	/home/kunta/mono32/bin/gmcs -debug -target:library \
		-out:$@ \
		$(ENGINECSFILES) \
		$(patsubst %.dll,-reference:%.dll,$(REFERENCED))

$(OPENSIMDLLDIR)/xmrengshare.dll: $(wildcard ../*/*.cx) $(wildcard ../*/*.h)
	make -C ../Compiler $(OPENSIMDLLDIR)/xmrengshare.dll

MMRScriptCompile.cs: ../Compiler/MMRScriptCompile.cx
	cpp -I../Compiler -C -P -nostdinc -o $@ $<

%.cs: %.cx $(wildcard ../*/*.h)
	cpp -I../Compiler -C -P -nostdinc -o $@ $<

# ------------------------------------------------------------------------------

xmrhelpers: $(OPENSIMDLLDIR)/xmrhelpers.so

$(OPENSIMDLLDIR)/xmrhelpers.so: xmrhelpers.o
	ld -m elf_i386 -share -g -o $(OPENSIMDLLDIR)/xmrhelpers.so xmrhelpers.o

xmrhelpers.o: xmrhelpers.c
	$(GCC) -m32 -c -o xmrhelpers.o xmrhelpers.c

# ------------------------------------------------------------------------------

