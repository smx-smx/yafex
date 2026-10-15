using System;
using System.IO;

using Yafex.FileSystems.Base.Vfs;

using Yafex.Support;
namespace Yafex.FileSystems.Squashfs2
{
    public sealed class Squashfs2DirectoryEntry : VfsDirEntry
    {
        private readonly string _name;
        private readonly int _type;
        private readonly long _startBlock;
        private readonly long _offset;
        private readonly Squashfs2Context _context;

        private Squashfs2Inode _inode;

        public Squashfs2DirectoryEntry(
            Squashfs2Context context, string name, int type, long startBlock, long offset)
        {
            _context = context;
            _name = name;
            _type = type;
            _startBlock = startBlock;
            _offset = offset;
        }

        public long StartBlock
        {
            get { return _startBlock; }
        }

        public long Offset
        {
            get { return _offset; }
        }

        private Squashfs2Inode Inode
        {
            get
            {
                if (_inode == null)
                {
                    _inode = _context.ReadInode(_startBlock, (int)_offset, _name);
                }
                return _inode;
            }
        }

        public override string FileName
        {
            get { return _name; }
        }

        public override bool IsDirectory
        {
            get
            {
                return _type == Squashfs2Constants.TypeDir
                    || _type == Squashfs2Constants.TypeLdir;
            }
        }

        public override bool IsSymlink
        {
            get { return _type == Squashfs2Constants.TypeSymlink; }
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
            get { return UnixTime.ToDateTime(Inode.Mtime); }
        }

        public override DateTime LastAccessTimeUtc
        {
            get { return UnixTime.ToDateTime(Inode.Mtime); }
        }

        public override DateTime LastWriteTimeUtc
        {
            get { return UnixTime.ToDateTime(Inode.Mtime); }
        }

        public override long UniqueCacheId
        {
            get { return (_startBlock << 32) ^ _offset; }
        }
    }
}
