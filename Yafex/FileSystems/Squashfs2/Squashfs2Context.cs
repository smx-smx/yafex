using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Yafex.FileSystems.Squashfs2
{
    public sealed class Squashfs2Context : Yafex.FileSystems.Base.Vfs.VfsContext
    {
        private readonly Stream _stream;

        public bool BigEndian;
        public uint InodeCount;
        public uint BlockSize;
        public int BlockLog;
        public byte Flags;
        public uint NoUids;
        public uint FragmentCount;
        public long BytesUsed;
        public long RootStart;
        public long RootOffset;
        public long InodeTableStart;
        public long DirTableStart;
        public long FragTableStart;

        private readonly List<Metablock> _inodeBlocks = new List<Metablock>();
        private readonly List<Metablock> _dirBlocks = new List<Metablock>();
        private uint[] _uids = new uint[0];
        private long[] _fragStarts = new long[0];
        private uint[] _fragSizes = new uint[0];

        private sealed class Metablock
        {
            public long FilePos;
            public byte[] Data = Array.Empty<byte>();
        }

        private Squashfs2Context(Stream stream)
        {
            _stream = stream;
        }

        private ushort U16(byte[] buf, long off)
        {
            return BigEndian
                ? Squashfs2Constants.ReadU16BE(buf, off)
                : Squashfs2Constants.ReadU16(buf, off);
        }

        private uint U32(byte[] buf, long off)
        {
            return BigEndian
                ? Squashfs2Constants.ReadU32BE(buf, off)
                : Squashfs2Constants.ReadU32(buf, off);
        }

        private ulong U64(byte[] buf, long off)
        {
            return BigEndian
                ? Squashfs2Constants.ReadU64BE(buf, off)
                : Squashfs2Constants.ReadU64(buf, off);
        }

        public static Squashfs2Context Open(Stream stream)
        {
            if (stream == null || !stream.CanSeek)
            {
                throw new IOException("squashfs needs a seekable stream");
            }
            if (stream.Length < Squashfs2Constants.SuperblockSize)
            {
                throw new IOException("image too small for squashfs superblock");
            }
            byte[] sb = Squashfs2Constants.ReadBytes(stream, 0, Squashfs2Constants.SuperblockSize);
            if (!Squashfs2Constants.HasMagic(sb, out bool bigEndian))
            {
                throw new IOException("not a squashfs image");
            }
            var ctx = new Squashfs2Context(stream);
            ctx.BigEndian = bigEndian;
            if (ctx.U16(sb, Squashfs2Constants.MajorOff) != Squashfs2Constants.MajorVersion)
            {
                throw new IOException("unsupported squashfs major version");
            }
            ctx.InodeCount = ctx.U32(sb, Squashfs2Constants.InodesOff);
            if (ctx.InodeCount == 0 || ctx.InodeCount > 16 * 1024 * 1024)
            {
                throw new IOException("bad squashfs inode count");
            }
            ctx.BlockLog = ctx.U16(sb, Squashfs2Constants.BlockLogOff);
            ctx.BlockSize = 1u << ctx.BlockLog;
            if (ctx.BlockLog < 12 || ctx.BlockLog > 20)
            {
                throw new IOException("bad squashfs block size");
            }
            uint blockSizeField = ctx.U32(sb, Squashfs2Constants.BlockSizeOff);
            if (blockSizeField != ctx.BlockSize)
            {
                throw new IOException("squashfs block size mismatch");
            }
            ctx.Flags = sb[Squashfs2Constants.FlagsOff];
            ctx.NoUids = sb[Squashfs2Constants.NoUidsOff];
            ulong rootRef = ctx.U64(sb, Squashfs2Constants.RootInodeOff);
            ctx.RootStart = (long)(rootRef >> 16);
            ctx.RootOffset = (long)(rootRef & 0xFFFF);
            ctx.FragmentCount = ctx.U32(sb, Squashfs2Constants.FragmentsOff);
            // v2 tables resolve through the 32-bit aliases.
            ctx.BytesUsed = ctx.U32(sb, Squashfs2Constants.BytesUsed2Off);
            long uidStart = ctx.U32(sb, Squashfs2Constants.UidStart2Off);
            ctx.InodeTableStart = ctx.U32(sb, Squashfs2Constants.InodeTable2Off);
            ctx.DirTableStart = ctx.U32(sb, Squashfs2Constants.DirTable2Off);
            ctx.FragTableStart = ctx.U32(sb, Squashfs2Constants.FragmentTable2Off);

            if (ctx.InodeTableStart <= 0 || ctx.DirTableStart <= ctx.InodeTableStart
                || ctx.DirTableStart >= stream.Length || ctx.FragTableStart > stream.Length)
            {
                throw new IOException("bad squashfs table range");
            }
            ctx.ReadMetablocks(ctx.InodeTableStart, ctx.DirTableStart, ctx._inodeBlocks);
            long fragIndex0 = ctx.FragmentCount > 0
                ? ctx.FragIndexStart()
                : ctx.FragTableStart;
            ctx.ReadMetablocks(ctx.DirTableStart, fragIndex0, ctx._dirBlocks);
            ctx.ReadIds(uidStart);
            ctx.ReadFragments();
            return ctx;
        }

        private long FragIndexStart()
        {
            byte[] ib = Squashfs2Constants.ReadBytes(_stream, FragTableStart, 4);
            return U32(ib, 0);
        }

        private void ReadMetablocks(long tableStart, long tableEnd, List<Metablock> run)
        {
            if (tableStart < 0 || tableEnd < tableStart || tableEnd > _stream.Length)
            {
                throw new IOException("bad squashfs table range");
            }
            long pos = tableStart;
            while (pos < tableEnd)
            {
                byte[] cb = Squashfs2Constants.ReadBytes(_stream, pos, 2);
                uint cByte = U16(cb, 0);
                uint len = cByte & 0x7FFF;
                if (pos + 2 + len > tableEnd)
                {
                    throw new IOException("squashfs metablock overruns its table");
                }
                byte[] stored = Squashfs2Constants.ReadBytes(_stream, pos + 2, (int)len);
                byte[] decomp = (cByte & Squashfs2Constants.StoredBitMeta) != 0
                    ? stored
                    : Inflate(stored, Squashfs2Constants.MetadataSize);
                // keyed table-relative: v2 (start_block, offset) coords are
                // relative to their table start (root (0x2002, x) = second
                // metablock right after a full 8KB first block)
                run.Add(new Metablock { FilePos = pos - tableStart, Data = decomp });
                pos += 2 + len;
            }
        }

        internal static byte[] Inflate(byte[] compressed, long maxOut)
        {
            using (var raw = new MemoryStream(compressed, false))
            using (var inflater = new DeflateStream(raw, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buf = new byte[1 << 16];
                long total = 0;
                for (;;)
                {
                    int n = inflater.Read(buf, 0, buf.Length);
                    if (n <= 0)
                    {
                        break;
                    }
                    total += n;
                    if (total > maxOut)
                    {
                        throw new IOException("squashfs inflate overrun");
                    }
                    output.Write(buf, 0, n);
                }
                return output.ToArray();
            }
        }

        private void ReadIds(long uidStart)
        {
            uint total = NoUids;
            _uids = new uint[total];
            for (uint i = 0; i < total; i++)
            {
                byte[] b = Squashfs2Constants.ReadBytes(_stream, uidStart + 4L * i, 4);
                _uids[i] = U32(b, 0);
            }
        }

        private void ReadFragments()
        {
            if (FragmentCount == 0)
            {
                return;
            }
            int indexes = (int)((FragmentCount * 16L + Squashfs2Constants.MetadataSize - 1)
                / Squashfs2Constants.MetadataSize);
            var payload = new List<byte>();
            for (int i = 0; i < indexes; i++)
            {
                byte[] ib = Squashfs2Constants.ReadBytes(_stream, FragTableStart + 8L * i, 8);
                long metaPos = (long)U64(ib, 0);
                byte[] cb = Squashfs2Constants.ReadBytes(_stream, metaPos, 2);
                uint cByte = U16(cb, 0);
                uint len = cByte & 0x7FFF;
                byte[] stored = Squashfs2Constants.ReadBytes(_stream, metaPos + 2, (int)len);
                byte[] decomp = (cByte & Squashfs2Constants.StoredBitMeta) != 0
                    ? stored
                    : Inflate(stored, Squashfs2Constants.MetadataSize);
                payload.AddRange(decomp);
            }
            _fragStarts = new long[FragmentCount];
            _fragSizes = new uint[FragmentCount];
            for (uint i = 0; i < FragmentCount; i++)
            {
                _fragStarts[i] = U32(payload.ToArray(), 8L * i);
                _fragSizes[i] = U32(payload.ToArray(), 8L * i + 4);
            }
        }

        private byte[] RunRead(List<Metablock> run, long blockStart, int blockOffset, int count)
        {
            // Records (e.g. wiz_dyn.htm's inode) may span metablocks: this
            // image packs inodes linearly, so continue into the following
            // metablocks (keyed table-relative, in table order) as needed.
            int index = -1;
            for (int i = 0; i < run.Count; i++)
            {
                // block addressing: exact match on the metablock whose
                // table-relative start equals the reference
                if (run[i].FilePos == blockStart)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                throw new IOException("squashfs metadata block not found");
            }
            if (blockOffset < 0)
            {
                throw new IOException("squashfs metadata read out of range");
            }
            var out_ = new byte[count];
            int done = 0;
            int off = blockOffset;
            while (done < count)
            {
                if (index >= run.Count)
                {
                    throw new IOException("squashfs metadata read out of range");
                }
                Metablock mb = run[index];
                if (off >= mb.Data.Length)
                {
                    // linear packing: carry the offset into the next block
                    off -= mb.Data.Length;
                    index++;
                    continue;
                }
                int take = Math.Min(count - done, mb.Data.Length - off);
                Array.Copy(mb.Data, off, out_, done, take);
                done += take;
                off = 0;
                index++;
            }
            return out_;
        }

        public byte[] InodeRunRead(long blockStart, int blockOffset, int count)
        {
            return RunRead(_inodeBlocks, blockStart, blockOffset, count);
        }

        public byte[] DirRunRead(long blockStart, int blockOffset, int count)
        {
            return RunRead(_dirBlocks, blockStart, blockOffset, count);
        }

        public Squashfs2Inode ReadInode(long startBlock, int offset, string name)
        {
            // Bit-packed base word: LSB-first on LE images, MSB-first on BE
            // (C bitfields laid from opposite ends). Decoded values match.
            byte[] head = InodeRunRead(startBlock, offset, 4);
            uint w0 = U32(head, 0);
            int type;
            uint mode;
            uint uid;
            if (!BigEndian)
            {
                type = (int)(w0 & 0xF);
                mode = (w0 >> 4) & 0xFFF;
                uid = (w0 >> 16) & 0xFF;
            }
            else
            {
                type = (int)(w0 >> 28);
                mode = (w0 >> 16) & 0xFFF;
                uid = (w0 >> 8) & 0xFF;
            }
            var inode = new Squashfs2Inode { Type = type, Mode = mode, Uid = uid };
            switch (type)
            {
                case Squashfs2Constants.TypeReg:
                case Squashfs2Constants.TypeLreg:
                    {
                        byte[] rest = InodeRunRead(startBlock, offset + 4, 20);
                        inode.Mtime = U32(rest, 0);
                        long contentStart = U32(rest, 4);
                        uint fragment = U32(rest, 8);
                        uint fragOffset = U32(rest, 12);
                        uint fileSize = U32(rest, 16);
                        inode.ContentStart = contentStart;
                        inode.Fragment = fragment;
                        inode.FragmentOffset = fragOffset;
                        inode.FileSize = fileSize;
                        uint blocks = fragment == Squashfs2Constants.InvalidFragment
                            ? (uint)((fileSize + BlockSize - 1) / BlockSize)
                            : (uint)(fileSize / BlockSize);
                        var sizes = new uint[blocks];
                        for (uint b = 0; b < blocks; b++)
                        {
                            byte[] sb = InodeRunRead(startBlock, offset + 24 + (int)(4 * b), 4);
                            sizes[b] = U32(sb, 0);
                        }
                        inode.BlockSizes = sizes;
                        break;
                    }
                case Squashfs2Constants.TypeDir:
                case Squashfs2Constants.TypeLdir:
                    {
                        byte[] rest = InodeRunRead(startBlock, offset + 4, 11);
                        uint sizeOff = U32(rest, 0);
                        uint fileSize;
                        uint dirOffset;
                        if (!BigEndian)
                        {
                            fileSize = sizeOff & 0x7FFFF;
                            dirOffset = (sizeOff >> 19) & 0x1FFF;
                        }
                        else
                        {
                            fileSize = sizeOff >> 13;
                            dirOffset = sizeOff & 0x1FFF;
                        }
                        inode.Mtime = U32(rest, 4);
                        inode.DirStart = BigEndian
                            ? (uint)((rest[8] << 16) | (rest[9] << 8) | rest[10])
                            : (uint)(rest[8] | (rest[9] << 8) | (rest[10] << 16));
                        inode.DirOffset = dirOffset;
                        inode.FileSize = fileSize;
                        break;
                    }
                case Squashfs2Constants.TypeSymlink:
                    {
                        byte[] rest = InodeRunRead(startBlock, offset + 4, 2);
                        uint linkSize = U16(rest, 0);
                        byte[] target = InodeRunRead(startBlock, offset + 6, (int)linkSize);
                        inode.SymlinkTarget = Encoding.ASCII.GetString(target);
                        inode.FileSize = linkSize;
                        break;
                    }
                case Squashfs2Constants.TypeBlkdev:
                case Squashfs2Constants.TypeChrdev:
                    {
                        byte[] rest = InodeRunRead(startBlock, offset + 4, 2);
                        inode.Rdev = U16(rest, 0);
                        break;
                    }
                default:
                    break;
            }
            return inode;
        }

        public List<Squashfs2DirectoryEntry> ReadDirectory(Squashfs2Inode dir)
        {
            // directory listing: header {count:8, start_block:24}, then
            // entries of {offset:13, type:3, size:8} + name[size+1]
            // (packed 3-byte form on BE images, 4-byte form on LE).
            var entries = new List<Squashfs2DirectoryEntry>();
            if (dir.FileSize <= 0)
            {
                return entries;
            }
            long total = dir.FileSize;
            long consumed = 0;
            while (consumed < total)
            {
                byte[] header = DirRunRead(dir.DirStart, (int)(dir.DirOffset + consumed), 4);
                int count = header[0] + 1;
                uint blk = BigEndian
                    ? (uint)((header[1] << 16) | (header[2] << 8) | header[3])
                    : (uint)(header[1] | (header[2] << 8) | (header[3] << 16));
                if (count <= 0 || count > 256)
                {
                    throw new IOException("squashfs directory has bad count");
                }
                consumed += 4;
                for (int i = 0; i < count; i++)
                {
                    // LE v2 entries are 4 bytes; this BE variant packs
                    // {offset:13, type:3} into 2 bytes + 1 length byte.
                    int ebLen = BigEndian ? 3 : 4;
                    byte[] eb = DirRunRead(dir.DirStart, (int)(dir.DirOffset + consumed), ebLen);
                    int entryOff;
                    int entryType;
                    int nameLen;
                    if (!BigEndian)
                    {
                        uint bits = U32(eb, 0);
                        entryOff = (int)(bits & 0x1FFF);
                        entryType = (int)((bits >> 13) & 0x7);
                        nameLen = (int)((bits >> 16) & 0xFF) + 1;
                    }
                    else
                    {
                        entryOff = (eb[0] << 5) | (eb[1] >> 3);
                        entryType = eb[1] & 0x7;
                        nameLen = eb[2] + 1;
                    }
                    if (entryType < 1 || entryType > 12 || nameLen < 1 || nameLen > 256)
                    {
                        throw new IOException("squashfs directory entry failed validation");
                    }
                    consumed += ebLen;
                    byte[] nameBytes = DirRunRead(dir.DirStart, (int)(dir.DirOffset + consumed), nameLen);
                    consumed += nameLen;
                    foreach (byte b in nameBytes)
                    {
                        if (b != 0 && (b < 0x20 || b >= 0x7F))
                        {
                            throw new IOException("squashfs directory entry failed validation");
                        }
                    }
                    string name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
                    if (name.Length == 0)
                    {
                        throw new IOException("squashfs directory entry failed validation");
                    }
                    entries.Add(new Squashfs2DirectoryEntry(
                        this, name, entryType, blk, entryOff));
                }
            }
            return entries;
        }

        public byte[] ReadDataBlock(long fileOffset, uint storedSize, long want)
        {
            byte[] stored = Squashfs2Constants.ReadBytes(_stream, fileOffset, (int)storedSize);
            return Squashfs2Decompress.Uncompress(stored, want);
        }

        public byte[] ReadStored(long fileOffset, long count)
        {
            return Squashfs2Constants.ReadBytes(_stream, fileOffset, (int)count);
        }

        public byte[] ReadFragment(uint fragment, uint fragOffset, long length)
        {
            if (fragment >= _fragStarts.Length)
            {
                throw new IOException("bad fragment index");
            }
            long dpos = 0;
            for (uint i = 0; i < fragment; i++)
            {
                dpos += _fragSizes[i];
            }
            byte[] fragBytes = Squashfs2Constants.ReadBytes(_stream, _fragStarts[fragment], (int)_fragSizes[fragment]);
            byte[] decomp;
            try
            {
                decomp = Inflate(fragBytes, length + fragOffset);
            }
            catch (IOException)
            {
                decomp = Squashfs2Decompress.Uncompress(fragBytes, length + fragOffset);
            }
            if (fragOffset + length > decomp.Length)
            {
                throw new IOException("squashfs fragment read out of range");
            }
            var out_ = new byte[length];
            Array.Copy(decomp, (int)fragOffset, out_, 0, (int)length);
            return out_;
        }
    }
}
