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
using Org.BouncyCastle.Crypto.Signers;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Transactions;
using Yafex.Cygwin;

namespace Yafex.Fuse
{
    public struct fuse_timespec
    {
        public nint tv_sec;
        public nint tv_nsec;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct fuse_stat
    {
        public ulong st_dev;
        public ulong st_ino;
        public uint st_mode;
        public uint st_nlink;
        public uint st_uid;
        public uint st_gid;
        public ulong st_rdev;
        public ulong st_size;
        public fuse_timespec st_atim;
        public fuse_timespec st_mtim;
        public fuse_timespec st_ctim;
        public int st_blksize;
        public long st_blocks;
        public fuse_timespec st_birthtim;
    }

    public struct fuse_file_info
    {
        public int flags;
        public uint fh_old;
        public int writepage;
        public uint flags2;
        public ulong fh;
        public ulong lock_owner;
    }

    public delegate int fuse_fill_dir(
        nint buf, string name,
        nint stbuf, nint off, nint flags);

    public delegate int fuse_getattr(
        [MarshalAs(UnmanagedType.LPStr)] string path,
        nint stbuf, nint fi);
    public delegate int fuse_readlink(
        [MarshalAs(UnmanagedType.LPStr)] string path,
        nint buf, nint size);
    public delegate int fuse_mknod(
        [MarshalAs(UnmanagedType.LPStr)] string path, uint mode, uint dev);
    public delegate int fuse_mkdir(
        [MarshalAs(UnmanagedType.LPStr)] string path, uint mode);
    public delegate int fuse_unlink(
        [MarshalAs(UnmanagedType.LPStr)] string path);
    public delegate int fuse_rmdir(
        [MarshalAs(UnmanagedType.LPStr)] string path);
    public delegate int fuse_symlink(
        [MarshalAs(UnmanagedType.LPStr)] string dstpath, string srcpath);
    public delegate int fuse_rename(
        [MarshalAs(UnmanagedType.LPStr)] string oldpath, string newpath);
    public delegate int fuse_link(
        [MarshalAs(UnmanagedType.LPStr)] string srcpath, string dstpath);
    public delegate int fuse_chmod(
        [MarshalAs(UnmanagedType.LPStr)] string path, uint mode);
    public delegate int fuse_chown(
        [MarshalAs(UnmanagedType.LPStr)] string path, uint uid, uint gid);
    public delegate int fuse_truncate(
        [MarshalAs(UnmanagedType.LPStr)] string path, nint size);
    public delegate int fuse_utime(
        [MarshalAs(UnmanagedType.LPStr)] string path, nint timbuf);
    public delegate int fuse_open(
        [MarshalAs(UnmanagedType.LPStr)] string path, nint fi);
    public delegate int fuse_read(
        [MarshalAs(UnmanagedType.LPStr)] string path, nint buf, nint size, nint off, nint fi);
    public delegate int fuse_statfs(
        [MarshalAs(UnmanagedType.LPStr)] string path, nint stbuf);
    public delegate int fuse_flush(
        [MarshalAs(UnmanagedType.LPStr)] string path, nint fi);
    public delegate int fuse_release(
        [MarshalAs(UnmanagedType.LPStr)] string ptath, nint fi);
    public delegate int fuse_fsync(
        [MarshalAs(UnmanagedType.LPStr)] string path, int datasync, nint fi);
    public delegate int fuse_setxattr(
        [MarshalAs(UnmanagedType.LPStr)] string path, string name, string value,
        nint size, int flags);
    public delegate int fuse_getxattr([MarshalAs(UnmanagedType.LPStr)] string path, string name, string value,
        nint size);
    public delegate int fuse_listxattr([MarshalAs(UnmanagedType.LPStr)] string path, nint namebuf, nint size);
    public delegate int fuse_removexattr([MarshalAs(UnmanagedType.LPStr)] string path, string name);
    public delegate int fuse_opendir([MarshalAs(UnmanagedType.LPStr)] string path, nint fi);
    public delegate int fuse_readdir(
        [MarshalAs(UnmanagedType.LPStr)]
        string path,
        nint buf, fuse_fill_dir filler,
        nint off, ref fuse_file_info fi);
    public delegate int fuse_releasedir(
        [MarshalAs(UnmanagedType.LPStr)] string path,
        nint fi);
    public delegate int fuse_fsyncdir(
        [MarshalAs(UnmanagedType.LPStr)] string path, int datasync, nint fi);
    public delegate nint fuse_init(nint conn, nint conf);
    public delegate void fuse_destroy_op(nint data);
    public delegate int fuse_access(
        [MarshalAs(UnmanagedType.LPStr)] string path, int mask);
    public delegate int fuse_create(
        [MarshalAs(UnmanagedType.LPStr)] string path, uint mode, nint fi);
    public delegate int fuse_ftruncate(
        [MarshalAs(UnmanagedType.LPStr)] string path, nint off, nint fi);
    public delegate int fuse_fgetattr(
        [MarshalAs(UnmanagedType.LPStr)] string path, nint stbuf, nint fi);
    public delegate int fuse_lock(
        [MarshalAs(UnmanagedType.LPStr)] string path, nint fi, int cmd, nint _lock);
    public delegate int fuse_utimens(
        [MarshalAs(UnmanagedType.LPStr)] string path, nint tv);
    public delegate int fuse_bmap(
        [MarshalAs(UnmanagedType.LPStr)] string path, nint blocksize, nint idx);
    public delegate int fuse_ioctl(
        [MarshalAs(UnmanagedType.LPStr)] string path, int cmd, nint arg, nint fi,
        uint flags, nint data);
    public delegate int fuse_poll(
        [MarshalAs(UnmanagedType.LPStr)] string data, nint fi, nint ph, nint reventsp);
    public delegate int fuse_write_buf(
        [MarshalAs(UnmanagedType.LPStr)] string path, nint buf, nint off, nint fi);
    public delegate int fuse_read_buf(
        [MarshalAs(UnmanagedType.LPStr)] string path, nint bufp, nint size, nint off, nint fi);
    public delegate int fuse_flock(
        [MarshalAs(UnmanagedType.LPStr)] string path, nint fi, int op);
    public delegate int fuse_fallocate(
        [MarshalAs(UnmanagedType.LPStr)] string path, int mode, nint off, nint len, nint fi);

