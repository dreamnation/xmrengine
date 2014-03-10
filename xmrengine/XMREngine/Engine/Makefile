MONODIR       ?= /home/kunta/mono64.2109
MONOBINDIR    ?= $(MONODIR)/bin
MONODLLDIR    ?= $(MONODIR)/lib/mono/4.0
OPENSIMDLLDIR ?= ../../../bin

COMMITHASH  := $(strip $(shell git log -1 | grep ^commit | sed 's/commit//g'))
COMMITDATE  := $(strip $(shell git log -1 --date=iso | grep ^Date: | sed 's/Date://g'))
COMMITCLEAN := $(strip $(shell git status | grep -q modified: ; echo $$?))

EXTHASHCODE := $(shell make -s -f makestrhashcode STR=world.dreamnation.net)

HAVE_MONOCONTS   ?= $(shell ../Compiler/findmonotasklet.sh $(MONOBINDIR) $(MONODLLDIR) Continuation)
HAVE_MMRUTHREADS ?= $(shell ../Compiler/findmonotasklet.sh $(MONOBINDIR) $(MONODLLDIR) MMRUThread)

XMRENGINEDLLS := xmrengine_sys.dll
ifeq ($(HAVE_MONOCONTS),1)
  XMRENGINEDLLS += xmrengine_con.dll
  XMRENGINEDLLS += xmrengine_con_ext.dll
endif
ifeq ($(HAVE_MMRUTHREADS),1)
  XMRENGINEDLLS += xmrengine_mmr.dll
endif

ENGINECXFILES := XMREngine.cx \
                 XMREvents.cx \
                 XMRInstBackend.cx \
                 XMRInstCapture.cx \
                 XMRInstCtor.cx \
                 XMRInstMain.cx \
                 XMRInstMisc.cx \
                 XMRInstQueue.cx \
                 XMRInstRun.cx \
                 XMRInstSorpra.cx \
                 XMRScriptThread.cx \
                 XMRScriptUThread.cx \
                 XMREngXmrTestLs.cx \
                 MMRScriptCompile.cx

REFERENCED := ../Compiler/xmrengshare.dll \
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

default: $(XMRENGINEDLLS)

# ------------------------------------------------------------------------------
#
# Create files needed for OpenSim
#
#  - xmrengine_sys.dll:
#      will run on anything including WindoesNT (system threads)
#      supports UThreadModel = "sys" only
#
#  - xmrengine_con.dll:
#      will run on unpatched Mono (user stack memcpy)
#      supports UThreadModel = "sys" and "con"
#
#  - xmrengine_mmr.dll:
#      requires MMR patched Mono (user stack pointer switching)
#      supports UThreadModel = "sys", "con" and "mmr"
#
# Copy exactly one of the above to your OpenSim/bin directory
# along with the ../Compiler/xmrengshare.dll file
#
xmrengine_sys.dll: $(ENGINECXFILES:.cx=_sys.cs) XMREngCommit_$(COMMITHASH)_$(COMMITCLEAN).cs $(REFERENCED)
	rm -f xmrengine_sys.dll*
	$(MONOBINDIR)/dmcs -debug -target:library \
		-out:$@ \
		$(ENGINECXFILES:.cx=_sys.cs) XMREngCommit_$(COMMITHASH)_$(COMMITCLEAN).cs \
		$(patsubst %.dll,-reference:%.dll,$(REFERENCED))

xmrengine_con.dll: $(ENGINECXFILES:.cx=_con.cs) XMREngCommit_$(COMMITHASH)_$(COMMITCLEAN).cs $(REFERENCED)
	rm -f xmrengine_con.dll*
	$(MONOBINDIR)/dmcs -debug -target:library \
		-out:$@ \
		$(ENGINECXFILES:.cx=_con.cs) XMREngCommit_$(COMMITHASH)_$(COMMITCLEAN).cs \
		$(patsubst %.dll,-reference:%.dll,$(REFERENCED)) \
		-reference:$(MONODLLDIR)/Mono.Tasklets.dll

xmrengine_con_ext.dll: $(ENGINECXFILES:.cx=_con_ext.cs) XMREngCommit_$(COMMITHASH)_$(COMMITCLEAN).cs $(REFERENCED)
	rm -f xmrengine_con_ext.dll*
	$(MONOBINDIR)/dmcs -debug -target:library \
		-out:$@ \
		$(ENGINECXFILES:.cx=_con_ext.cs) XMREngCommit_$(COMMITHASH)_$(COMMITCLEAN).cs \
		$(patsubst %.dll,-reference:%.dll,$(REFERENCED)) \
		-reference:$(MONODLLDIR)/Mono.Tasklets.dll

xmrengine_mmr.dll: $(ENGINECXFILES:.cx=_mmr.cs) XMREngCommit_$(COMMITHASH)_$(COMMITCLEAN).cs $(REFERENCED)
	rm -f xmrengine_mmr.dll*
	$(MONOBINDIR)/dmcs -debug -target:library \
		-out:$@ \
		$(ENGINECXFILES:.cx=_mmr.cs) XMREngCommit_$(COMMITHASH)_$(COMMITCLEAN).cs \
		$(patsubst %.dll,-reference:%.dll,$(REFERENCED)) \
		-reference:$(MONODLLDIR)/Mono.Tasklets.dll

XMREngCommit_$(COMMITHASH)_$(COMMITCLEAN).cs: XMREngCommit.cx
	rm -f XMREngCommit_*.cs
	cpp -I../Compiler \
		-DGITCOMMITHASH='"$(COMMITHASH)"' \
		-DGITCOMMITDATE='"$(COMMITDATE)"' \
		-DGITCOMMITCLEAN=$(COMMITCLEAN) -C -P -nostdinc -o $@ $<

%_sys.cs: %.cx $(wildcard ../*/*.h)
	cpp -I../Compiler -DHAVE_MONOCONTS=0 -DHAVE_MMRUTHREADS=0 -DEXTERNAL=0 -C -P -nostdinc -o $@ $<

%_con.cs: %.cx $(wildcard ../*/*.h)
	cpp -I../Compiler -DHAVE_MONOCONTS=1 -DHAVE_MMRUTHREADS=0 -DEXTERNAL=0 -C -P -nostdinc -o $@ $<

%_con_ext.cs: %.cx $(wildcard ../*/*.h)
	cpp -I../Compiler -DHAVE_MONOCONTS=1 -DHAVE_MMRUTHREADS=0 -DEXTERNAL=$(EXTHASHCODE) -C -P -nostdinc -o $@ $<

%_mmr.cs: %.cx $(wildcard ../*/*.h)
	cpp -I../Compiler -DHAVE_MONOCONTS=1 -DHAVE_MMRUTHREADS=1 -DEXTERNAL=0 -C -P -nostdinc -o $@ $<

../Compiler/xmrengshare.dll: $(wildcard ../Compiler/*.cx) $(wildcard ../Compiler/*.h)
	make -C ../Compiler xmrengshare.dll

# ------------------------------------------------------------------------------

