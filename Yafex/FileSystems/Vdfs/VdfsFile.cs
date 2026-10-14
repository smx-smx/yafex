using System;
using System.Collections.Generic;
using System.IO;

using Yafex.FileSystems.Base.Streams;
using Yafex.FileSystems.Base;
using Yafex.FileSystems.Base.Vfs;

namespace Yafex.FileSystems.Vdfs
{
    /// <summary>
    /// Windowed read-only buffer over a file's block extents: seeks never
    /// materialize the file, reads only touch their covering blocks.
    /// </summary>
    internal sealed class VdfsExtentBuffer : IBuffer
    {
        private readonly VdfsContext _ctx;
        private readonly List<VdfsExtent> _extents;
        private readonly long _length;

        public VdfsExtentBuffer(VdfsContext ctx, List<VdfsExtent> extents, long length)
        {
            _ctx = ctx;
            _extents = extents;
            _length = length;
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
            ulong skip = (ulong)pos;
            foreach (VdfsExtent ext in _extents)
            {
                if (count <= 0 || ext.IsEmpty)
                {
                    continue;
                }
                ulong extBytes = ext.Length * (ulong)_ctx.BlockSize;
                if (skip >= extBytes)
                {
                    skip -= extBytes;
                    continue;
                }
                ulong avail = extBytes - skip;
                int take = avail < (ulong)count ? (int)avail : count;
                byte[] raw = _ctx.ReadBlocks(ext.Begin, ext.Length);
                Array.Copy(raw, (int)skip, buffer, offset + total, take);
                total += take;
                count -= take;
                skip = 0;
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

    internal sealed class VdfsMemoryBuffer : IBuffer
    {
        private readonly byte[] _data;

        public VdfsMemoryBuffer(byte[] data)
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

    public class VdfsFile : IVfsFile
    {
        protected readonly VdfsContext Context;
        protected readonly VdfsCatRecord Record;
        private IBuffer _content;
        private long? _unpacked;

        public VdfsFile(VdfsContext context, VdfsCatRecord record)
        {
            Context = context;
            Record = record;
        }

        public virtual DateTime CreationTimeUtc
        {
            get { return Entry.CreationTimeUtc; }
            set { throw new NotSupportedException(); }
        }

        public virtual DateTime LastAccessTimeUtc
        {
            get { return Entry.LastAccessTimeUtc; }
            set { throw new NotSupportedException(); }
        }

        public virtual DateTime LastWriteTimeUtc
        {
            get { return Entry.LastWriteTimeUtc; }
            set { throw new NotSupportedException(); }
        }

        public virtual FileAttributes FileAttributes
        {
            get { return Entry.FileAttributes; }
            set { throw new NotSupportedException(); }
        }

        public virtual long FileLength
        {
            get
            {
                if (Record.Value.Length < VdfsConstants.FileRecordSize)
                {
                    return 0;
                }
                long size = (long)VdfsConstants.ReadU64(
                    Record.Value, VdfsConstants.FolderRecordSize + VdfsConstants.ForkSizeOff);
                if (size < 0)
                {
                    return 0;
                }
                if (Entry.IsCompressed)
                {
                    // size_in_bytes is the compressed blob size; report the
                    // descriptor's unpacked size so reads never truncate.
                    if (_unpacked == null)
                    {
                        try
                        {
                            _unpacked = (long)Context.GetUnpackedSize(Record);
                        }
                        catch (IOException)
                        {
                            _unpacked = size;
                        }
                    }
                    return _unpacked.Value < 0 ? size : _unpacked.Value;
                }
                return size;
            }
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
            bool compressed = (Entry.Flags & VdfsConstants.FlagCompressed) != 0;
            if (compressed)
            {
                return new VdfsMemoryBuffer(Context.ReadCompressed(Record));
            }
            if (Record.Value.Length < VdfsConstants.FileRecordSize)
            {
                return new VdfsMemoryBuffer(new byte[0]);
            }
            ulong blocks = VdfsConstants.ReadU64(
                Record.Value, VdfsConstants.FolderRecordSize + VdfsConstants.ForkBlocksOff);
            if (blocks == 0)
            {
                return new VdfsMemoryBuffer(Context.ReadForkData(Record));
            }
            // Extent-mapped: resolve once, serve windows from the volume.
            var fork = new byte[VdfsConstants.ForkDataLen];
            Array.Copy(
                Record.Value,
                VdfsConstants.FolderRecordSize + VdfsConstants.ForkDataOff, fork, 0, fork.Length);
            List<VdfsExtent> extents = Context.ResolveForRead(Record.ObjectId, blocks, fork);
            return new VdfsExtentBuffer(Context, extents, FileLength);
        }

        private VdfsDirectoryEntry _entry;

        protected VdfsDirectoryEntry Entry
        {
            get
            {
                if (_entry == null)
                {
                    _entry = new VdfsDirectoryEntry(Record);
                }
                return _entry;
            }
        }
    }

    public sealed class VdfsSymlink : VdfsFile, IVfsSymlink<VdfsDirectoryEntry, VdfsFile>
    {
        public VdfsSymlink(VdfsContext context, VdfsCatRecord record)
            : base(context, record)
        {
        }

        public string TargetPath
        {
            get { return Context.ReadSymlinkTarget(Record); }
        }

        public override long FileLength
        {
            get { return 0; }
        }

        protected override IBuffer OpenContent()
        {
            return new VdfsMemoryBuffer(new byte[0]);
        }
    }

    public sealed class VdfsDirectory : VdfsFile, IVfsDirectory<VdfsDirectoryEntry, VdfsFile>
    {
        public VdfsDirectory(VdfsContext context, VdfsCatRecord record)
            : base(context, record)
        {
        }

        public ICollection<VdfsDirectoryEntry> AllEntries
        {
            get
            {
                var result = new List<VdfsDirectoryEntry>();
                var tree = new VdfsBtree(Context, Context.CatTreeIno);
                foreach (VdfsCatRecord rec in tree.Children(Record.ObjectId))
                {
                    result.Add(new VdfsDirectoryEntry(rec));
                }
                return result;
            }
        }

        public VdfsDirectoryEntry Self
        {
            get { return null; }
        }

        public VdfsDirectoryEntry GetEntryByName(string name)
        {
            var tree = new VdfsBtree(Context, Context.CatTreeIno);
            VdfsCatRecord rec = tree.FindChild(Record.ObjectId, name);
            if (rec == null)
            {
                return null;
            }
            return new VdfsDirectoryEntry(rec);
        }

        public VdfsDirectoryEntry CreateNewFile(string name)
        {
            throw new NotSupportedException();
        }

        public override long FileLength
        {
            get { return 0; }
        }

        protected override IBuffer OpenContent()
        {
            return new VdfsMemoryBuffer(new byte[0]);
        }
    }
}
