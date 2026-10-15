using System;
using System.IO;

using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.Ufs
{
    public sealed class UfsDirectoryEntry : VfsDirEntry
    {
        private readonly UfsContext _context;
        private readonly UfsDirent _dirent;

        private UfsInode _inode;

        public UfsDirectoryEntry(UfsContext context, UfsDirent dirent)
        {
            _context = context;
            _dirent = dirent;
        }

        public uint Ino
        {
            get { return _dirent.Ino; }
        }

        private UfsInode Inode
        {
            get
            {
                if (_inode == null)
                {
                    _inode = _context.GetInode(_dirent.Ino);
                }
                return _inode;
            }
        }

        private bool TypeIsDir
        {
            get
            {
                return _dirent.Dtype == UfsConstants.DtypeDir || Inode.IsDirectory;
            }
        }

        private bool TypeIsSymlink
        {
            get
            {
                return _dirent.Dtype == UfsConstants.DtypeSymlink || Inode.IsSymlink;
            }
        }

        public override string FileName
        {
            get { return _dirent.Name; }
        }

        public override bool IsDirectory
        {
            get { return TypeIsDir; }
        }

        public override bool IsSymlink
        {
            get { return TypeIsSymlink; }
        }

        public override FileAttributes FileAttributes
        {
            get
            {
                FileAttributes attr = IsDirectory ? FileAttributes.Directory : FileAttributes.Normal;
                if (IsSymlink)
                {
                    attr |= FileAttributes.ReparsePoint;
                }
                return attr;
            }
        }

        public override bool HasVfsFileAttributes
        {
            get { return false; }
        }

        public override bool HasVfsTimeInfo
        {
            get { return true; }
        }

        public override DateTime CreationTimeUtc
        {
            get { return DateTimeOffsetExtensions.FromUnixSeconds(Inode.Mtime); }
        }

        public override DateTime LastAccessTimeUtc
        {
            get { return DateTimeOffsetExtensions.FromUnixSeconds(Inode.Mtime); }
        }

        public override DateTime LastWriteTimeUtc
        {
            get { return DateTimeOffsetExtensions.FromUnixSeconds(Inode.Mtime); }
        }

        public override long UniqueCacheId
        {
            get { return _dirent.Ino; }
        }
    }
}
