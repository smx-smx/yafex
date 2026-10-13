using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

using DiscUtils.Vfs;

namespace Yafex.FileSystems.Squashfs3
{
    /// <summary>
    /// Parsed inode: content addressing plus file payload parameters.
    /// Locations are (metablock byte offset from table start, offset into
    /// the decompressed metablock). File data runs contiguously from
    /// DataStart with per-block stored sizes in BlockSizes.
    /// </summary>
    public sealed class Squashfs3Inode
    {
        public int Type;
        public ushort Mode;
        public uint Uid;
        public uint Gid;
        public uint Mtime;
        public long ContentStart;
        public long ContentOffset;
        public long FileSize;
        public uint Fragment = Squashfs3Constants.InvalidFragment;
        public uint FragmentOffset;
        public uint[] BlockSizes = new uint[0];
        public string SymlinkTarget = string.Empty;
        public long Rdev;
    }

    internal sealed class MetablockRun
    {
        public readonly List<long> Offsets = new List<long>();
        public readonly List<byte[]> Blocks = new List<byte[]>();

        /// <summary>
        /// Reads count bytes at (metablock table offset, decompressed
        /// offset), spanning forward into following metablocks as needed.
        /// The two coordinates live in different spaces (compressed file
        /// offsets vs decompressed bytes) and must not be added.
        /// </summary>
        public byte[] Read(long metaStart, long decompOff, long count)
        {
            if (count < 0)
            {
                throw new IOException("bad squashfs read length");
            }
            int i = 0;
            while (i < Offsets.Count && Offsets[i] != metaStart)
            {
                i++;
            }
            if (i >= Offsets.Count)
            {
                throw new IOException("squashfs metablock not found");
            }
            var output = new byte[count];
            long done = 0;
            long inBlk = decompOff;
            // Contents may start past the first metablock's end (large
            // dirs spanning metablocks): normalize forward.
            while (inBlk >= Blocks[i].Length)
            {
                inBlk -= Blocks[i].Length;
                i++;
                if (i >= Offsets.Count)
                {
                    throw new IOException("bad squashfs table offset: meta " + metaStart.ToString("X") + " off " + decompOff);
                }
            }
            while (done < count)
            {
                if (i >= Offsets.Count)
                {
                    throw new IOException("squashfs table read past end");
                }
                if (inBlk < 0 || inBlk >= Blocks[i].Length)
                {
                    throw new IOException("bad squashfs table offset: meta " + metaStart.ToString("X") + " off " + decompOff);
                }
                long take = count - done;
                long avail = Blocks[i].Length - inBlk;
                if (take > avail)
                {
                    take = avail;
                }
                Array.Copy(Blocks[i], (int)inBlk, output, (int)done, (int)take);
                done += take;
                inBlk = 0;
                i++;
            }
            return output;
        }
    }

    /// <summary>
    /// Read-only squashfs 3.x engine: superblock, metablock runs, id and
    /// fragment tables, inode/dir/file reads. File DATA stays on the stream
    /// and is served windowed; only metadata is materialized.
    /// </summary>
    public sealed class Squashfs3Context : VfsContext
    {
        private readonly Stream _stream;

        public uint InodeCount;
        public uint BlockSize;
        public int BlockLog;
        public byte Flags;
        public int NoUids;
        public int NoGuids;
        public uint FragmentCount;
        public long BytesUsed;
        public long InodeTableStart;
        public long DirectoryTableStart;
        public long FragmentTableStart;
        public long RootStart;
        public long RootOffset;

        private readonly MetablockRun _inodeRun = new MetablockRun();
        private readonly MetablockRun _dirRun = new MetablockRun();
        private uint[] _uids = new uint[0];
        private long[] _fragStarts = new long[0];
        private uint[] _fragSizes = new uint[0];

        public bool BigEndian;

        private ushort U16(byte[] buf, long off)
        {
            return BigEndian
                ? Squashfs3Constants.ReadU16BE(buf, off)
                : Squashfs3Constants.ReadU16(buf, off);
        }

