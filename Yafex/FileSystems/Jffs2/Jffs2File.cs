using System;
using System.Collections.Generic;
using System.IO;

using Yafex.FileSystems.Base.Streams;
using Yafex.FileSystems.Base.Vfs;

using Yafex.Support;
namespace Yafex.FileSystems.Jffs2
{
    internal sealed class Jffs2MemoryBuffer : IBuffer
    {
        private readonly byte[] _data;

        public Jffs2MemoryBuffer(byte[] data)
        {
            _data = data;
        }

        public bool CanRead
        {
            get { return true; }
        }

        public bool CanWrite
        {
            get { return false; }
        }

        public long Capacity
        {
            get { return _data.Length; }
        }

        public IEnumerable<StreamExtent> Extents
        {
            get { yield return new StreamExtent(0, _data.Length); }
        }

        public int Read(long pos, byte[] buffer, int offset, int count)
        {
            if (pos >= _data.Length)
            {
                return 0;
            }
            int take = count;
            if (pos + take > _data.Length)
            {
                take = _data.Length - (int)pos;
            }
            Array.Copy(_data, (int)pos, buffer, offset, take);
            return take;
        }

        public void Write(long pos, byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public void Clear(long pos, int count)
        {
            throw new NotSupportedException();
        }

        public void Flush()
        {
        }

        public void SetCapacity(long value)
        {
            throw new NotSupportedException();
        }

        public IEnumerable<StreamExtent> GetExtentsInRange(long start, long count)
        {
            if (start < _data.Length)
            {
                long len = start + count > _data.Length ? _data.Length - start : count;
                yield return new StreamExtent(start, len);
            }
        }
    }

    public class Jffs2File : IVfsFile
    {
        protected readonly Jffs2Context Context;
        protected readonly Jffs2Inode Inode;
        private IBuffer _content;

        public Jffs2File(Jffs2Context context, Jffs2Inode inode)
        {
            Context = context;
            Inode = inode;
        }

        public virtual DateTime CreationTimeUtc
        {
            get { return UnixTime.ToDateTime(Inode.Mtime); }
            set { throw new NotSupportedException(); }
        }

        public virtual DateTime LastAccessTimeUtc
        {
            get { return UnixTime.ToDateTime(Inode.Mtime); }
            set { throw new NotSupportedException(); }
        }

        public virtual DateTime LastWriteTimeUtc
        {
            get { return UnixTime.ToDateTime(Inode.Mtime); }
            set { throw new NotSupportedException(); }
        }


        public virtual FileAttributes FileAttributes
        {
            get { return FileAttributes.ReadOnly | FileAttributes.Normal; }
            set { throw new NotSupportedException(); }
        }

        public virtual long FileLength
        {
            get { return Inode.Size; }
        }

        public virtual IBuffer FileContent
        {
            get
            {
                if (_content == null)
                {
                    _content = OpenContent();
                }
                return _content;
            }
        }

        protected virtual IBuffer OpenContent()
        {
            return new Jffs2MemoryBuffer(Context.ReadFile(Inode.Ino));
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
            return new Jffs2MemoryBuffer(new byte[0]);
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
            return new Jffs2MemoryBuffer(new byte[0]);
        }
    }
}
