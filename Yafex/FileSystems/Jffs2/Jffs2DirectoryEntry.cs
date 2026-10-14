using System;
using System.IO;

using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.Jffs2
{
    public sealed class Jffs2DirectoryEntry : VfsDirEntry
    {
        private readonly Jffs2Context _context;
        private readonly Jffs2Dirent _dirent;

        private Jffs2Inode _inode;

        public Jffs2DirectoryEntry(Jffs2Context context, Jffs2Dirent dirent)
        {
            _context = context;
            _dirent = dirent;
        }

        public uint Ino
        {
            get { return _dirent.Ino; }
        }

        private Jffs2Inode Inode
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
                return _dirent.Dtype == Jffs2Constants.DtypeDir
                    || (Inode.Mode & Jffs2Constants.ModeTypeMask) == 0x4000;
            }
        }

        private bool TypeIsSymlink
        {
            get
            {
                return _dirent.Dtype == Jffs2Constants.DtypeSymlink
                    || (Inode.Mode & Jffs2Constants.ModeTypeMask) == 0xA000;
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
            get { return false; }
        }

        public override DateTime CreationTimeUtc
        {
            get { return Jffs2File.UnixTime(Inode.Mtime); }
        }

        public override DateTime LastAccessTimeUtc
        {
            get { return Jffs2File.UnixTime(Inode.Mtime); }
        }

        public override DateTime LastWriteTimeUtc
        {
            get { return Jffs2File.UnixTime(Inode.Mtime); }
        }

        public override long UniqueCacheId
        {
            get { return _dirent.Ino; }
        }
    }
}
