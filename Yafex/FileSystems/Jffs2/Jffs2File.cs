using System;
using System.Collections.Generic;
using System.IO;

using Yafex.FileSystems.Base.Streams;
using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.Jffs2
{

    public class Jffs2File : UnixVfsFile
    {
        protected readonly Jffs2Context Context;
        protected readonly Jffs2Inode Inode;

        public Jffs2File(Jffs2Context context, Jffs2Inode inode)
        {
            Context = context;
            Inode = inode;
        }

        protected override long Mtime => Inode.Mtime;

        public override long FileLength
        {
            get { return Inode.Size; }
        }

        protected override IBuffer OpenContent()
        {
            return new ReadOnlyByteBuffer(Context.ReadFile(Inode.Ino));
        }
    }

    public sealed class Jffs2Symlink : Jffs2File, IVfsSymlink<Jffs2DirectoryEntry, Jffs2File>
    {
        public Jffs2Symlink(Jffs2Context context, Jffs2Inode inode)
            : base(context, inode)
        {
        }

        public string TargetPath
        {
            get
            {
                byte[] raw = Context.ReadFile(Inode.Ino);
                int len = raw.Length;
                while (len > 0 && raw[len - 1] == 0)
                {
                    len--;
                }
                return System.Text.Encoding.ASCII.GetString(raw, 0, len);
            }
        }

        public override long FileLength
        {
            get { return 0; }
        }

        protected override IBuffer OpenContent()
        {
            return ReadOnlyByteBuffer.Empty;
        }
    }

    public sealed class Jffs2Directory
        : Jffs2File, IVfsDirectory<Jffs2DirectoryEntry, Jffs2File>
    {
        private readonly uint _ino;

        public Jffs2Directory(Jffs2Context context, Jffs2Inode inode)
            : base(context, inode)
        {
            _ino = inode.Ino;
        }

        public ICollection<Jffs2DirectoryEntry> AllEntries
        {
            get
            {
                var result = new List<Jffs2DirectoryEntry>();
                foreach (Jffs2Dirent e in Context.ReadDirectory(_ino))
                {
                    result.Add(new Jffs2DirectoryEntry(Context, e));
                }
                return result;
            }
        }

        public Jffs2DirectoryEntry Self
        {
            get { return null; }
        }

        public Jffs2DirectoryEntry GetEntryByName(string name)
        {
            foreach (Jffs2Dirent e in Context.ReadDirectory(_ino))
            {
                if (e.Name == name)
                {
                    return new Jffs2DirectoryEntry(Context, e);
                }
            }
            return null;
        }

        public Jffs2DirectoryEntry CreateNewFile(string name)
        {
            throw new NotSupportedException();
        }

        public override long FileLength
        {
            get { return 0; }
        }

        protected override IBuffer OpenContent()
        {
            return ReadOnlyByteBuffer.Empty;
        }
    }
}