        private uint U32(byte[] buf, long off)
        {
            return BigEndian
                ? Squashfs3Constants.ReadU32BE(buf, off)
                : Squashfs3Constants.ReadU32(buf, off);
        }

        private ulong U64(byte[] buf, long off)
        {
            return BigEndian
                ? Squashfs3Constants.ReadU64BE(buf, off)
                : Squashfs3Constants.ReadU64(buf, off);
        }

        private Squashfs3Context(Stream stream)
        {
            _stream = stream;
        }

        public static Squashfs3Context Open(Stream stream)
        {
            if (stream == null || !stream.CanSeek)
            {
                throw new IOException("squashfs needs a seekable stream");
            }
            if (stream.Length < Squashfs3Constants.SuperblockSize)
            {
                throw new IOException("image too small for squashfs superblock");
            }
            byte[] sb = Squashfs3Constants.ReadBytes(stream, 0, Squashfs3Constants.SuperblockSize);
            if (!Squashfs3Constants.HasMagic(sb, out bool bigEndian))
            {
                throw new IOException("not a squashfs image");
            }
            var ctx = new Squashfs3Context(stream);
            ctx.BigEndian = bigEndian;
            if (ctx.U16(sb, Squashfs3Constants.MajorOff) != Squashfs3Constants.MajorVersion)
            {
                throw new IOException("unsupported squashfs major version");
            }
            ctx.InodeCount = ctx.U32(sb, Squashfs3Constants.InodesOff);
            ctx.BlockLog = ctx.U16(sb, Squashfs3Constants.BlockLogOff);
            ctx.BlockSize = 1u << ctx.BlockLog;
            if (ctx.BlockLog < 12 || ctx.BlockLog > 20)
            {
                throw new IOException("bad squashfs block size");
            }
            uint blockSizeField = ctx.U32(sb, Squashfs3Constants.BlockSizeOff);
            if (blockSizeField != ctx.BlockSize)
            {
                throw new IOException("squashfs block size mismatch");
            }
            ctx.Flags = sb[Squashfs3Constants.FlagsOff];
            ctx.NoUids = sb[Squashfs3Constants.NoUidsOff];
            ctx.NoGuids = sb[Squashfs3Constants.NoGuidsOff];
            ulong rootRef = ctx.U64(sb, Squashfs3Constants.RootInodeOff);
            ctx.RootStart = (long)(rootRef >> 16);
            ctx.RootOffset = (long)(rootRef & 0xFFFF);
            ctx.FragmentCount = ctx.U32(sb, Squashfs3Constants.FragmentsOff);
            ctx.BytesUsed = (long)ctx.U64(sb, Squashfs3Constants.BytesUsedOff);
            long uidStart = (long)ctx.U64(sb, Squashfs3Constants.UidStartOff);
            ctx.InodeTableStart = (long)ctx.U64(sb, Squashfs3Constants.InodeTableOff);
            ctx.DirectoryTableStart = (long)ctx.U64(sb, Squashfs3Constants.DirTableOff);
            ctx.FragmentTableStart = (long)ctx.U64(sb, Squashfs3Constants.FragTableOff);

            ctx.ReadMetablocks(ctx.InodeTableStart, ctx.DirectoryTableStart, ctx._inodeRun);
            long fragIndex0 = ctx.FragmentCount > 0
                ? (long)ctx.U64(
                    Squashfs3Constants.ReadBytes(stream, ctx.FragmentTableStart, 8), 0)
                : ctx.FragmentTableStart;
            ctx.ReadMetablocks(ctx.DirectoryTableStart, fragIndex0, ctx._dirRun);
            ctx.ReadIds(uidStart);
            ctx.ReadFragments();
            return ctx;
        }

