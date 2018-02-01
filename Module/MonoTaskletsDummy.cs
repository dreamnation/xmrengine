// COPYRIGHT 2009,2012,2016 Mike Rieker, Beverly, MA, USA, mrieker@nii.net

// Used to build a dummy Mono.Tasklets.dll file when running on Windows
// Will also work if running with mono, it will just not allow use of
// the "con" and "mmr" thread models, only "sys" will work.

using System;

namespace Mono.Tasklets {
    public class Continuation : IDisposable
    {
        public Continuation ()
        {
            throw new NotSupportedException ("'con' thread model requires mono");
        }
        public void Dispose ()
        { }

        public void Mark ()
        { }

        public int Store (int state)
        {
            return 0;
        }

        public void Restore (int state)
        { }
    }
}
