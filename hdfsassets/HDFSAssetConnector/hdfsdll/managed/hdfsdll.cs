using System;
using System.Runtime.InteropServices;
using System.Security;

namespace HDFS
{
    public static class HdfsClient
    {
        [DllImport("hdfsdll", EntryPoint = "OpenHdfs"), SuppressUnmanagedCodeSecurity]
        public static extern int OpenHdfs([MarshalAs(UnmanagedType.LPArray)] byte[] host, int port);

        [DllImport("hdfsdll", EntryPoint = "CloseHdfs"), SuppressUnmanagedCodeSecurity]
        public static extern void CloseHdfs();

        [DllImport("hdfsdll", EntryPoint = "Open"), SuppressUnmanagedCodeSecurity]
        public static extern int Open([MarshalAs(UnmanagedType.LPArray)] byte[] path, int flags, int replicas);

        [DllImport("hdfsdll", EntryPoint = "Close"), SuppressUnmanagedCodeSecurity]
        public static extern int Close(int fd);

        [DllImport("hdfsdll", EntryPoint = "Read"), SuppressUnmanagedCodeSecurity]
        public static extern int Read(int fd, [MarshalAs(UnmanagedType.LPArray)] byte[] buf, int len);

        [DllImport("hdfsdll", EntryPoint = "Write"), SuppressUnmanagedCodeSecurity]
        public static extern int Write(int fd, [MarshalAs(UnmanagedType.LPArray)] byte[] buf, int len);

        [DllImport("hdfsdll", EntryPoint = "Size"), SuppressUnmanagedCodeSecurity]
        public static extern int Size([MarshalAs(UnmanagedType.LPArray)] byte[] path);

        [DllImport("hdfsdll", EntryPoint = "Mkdirs"), SuppressUnmanagedCodeSecurity]
        public static extern int Mkdirs([MarshalAs(UnmanagedType.LPArray)] byte[] path);

    }
}
