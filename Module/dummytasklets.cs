// Author: Paolo Molaro <lupus@ximian.com>
//
// Copyright (C) 2009 Novell (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// COPYRIGHT 2009,2012,2016 Mike Rieker, Beverly, MA, USA, mrieker@nii.net

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Mono.Tasklets {
    public class Continuation : IDisposable
    {
        public Continuation ()
        {
            throw new Exception ("not suported");
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
            throw new Exception ("not suported");
        }

        public MMRUThread (string name)
        {
            throw new Exception ("not suported");
        }

        public MMRUThread (IntPtr stackSize)
        {
            throw new Exception ("not suported");
        }

        public MMRUThread (IntPtr stackSize, string name)
        {
            throw new Exception ("not suported");
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
