MONODIR       ?= /home/kunta/mono64.2109
MONOBINDIR    ?= $(MONODIR)/bin
MONODLLDIR    ?= $(MONODIR)/lib/mono/4.0
OPENSIMDLLDIR ?= ../../../bin

COMMITHASH  := $(strip $(shell git log -1 | grep ^commit | sed 's/commit//g'))
COMMITDATE  := $(strip $(shell git log -1 --date=iso | grep ^Date: | sed 's/Date://g'))
COMMITCLEAN := $(strip $(shell git status | grep -q modified: ; echo $$?))

HAVE_MONOCONTS   ?= $(shell ../Compiler/findmonotasklet.sh $(MONOBINDIR) $(MONODLLDIR) Continuation)
HAVE_MMRUTHREADS ?= $(shell ../Compiler/findmonotasklet.sh $(MONOBINDIR) $(MONODLLDIR) MMRUThread)

DEFS := -DHAVE_MONOCONTS=$(HAVE_MONOCONTS) -DHAVE_MMRUTHREADS=$(HAVE_MMRUTHREADS)

ENGINECSFILES := XMREngine_$(COMMITHASH)_$(COMMITCLEAN).cs \
                 XMREvents.cs \
                 XMRInstBackend.cs \
                 XMRInstCapture.cs \
                 XMRInstCtor.cs \
                 XMRInstMain.cs \
                 XMRInstMisc.cs \
                 XMRInstQueue.cs \
                 XMRInstRun.cs \
                 XMRInstSorpra.cs \
                 XMRScriptThread.cs \
                 XMRScriptUThread.cs \
                 XMREngXmrTestLs.cs \
                 MMRScriptCompile.cs

REFERENCED := $(OPENSIMDLLDIR)/xmrengshare.dll \
              $(OPENSIMDLLDIR)/log4net.dll \
              $(OPENSIMDLLDIR)/Nini.dll \
              $(OPENSIMDLLDIR)/Mono.Addins.dll \
              $(OPENSIMDLLDIR)/OpenMetaverse.dll \
              $(OPENSIMDLLDIR)/OpenMetaverse.StructuredData.dll \
              $(OPENSIMDLLDIR)/OpenMetaverseTypes.dll \
              $(OPENSIMDLLDIR)/OpenSim.Framework.dll \
              $(OPENSIMDLLDIR)/OpenSim.Framework.Console.dll \
              $(OPENSIMDLLDIR)/OpenSim.Framework.Monitoring.dll \
              $(OPENSIMDLLDIR)/OpenSim.Region.ClientStack.LindenCaps.dll \
              $(OPENSIMDLLDIR)/OpenSim.Region.CoreModules.dll \
              $(OPENSIMDLLDIR)/OpenSim.Region.Framework.dll \
              $(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.Shared.dll \
              $(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.Shared.Api.dll \
              $(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll

ifeq ($(HAVE_MONOTASKLETS),1)
	REFERENCED += $(MONODLLDIR)/Mono.Tasklets.dll
else
  ifeq ($(HAVE_MMRUTHREADS),1)
	REFERENCED += $(MONODLLDIR)/Mono.Tasklets.dll
  endif
endif

default: \
	$(OPENSIMDLLDIR)/xmrengshare.dll \
	$(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.XMREngine.dll

# ------------------------------------------------------------------------------
#
# Create files needed for OpenSim
#
$(OPENSIMDLLDIR)/xmrengshare.dll: $(wildcard ../*/*.cx) $(wildcard ../*/*.h)
	make -C ../Compiler $(OPENSIMDLLDIR)/xmrengshare.dll

$(OPENSIMDLLDIR)/OpenSim.Region.ScriptEngine.XMREngine.dll: $(ENGINECSFILES) $(REFERENCED)
	$(MONOBINDIR)/dmcs -debug -target:library \
		-out:$@ \
		$(ENGINECSFILES) \
		$(patsubst %.dll,-reference:%.dll,$(REFERENCED))

XMREngine_$(COMMITHASH)_$(COMMITCLEAN).cs: XMREngine.cx $(wildcard ../*/*.h)
	rm -f XMREngine_*.cs
	cpp -I../Compiler $(DEFS) \
		-DGITCOMMITHASH='"$(COMMITHASH)"' \
		-DGITCOMMITDATE='"$(COMMITDATE)"' \
		-DGITCOMMITCLEAN=$(COMMITCLEAN) -C -P -nostdinc -o $@ $<

XMRScriptUThread.cs: ../Compiler/XMRScriptUThread.cx
	cpp -I../Compiler $(DEFS) -C -P -nostdinc -o $@ $<

%.cs: %.cx $(wildcard ../*/*.h)
	cpp -I../Compiler $(DEFS) -C -P -nostdinc -o $@ $<

# ------------------------------------------------------------------------------

