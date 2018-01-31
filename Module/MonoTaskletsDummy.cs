// COPYRIGHT 2009,2012,2016 Mike Rieker, Beverly, MA, USA, mrieker@nii.net

// Used to build a dummy Mono.Tasklets.dll file when running on Windows
// Will also work if running with mono, it will just not allow use of
// the "con" and "mmr" thread models, only "sys" will work.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Mono.Tasklets {
    public class Continuation : IDisposable
    {
        public Continuation ()
        {
            throw new Exception ("'con' thread model not suported");
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

    public class MMRUThread : IDisposable
    {
        public delegate void Entry ();

        public static IntPtr StackLeft () { return (IntPtr)0; }

        public string Name { get { return null; } }

        public MMRUThread ()
        {
            throw new Exception ("'mmr' thread model not suported");
        }

        public MMRUThread (string name)
        {
            throw new Exception ("'mmr' thread model not suported");
        }

        public MMRUThread (IntPtr stackSize)
        {
            throw new Exception ("'mmr' thread model not suported");
        }

        public MMRUThread (IntPtr stackSize, string name)
        {
            throw new Exception ("'mmr' thread model not suported");
        }

        public void Dispose ()
        { }

        public void Start (Entry entry)
        { }

        public static void Suspend () { }
        public static void Suspend (Exception except)
        { }

        public void Resume () { }
        public void Resume (Exception except)
        { }

        public static void Exit () { }
        public static void Exit (Exception except)
        { }

        public int Active ()
        {
            return 0;
        }

        public static MMRUThread Current ()
        {
            return null;
        }

        public Exception StartEx (Entry entry)
        {
            return null;
        }

        public static Exception SuspendEx (Exception except)
        {
            return null;
        }

        public Exception ResumeEx (Exception except)
        {
            return null;
        }

        public static Exception ExitEx (Exception except)
        {
            return null;
        }
    }
}
