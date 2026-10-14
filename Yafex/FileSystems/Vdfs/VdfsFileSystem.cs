using System;
using System.IO;

using Yafex.FileSystems.Base;
using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.Vdfs
{
    /// <summary>
    /// Read-only Samsung VDFS v4 driver. Opens a volume image (a partition
    /// slice starting at the "VDFS2007" volume-begins sector) and exposes it
    /// through the standard <see cref="DiscFileSystem"/> API.
    /// </summary>
    public sealed class VdfsFileSystem : VfsReadOnlyFileSystem<VdfsDirectoryEntry, VdfsFile, VdfsDirectory, VdfsContext>
    {
        public VdfsFileSystem(Stream stream)
            : base(new DiscFileSystemOptions())
        {
            var context = VdfsContext.Open(stream);
            Context = context;

            var tree = new VdfsBtree(context, context.CatTreeIno);
            VdfsCatRecord root = tree.FindChild(VdfsConstants.RootDirObjId, "root");
            if (root == null)
            {
                foreach (VdfsCatRecord candidate in tree.Children(VdfsConstants.RootDirObjId))
                {
                    root = candidate;
                    break;
                }
            }
            if (root == null)
            {
                throw new IOException("VDFS root directory not found");
            }
            RootDirectory = new VdfsDirectory(context, root);
        }

        /// <summary>
        /// True when <paramref name="stream"/> starts with a VDFS volume
        /// header and superblock (checked without throwing).
        /// </summary>
        public static bool Detect(Stream stream)
        {
            if (stream == null || !stream.CanSeek || stream.Length < VdfsConstants.SectorSize * 8)
            {
                return false;
            }
            try
            {
                long pos = stream.Position;
                try
                {
                    byte[] vb = VdfsConstants.ReadBytes(stream, VdfsConstants.VbOffset, 8);
                    if (vb[0] != 'V' || vb[1] != 'D' || vb[2] != 'F' || vb[3] != 'S')
                    {
                        return false;
                    }
                    byte[] sb = VdfsConstants.ReadBytes(stream, VdfsConstants.SbOffset, VdfsConstants.SectorSize);
                    return sb[0] == 'V' && sb[1] == 'D' && sb[2] == 'F' && sb[3] == 'S';
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
            get { return "VDFS"; }
        }

        public override string VolumeLabel
        {
            get { return Context.VolumeName; }
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

        protected override VdfsFile ConvertDirEntryToFile(VdfsDirectoryEntry dirEntry)
        {
            VdfsCatRecord rec = dirEntry.Record;
            switch (rec.Type)
            {
                case VdfsConstants.RecFolder:
                    return new VdfsDirectory(Context, rec);
                case VdfsConstants.RecFile:
                    if (dirEntry.IsSymlink)
                    {
                        return new VdfsSymlink(Context, rec);
                    }
                    return new VdfsFile(Context, rec);
                default:
                    // Hard/internal links carry the target's object id: hand
                    // back the real record so links read as their target.
                    // Directory links are mounted without protection against
                    // pathological cycles (mkfs never emits those).
                    VdfsCatRecord target = Context.LookupObject(rec.ObjectId);
                    if (target == null)
                    {
                        throw new IOException("VDFS dangling link object " + rec.ObjectId);
                    }
                    if (target.Type == VdfsConstants.RecFolder)
                    {
                        return new VdfsDirectory(Context, target);
                    }
                    var entry = new VdfsDirectoryEntry(target);
                    if (entry.IsSymlink)
                    {
                        return new VdfsSymlink(Context, target);
                    }
                    return new VdfsFile(Context, target);
            }
        }
    }
}
