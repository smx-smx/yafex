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
using System.IO;
using System.Runtime.InteropServices;

namespace Yafex.Fuse
{
    public enum PosixError : int
    {
        EPERM = 1,      /* Not owner */
        ENOENT = 2, /* No such file or directory */
        ESRCH = 3,      /* No such process */
        EINTR = 4,      /* Interrupted system call */
        EIO = 5,        /* I/O error */
        ENXIO = 6,      /* No such device or address */
        E2BIG = 7,      /* Arg list too long */
        ENOEXEC = 8,    /* Exec format error */
        EBADF = 9,      /* Bad file number */
        ECHILD = 10,    /* No children */
        EAGAIN = 11,    /* No more processes */
        ENOMEM = 12,    /* Not enough space */
        EACCES = 13,    /* Permission denied */
        EFAULT = 14,    /* Bad address */
        ENOTBLK = 15,   /* Block device required */
        EBUSY = 16, /* Device or resource busy */
        EEXIST = 17,    /* File exists */
        EXDEV = 18, /* Cross-device link */
        ENODEV = 19,    /* No such device */
        ENOTDIR = 20,   /* Not a directory */
        EISDIR = 21,    /* Is a directory */
        EINVAL = 22,    /* Invalid argument */
        ENFILE = 23,    /* Too many open files in system */
        EMFILE = 24,    /* File descriptor value too large */
        ENOTTY = 25,    /* Not a character device */
        ETXTBSY = 26,   /* Text file busy */
        EFBIG = 27, /* File too large */
        ENOSPC = 28,    /* No space left on device */
        ESPIPE = 29,    /* Illegal seek */
        EROFS = 30, /* Read-only file system */
        EMLINK = 31,    /* Too many links */
        EPIPE = 32, /* Broken pipe */
        EDOM = 33,      /* Mathematics argument out of domain of function */
        ERANGE = 34,    /* Result too large */
        ENOMSG = 35,    /* No message of desired type */
        EIDRM = 36, /* Identifier removed */
        ECHRNG = 37,    /* Channel number out of range */
        EL2NSYNC = 38,  /* Level 2 not synchronized */
        EL3HLT = 39,    /* Level 3 halted */
        EL3RST = 40,    /* Level 3 reset */
        ELNRNG = 41,    /* Link number out of range */
        EUNATCH = 42,   /* Protocol driver not attached */
        ENOCSI = 43,    /* No CSI structure available */
        EL2HLT = 44,    /* Level 2 halted */
        EDEADLK = 45,   /* Deadlock */
        ENOLCK = 46,    /* No lock */
        EBADE = 50, /* Invalid exchange */
        EBADR = 51, /* Invalid request descriptor */
        EXFULL = 52,    /* Exchange full */
        ENOANO = 53,    /* No anode */
        EBADRQC = 54,   /* Invalid request code */
        EBADSLT = 55,   /* Invalid slot */
        EDEADLOCK = 56, /* File locking deadlock error */
        EBFONT = 57,    /* Bad font file fmt */
        ENOSTR = 60,    /* Not a stream */
        ENODATA = 61,   /* No data (for no delay io) */
        ETIME = 62, /* Stream ioctl timeout */
        ENOSR = 63, /* No stream resources */
        ENONET = 64,    /* Machine is not on the network */
        ENOPKG = 65,    /* Package not installed */
        EREMOTE = 66,   /* The object is remote */
        ENOLINK = 67,   /* Virtual circuit is gone */
        EADV = 68,      /* Advertise error */
        ESRMNT = 69,    /* Srmount error */
        ECOMM = 70, /* Communication error on send */
        EPROTO = 71,    /* Protocol error */
        EMULTIHOP = 74, /* Multihop attempted */
        ELBIN = 75, /* Inode is remote (not really error) */
        EDOTDOT = 76,   /* Cross mount point (not really error) */
        EBADMSG = 77,   /* Bad message */
        EFTYPE = 79,    /* Inappropriate file type or format */
        ENOTUNIQ = 80,  /* Given log. name not unique */
        EBADFD = 81,    /* f.d. invalid for this operation */
        EREMCHG = 82,   /* Remote address changed */
        ELIBACC = 83,   /* Can't access a needed shared lib */
        ELIBBAD = 84,   /* Accessing a corrupted shared lib */
        ELIBSCN = 85,   /* .lib section in a.out corrupted */
        ELIBMAX = 86,   /* Attempting to link in too many libs */
        ELIBEXEC = 87,  /* Attempting to exec a shared library */
        ENOSYS = 88,    /* Function not implemented */
        ENMFILE = 89,      /* No more files */
        ENOTEMPTY = 90, /* Directory not empty */
        ENAMETOOLONG = 91,  /* File or path name too long */
        ELOOP = 92, /* Too many symbolic links */
        EOPNOTSUPP = 95,    /* Operation not supported on socket */
        EPFNOSUPPORT = 96, /* Protocol family not supported */
        ECONNRESET = 104,  /* Connection reset by peer */
        ENOBUFS = 105,  /* No buffer space available */
        EAFNOSUPPORT = 106, /* Address family not supported by protocol family */
        EPROTOTYPE = 107,   /* Protocol wrong type for socket */
        ENOTSOCK = 108, /* Socket operation on non-socket */
        ENOPROTOOPT = 109,  /* Protocol not available */
        ESHUTDOWN = 110,    /* Can't send after socket shutdown */
        ECONNREFUSED = 111, /* Connection refused */
        EADDRINUSE = 112,       /* Address already in use */
        ECONNABORTED = 113, /* Software caused connection abort */
        ENETUNREACH = 114,      /* Network is unreachable */
        ENETDOWN = 115,     /* Network interface is not configured */
        ETIMEDOUT = 116,        /* Connection timed out */
        EHOSTDOWN = 117,        /* Host is down */
        EHOSTUNREACH = 118, /* Host is unreachable */
        EINPROGRESS = 119,      /* Connection already in progress */
        EALREADY = 120,     /* Socket already connected */
        EDESTADDRREQ = 121, /* Destination address required */
        EMSGSIZE = 122,     /* Message too long */
        EPROTONOSUPPORT = 123,  /* Unknown protocol */
        ESOCKTNOSUPPORT = 124,  /* Socket type not supported */
        EADDRNOTAVAIL = 125,    /* Address not available */
        ENETRESET = 126,        /* Connection aborted by network */
        EISCONN = 127,      /* Socket is already connected */
        ENOTCONN = 128,     /* Socket is not connected */
        ETOOMANYREFS = 129,
        EPROCLIM = 130,
        EUSERS = 131,
        EDQUOT = 132,
        ESTALE = 133,
        ENOTSUP = 134,      /* Not supported */
        ENOMEDIUM = 135,   /* No medium (in tape drive) */
        ENOSHARE = 136,    /* No such host or network path */
        ECASECLASH = 137,  /* Filename exists with different case */
        EILSEQ = 138,       /* Illegal byte sequence */
        EOVERFLOW = 139,    /* Value too large for defined data type */
        ECANCELED = 140,    /* Operation canceled */
        ENOTRECOVERABLE = 141,  /* State not recoverable */
        EOWNERDEAD = 142,   /* Previous owner died */
        ESTRPIPE = 143, /* Streams pipe error */
        EWOULDBLOCK = EAGAIN,	/* Operation would block */
    }