    [StructLayout(LayoutKind.Sequential)]
    public struct fuse_operations
    {
        public fuse_getattr getattr;
        public fuse_readlink readlink;
        public fuse_mknod mknod;
        public fuse_mkdir mkdir;
        public fuse_unlink unlink;
        public fuse_rmdir rmdir;
        public fuse_symlink symlink;
        public fuse_rename rename;
        public fuse_link link;
        public fuse_chmod chmod;
        public fuse_chown chown;
        public fuse_truncate truncate;
        public fuse_open open;
        public fuse_read_buf read;
        public fuse_write_buf write;
        public fuse_statfs statfs;
        public fuse_flush flush;
        public fuse_release release;
        public fuse_fsync fsync;
        public fuse_setxattr setxattr;
        public fuse_getxattr getxattr;
        public fuse_listxattr listxattr;
        public fuse_removexattr removexattr;
        public fuse_opendir opendir;
        public fuse_readdir readdir;
        public fuse_releasedir releasedir;
        public fuse_fsyncdir fsyncdir;
        public fuse_init init;
        public fuse_destroy destroy;
        public fuse_access access;
        public fuse_create create;
        public fuse_lock _lock;
        public fuse_utimens utimens;
        public fuse_bmap bmap;
        public fuse_ioctl ioctl;
        public fuse_poll poll;
        public fuse_write_buf write_buf;
        public fuse_read_buf read_buf;
        public fuse_flock flock;
        public fuse_fallocate fallocate;
    }

