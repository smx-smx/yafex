using System;
using System.IO;

using DiscUtils;
using DiscUtils.Vfs;

namespace Yafex.FileSystems.Squashfs3
{
    /// <summary>
    /// Read-only squashfs 3.x driver (LG EPK2 images ship 3.1). Opens the
    /// image from a stream and exposes it through DiscFileSystem.
    /// </summary>
    public sealed class Squashfs3FileSystem
        : VfsReadOnlyFileSystem<Squashfs3DirectoryEntry, Squashfs3File, Squashfs3Directory, Squashfs3Context>
    {
        public const uint Magic = Squashfs3Constants.Magic;
        public const ushort MajorVersion = Squashfs3Constants.MajorVersion;

        /// <summary>Magic sniff: "hsqs" is little-endian, "sqsh" big-endian.</summary>
        public static bool HasMagic(byte[] prefix, out bool bigEndian)
        {
            return Squashfs3Constants.HasMagic(prefix, out bigEndian);
        }
        public Squashfs3FileSystem(Stream stream)
            : base(new DiscFileSystemOptions())
        {
            var context = Squashfs3Context.Open(stream);
            Context = context;
            Squashfs3Inode root = context.ReadInode(context.RootStart, context.RootOffset, string.Empty);
            RootDirectory = new Squashfs3Directory(context, root, context.RootStart, context.RootOffset);
        }

        public static bool Detect(Stream stream)
        {
            if (stream == null || !stream.CanSeek || stream.Length < Squashfs3Constants.SuperblockSize)
            {
                return false;
            }
            try
            {
                long pos = stream.Position;
                try
                {
                    byte[] sb = Squashfs3Constants.ReadBytes(stream, 0, Squashfs3Constants.SuperblockSize);
                    if (!Squashfs3Constants.HasMagic(sb, out bool bigEndian))
                    {
                        return false;
                    }
                    ushort major = bigEndian
                        ? Squashfs3Constants.ReadU16BE(sb, Squashfs3Constants.MajorOff)
                        : Squashfs3Constants.ReadU16(sb, Squashfs3Constants.MajorOff);
                    return major == Squashfs3Constants.MajorVersion;
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
            get { return "SquashFS3"; }
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

        protected override Squashfs3File ConvertDirEntryToFile(Squashfs3DirectoryEntry dirEntry)
        {
            Squashfs3Inode inode = Context.ReadInode(dirEntry.StartBlock, dirEntry.Offset, dirEntry.FileName);
            if (dirEntry.IsDirectory)
            {
                return new Squashfs3Directory(Context, inode, dirEntry.StartBlock, dirEntry.Offset);
            }
            if (dirEntry.IsSymlink)
            {
                return new Squashfs3Symlink(Context, inode);
            }
            return new Squashfs3File(Context, inode);
        }
    }
}