    public enum StatMode : int
    {
        _IFMT = 0xF000,    /* type of file */
        _IFDIR = 0x4000,   /* directory */
        _IFCHR = 0x2000,   /* character special */
        _IFBLK = 0x6000,   /* block special */
        _IFREG = 0x8000,   /* regular */
        _IFLNK = 0xA000,   /* symbolic link */
        _IFSOCK = 0xC000,  /* socket */
        _IFIFO = 0x1000,   /* fifo */
        S_BLKSIZE = 1024,  /* size of a block */
        S_ISUID = 0x800,  /* set user id on execution */
        S_ISGID = 0x400,  /* set group id on execution */
        S_ISVTX = 0x200,  /* save swapped text even after use */
        S_IREAD = 0x100,  /* read permission, owner */
        S_IWRITE = 0x80,     /* write permission, owner */
        S_IEXEC = 0x40,  /* execute/search permission, owner */
        S_ENFMT = 0x400,  /* enforcement-mode locking */
        S_IFMT = _IFMT,
        S_IFDIR = _IFDIR,
        S_IFCHR = _IFCHR,
        S_IFBLK = _IFBLK,
        S_IFREG = _IFREG,
        S_IFLNK = _IFLNK,
        S_IFSOCK = _IFSOCK,
        S_IFIFO = _IFIFO,
    }


