using System;
using System.IO;

using DiscUtils;
using DiscUtils.Vfs;

namespace Yafex.FileSystems.Ufs
{
    /// <summary>
    /// Read-only UFS (Berkeley FFS) driver: UFS1 and UFS2, either endianness
    /// (e.g. Panasonic SDDL/FreeBSD firmware payloads). Opens the image from
    /// a stream positioned at the filesystem start and exposes it through
    /// DiscFileSystem.
    /// </summary>
    public sealed class UfsFileSystem
        : VfsReadOnlyFileSystem<UfsDirectoryEntry, UfsFile, UfsDirectory, UfsContext>
    {
        public UfsFileSystem(Stream stream)
            : base(new DiscFileSystemOptions())
        {
            var context = UfsContext.Open(stream);
            Context = context;
            RootDirectory = new UfsDirectory(context, context.GetInode(UfsConstants.RootIno));
        }

        public static bool Detect(Stream stream)
        {
            if (stream == null || !stream.CanSeek)
            {
                return false;
            }
            try
            {
                long pos = stream.Position;
                try
                {
                    UfsContext.Open(stream);
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
                finally
                {
                    stream.Position = pos;
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        // Cheap mount-time gate over a file prefix: validates the superblock
        // fields (magic, geometry, masks, time) without touching inodes,
        // so it works for images of any size. Full validation (root walk)
        // happens in Detect/Open on expand.
        public static bool HeadMatches(byte[] head)
        {
            if (head == null)
            {
                return false;
            }
            long[] bases = new long[]
            {
                UfsConstants.SuperblockUfs1,
                UfsConstants.SuperblockUfs2,
                UfsConstants.SuperblockFloppy,
                UfsConstants.SuperblockPiggy,
            };
            foreach (long sbBase in bases)
            {
                if (sbBase + UfsConstants.SuperblockLength > head.Length)
                {
                    continue;
                }
                var sb = new byte[UfsConstants.SuperblockLength];
                Array.Copy(head, (int)sbBase, sb, 0, sb.Length);
                foreach (bool bigEndian in new bool[] { false, true })
                {
                    try
                    {
                        UfsSuperblock.Read(sb, bigEndian);
                        return true;
                    }
                    catch (IOException)
                    {
                    }
                }
            }
            return false;
        }

        public override string FriendlyName
        {
            get { return "UFS"; }
        }

        public override string VolumeLabel
        {
            get { return string.Empty; }
        }

        public override long Size
        {
            get { throw new NotSupportedException("Filesystem size is not (yet) supported"); }
        }

        public override long UsedSpace
        {
            get { throw new NotSupportedException("Filesystem size is not (yet) supported"); }
        }

        public override long AvailableSpace
        {
            get { throw new NotSupportedException("Filesystem size is not (yet) supported"); }
        }

        protected override UfsFile ConvertDirEntryToFile(UfsDirectoryEntry dirEntry)
        {
            UfsInode inode = Context.GetInode(dirEntry.Ino);
            if (dirEntry.IsDirectory)
            {
                return new UfsDirectory(Context, inode);
            }
            if (dirEntry.IsSymlink)
            {
                return new UfsSymlink(Context, inode);
            }
            return new UfsFile(Context, inode);
        }
    }
}
