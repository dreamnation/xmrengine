//////////////////////////////////////////////////////////////
//
// Copyright (c) 2009 Careminster Limited and Melanie Thielker
//
// All rights reserved
//

using Mono.Tasklets;
using System;
using System.IO;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Lifetime;
using OpenSim.Region.ScriptEngine.Shared.Api.Runtime;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;

namespace OpenSim.Region.ScriptEngine.XMREngine.Loader
{
    public class XMRLoader : IDisposable
    {
        private ScriptBaseClass m_ScriptBase;
        private ScriptWrapper m_Wrapper = null;
        private ScriptObjCode m_ObjCode = null;
        private string m_DescName;
        private string m_AssetID;
        public StateChangeDelegate m_StateChange;

        public XMRLoader()
        {
            m_ScriptBase = new ScriptBaseClass();
        }

        ////~XMRLoader()
        ////{
        ////    if (m_DescName != null) {
        ////        Console.WriteLine("[XMREngine] ~XMRLoader*: gc " + m_DescName);
        ////    } else {
        ////        Console.WriteLine("[XMREngine] ~XMRLoader*: gc (unknown)");
        ////    }
        ////}

        public void Dispose()
        {
            if (m_Wrapper != null)
                m_Wrapper.Dispose();
            m_Wrapper = null;
        }

        public void InitApi(string name, IScriptApi api)
        {
            m_ScriptBase.InitApi(name, api);
        }

        public Exception Load(ScriptObjCode objCode, string descName, string assetID)
        {
            m_ObjCode  = objCode;
            m_DescName = descName;
            m_AssetID  = assetID;

            try
            {
                m_Wrapper = new ScriptWrapper(objCode, descName);
            }
            catch (Exception e)
            {
                return e;
            }

            if (m_Wrapper == null)
            {
                return new Exception ("ScriptWrapper.CreateScriptInstance returned null");
            }

            m_Wrapper.beAPI = m_ScriptBase;
            m_Wrapper.alwaysSuspend = true;
            m_Wrapper.stateChange = CallLoaderStateChange;

            return null;
        }

        public string GetStateName(int stateCode)
        {
            return m_Wrapper.GetStateName(stateCode);
        }

        public Exception PostEvent(string eventName, Object[] args)
        {
            try
            {
                m_Wrapper.StartEventHandler(eventName, args);
            }
            catch (Exception e)
            {
                return e;
            }
            return null;
        }

        public bool RunOne()
        {
            return m_Wrapper.ResumeEventHandler();
        }

        public void Reset()
        {
            m_Wrapper.Dispose();
            m_Wrapper = null;

            Load(m_ObjCode, m_DescName, m_AssetID);
        }

        public int StateCode
        {
            get { return m_Wrapper.stateCode; }
        }

        public int GetStateEventFlags(int stateCode)
        {
            if ((stateCode < 0) ||
                (stateCode >= m_Wrapper.objCode.scriptEventHandlerTable.GetLength(0)))
            {
                return 0;
            }

            int code = 0;
            for (int i = 0 ; i < 32; i ++)
            {
                if (m_Wrapper.objCode.scriptEventHandlerTable[stateCode, i] != null)
                {
                    code |= 1 << i;
                }
            }

            return code;
        }

        public bool DoGblInit
        {
            get { return m_Wrapper.doGblInit; }
            set { m_Wrapper.doGblInit = value; }
        }

        public Byte[] GetSnapshot()
        {
            MemoryStream ms = new MemoryStream();

            bool suspended = m_Wrapper.MigrateOutEventHandler(ms);

            ms.WriteByte((byte)(suspended ? 1 : 0));

            Byte[] data = ms.ToArray();

            ms.Close();

            return data;
        }

        public bool RestoreSnapshot(Byte[] data)
        {
            MemoryStream ms = new MemoryStream();

            ms.Write(data, 0, data.Length);
            ms.Seek(0, SeekOrigin.Begin);

            m_Wrapper.MigrateInEventHandler(ms);

            bool suspended = ms.ReadByte() > 0;

            ms.Close();

            return suspended;
        }

        private void CallLoaderStateChange(string newState)
        {
            m_StateChange(newState);
        }
    }
}
