#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
using Microsoft.Win32;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Yafex.Cygwin
{
    public enum CygwinPathConversion
    {
        CCP_POSIX_TO_WIN_A = 0,
        CCP_ABSOLUTE = 0,

        CCP_POSIX_TO_WIN_W = 1,
        CCP_WIN_A_TO_POSIX = 2,
        CCP_WIN_W_TO_POSIX = 3,
        CCP_RELATIVE = 0x100,
        CCP_PROC_CYGDRIVE = 0x200,
    }


    public class Cygwin
    {
        [DllImport("cygwin1", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr cygwin_conv_path(
            CygwinPathConversion what, IntPtr from, IntPtr to, IntPtr size
        );

        [DllImport("cygwin1", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr read(int fd, IntPtr buf, IntPtr count);

        [DllImport("cygwin1", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr write(int fd, IntPtr buf, IntPtr count);

        [DllImport("kernel32.dll", SetLastError = true)]
        [PreserveSig]
        public static extern uint GetModuleFileName([In] IntPtr hModule,
            [Out] StringBuilder lpFilename,
            [In][MarshalAs(UnmanagedType.U4)] int nSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr GetModuleHandle([MarshalAs(UnmanagedType.LPWStr)] string lpModuleName);

        [DllImport("kernel32.dll")]
        static extern uint GetLastError();

        public static long Read(int fd, byte[] data, int offset, int length)
        {
            var gch = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                return read(fd, gch.AddrOfPinnedObject() + offset, new IntPtr(length)).ToInt64();
            }
            finally
            {
                gch.Free();
            }
        }

        public static long Write(int fd, byte[] data, int offset, int length)
        {
            var gch = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                return write(fd, gch.AddrOfPinnedObject() + offset, new IntPtr(length)).ToInt64();
            }
            finally
            {
                gch.Free();
            }
        }

        public static string? GetInstalledCygwinBin()
        {
            var rootdir = (string?)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Cygwin\setup", "rootdir", null);
            if (rootdir == null) return null;
            return rootdir + "/bin";
        }

        public static string? GetCurrentCygwinBin()
        {
            var hCygwin = GetModuleHandle("cygwin1");
            if (hCygwin == IntPtr.Zero) return null;
            var sb = new StringBuilder(256);
            GetModuleFileName(hCygwin, sb, sb.Capacity);
            if (GetLastError() != 0)
            {
                return null;
            }
            return sb.ToString();
        }

        public static string ToWindowsPath(string path)
        {
            var fromPtr = Marshal.StringToHGlobalAnsi(path);
            string result;
            try
            {
                var flags = CygwinPathConversion.CCP_POSIX_TO_WIN_A | CygwinPathConversion.CCP_ABSOLUTE;
                var bufSize = cygwin_conv_path(flags, fromPtr, IntPtr.Zero, IntPtr.Zero);
                var buffer = Marshal.AllocHGlobal(bufSize.ToInt32());
                try
                {
                    cygwin_conv_path(flags, fromPtr, buffer, bufSize);
                    var converted = Marshal.PtrToStringAnsi(buffer);
                    if(converted == null)
                    {
                        throw new InvalidOperationException("Failed to convert string");
                    }
                    result = converted;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(fromPtr);
            }
            return result;
        }

        public static string ToPosixPath(string path)
        {
            var fromPtr = Marshal.StringToHGlobalAnsi(path);
            string result;
            try
            {
                var flags = CygwinPathConversion.CCP_WIN_A_TO_POSIX | CygwinPathConversion.CCP_ABSOLUTE;

                var bufSize = cygwin_conv_path(flags, fromPtr, IntPtr.Zero, IntPtr.Zero);
                var buffer = Marshal.AllocHGlobal(bufSize.ToInt32());
                try
                {
                    cygwin_conv_path(flags, fromPtr, buffer, bufSize);
                    var converted = Marshal.PtrToStringAnsi(buffer);
                    if(converted == null)
                    {
                        throw new InvalidOperationException("Failed to convert string");
                    }
                    result = converted;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(fromPtr);
            }
            return result;
        }
    }
}
