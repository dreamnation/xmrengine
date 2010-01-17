////////////////////////////////////////////////////////////////
//
// (c) 2009, 2010 Careminster Limited and Melanie Thielker
//
// All rights reserved
//

#include <hdfs.h>
#include <stdio.h>
#include <stdlib.h>
#include <fcntl.h>

extern "C"{

hdfsFS fs = 0;

int OpenHdfs(unsigned char *host, int port)
{
    fs = hdfsConnect((char *)host, port);
    if (fs != 0)
    {
        hdfsSetWorkingDirectory(fs, "/");
        return 0;
    }

    return -1;
}

void CloseHdfs()
{
    hdfsDisconnect(fs);
    fs = 0;
}

int Open(unsigned char *path, int flags, int replicas)
{
    if (hdfsExists(fs, (char *)path) < 0)
    {
        if (!(flags & 1))
            return 0;

        hdfsDelete(fs, (char *)path);
    }

    hdfsFile ret = hdfsOpenFile(fs, (char *)path, flags,
          0, replicas, 0);
    return (int)ret;
}

int Close(int fd)
{
    return hdfsCloseFile(fs, (hdfsFile)fd);
}

int Read(int fd, unsigned char *buf, int len)
{
    return hdfsRead(fs, (hdfsFile)fd, (char *)buf, len);
}

int Write(int fd, unsigned char *buf, int len)
{
    return hdfsWrite(fs, (hdfsFile)fd, (char *)buf, len);
}

int Size(unsigned char *path)
{
    hdfsFileInfo *info;

    info = hdfsGetPathInfo(fs, (char *)path);
    if (info == 0)
        return -1;
    int size = info->mSize;
    hdfsFreeFileInfo(info, 1);

    return size;
}

int Mkdirs(unsigned char *path)
{
    return hdfsCreateDirectory(fs, (char *)path);
}

}
