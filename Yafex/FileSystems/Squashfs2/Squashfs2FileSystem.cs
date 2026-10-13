using System;
using System.IO;

using DiscUtils;
using DiscUtils.Vfs;

namespace Yafex.FileSystems.Squashfs2
{
    /// <summary>
    /// Read-only squashfs 2.x driver (e.g. SerComm/Broadcom big-endian
    /// images): opens the image from a stream and exposes it through
    /// DiscFileSystem.
    /// </summary>
    public sealed class Squashfs2FileSystem
        : VfsReadOnlyFileSystem<Squashfs2DirectoryEntry, Squashfs2File, Squashfs2Directory, Squashfs2Context>
    {
        public const uint Magic = Squashfs2Constants.Magic;
        public const ushort MajorVersion = Squashfs2Constants.MajorVersion;

        /// <summary>Magic sniff: "hsqs" is little-endian, "sqsh" big-endian.</summary>
        public static bool HasMagic(byte[] prefix, out bool bigEndian)
        {
            return Squashfs2Constants.HasMagic(prefix, out bigEndian);
        }

        public Squashfs2FileSystem(Stream stream)
            : base(new DiscFileSystemOptions())
        {
            var context = Squashfs2Context.Open(stream);
            Context = context;
            Squashfs2Inode root = context.ReadInode(context.RootStart, (int)context.RootOffset, string.Empty);
            RootDirectory = new Squashfs2Directory(context, root, context.RootStart, context.RootOffset);
        }

        public static bool Detect(Stream stream)
        {
            if (stream == null || !stream.CanSeek || stream.Length < Squashfs2Constants.SuperblockSize)
            {
                return false;
            }
            try
            {
                long pos = stream.Position;
                try
                {
                    byte[] sb = Squashfs2Constants.ReadBytes(stream, 0, Squashfs2Constants.SuperblockSize);
                    if (!Squashfs2Constants.HasMagic(sb, out bool bigEndian))
                    {
                        return false;
                    }
                    ushort major = bigEndian
                        ? Squashfs2Constants.ReadU16BE(sb, Squashfs2Constants.MajorOff)
                        : Squashfs2Constants.ReadU16(sb, Squashfs2Constants.MajorOff);
                    return major == Squashfs2Constants.MajorVersion;
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

        public override string FriendlyName
        {
            get { return "SquashFS2"; }
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

        protected override Squashfs2File ConvertDirEntryToFile(Squashfs2DirectoryEntry dirEntry)
        {
            Squashfs2Inode inode = Context.ReadInode(dirEntry.StartBlock, (int)dirEntry.Offset, dirEntry.FileName);
            if (dirEntry.IsDirectory)
            {
                return new Squashfs2Directory(Context, inode, dirEntry.StartBlock, dirEntry.Offset);
            }
            if (dirEntry.IsSymlink)
            {
                return new Squashfs2Symlink(Context, inode);
            }
            return new Squashfs2File(Context, inode);
        }
    }
}
