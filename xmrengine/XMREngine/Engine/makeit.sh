#!/bin/bash -v
mcs -d:'TRACE;DEBUG' -debug \
	-out:xmrengine.dll -target:library \
	-r:../../../bin_090//log4net.dll \
	-r:../../../bin_090//Nini.dll \
	-r:../../../bin_090//Mono.Addins.dll \
	-r:../../../bin_090//OpenMetaverse.dll \
	-r:../../../bin_090//OpenMetaverse.StructuredData.dll \
	-r:../../../bin_090//OpenMetaverseTypes.dll \
	-r:../../../bin_090//OpenSim.Framework.dll \
	-r:../../../bin_090//OpenSim.Framework.Console.dll \
	-r:../../../bin_090//OpenSim.Framework.Monitoring.dll \
	-r:../../../bin_090//OpenSim.Region.ClientStack.LindenCaps.dll \
	-r:../../../bin_090//OpenSim.Region.CoreModules.dll \
	-r:../../../bin_090//OpenSim.Region.Framework.dll \
	-r:../../../bin_090//OpenSim.Region.ScriptEngine.Shared.dll \
	-r:../../../bin_090//OpenSim.Region.ScriptEngine.Shared.Api.dll \
	-r:../../../bin_090//OpenSim.Region.ScriptEngine.Shared.Api.Runtime.dll \
	-r:../../../bin_090//OpenSim.Services.Interfaces.dll \
	-r:Mono.Tasklets.dll \
	-r:System.Drawing.dll \
MMRScriptCompile.cx \
XMREngine.cx \
XMREngXmrTestLs.cx \
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
../Compiler/IEventHandlers.cx \
../Compiler/MMRDelegateCommon.cx \
../Compiler/MMRInternalFuncDict.cx \
../Compiler/MMRScriptBinOpStr.cx \
../Compiler/MMRScriptCodeGen.cx \
../Compiler/MMRScriptCollector.cx \
../Compiler/MMRScriptCompValu.cx \
../Compiler/MMRScriptConsts.cx \
../Compiler/MMRScriptEventCode.cx \
../Compiler/MMRScriptInlines.cx \
../Compiler/MMRScriptMyILGen.cx \
../Compiler/MMRScriptObjCode.cx \
../Compiler/MMRScriptObjWriter.cx \
../Compiler/MMRScriptReduce.cx \
../Compiler/MMRScriptTokenize.cx \
../Compiler/MMRScriptTypeCast.cx \
../Compiler/MMRScriptVarDict.cx \
../Compiler/MMRWebRequest.cx \
../Compiler/XMRArray.cx \
../Compiler/XMRHeapTracker.cx \
../Compiler/XMRInstAbstract.cx \
../Compiler/XMRObjectTokens.cx \
../Compiler/XMRSDTypeClObj.cx