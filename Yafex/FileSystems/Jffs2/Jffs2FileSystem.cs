using System;
using System.IO;

using Yafex.FileSystems.Base;
using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.Jffs2
{
    /// <summary>
    /// Read-only JFFS2 driver (small NOR-style images, e.g. LG mode parts).
    /// Opens the image from a stream and exposes it through DiscFileSystem.
    /// </summary>
    public sealed class Jffs2FileSystem
        : VfsReadOnlyFileSystem<Jffs2DirectoryEntry, Jffs2File, Jffs2Directory, Jffs2Context>
    {
        public Jffs2FileSystem(Stream stream)
            : base(new DiscFileSystemOptions())
        {
            var context = Jffs2Context.Open(stream);
            Context = context;
            // the root inode is implicit on flash: synthesize it
            RootDirectory = new Jffs2Directory(context, new Jffs2Inode { Ino = 1, Mode = 0x41ED });
        }

        public static bool Detect(Stream stream)
        {
            if (stream == null || !stream.CanSeek || stream.Length < 12)
            {
                return false;
            }
            try
            {
                long pos = stream.Position;
                try
                {
                    byte[] head = Jffs2Constants.ReadBytes(stream, 0, 12);
                    if (Jffs2Constants.ReadU16(head, 0) != Jffs2Constants.Magic)
                    {
                        return false;
                    }
                    int type = Jffs2Constants.ReadU16(head, 2) & 0x0FFF;
                    long totlen = Jffs2Constants.ReadU32(head, 4);
                    return (type == Jffs2Constants.TypeCleanmarker
                        || type == Jffs2Constants.TypeDirent
                        || type == Jffs2Constants.TypeInode)
                        && totlen >= 12 && totlen <= stream.Length;
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
            get { return "JFFS2"; }
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

        protected override Jffs2File ConvertDirEntryToFile(Jffs2DirectoryEntry dirEntry)
        {
            Jffs2Inode inode = Context.GetInode(dirEntry.Ino);
            if (dirEntry.IsDirectory)
            {
                return new Jffs2Directory(Context, inode);
            }
            if (dirEntry.IsSymlink)
            {
                return new Jffs2Symlink(Context, inode);
            }
            return new Jffs2File(Context, inode);
        }
    }
}
