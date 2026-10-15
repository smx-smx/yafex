using System;
using System.Collections.Generic;
using System.IO;

using Yafex.FileSystems.Base.Streams;
using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.Ufs
{

    public class UfsFile : UnixVfsFile
    {
        protected readonly UfsContext Context;
        protected readonly UfsInode Inode;

        public UfsFile(UfsContext context, UfsInode inode)
        {
            Context = context;
            Inode = inode;
        }

        protected override long Mtime => Inode.Mtime;

        public override long FileLength
        {
            get { return (long)Inode.Size; }
        }

        protected override IBuffer OpenContent()
        {
            return new ReadOnlyByteBuffer(Context.ReadFile(Inode.Ino));
        }
    }

    public sealed class UfsSymlink : UfsFile, IVfsSymlink<UfsDirectoryEntry, UfsFile>
    {
        public UfsSymlink(UfsContext context, UfsInode inode)
            : base(context, inode)
        {
        }

        public string TargetPath
        {
            get
            {
                byte[] raw = Context.ReadSymlink(Inode.Ino);
                int len = raw.Length;
                while (len > 0 && raw[len - 1] == 0)
                {
                    len--;
                }
                return System.Text.Encoding.UTF8.GetString(raw, 0, len);
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

    public sealed class UfsDirectory
        : UfsFile, IVfsDirectory<UfsDirectoryEntry, UfsFile>
    {
        private readonly uint _ino;

        public UfsDirectory(UfsContext context, UfsInode inode)
            : base(context, inode)
        {
            _ino = inode.Ino;
        }

        public ICollection<UfsDirectoryEntry> AllEntries
        {
            get
            {
                var result = new List<UfsDirectoryEntry>();
                foreach (UfsDirent e in Context.ReadDirectory(_ino))
                {
                    // '.' and '..' are real UFS entries but must stay hidden
                    // from enumeration (like DiscUtils.Ext does), otherwise
                    // tree walks recurse into themselves forever.
                    if (e.Name == "." || e.Name == "..")
                    {
                        continue;
                    }
                    result.Add(new UfsDirectoryEntry(Context, e));
                }
                return result;
            }
        }

        public UfsDirectoryEntry Self
        {
            get { return null; }
        }

        public UfsDirectoryEntry GetEntryByName(string name)
        {
            foreach (UfsDirent e in Context.ReadDirectory(_ino))
            {
                if (e.Name == name)
                {
                    return new UfsDirectoryEntry(Context, e);
                }
            }
            return null;
        }

        public UfsDirectoryEntry CreateNewFile(string name)
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
