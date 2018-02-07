/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace OpenSim.Region.ScriptEngine.XMREngine {

    public partial class XMRInstance
    {
        private int utactive;   // -1: hibernating
                                //  0: exited
                                //  1: running

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
             * We should only be called when no event handler running.
             */
            if (utactive != 0) throw new Exception ("utactive=" + utactive);

            /*
             * Start script event handler from very beginning.
             */
            utactive = 1;
            Exception except = null;
            callMode = XMRInstance.CallMode_NORMAL;
            try {
                CallSEH ();                 // run script event handler
                utactive = 0;
            } catch (StackHibernateException) {
                if (callMode != XMRInstance.CallMode_SAVE) {
                    throw new Exception ("callMode=" + callMode);
                }
                utactive = -1;              // it is hibernating, can be resumed
            } catch (Exception e) {
                utactive = 0;
                except = e;                 // threw exception, save for Start()/Resume()
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
            /*
             * We should only be called when script is hibernating.
             */
            if (utactive >= 0) throw new Exception ("utactive=" + utactive);

            /*
             * Resume script from captured stack.
             */
            callMode = XMRInstance.CallMode_RESTORE;
            suspendOnCheckRunTemp = true;
            Exception except = null;
            try {
                CallSEH ();                 // run script event handler
                utactive = 0;
            } catch (StackHibernateException) {
                if (callMode != XMRInstance.CallMode_SAVE) {
                    throw new Exception ("callMode=" + callMode);
                }
                utactive = -1;
            } catch (Exception e) {
                utactive = 0;
                except = e;                 // threw exception, save for Start()/Resume()
            }

            /*
             * Return whether or not script threw an exception.
             */
            return except;
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
            return utactive;
        }

        /**
         * @brief Called by the script event handler whenever it wants to hibernate.
         */
        public void Hiber ()
        {
            if (callMode != XMRInstance.CallMode_NORMAL) {
                throw new Exception ("callMode=" + callMode);
            }

            switch (utactive) {

                // the stack has been restored as a result of calling ResumeEx()
                // say the microthread is now active and resume processing
                case -1: {
                    utactive = 1;
                    return;
                }

                // the script event handler wants to hibernate
                // capture stack frames and unwind to Start() or Resume()
                case 1: {
                    callMode = XMRInstance.CallMode_SAVE;
                    stackFrames = null;
                    throw new StackHibernateException ();
                }

                default: throw new Exception ("utactive=" + utactive);
            }
        }

        /**
         * @brief Number of remaining stack bytes.
         */
        public int StackLeft ()
        {
            return 0x7FFFFFFF;
        }

        public class StackHibernateException : Exception, IXMRUncatchable { }
    }
}