        private void ReadMetablocks(long tableStart, long tableEnd, MetablockRun run)
        {
            if (tableStart < 0 || tableEnd < tableStart || tableEnd > _stream.Length)
            {
                throw new IOException("bad squashfs table range");
            }
            long pos = tableStart;
            while (pos < tableEnd)
            {
                byte[] cb = Squashfs3Constants.ReadBytes(_stream, pos, 2);
                uint cByte = U16(cb, 0);
                uint len = cByte & 0x7FFF;
                if (pos + 2 + len > tableEnd)
                {
                    throw new IOException("squashfs metablock overruns its table");
                }
                byte[] stored = Squashfs3Constants.ReadBytes(_stream, pos + 2, (int)len);
                byte[] decomp = (cByte & Squashfs3Constants.StoredBitMeta) != 0
                    ? stored
                    : Inflate(stored, Squashfs3Constants.MetadataSize);
                run.Offsets.Add(pos - tableStart);
                run.Blocks.Add(decomp);
                pos += 2 + len;
            }
        }

        internal static byte[] Inflate(byte[] compressed, long maxOut)
        {
            int start = compressed.Length >= 2 && compressed[0] == 0x78 ? 2 : 0;
            using (var raw = new MemoryStream(compressed, start, compressed.Length - start, false))
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
            int total = NoUids + NoGuids;
            _uids = new uint[total];
            for (int i = 0; i < total; i++)
            {
                byte[] b = Squashfs3Constants.ReadBytes(_stream, uidStart + 4L * i, 4);
                _uids[i] = U32(b, 0);
            }
        }

        private void ReadFragments()
        {
            if (FragmentCount == 0)
            {
                return;
            }
            int indexes = (int)((FragmentCount * 16L + Squashfs3Constants.MetadataSize - 1)
                / Squashfs3Constants.MetadataSize);
            var payload = new List<byte>();
            for (int i = 0; i < indexes; i++)
            {
                byte[] ib = Squashfs3Constants.ReadBytes(_stream, FragmentTableStart + 8L * i, 8);
                long metaPos = (long)U64(ib, 0);
                byte[] cb = Squashfs3Constants.ReadBytes(_stream, metaPos, 2);
                uint cByte = U16(cb, 0);
                uint len = cByte & 0x7FFF;
                byte[] stored = Squashfs3Constants.ReadBytes(_stream, metaPos + 2, (int)len);
                byte[] decomp = (cByte & Squashfs3Constants.StoredBitMeta) != 0
                    ? stored
                    : Inflate(stored, Squashfs3Constants.MetadataSize);
                payload.AddRange(decomp);
            }
            var starts = new List<long>();
            var sizes = new List<uint>();
            byte[] all = payload.ToArray();
            for (long off = 0; off + 16 <= all.Length && (uint)starts.Count < FragmentCount; off += 16)
            {
                starts.Add((long)U64(all, off));
                sizes.Add(U32(all, off + 8));
            }
            _fragStarts = starts.ToArray();
            _fragSizes = sizes.ToArray();
        }

        private uint MapId(uint idx, bool guid, uint fallback)
        {
            if (guid && idx == Squashfs3Constants.GuidSameAsUid)
            {
                return fallback;
            }
            int pos = guid ? NoUids + (int)idx : (int)idx;
            if (pos < 0 || pos >= _uids.Length)
            {
                throw new IOException("squashfs id out of range");
            }
            return _uids[pos];
        }

