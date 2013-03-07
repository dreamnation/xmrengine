CFLAGS := `pkg-config --cflags mono`
LFLAGS := `pkg-config --libs mono`

DEFS := -DHAVE_HEAPTRACKER=1 -DHAVE_MMRUTHREADS=1
GCC  := gcc $(CFLAGS) -Wall -O2 -g

COMMITHASH  := $(strip $(shell git log -1 | grep ^commit | sed 's/commit//g'))
COMMITDATE  := $(strip $(shell git log -1 --date=iso | grep ^Date: | sed 's/Date://g'))
COMMITCLEAN := $(strip $(shell git status | grep -q modified: ; echo $$?))

OPENSIMDLLDIR ?= ../../../bin

ENGINECSFILES = XMREngine_$(COMMITHASH)_$(COMMITCLEAN).cs \
                XMREvents.cs \
                XMRInstBackend.cs \
                XMRInstCapture.cs \
                XMRInstCtor.cs \
                XMRInstMain.cs \
                XMRInstMisc.cs \
                XMRInstQueue.cs \
                XMRInstRun.cs \
                XMRScriptThread.cs \
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
             /home/kunta/mono64.2109/lib/mono/2.0/Mono.Tasklets.dll

default: \
	$(OPENSIMDLLDIR)/xmrengshare.dll \
	$(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.XMREngine.dll

# ------------------------------------------------------------------------------
#
# Create files needed for OpenSim
#
$(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.XMREngine.dll: $(ENGINECSFILES) $(REFERENCED)
	gmcs -debug -target:library \
		-out:$@ \
		$(ENGINECSFILES) \
		$(patsubst %.dll,-reference:%.dll,$(REFERENCED))

$(OPENSIMDLLDIR)/xmrengshare.dll: $(wildcard ../*/*.cx) $(wildcard ../*/*.h)
	make -C ../Compiler $(OPENSIMDLLDIR)/xmrengshare.dll

MMRScriptCompile.cs: ../Compiler/MMRScriptCompile.cx
	cpp -I../Compiler $(DEFS) -C -P -nostdinc -o $@ $<

XMREngine_$(COMMITHASH)_$(COMMITCLEAN).cs: XMREngine.cx $(wildcard ../*/*.h)
	rm -f XMREngine_*.cs
	cpp -I../Compiler $(DEFS) \
		-DGITCOMMITHASH='"$(COMMITHASH)"' \
		-DGITCOMMITDATE='"$(COMMITDATE)"' \
		-DGITCOMMITCLEAN=$(COMMITCLEAN) -C -P -nostdinc -o $@ $<

%.cs: %.cx $(wildcard ../*/*.h)
	cpp -I../Compiler $(DEFS) -C -P -nostdinc -o $@ $<

# ------------------------------------------------------------------------------

