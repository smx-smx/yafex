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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using log4net;

namespace Yafex.Fuse
{
    /// <summary>Linux x86-64 glibc struct stat (144 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LinuxStat
    {
        public ulong st_dev;
        public ulong st_ino;
        public ulong st_nlink;
        public uint st_mode;
        public uint st_uid;
        public uint st_gid;
        public int __pad0;
        public ulong st_rdev;
        public long st_size;
        public long st_blksize;
        public long st_blocks;
        public long st_atim_sec;
        public long st_atim_nsec;
        public long st_mtim_sec;
        public long st_mtim_nsec;
        public long st_ctim_sec;
        public long st_ctim_nsec;
        public long __unused0;
        public long __unused1;
        public long __unused2;
    }

    public delegate int LinuxFillDir(nint buf,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        nint stbuf, long off, int flags);
    public delegate int LinuxGetattr(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        nint stbuf, nint fi);
    public delegate int LinuxAccess(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mask);
    public delegate int LinuxChmod(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode, nint fi);
    public delegate int LinuxChown(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint uid, uint gid, nint fi);
    public delegate int LinuxMkdir(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
    public delegate int LinuxSymlink(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string dstpath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string srcpath);
    public delegate int LinuxOpen(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint fi);
    public delegate int LinuxRead(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint buf, nint size, long off, nint fi);
    public delegate int LinuxWrite(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint buf, nint size, long off, nint fi);
    public delegate int LinuxOpendir(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint fi);
    public delegate int LinuxReaddir(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        nint buf, LinuxFillDir filler, long off, nint fi, int flags);
    public delegate nint LinuxInit(nint conn, nint conf);

    /// <summary>libfuse3 struct fuse_operations field order (through lseek).
    /// Every slot is a function pointer, so only order matters for layout;
    /// signatures matter for the callbacks we actually implement.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LinuxFuseOperations
    {
        public LinuxGetattr getattr;
        public nint readlink;
        public nint mknod;
        public LinuxMkdir mkdir;
        public nint unlink;
        public nint rmdir;
        public LinuxSymlink symlink;
        public nint rename;
        public nint link;
        public LinuxChmod chmod;
        public LinuxChown chown;
        public nint truncate;
        public LinuxOpen open;
        public LinuxRead read;
        public LinuxWrite write;
        public nint statfs;
        public nint flush;
        public nint release;
        public nint fsync;
        public nint setxattr;
        public nint getxattr;
        public nint listxattr;
        public nint removexattr;
        public LinuxOpendir opendir;
        public LinuxReaddir readdir;
        public nint releasedir;
        public nint fsyncdir;
        public LinuxInit init;
        public nint destroy;
        public LinuxAccess access;
        public nint create;
        public nint lock_;
        public nint utimens;
        public nint bmap;
        public nint ioctl;
        public nint poll;
        public nint write_buf;
        public nint read_buf;
        public nint flock;
        public nint fallocate;
        public nint copy_file_range;
        public nint lseek;
    }

    /// <summary>Linux libfuse3 backend: same VFS logic as the Cygwin path,
    /// exact fuse3 ABI (144-byte stat, 4-arg filler). Single-threaded loop,
    /// like fuse_main on Windows. Unmount with `fusermount3 -u DIR`.</summary>
    public class FuseLinuxInterop
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(FuseLinuxInterop));

        private readonly IVfs _vfs;

        // delegates must stay rooted for the whole mount session
        private readonly LinuxGetattr _getattr;
        private readonly LinuxAccess _access;
        private readonly LinuxChmod _chmod;
        private readonly LinuxChown _chown;
        private readonly LinuxMkdir _mkdir;
        private readonly LinuxSymlink _symlink;
        private readonly LinuxOpen _open;
        private readonly LinuxRead _read;
        private readonly LinuxWrite _write;
        private readonly LinuxOpendir _opendir;
        private readonly LinuxReaddir _readdir;
        private readonly LinuxInit _init;

        public FuseLinuxInterop(YafexVfs vfs)
        {
            _vfs = vfs;
            _getattr = Getattr;
            _access = Access;
            _chmod = Chmod;
            _chown = Chown;
            _mkdir = Mkdir;
            _symlink = Symlink;
            _open = Open;
            _read = Read;
            _write = Write;
            _opendir = Opendir;
            _readdir = Readdir;
            _init = Init;
        }

        private int Fail(Exception ex, string context)
        {
            log.Error($"error in {context}", ex);
            return -(int)PosixError.EIO;
        }

        private static void Trace(string message)
        {
            Console.Error.WriteLine(message);
            Console.Error.Flush();
        }

        private IVfsNode? Lookup(string path)
        {
            return _vfs.LookupPath(path);
        }

