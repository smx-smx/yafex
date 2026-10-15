using System;
using System.IO;

using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.Squashfs3
{
    public sealed class Squashfs3DirectoryEntry : UnixVfsDirEntry
    {
        private readonly string _name;
        private readonly int _type;
        private readonly long _startBlock;
        private readonly long _offset;
        private readonly Squashfs3Context _context;

        private Squashfs3Inode _inode;

        public Squashfs3DirectoryEntry(
            Squashfs3Context context, string name, int type, long startBlock, long offset)
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

        private Squashfs3Inode Inode
        {
            get
            {
                if (_inode == null)
                {
                    _inode = _context.ReadInode(_startBlock, _offset, _name);
                }
                return _inode;
            }
        }

        protected override long Mtime => Inode.Mtime;

        public override string FileName
        {
            get { return _name; }
        }

        public override bool IsDirectory
        {
            get
            {
                return _type == Squashfs3Constants.TypeDir
                    || _type == Squashfs3Constants.TypeLdir;
            }
        }

        public override bool IsSymlink
        {
            get
            {
                return _type == Squashfs3Constants.TypeSymlink
                    || _type == 10;
            }
        }

        public override long UniqueCacheId
        {
            get { return (_startBlock << 32) ^ _offset; }
        }
    }
}
