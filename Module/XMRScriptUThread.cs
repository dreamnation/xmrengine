/***************************************************\
 *  COPYRIGHT 2013, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using Mono.Tasklets;
using System;
using System.Collections.Generic;
using System.Threading;



/***************************\
 *  Use standard C# code   *
 *  - uses system threads  *
\***************************/

namespace OpenSim.Region.ScriptEngine.XMREngine {

    public class ScriptUThread_Sys : IScriptUThread, IDisposable
    {
        private Exception except;
        private int active;     // -1: hibernating
                                //  0: exited
                                //  1: running
        private object activeLock = new object ();
        private XMRInstance instance;

        public ScriptUThread_Sys (XMRInstance instance)
        {
            this.instance = instance;
        }

        /**
         * @brief Start script event handler from the beginning.
         *        Return when either the script event handler completes
         *        or the script calls Hiber().
         * @returns null: script did not throw any exception so far
         *          else: script threw an exception
         */
        public Exception StartEx ()
        {
            lock (activeLock) {

                /*
                 * We should only be called when script is inactive.
                 */
                if (active != 0) throw new Exception ("active=" + active);

                /*
                 * Tell CallSEHThread() to run script event handler in a thread.
                 */
                active = 1;
                TredPoo.RunSomething (CallSEHThread);

                /*
                 * Wait for script to call Hiber() or for script to
                 * return back out to CallSEHThread().
                 */
                while (active > 0) {
                    Monitor.Wait (activeLock);
                }
            }

            /*
             * Return whether or not script threw an exception.
             */
            return except;
        }

        /**
         * @brief We now want to run some more script code from where it last hibernated
         *        until it either finishes the script event handler or until the script
         *        calls Hiber() again.
         */
        public Exception ResumeEx ()
        {
            lock (activeLock) {

                /*
                 * We should only be called when script is hibernating.
                 */
                if (active >= 0) throw new Exception ("active=" + active);

                /*
                 * Tell Hiber() to return back to script.
                 */
                active = 1;
                Monitor.PulseAll (activeLock);

                /*
                 * Wait for script to call Hiber() again or for script to
                 * return back out to CallSEHThread().
                 */
                while (active > 0) {
                    Monitor.Wait (activeLock);
                }
            }

            /*
             * Return whether or not script threw an exception.
             */
            return except;
        }

        /**
         * @brief Script is being closed out.
         *        Terminate thread asap.
         */
        public void Dispose ()
        {
            lock (activeLock) {
                instance = null;
                Monitor.PulseAll (activeLock);
            }
        }

        /**
         * @brief Determine if script is active.
         * Returns: 0: nothing started or has returned
         *             Resume() must not be called
         *             Start() may be called
         *             Hiber() must not be called
         *         -1: thread has called Hiber()
         *             Resume() may be called
         *             Start() may be called
         *             Hiber() must not be called
         *          1: thread is running
         *             Resume() must not be called
         *             Start() must not be called
         *             Hiber() may be called
         */
        public int Active ()
        {
            return active;
        }

        /**
         * @brief This thread executes the script event handler code.
         */
        private void CallSEHThread ()
        {
            lock (activeLock) {
                if (active <= 0) throw new Exception ("active=" + active);

                except = null;                  // assume completion without exception
                try {
                    instance.CallSEH ();        // run script event handler
                } catch (Exception e) {
                    except = e;                 // threw exception, save for Start()/Resume()
                }

                active = 0;                     // tell Start() or Resume() we're done
                Monitor.PulseAll (activeLock);
            }
        }

        /**
         * @brief Called by the script event handler whenever it wants to hibernate.
         */
        public void Hiber ()
        {
            if (active <= 0) throw new Exception ("active=" + active);

            // tell Start() or Resume() we are hibernating
            active = -1;
            Monitor.PulseAll (activeLock);

            // wait for Resume() or Dispose() to be called
            while ((active < 0) && (instance != null)) {
                Monitor.Wait (activeLock);
            }

            // don't execute any more script code, just exit
            if (instance == null) {
                throw new AbortedByDisposeException ();
            }
        }

