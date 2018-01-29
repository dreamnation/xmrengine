/***************************************************\
 *  COPYRIGHT 2012, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace OpenSim.Region.ScriptEngine.XMREngine {

    public class DelegateCommon {
        private string sig;  // rettype(arg1type,arg2type,...), eg, "void(list,string,integer)"
        private Type type;   // resultant delegate type

        private static Dictionary<string, DelegateCommon> delegateCommons = new Dictionary<string, DelegateCommon> ();
        private static Dictionary<Type, DelegateCommon> delegateCommonsBySysType = new Dictionary<Type, DelegateCommon> ();
        private static ModuleBuilder delegateModuleBuilder = null;
        public  static Type[] constructorArgTypes = new Type[] { typeof (object), typeof (IntPtr) };

        private DelegateCommon () { }

        public static Type GetType (System.Type ret, System.Type[] args, string sig)
        {
            DelegateCommon dc;
            lock (delegateCommons) {
                if (!delegateCommons.TryGetValue (sig, out dc)) {
                    dc = new DelegateCommon ();
                    dc.sig  = sig;
                    dc.type = CreateDelegateType (sig, ret, args);
                    delegateCommons.Add (sig, dc);
                    delegateCommonsBySysType.Add (dc.type, dc);
                }
            }
            return dc.type;
        }

        public static Type TryGetType (string sig)
        {
            DelegateCommon dc;
            lock (delegateCommons) {
                if (!delegateCommons.TryGetValue (sig, out dc)) dc = null;
            }
            return (dc == null) ? null : dc.type;
        }

        public static string TryGetName (Type t)
        {
            DelegateCommon dc;
            lock (delegateCommons) {
                if (!delegateCommonsBySysType.TryGetValue (t, out dc)) dc = null;
            }
            return (dc == null) ? null : dc.sig;
        }

        // http://blog.bittercoder.com/PermaLink,guid,a770377a-b1ad-4590-9145-36381757a52b.aspx
        private static Type CreateDelegateType (string name, Type retType, Type[] argTypes)
        {
            if (delegateModuleBuilder == null) {
                AssemblyName assembly = new AssemblyName();
                assembly.Name = "CustomDelegateAssembly";
                AssemblyBuilder assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(assembly, AssemblyBuilderAccess.Run);
                delegateModuleBuilder = assemblyBuilder.DefineDynamicModule("CustomDelegateModule");
            }

            TypeBuilder typeBuilder = delegateModuleBuilder.DefineType(name, 
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class |
                TypeAttributes.AnsiClass | TypeAttributes.AutoClass, typeof (MulticastDelegate));

            ConstructorBuilder constructorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.RTSpecialName | MethodAttributes.HideBySig | MethodAttributes.Public,
                CallingConventions.Standard, constructorArgTypes);
            constructorBuilder.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

            MethodBuilder methodBuilder = typeBuilder.DefineMethod("Invoke",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot |
                MethodAttributes.Virtual, retType, argTypes);
            methodBuilder.SetImplementationFlags(MethodImplAttributes.Managed | MethodImplAttributes.Runtime);

            return typeBuilder.CreateType();
        }
    }
}