        public Squashfs3Inode ReadInode(long startBlock, long offset, string name)
        {
            byte[] header = _inodeRun.Read(startBlock, offset, 12);
            uint w0 = U32(header, 0);
            // Bit-packed base word: LSB-first on LE images, MSB-first on BE
            // (C bitfields laid from opposite ends). Decoded values match.
            int type;
            ushort mode;
            byte uidIdx;
            byte guidIdx;
            if (!BigEndian)
            {
                type = (int)(w0 & 0xF);
                mode = (ushort)((w0 >> 4) & 0xFFF);
                uidIdx = (byte)((w0 >> 16) & 0xFF);
                guidIdx = (byte)((w0 >> 24) & 0xFF);
            }
            else
            {
                type = (int)(w0 >> 28);
                mode = (ushort)((w0 >> 16) & 0xFFF);
                uidIdx = (byte)((w0 >> 8) & 0xFF);
                guidIdx = (byte)(w0 & 0xFF);
            }
            var inode = new Squashfs3Inode();
            inode.Type = type;
            inode.Mode = mode;
            inode.Mtime = U32(header, 4);
            inode.Uid = MapId(uidIdx, false, 0);
            inode.Gid = MapId(guidIdx, true, inode.Uid);

            switch (type)
            {
                case Squashfs3Constants.TypeLdir:
                {
                    // ldir bitfields straddle units (file_size:27 +
                    // offset:13 = 40 bits): offset spills into @+20 and the
                    // plain start_block slides to @+21 (verified by offsetof).
                    // BE mirrors the packing MSB-first.
                    byte[] rest = _inodeRun.Read(startBlock, offset + 12, 15);
                    if (!BigEndian)
                    {
                        uint sizeOff = U32(rest, 4);
                        inode.FileSize = sizeOff & 0x7FFFFFF;
                        inode.ContentOffset = (sizeOff >> 27) | ((uint)rest[8] << 5);
                        inode.ContentStart = U32(rest, 9);
                    }
                    else
                    {
                        inode.FileSize = ((uint)rest[4] << 19) | ((uint)rest[5] << 11)
                            | ((uint)rest[6] << 3) | ((uint)rest[7] >> 5);
                        inode.ContentOffset = ((uint)(rest[7] & 0x1F) << 8) | rest[8];
                        inode.ContentStart = U32(rest, 9);
                    }
                    break;
                }
                case Squashfs3Constants.TypeDir:
                {
                    byte[] rest = _inodeRun.Read(startBlock, offset + 12, 12);
                    if (!BigEndian)
                    {
                        uint sizeOff = U32(rest, 4);
                        inode.FileSize = sizeOff & 0x7FFFF;
                        inode.ContentOffset = sizeOff >> 19;
                        inode.ContentStart = U32(rest, 8);
                    }
                    else
                    {
                        inode.FileSize = ((uint)rest[4] << 11) | ((uint)rest[5] << 3)
                            | ((uint)rest[6] >> 5);
                        inode.ContentOffset = ((uint)(rest[6] & 0x1F) << 8) | rest[7];
                        inode.ContentStart = U32(rest, 8);
                    }
                    break;
                }
                case Squashfs3Constants.TypeReg:
                case Squashfs3Constants.TypeLreg:
                {
                    bool large = type == Squashfs3Constants.TypeLreg;
                    byte[] rest = _inodeRun.Read(startBlock, offset + 12, large ? 28 : 20);
                    int p = 0;
                    if (large)
                    {
                        p += 4;
                    }
                    long dataStart = (long)U64(rest, p);
                    p += 8;
                    uint frag = U32(rest, p);
                    p += 4;
                    uint fragOff = U32(rest, p);
                    p += 4;
                    long size = large
                        ? (long)U64(rest, p)
                        : U32(rest, p);
                    inode.ContentStart = dataStart;
                    inode.Fragment = frag;
                    inode.FragmentOffset = fragOff;
                    inode.FileSize = size;
                    long blocks = frag == Squashfs3Constants.InvalidFragment
                        ? (size + BlockSize - 1) >> BlockLog
                        : size >> BlockLog;
                    if (blocks < 0 || blocks > (1L << 24))
                    {
                        throw new IOException("bad squashfs block count");
                    }
                    byte[] list = _inodeRun.Read(startBlock, offset + 12 + (large ? 28 : 20), blocks * 4);
                    var sizes = new uint[blocks];
                    for (long b = 0; b < blocks; b++)
                    {
                        sizes[b] = U32(list, b * 4);
                    }
                    inode.BlockSizes = sizes;
                    break;
                }
                case Squashfs3Constants.TypeSymlink:
                {
                    byte[] rest = _inodeRun.Read(startBlock, offset + 12, 6);
                    uint linkSize = U16(rest, 4);
                    byte[] target = _inodeRun.Read(startBlock, offset + 18, linkSize);
                    inode.SymlinkTarget = Encoding.ASCII.GetString(target);
                    inode.FileSize = linkSize;
                    break;
                }
                case 10: // large symlink: base + nlink + size + target
                {
                    byte[] rest = _inodeRun.Read(startBlock, offset + 12, 6);
                    uint linkSize = U16(rest, 4);
                    byte[] target = _inodeRun.Read(startBlock, offset + 22, linkSize);
                    inode.SymlinkTarget = Encoding.ASCII.GetString(target);
                    inode.FileSize = linkSize;
                    break;
                }
                default:
                {
                    // blkdev/chrdev/fifo/socket/ipc and large variants of
                    // non-regular types: no content, keep address defaults.
                    if (type == Squashfs3Constants.TypeBlkdev || type == Squashfs3Constants.TypeChrdev)
                    {
                        byte[] rest = _inodeRun.Read(startBlock, offset + 12, 6);
                        inode.Rdev = U16(rest, 4);
                    }
                    break;
                }
            }
            return inode;
        }