        /**
         * @brief Number of remaining stack bytes.
         */
        public int StackLeft ()
        {
            return 0x7FFFFFFF;
        }

        public class AbortedByDisposeException : Exception, IXMRUncatchable { }

        /**
         * @brief Pool of threads that run script event handlers.
         */
        private class TredPoo {
            private static readonly TimeSpan idleTimeSpan = new TimeSpan (0, 0, 1, 0, 0);  // 1 minute

            private static int tredPooAvail = 0;
            private static object tredPooLock = new object ();
            private static Queue<ThreadStart> tredPooQueue = new Queue<ThreadStart> ();

            /**
             * @brief Queue a function for execution in a system thread.
             */
            public static void RunSomething (ThreadStart entry)
            {
                lock (tredPooLock) {
                    tredPooQueue.Enqueue (entry);
                    Monitor.Pulse (tredPooLock);
                    if (tredPooAvail < tredPooQueue.Count) {
                        new TredPoo ();
                    }
                }
            }

            /**
             * @brief Start a new system thread.
             *        It will shortly attempt to dequeue work or if none,
             *        add itself to the available thread list.
             */
            private TredPoo ()
            {
                Thread thread = new Thread (Main);
                thread.Name   = "XMRUThread_sys";
                thread.IsBackground = true;
                thread.Start ();
                tredPooAvail ++;
            }

            /**
             * @brief Executes items from the queue or waits a little while
             *        if nothing.  If idle for a while, it exits.
             */
            private void Main ()
            {
                int first = 1;
                ThreadStart entry;
                while (true) {
                    lock (tredPooLock) {
                        tredPooAvail -= first;
                        first = 0;
                        while (tredPooQueue.Count <= 0) {
                            tredPooAvail ++;
                            bool keepgoing = Monitor.Wait (tredPooLock, idleTimeSpan);
                            -- tredPooAvail;
                            if (!keepgoing) return;
                        }
                        entry = tredPooQueue.Dequeue ();
                    }
                    entry ();
                }
            }
        }
    }
}



/*************************************\
 *  Use Mono.Tasklets.Continuations  *
 *  - memcpy's stack                 *
\*************************************/

namespace OpenSim.Region.ScriptEngine.XMREngine {

    public partial class XMRInstance {
        public Mono.Tasklets.Continuation engstack;
        public Mono.Tasklets.Continuation scrstack;
    }

    public class ScriptUThread_Con : IScriptUThread, IDisposable
    {
        private XMRInstance instance;

        public ScriptUThread_Con (XMRInstance instance)
        {
            this.instance = instance;
        }

        private const int SAVEENGINESTACK = 0;
        private const int LOADENGINESTACK = 1;
        private const int SAVESCRIPTSTACK = 2;
        private const int LOADSCRIPTSTACK = 3;

        private Exception except;
        private int active;

        /**
         * @brief Start script event handler from the beginning.
         *        Return when either the script event handler completes
         *        or the script calls Hiber().
         * @returns null: script did not throw any exception so far
         *          else: script threw an exception
         */
        public Exception StartEx ()
        {
            /*
             * Save engine stack so we know how to jump back to engine in case
             * the script calls Hiber().
             */
            switch (instance.engstack.Store (SAVEENGINESTACK)) {

                /*
                 * Engine stack has been saved, start running the event handler.
                 */
                case SAVEENGINESTACK: {

                    /*
                     * Run event handler according to stackFrames.
                     * In either case it is assumed that stateCode and eventCode
                     * indicate which event handler is to be called and that ehArgs
                     * points to the event handler argument list.
                     */
                    active = 1;
                    except = null;
                    try {
                        instance.CallSEH ();
                    } catch (Exception e) {
                        except = e;
                    }

                    /*
                     * We now want to return to the script engine.
                     * Setting active = 0 means the microthread has exited.
                     * We need to call engstack.Restore() in case the script called Hiber()
                     * anywhere, we want to return out the corresponding Restore() and not the
                     * Start().
                     */
                    active = 0;
                    instance.engstack.Restore (LOADENGINESTACK);
                    throw new Exception ("returned from Restore()");
                }

                /*
                 * Script called Hiber() somewhere so just return back out.
                 */
                case LOADENGINESTACK: {
                    break;
                }

                default: throw new Exception ("bad engstack code");
            }

            return except;
        }