    public class FuseFsBase
    {
        public readonly fuse_operations ops;

        private readonly TextWriter os = Console.Error;

        private IVfs Vfs;

        private void Trace(string message)
        {
            os.WriteLine(message);
            os.Flush();
        }

        public FuseFsBase(YafexVfs vfs)
        {
            this.Vfs = vfs;
            this.ops = new fuse_operations
            {
                getattr = this.getattr,
                access = this.access,
                chmod = this.chmod,
                chown = this.chown,
                readdir = this.readdir,
                symlink = this.symlink,
                mkdir = this.mkdir,
                init = this.init,
                open = this.open,
                read = this.read,
                write = this.write,
                opendir = this.opendir
            };
        }

        private int opendir(string path, nint fi)
        {
            Trace($"opendir: {path}");
            return 0;
        }

        private int write(string path, nint buf, nint off, nint fi)
        {
            Trace($"write: {path}");
            return 0;
        }

        private int read(string path, nint bufp, nint size, nint off, nint fi)
        {
            Trace($"read: {path}");
            var node = Vfs.LookupPath(path);
            if (node == null) return -(int)PosixError.ENOENT;
            if (node.Type != VfsNodeType.File || node is not IVfsFile file)
            {
                return -(int)PosixError.EISDIR;
            }

            var data = file.Read(off, size);
            if (data == null)
            {
                return -(int)PosixError.EIO;
            }

            Marshal.Copy(data, 0, bufp, data.Length);
            return data.Length;
        }

        private int open(string path, nint fi)
        {
            Trace($"open: {path}");
            var node = Vfs.LookupPath(path);
            if (node == null) return -(int)PosixError.ENOENT;
            if (node.Type != VfsNodeType.File)
            {
                return -(int)PosixError.EISDIR;
            }
            return 0;
        }

        private int getattr(string path, nint stbuf, nint fi)
        {
            Trace($"getattr: {path}");

            var node = Vfs.LookupPath(path);
            if (node == null)
            {
                return -(int)PosixError.ENOENT;

            }

            var stat = new fuse_stat()
            {
                st_mode = (uint)node.Mode,
                st_size = (ulong)node.Size,
                // $FIXME
                st_nlink = (node.Type == VfsNodeType.Directory) ? 2u : 1u
            };

            Marshal.StructureToPtr(stat, stbuf, false);
            return 0;
        }

        private nint init(nint conn, nint conf)
        {
            Trace("init");
            return 0;
        }

        private int mkdir(string path, uint mode)
        {

            Trace("mkdir");
            return 0;
        }

        private int symlink(string dstpath, string srcpath)
        {
            Trace("symlink");
            return 0;
        }

        private int chown(string path, uint uid, uint gid)
        {
            Trace("chown");
            return 0;
        }

        private int chmod(string path, uint mode)
        {
            Trace("chmod");
            return 0;
        }

        private int access(string path, int mask)
        {
            Trace("access");
            return 0;
        }

        private int readdir(string path, nint buf, fuse_fill_dir filler, nint off, ref fuse_file_info fi)
        {
            Trace($"readdir: {path}");

            var node = Vfs.LookupPath(path);
            if (node == null)
            {
                return -(int)PosixError.ENOENT;
            }

            filler(buf, ".", 0, 0, 0);
            filler(buf, "..", 0, 0, 0);

            foreach (var itm in node.Tree)
            {
                Trace($"child: {itm.Key}");
                filler(buf, itm.Key, 0, 0, 0);
            }

            return 0;
        }
    }
}
