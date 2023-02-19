#region License
/*
 * Copyright (c) 2023 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Yafex
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