        public void Dispose ()
        { }

        /**
         * @brief Determine if script is active.
         * Returns: 0: nothing started or has returned
         *             Resume() must not be called
         *             Start() may be called
         *             Hiber() must not be called
         *         -1: thread has called Hiber()
         *             Resume() may be called
         *             Start() may be called
         *             Hiber() must not be called
         *          1: thread is running
         *             Resume() must not be called
         *             Start() must not be called
         *             Hiber() may be called
         */
        public int Active ()
        {
            return active;
        }

        /**
         * @brief Called by the script wherever it wants to hibernate.
         *        So this means to save the scripts stack in 'instance.scrstack' then
         *        restore the engstack to cause us to return back to the engine.
         */
        public void Hiber ()
        {
            /*
             * Save where we are in the script's code in 'instance.scrstack'
             * so we can wake the script when Resume() is called.
             */
            switch (instance.scrstack.Store (SAVESCRIPTSTACK)) {

                /*
                 * Script's stack is now saved in 'instance.scrstack'.
                 * Reload the engine's stack from 'instance.engstack' and jump to it.
                 */
                case SAVESCRIPTSTACK: {
                    active = -1;
                    instance.engstack.Restore (LOADENGINESTACK);
                    throw new Exception ("returned from Restore()");
                }

                /*
                 * Resume() was just called and we want to resume executing script code.
                 */
                case LOADSCRIPTSTACK: {
                    break;
                }

                default: throw new Exception ("bad scrstack code");
            }
        }

        /**
         * @brief We now want to run some more script code from where it last hibernated
         *        until it either finishes the script event handler or until the script
         *        calls Hiber() again.
         */
        public Exception ResumeEx ()
        {
            /*
             * Save where we are in the engine's code in 'instance.engstack'
             * so if the script calls Hiber() again or exits, we know how to get
             * back to the engine.
             */
            switch (instance.engstack.Store (SAVEENGINESTACK)) {

                /*
                 * This is original call to Resume() from the engine,
                 * jump to where we left off within Hiber().
                 */
                case SAVEENGINESTACK: {
                    active = 1;
                    instance.scrstack.Restore (LOADSCRIPTSTACK);
                    throw new Exception ("returned from Restore()");
                }

                /*
                 * Script has called Hiber() again, so return back to
                 * script engine code.
                 */
                case LOADENGINESTACK: {
                    break;
                }

                default: throw new Exception ("bad engstack code");
            }

            return except;
        }

        /**
         * @brief Number of remaining stack bytes.
         */
        public int StackLeft ()
        {
            return 0x7FFFFFFF;
        }
    }
}



/***********************************\
 *  Use Mono.Tasklets.MMRUThreads  *
 *  - switches stack pointer       *
\***********************************/

namespace OpenSim.Region.ScriptEngine.XMREngine {

    public class ScriptUThread_MMR : MMRUThread, IScriptUThread
    {
        private XMRInstance instance;

        public ScriptUThread_MMR (XMRInstance instance)
                : base ((IntPtr)instance.m_StackSize, instance.m_DescName)
        {
            this.instance = instance;
        }

        public Exception StartEx ()
        {
            return StartEx (instance.CallSEH);
        }

        public Exception ResumeEx ()
        {
            return ResumeEx (null);
        }

        public void Hiber ()
        {
            MMRUThread.Suspend ();
        }

        public new int StackLeft ()
        {
            return (int)MMRUThread.StackLeft ();
        }
    }
}
