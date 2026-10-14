using System;
using System.Collections.Generic;
using System.IO;

using Yafex.FileSystems.Base.Streams;
using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.Squashfs2
{
    /// <summary>
    /// Windowed read-only buffer over a regular file's data blocks plus an
    /// optional fragment tail: seeks never materialize the file, reads only
    /// touch their covering blocks.
    /// </summary>
    internal sealed class Squashfs2FileBuffer : IBuffer
    {
        private readonly Squashfs2Context _ctx;
        private readonly Squashfs2Inode _inode;
        private readonly long _length;
        private readonly long _blocksEnd;
        private readonly long[] _blockFileOff;

        public Squashfs2FileBuffer(Squashfs2Context ctx, Squashfs2Inode inode)
        {
            _ctx = ctx;
            _inode = inode;
            _length = inode.FileSize;
            // file offsets of each data block (prefix sums of stored sizes)
            _blockFileOff = new long[inode.BlockSizes.Length + 1];
            _blockFileOff[0] = inode.ContentStart;
            for (int b = 0; b < inode.BlockSizes.Length; b++)
            {
                uint e = inode.BlockSizes[b];
                _blockFileOff[b + 1] = _blockFileOff[b]
                    + (e == 0 ? 0 : (e & ~Squashfs2Constants.StoredBitBlock));
            }
            _blocksEnd = ((long)inode.BlockSizes.Length) * ctx.BlockSize;
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
            get { return _length; }
        }

        public IEnumerable<StreamExtent> Extents
        {
            get { yield return new StreamExtent(0, _length); }
        }

        public int Read(long pos, byte[] buffer, int offset, int count)
        {
            if (pos >= _length)
            {
                return 0;
            }
            long left = _length - pos;
            if (count > left)
            {
                count = (int)left;
            }
            int total = 0;
            while (count > 0)
            {
                if (pos < _blocksEnd)
                {
                    long block = pos / _ctx.BlockSize;
                    long inBlock = pos % _ctx.BlockSize;
                    uint entry = _inode.BlockSizes[(int)block];
                    long pageLen = _ctx.BlockSize;
                    long pageRemain = _length - block * _ctx.BlockSize;
                    if (pageLen > pageRemain)
                    {
                        pageLen = pageRemain;
                    }
                    if (pos + count > _blocksEnd)
                    {
                        count = (int)(_blocksEnd - pos);
                    }
                    int take;
                    if (entry == 0)
                    {
                        take = count;
                        long avail = pageLen - inBlock;
                        if (take > avail)
                        {
                            take = (int)avail;
                        }
                        Array.Clear(buffer, offset + total, take);
                    }
                    else
                    {
                        uint stored = entry & ~Squashfs2Constants.StoredBitBlock;
                        long fileOff = _blockFileOff[(int)block];
                        long avail = pageLen - inBlock;
                        take = count;
                        if (take > avail)
                        {
                            take = (int)avail;
                        }
                        byte[] inflated;
                        if ((entry & Squashfs2Constants.StoredBitBlock) != 0)
                        {
                            byte[] raw = _ctx.ReadStored(fileOff, stored);
                            inflated = new byte[pageLen];
                            Array.Copy(raw, 0, inflated, 0, (int)Math.Min(raw.Length, pageLen));
                        }
                        else
                        {
                            inflated = _ctx.ReadDataBlock(fileOff, stored, pageLen);
                        }
                        Array.Copy(inflated, (int)inBlock, buffer, offset + total, take);
                    }
                    pos += take;
                    total += take;
                    count -= take;
                }
                else
                {
                    long fpos = pos - _blocksEnd;
                    byte[] tail = _ctx.ReadFragment(
                        _inode.Fragment, (uint)(_inode.FragmentOffset + fpos), count);
                    Array.Copy(tail, 0, buffer, offset + total, count);
                    total += count;
                    count = 0;
                }
            }
            return total;
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
            if (start < _length)
            {
                long len = start + count > _length ? _length - start : count;
                yield return new StreamExtent(start, len);
            }
        }
    }

    internal sealed class Squashfs2MemoryBuffer : IBuffer
    {
        private readonly byte[] _data;

        public Squashfs2MemoryBuffer(byte[] data)
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
            if (pos + count > _data.Length)
            {
                count = (int)(_data.Length - pos);
            }
            Array.Copy(_data, pos, buffer, offset, count);
            return count;
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
            yield return new StreamExtent(0, _data.Length);
        }
    }

    public class Squashfs2File : IVfsFile
    {
        protected readonly Squashfs2Context Context;
        protected readonly Squashfs2Inode Inode;
        private IBuffer _content;

        public Squashfs2File(Squashfs2Context context, Squashfs2Inode inode)
        {
            Context = context;
            Inode = inode;
        }

        public virtual DateTime CreationTimeUtc
        {
            get { return UnixTime(Inode.Mtime); }
            set { throw new NotSupportedException(); }
        }

        public virtual DateTime LastAccessTimeUtc
        {
            get { return UnixTime(Inode.Mtime); }
            set { throw new NotSupportedException(); }
        }

        public virtual DateTime LastWriteTimeUtc
        {
            get { return UnixTime(Inode.Mtime); }
            set { throw new NotSupportedException(); }
        }

        internal static DateTime UnixTime(uint secs)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(secs);
        }

        public virtual FileAttributes FileAttributes
        {
            get { return FileAttributes.ReadOnly | FileAttributes.Normal; }
            set { throw new NotSupportedException(); }
        }

        public virtual long FileLength
        {
            get { return Inode.FileSize < 0 ? 0 : Inode.FileSize; }
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
            return new Squashfs2FileBuffer(Context, Inode);
        }
    }

    public sealed class Squashfs2Symlink : Squashfs2File, IVfsSymlink<Squashfs2DirectoryEntry, Squashfs2File>
    {
        public Squashfs2Symlink(Squashfs2Context context, Squashfs2Inode inode)
            : base(context, inode)
        {
        }

        public string TargetPath
        {
            get { return Inode.SymlinkTarget; }
        }

        public override long FileLength
        {
            get { return 0; }
        }

        protected override IBuffer OpenContent()
        {
            return new Squashfs2MemoryBuffer(new byte[0]);
        }
    }

    public sealed class Squashfs2Directory
        : Squashfs2File, IVfsDirectory<Squashfs2DirectoryEntry, Squashfs2File>
    {
        private readonly long _startBlock;
        private readonly long _offset;

        public Squashfs2Directory(Squashfs2Context context, Squashfs2Inode inode, long startBlock, long offset)
            : base(context, inode)
        {
            _startBlock = startBlock;
            _offset = offset;
        }

        public ICollection<Squashfs2DirectoryEntry> AllEntries
        {
            get
            {
                var result = new List<Squashfs2DirectoryEntry>();
                foreach (Squashfs2DirectoryEntry e in Context.ReadDirectory(Inode))
                {
                    result.Add(e);
                }
                return result;
            }
        }

        public Squashfs2DirectoryEntry Self
        {
            get { return null; }
        }

        public Squashfs2DirectoryEntry GetEntryByName(string name)
        {
            foreach (Squashfs2DirectoryEntry e in Context.ReadDirectory(Inode))
            {
                if (e.FileName == name)
                {
                    return e;
                }
            }
            return null;
        }

        public Squashfs2DirectoryEntry CreateNewFile(string name)
        {
            throw new NotSupportedException();
        }

        public override long FileLength
        {
            get { return 0; }
        }

        protected override IBuffer OpenContent()
        {
            return new Squashfs2MemoryBuffer(new byte[0]);
        }
    }
}
