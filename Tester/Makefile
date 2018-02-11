MONODIR       ?= /usr
MONOBINDIR    ?= $(MONODIR)/bin
MONODLLDIR    ?= $(MONODIR)/lib/mono/4.5
OPENSIMDLLDIR ?= osbin
SOURCEDIR     ?= ../Module

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

TESTERVERFILE := xmrengtestver_$(COMMITHASH)_$(COMMITCLEAN).cs
TESTERCSFILES := xmrengtest.cs xmrengtestilslapi.cs $(TESTERVERFILE)

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

xmrengtest.exe: $(TESTERCSFILES) $(SOURCEFILES)
	$(MONOBINDIR)/mcs -debug -out:$@ \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS)) \
		-reference:$(MONODLLDIR)/System.Drawing.dll \
		$(TESTERCSFILES) $(SOURCEFILES)

xmrengtestilslapi.cs: xmrengtestilslapi.pfx makexmrengtestilslapi.inc xmrengtestilslapi.sfx
	cat xmrengtestilslapi.pfx makexmrengtestilslapi.inc xmrengtestilslapi.sfx > xmrengtestilslapi.cs

$(TESTERVERFILE):
	rm -f xmrengtestver*.cs
	echo "namespace OpenSim.Region.ScriptEngine.XMREngine { public partial class XMREngTest {" > $(TESTERVERFILE)
	echo "public static readonly string gitcommithash  = \"$(COMMITHASH)\";" >> $(TESTERVERFILE)
	echo "public static readonly string gitcommitdate  = \"$(COMMITDATE)\";" >> $(TESTERVERFILE)
	echo "public static readonly int    gitcommitclean = $(COMMITCLEAN);"    >> $(TESTERVERFILE)
	echo "} }" >> $(TESTERVERFILE)

makexmrengtestilslapi.inc: makexmrengtestilslapi.exe $(patsubst %,$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS))
	MONO_PATH=$(OPENSIMDLLDIR) $(MONOBINDIR)/mono --debug makexmrengtestilslapi.exe > makexmrengtestilslapi.tmp
	mv -f makexmrengtestilslapi.tmp makexmrengtestilslapi.inc

makexmrengtestilslapi.exe: makexmrengtestilslapi.cs
	$(MONOBINDIR)/mcs -debug -out:$@ \
		$(patsubst %,-reference:$(OPENSIMDLLDIR)/%,$(REFDOPENSIMDLLS)) \
		$<