    public static class Helpers
    {
        public static int OctalLiteral(int octal)
        {
            int res = 0;

            int tmp = octal;
            for(int i=1; tmp > 0; i*= 8)
            {
                int digit = tmp % 10;
                tmp /= 10;
                res += digit * i;
            }
            return res;
        }

        public static int CopyString(nint ptr, string str)
        {
            var encoder = Encoding.UTF8.GetEncoder();
            var nBytes = encoder.GetByteCount(str, true) + 1;
            var buf = new byte[nBytes];
            encoder.GetBytes(str, buf, true);
            Marshal.Copy(buf, 0, ptr, nBytes);
            return nBytes;
        }
    }

    public struct fuse_args
    {
        /// <summary>
        /// Argument count
        /// </summary>
        public int argc;
        /// <summary>
        /// Arugment vector
        /// </summary>
        public nint argv;
        /// <summary>
        /// Is 'argv' allocated?
        /// </summary>
        public int allocated;

        public static fuse_args Create(IList<string> argv,
            out nint outBuffer)
        {
            int argc = argv.Count;
            // pointers (+ last NULL) + string data
            var pointers_size = IntPtr.Size * (argc + 1);
            var strings_size = argv.Sum(a => a.Length + 1);


            var buf = Marshal.AllocHGlobal(pointers_size + strings_size);
            var ptr_base = buf;
            var str_base = buf + pointers_size;

            foreach(var arg in argv)
            { 
                // write string data
                var nBytes = Helpers.CopyString(str_base, arg);
                // write pointer to string
                Marshal.WriteIntPtr(ptr_base, str_base);

                ptr_base += IntPtr.Size;
                str_base += nBytes;
            }

            var fuse_args = new fuse_args()
            {
                argc = argv.Count,
                argv = buf,
                // fuse should not try to free .NET strings
                allocated = 0
            };

            outBuffer = buf;
            return fuse_args;
        }
    }

    public struct fuse_opt
    {
        string? templ;
        uint offset;
        int value;

        public static nint CreateOptionList(IList<fuse_opt> opts)
        {
            var st_size = Marshal.SizeOf(typeof(fuse_opt)) * (opts.Count + 1);
            var data_size = opts.Sum(s => s.templ.Length + 1);
            
            var buf = Marshal.AllocHGlobal(st_size + data_size);

            var st_base = buf;
            var str_base = buf + st_size;

            void WriteOptionDescriptor(fuse_opt opt)
            {
                if (opt.templ != null)
                {
                    var nBytes = Helpers.CopyString(str_base, opt.templ);
                    Marshal.WriteIntPtr(st_base, str_base);
                } else
                {
                    Marshal.WriteIntPtr(st_base, IntPtr.Zero);
                }
                st_base += IntPtr.Size;

                Marshal.WriteInt32(st_base, (int)opt.offset); // offset
                st_base += sizeof(int);

                Marshal.WriteInt32(st_base, opt.value); // key
                st_base += sizeof(int);
            }

            foreach(var opt in opts)
            {
                WriteOptionDescriptor(opt);
            }

            // last struct member is NULL, 0, 0
            WriteOptionDescriptor(new fuse_opt
            {
                templ = null,
                offset = 0,
                value = 0
            });
            return buf;
        }
    }

    public delegate int fuse_main_real(
        int argc, nint argv, ref fuse_operations op,
        nint op_size, nint private_data
    );

    public delegate int fuse_opt_parse(
        ref fuse_args args,
        nint data, nint opts, nint proc
    );

    public delegate nint fuse_mount(
        string mountpoint,
        ref fuse_args args
    );

    public delegate nint fuse_new(
        nint ch, ref fuse_args args,
        ref fuse_operations ops,
        nint opsize, nint data
    );

    public delegate void fuse_unmount(
        string mountpoint, nint ch
    );

    public delegate void fuse_destroy(nint f);
    public delegate int fuse_daemonize(int foreground);
    public delegate int fuse_loop(nint f);