        public List<Squashfs3DirectoryEntry> ReadDirectory(Squashfs3Inode dir)
        {
            var entries = new List<Squashfs3DirectoryEntry>();
            if (dir.FileSize <= 3)
            {
                return entries;
            }
            long total = dir.FileSize - 3;
            long consumed = 0;
            while (consumed < total)
            {
                byte[] header = _dirRun.Read(dir.ContentStart, dir.ContentOffset + consumed, 9);
                int count = header[0] + 1;
                uint blk = U32(header, 1);
                consumed += 9;
                for (int i = 0; i < count; i++)
                {
                    byte[] eb = _dirRun.Read(dir.ContentStart, dir.ContentOffset + consumed, 5);
                    int entryOff;
                    int entryType;
                    if (!BigEndian)
                    {
                        uint packed = U16(eb, 0);
                        entryOff = (int)(packed & 0x1FFF);
                        entryType = (int)((packed >> 13) & 0x7);
                    }
                    else
                    {
                        entryOff = (eb[0] << 5) | (eb[1] >> 3);
                        entryType = eb[1] & 0x7;
                    }
                    int nameLen = eb[2] + 1;
                    consumed += 5;
                    byte[] nameBytes = _dirRun.Read(dir.ContentStart, dir.ContentOffset + consumed, nameLen);
                    consumed += nameLen;
                    entries.Add(new Squashfs3DirectoryEntry(
                        this, Encoding.ASCII.GetString(nameBytes), entryType, blk, entryOff));
                }
            }
            return entries;
        }

        public byte[] ReadDataBlock(long fileOffset, uint storedSize, long want)
        {
            byte[] stored = Squashfs3Constants.ReadBytes(_stream, fileOffset, (int)storedSize);
            return Inflate(stored, want);
        }

        public byte[] ReadStored(long fileOffset, long count)
        {
            return Squashfs3Constants.ReadBytes(_stream, fileOffset, (int)count);
        }

        public byte[] ReadFragment(uint fragment, uint fragOffset, long length)
        {
            if (fragment >= _fragStarts.Length)
            {
                throw new IOException("bad fragment index");
            }
            // Fragment sizes use the data-block convention (bit 24 set =
            // stored raw, else zlib-compressed).
            uint rawSize = _fragSizes[fragment];
            uint size = rawSize & ~Squashfs3Constants.StoredBitBlock;
            // Sizes are advisory: at least one observed image carries a
            // bogus oversized entry. Clamp to the image; inflation stops at
            // the deflate end marker and the slice check still rejects
            // short data.
            long avail = _stream.Length - _fragStarts[fragment];
            if (avail < 0)
            {
                throw new IOException("fragment out of range");
            }
            long take = size;
            if (take > avail)
            {
                take = avail;
            }
            byte[] stored = Squashfs3Constants.ReadBytes(_stream, _fragStarts[fragment], (int)take);
            byte[] all;
            if ((rawSize & Squashfs3Constants.StoredBitBlock) != 0)
            {
                all = stored;
            }
            else
            {
                all = Inflate(stored, 1 << 20);
            }
            if ((long)fragOffset + length > all.Length)
            {
                throw new IOException("fragment slice out of range");
            }
            var slice = new byte[length];
            Array.Copy(all, (int)fragOffset, slice, 0, (int)length);
            return slice;
        }
    }
}
