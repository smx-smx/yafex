using System;
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