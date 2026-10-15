using System;
using System.IO;

using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.Ufs
{
    public sealed class UfsDirectoryEntry : UnixVfsDirEntry
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

        protected override long Mtime => Inode.Mtime;

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

        public override bool HasVfsTimeInfo
        {
            get { return true; }
        }

        public override long UniqueCacheId
        {
            get { return _dirent.Ino; }
        }
    }
}