        private int Getattr(string path, nint stbuf, nint fi)
        {
            try
            {
                Trace($"getattr: {path}");
                var node = Lookup(path);
                if (node == null) return -(int)PosixError.ENOENT;
                // VfsNode.Mode already carries S_IFDIR/S_IFREG
                var stat = new LinuxStat
                {
                    st_mode = (uint)node.Mode,
                    st_size = node.Size,
                    st_nlink = (node.Type == VfsNodeType.Directory) ? 2u : 1u,
                    st_blksize = 4096,
                    st_blocks = (node.Size + 511) / 512,
                };
                Marshal.StructureToPtr(stat, stbuf, false);
                return 0;
            }
            catch (Exception ex)
            {
                return Fail(ex, $"getattr({path})");
            }
        }

        private int Access(string path, int mask) => 0;
        private int Chmod(string path, uint mode, nint fi) => 0;
        private int Chown(string path, uint uid, uint gid, nint fi) => 0;
        private int Mkdir(string path, uint mode) => 0;
        private int Symlink(string dstpath, string srcpath) => 0;
        private int Write(string path, nint buf, nint size, long off, nint fi) => 0;

        private int Open(string path, nint fi)
        {
            try
            {
                Trace($"open: {path}");
                var node = Lookup(path);
                if (node == null) return -(int)PosixError.ENOENT;
                if (node.Type != VfsNodeType.File) return -(int)PosixError.EISDIR;
                return 0;
            }
            catch (Exception ex)
            {
                return Fail(ex, $"open({path})");
            }
        }

        private int Opendir(string path, nint fi)
        {
            try
            {
                Trace($"opendir: {path}");
                var node = Lookup(path);
                if (node == null) return -(int)PosixError.ENOENT;
                if (node.Type != VfsNodeType.Directory) return -(int)PosixError.ENOTDIR;
                return 0;
            }
            catch (Exception ex)
            {
                return Fail(ex, $"opendir({path})");
            }
        }

        private int Read(string path, nint bufp, nint size, long off, nint fi)
        {
            try
            {
                Trace($"read: {path}");
                var node = Lookup(path);
                if (node == null) return -(int)PosixError.ENOENT;
                if (node.Type != VfsNodeType.File || node is not IVfsFile file)
                {
                    return -(int)PosixError.EISDIR;
                }
                var data = file.Read(off, size);
                if (data == null) return -(int)PosixError.EIO;
                Marshal.Copy(data, 0, bufp, data.Length);
                return data.Length;
            }
            catch (Exception ex)
            {
                return Fail(ex, $"read({path})");
            }
        }

        private int Readdir(string path, nint buf, LinuxFillDir filler, long off, nint fi, int flags)
        {
            try
            {
                Trace($"readdir: {path}");
                var node = Lookup(path);
                if (node == null) return -(int)PosixError.ENOENT;
                filler(buf, ".", 0, 0, 0);
                Trace("filler dotdot");
                filler(buf, "..", 0, 0, 0);
                Trace("filler loop");
                foreach (var itm in node.Tree)
                {
                    Trace($"child: {itm.Key}");
                    filler(buf, itm.Key, 0, 0, 0);
                    Trace($"child done: {itm.Key}");
                }
                Trace("readdir return 0");
                return 0;
            }
            catch (Exception ex)
            {
                return Fail(ex, $"readdir({path})");
            }
        }

        private nint Init(nint conn, nint conf) => 0;

        [DllImport("libfuse3.so.4", CallingConvention = CallingConvention.Cdecl)]
        private static extern int fuse_main_real(int argc, nint argv, nint op, nint opSize, nint data);

        public static void Start(YafexVfs vfs, string mountPoint)
        {
            var interop = new FuseLinuxInterop(vfs);
            var ops = new LinuxFuseOperations
            {
                getattr = interop._getattr,
                mkdir = interop._mkdir,
                symlink = interop._symlink,
                chmod = interop._chmod,
                chown = interop._chown,
                open = interop._open,
                read = interop._read,
                write = interop._write,
                opendir = interop._opendir,
                readdir = interop._readdir,
                init = interop._init,
                access = interop._access,
            };
            int opSize = Marshal.SizeOf<LinuxFuseOperations>();
            nint opPtr = Marshal.AllocHGlobal(opSize);
            Marshal.StructureToPtr(ops, opPtr, false);

            var argv = new List<string> { "yafex", "-f", mountPoint };
            int argc = argv.Count;
            var ptrSize = nint.Size * (argc + 1);
            var strSize = argv.Sum(a => Encoding.UTF8.GetByteCount(a) + 1);
            nint argvBuf = Marshal.AllocHGlobal(ptrSize + strSize);
            nint pPtr = argvBuf;
            nint pStr = argvBuf + ptrSize;
            foreach (var arg in argv)
            {
                var nBytes = Helpers.CopyString(pStr, arg);
                Marshal.WriteIntPtr(pPtr, pStr);
                pPtr += nint.Size;
                pStr += nBytes;
            }
            Marshal.WriteIntPtr(pPtr, nint.Zero);

            // argv/op buffers are process-lifetime: libfuse may realloc/free
            // them on unmount paths; never touch them again after this call.
            int rc = fuse_main_real(argc, argvBuf, opPtr, opSize, nint.Zero);
            if (rc != 0)
            {
                throw new Exception($"fuse_main_real failed with code {rc}");
            }
        }
    }
}