    public class FuseInteropContext : IDisposable
    {
        private readonly nint libHandle;
        public readonly fuse_opt_parse fuse_opt_parse;
        public readonly fuse_new fuse_new;
        public readonly fuse_mount fuse_mount;
        public readonly fuse_daemonize fuse_daemonize;
        public readonly fuse_unmount fuse_unmount;
        public readonly fuse_destroy fuse_destroy;
        public readonly fuse_loop fuse_loop;
        public readonly fuse_main_real fuse_main_real;

        private T GetExport<T>(string name) where T : Delegate
        {
            if (!NativeLibrary.TryGetExport(libHandle, name, out var ptr))
            {
                throw new Exception($"Couldn't resolve export '{name}'");
            }
            return Marshal.GetDelegateForFunctionPointer<T>(ptr);
        }

        public void Dispose()
        {
            NativeLibrary.Free(libHandle);
        }

        public FuseInteropContext(nint libHandle)
        {
            if(libHandle == 0)
            {
                throw new ArgumentException("Invalid handle");
            }

            this.libHandle = libHandle;
            fuse_opt_parse = GetExport<fuse_opt_parse>(nameof(fuse_opt_parse));
            fuse_new = GetExport<fuse_new>(nameof(fuse_new));
            fuse_mount = GetExport<fuse_mount>(nameof(fuse_mount));
            fuse_daemonize = GetExport<fuse_daemonize>(nameof(fuse_daemonize));
            fuse_unmount = GetExport<fuse_unmount>(nameof(fuse_unmount));
            fuse_destroy = GetExport<fuse_destroy>(nameof(fuse_destroy));
            fuse_loop = GetExport<fuse_loop>(nameof(fuse_loop));
            fuse_main_real = GetExport<fuse_main_real>(nameof(fuse_main_real));
        }
    }
    public class FuseInterop
    {
        private const string CYGFUSE_LIB_NAME = "cygfuse-3.2.dll";

        private string? GetCygwinBinDir()
        {
            var path = Cygwin.Cygwin.GetInstalledCygwinBin();
            if (path != null) return path;
            path = Cygwin.Cygwin.GetCurrentCygwinBin();
            if (path != null) return path;
            return null;
        }

        private FuseInteropContext Initialize()
        {
            var bindir = GetCygwinBinDir();
            if(bindir == null)
            {
                throw new Exception("Couldn't locate cygwin bin dir");
            }

            var libPath = Path.Combine(bindir, CYGFUSE_LIB_NAME);
            if (!File.Exists(libPath))
            {
                throw new Exception("Couldn't locate library " + CYGFUSE_LIB_NAME);
            }

            var libHandle = NativeLibrary.Load(libPath);

            return new FuseInteropContext(libHandle);

        }

        public static void Start(YafexVfs vfs, string mountPoint)
        {
            var fuse = new FuseInterop();

            var ctx = fuse.Initialize();
            
            var args = fuse_args.Create(new List<string>(){
                "ezdotnet", // argv0
                "-f", // important
                //"-d",
                "-o", "VolumeSerialNumber=1234",
                "-o", "umask=000",
                mountPoint
            }, out var args_buf);
            var opts = fuse_opt.CreateOptionList(new List<fuse_opt>() { });

            if(ctx.fuse_opt_parse(ref args, 0, opts, 0) < 0)
            {
                throw new Exception("fuse_opt_parse failed");
            }

            var fsb = new FuseFsBase(vfs);
            var ops = fsb.ops;

            var ops_size = Marshal.SizeOf<fuse_operations>();

            Console.WriteLine("Starting main loop...");
            ctx.fuse_main_real(args.argc, args.argv, ref ops, ops_size, 0);
            Console.WriteLine("Finished");

            Marshal.FreeHGlobal(opts);
            //Marshal.FreeHGlobal(args.argv);
        }
    }
}
