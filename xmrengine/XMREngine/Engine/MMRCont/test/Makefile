
HOME=/home/mrieker

scripttest.exe: \
		MMRScriptTest.cs \
		testlslapi.cs \
		$(HOME)/monosrc/mcs/class/lib/net_2_0/Mono.Tasklets.dll \
		$(HOME)/opensim/bin/log4net.dll \
		$(HOME)/opensim/bin/OpenMetaverseTypes.dll \
		$(HOME)/opensim/bin/OpenSim.Region.Framework.dll \
		$(HOME)/opensim/bin/OpenSim.Region.ScriptEngine.Shared.dll \
		$(HOME)/opensim/bin/OpenSim.Region.ScriptEngine.Shared.Api.dll \
		$(HOME)/opensim/bin/OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll \
		$(HOME)/opensim/bin/OpenSim.Region.ScriptEngine.XMREngine.Engine.MMRCont.dll
	gmcs -debug -out:scripttest.exe \
		MMRScriptTest.cs \
		testlslapi.cs \
		-reference:$(HOME)/monosrc/mcs/class/lib/net_2_0/Mono.Tasklets.dll \
		-reference:$(HOME)/opensim/bin/log4net.dll \
		-reference:$(HOME)/opensim/bin/OpenMetaverseTypes.dll \
		-reference:$(HOME)/opensim/bin/OpenSim.Region.Framework.dll \
		-reference:$(HOME)/opensim/bin/OpenSim.Region.ScriptEngine.Shared.dll \
		-reference:$(HOME)/opensim/bin/OpenSim.Region.ScriptEngine.Shared.Api.dll \
		-reference:$(HOME)/opensim/bin/OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll \
		-reference:$(HOME)/opensim/bin/OpenSim.Region.ScriptEngine.XMREngine.Engine.MMRCont.dll
