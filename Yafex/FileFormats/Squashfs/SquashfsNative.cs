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
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Yafex.FileFormats.Squashfs
{
	public class SquashfsNative
	{
		[DllImport("squashfs", CallingConvention = CallingConvention.Cdecl)]
		private static extern int is_squashfs([MarshalAs(UnmanagedType.LPStr)] string filename);

		[DllImport("squashfs", CallingConvention = CallingConvention.Cdecl)]
		private static extern int unsquashfs([MarshalAs(UnmanagedType.LPStr)] string sourceFile, 
											 [MarshalAs(UnmanagedType.LPStr)] string destDir);

		public bool IsSquashfs(string sourceFile) {
			return is_squashfs(sourceFile) == 1;
		}

		public bool ExtractSquashfs(string sourceFile, string destDir) {
			return unsquashfs(sourceFile, destDir) == 0;
		}
	}
}
