using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Smx.Yafex
{
	public class EzDotnetCompat
	{
        private static unsafe void ReplaceGetEntryAssembly() {
            MethodInfo methodToReplace = typeof(Assembly).GetMethod("GetEntryAssembly", BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo methodToInject = typeof(EzDotnetCompat).GetMethod("MyGetEntryAssembly", BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            RuntimeHelpers.PrepareMethod(methodToReplace.MethodHandle);
            RuntimeHelpers.PrepareMethod(methodToInject.MethodHandle);

            long* inj = (long*)methodToInject.MethodHandle.Value.ToPointer() + 1;
            long* tar = (long*)methodToReplace.MethodHandle.Value.ToPointer() + 1;

            byte* injInst = (byte*)*inj;
            byte* tarInst = (byte*)*tar;

            int* injSrc = (int*)(injInst + 1);
            int* tarSrc = (int*)(tarInst + 1);

            *tarSrc = (((int)injInst + 5) + *injSrc) - ((int)tarInst + 5);
        }

        private static Assembly MyGetEntryAssembly() {
            return typeof(Program).Assembly;
		}

        public static void InstallHooks() {
            // this is required for Log4Net to function properly 
            ReplaceGetEntryAssembly();
		}
	}
}
