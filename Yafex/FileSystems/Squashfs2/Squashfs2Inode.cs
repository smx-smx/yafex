using System;
using System.Collections.Generic;
using System.IO;

using DiscUtils.Streams;

namespace Yafex.FileSystems.Squashfs2
{
    public sealed class Squashfs2Inode
    {
        public int Type;
        public uint Mode;
        public uint Uid;
        public uint Mtime;
        public long FileSize;
        // Regular files.
        public long ContentStart;
        public uint Fragment = Squashfs2Constants.InvalidFragment;
        public uint FragmentOffset;
        public uint[] BlockSizes = new uint[0];
        // Directories.
        public long DirStart;
        public long DirOffset;
        // Symlinks.
        public string SymlinkTarget = string.Empty;
        // Devices.
        public uint Rdev;
    }
}
