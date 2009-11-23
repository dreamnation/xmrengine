// ******************************************************************
// Copyright (c) 2008, 2009 Melanie Thielker
//
// All rights reserved
//

#define LINUX
using System;
using System.Text;


#if WINDOWS
using System.Runtime.InteropServices;
using System.Management;

namespace Xumeo
{
    public class HardwareInfo
    {
        
        /// <summary>
        /// return Volume Serial Number from hard drive
        /// </summary>
        /// <param name="strDriveLetter">[optional] Drive letter</param>
        /// <returns>[string] VolumeSerialNumber</returns>
        public string GetVolumeSerial(string strDriveLetter)
        {
        if( strDriveLetter=="" || strDriveLetter==null) strDriveLetter="C";
        ManagementObject disk = 
            new ManagementObject("win32_logicaldisk.deviceid=\"" + strDriveLetter +":\"");
        disk.Get();
        return disk["VolumeSerialNumber"].ToString();
        }
        
        /// <summary>
        /// Returns MAC Address from first Network Card in Computer
        /// </summary>
        /// <returns>[string] MAC Address</returns>
        public string GetMACAddress()
            {
            ManagementClass mc = new ManagementClass("Win32_NetworkAdapterConfiguration");
            ManagementObjectCollection moc = mc.GetInstances();
            string MACAddress=String.Empty;
            foreach(ManagementObject mo in moc)
            {
                if(MACAddress==String.Empty)  // only return MAC Address from first card
                {
                    if((bool)mo["IPEnabled"] == true) MACAddress= mo["MacAddress"].ToString() ;
                }
                        mo.Dispose();
            }
            MACAddress=MACAddress.Replace(":","");
            return MACAddress;
            }
        /// <summary>
        /// Return processorId from first CPU in machine
        /// </summary>
        /// <returns>[string] ProcessorId</returns>
        public string GetCPUId()
        {
            string cpuInfo =  String.Empty;
            string temp=String.Empty;
            ManagementClass mc = new ManagementClass("Win32_Processor");
            ManagementObjectCollection moc = mc.GetInstances();
            foreach(ManagementObject mo in moc)
            {
                if(cpuInfo==String.Empty) 
                {// only return cpuInfo from first CPU
                    cpuInfo = mo.Properties["ProcessorId"].Value.ToString();
                }             
            }
            return cpuInfo;
        }
    }
}
#endif

#if LINUX
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Xumeo
{
    public class HardwareInfo
    {
        public string GetVolumeSerial(string strDriveLetter)
        {
            return "";
        }

        public string GetMACAddress()
        {
            StreamReader reader = File.OpenText("/sys/class/net/eth0/address");

            string line;
            List<string> nics = new List<string>();

            while ((line = reader.ReadLine()) != null)
            {
                return line.Replace(":", "");
            }

            return "555555555555";
        }

        public string GetCPUId()
        {
            StreamReader reader = File.OpenText("/proc/cpuinfo");

            string line;
            List<string> cpus = new List<string>();

            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("model name"))
                    cpus.Add(line);
            }

            reader.Close();

            string cpustring = String.Empty;

            foreach (string s in cpus)
                cpustring += s;
            
            System.Security.Cryptography.MD5CryptoServiceProvider md5 =
                    new System.Security.Cryptography.MD5CryptoServiceProvider();

            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(cpustring);
            
            bytes = md5.ComputeHash(bytes);

            return BitConverter.ToString(bytes).Replace("-", "");
        }
    }
}

#endif
