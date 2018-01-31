MONODIR       ?= /usr
MONOBINDIR    ?= $(MONODIR)/bin
MONODLLDIR    ?= $(MONODIR)/lib/mono/4.5
OPENSIMDLLDIR ?= bin_090
TASKLETSDLL   ?= $(MONODLLDIR)/Mono.Tasklets.dll
SOURCEDIR     ?= ../../../Module

COMMITHASH  := $(strip $(shell git log -1 | grep ^commit | sed 's/commit//g'))
COMMITDATE  := $(strip $(shell git log -1 --date=iso | grep ^Date: | sed 's/Date://g'))
COMMITCLEAN := $(strip $(shell git status | grep -q modified: ; echo $$?))

SOURCEFILES := \
	$(SOURCEDIR)/MMRDelegateCommon.cs \
	$(SOURCEDIR)/MMRIEventHandlers.cs \
	$(SOURCEDIR)/MMRInternalFuncDict.cs \
	$(SOURCEDIR)/MMRScriptBinOpStr.cs \
	$(SOURCEDIR)/MMRScriptCodeGen.cs \
	$(SOURCEDIR)/MMRScriptCollector.cs \
	$(SOURCEDIR)/MMRScriptCompValu.cs \
	$(SOURCEDIR)/MMRScriptConsts.cs \
	$(SOURCEDIR)/MMRScriptEventCode.cs \
	$(SOURCEDIR)/MMRScriptInlines.cs \
	$(SOURCEDIR)/MMRScriptMyILGen.cs \
	$(SOURCEDIR)/MMRScriptObjCode.cs \
	$(SOURCEDIR)/MMRScriptObjWriter.cs \
	$(SOURCEDIR)/MMRScriptReduce.cs \
	$(SOURCEDIR)/MMRScriptTokenize.cs \
	$(SOURCEDIR)/MMRScriptTypeCast.cs \
	$(SOURCEDIR)/MMRScriptVarDict.cs \
	$(SOURCEDIR)/MMRWebRequest.cs \
	$(SOURCEDIR)/XMRArray.cs \
	$(SOURCEDIR)/XMRHeapTracker.cs \
	$(SOURCEDIR)/XMRInstAbstract.cs \
	$(SOURCEDIR)/XMRObjectTokens.cs \
	$(SOURCEDIR)/XMRScriptUThread.cs \
	$(SOURCEDIR)/XMRSDTypeClObj.cs

TESTERCSFILES := \
	xmrengtest.cs \
	$(SOURCEFILES)

REFDOPENSIMDLLS := \
	OpenMetaverseTypes.dll \
	OpenSim.Framework.dll \
	OpenSim.Region.ScriptEngine.Shared.dll \
	OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll

default: xmrengtest.zip

xmrengtest.zip: xmrengtest.exe $(patsubst %,$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS))
	$(foreach dll,$(REFDOPENSIMDLLS),$(shell cp $(OPENSIMDLLDIR)/$(dll) $(dll)))
	rm -f xmrengtest.zip
	zip -D xmrengtest.zip xmrengtest.exe $(REFDOPENSIMDLLS)
	rm -f $(REFDOPENSIMDLLS)

xmrengtest.exe: $(TESTERCSFILES) $(wildcard *.h)
	$(MONOBINDIR)/mcs -debug -out:$@ \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS)) \
		-reference:$(MONODLLDIR)/Mono.Tasklets.dll \
		-reference:$(MONODLLDIR)/System.Drawing.dll \
		$(TESTERCSFILES)

xmrengtest.cs: xmrengtest.cx makexmrengtestilslapi.inc
	cpp -C -P -nostdinc -o $@ $<

makexmrengtestilslapi.inc: makexmrengtestilslapi.exe $(patsubst %,$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS))
	MONO_PATH=$(OPENSIMDLLDIR) $(MONOBINDIR)/mono --debug makexmrengtestilslapi.exe > makexmrengtestilslapi.tmp
	mv -f makexmrengtestilslapi.tmp makexmrengtestilslapi.inc

makexmrengtestilslapi.exe: makexmrengtestilslapi.cs
	$(MONOBINDIR)/mcs -debug -out:$@ \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS)) \
		$<

