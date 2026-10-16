#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
// Synthetic MStar flash-plan + sparse-overlay tests: script excerpts and
// tiny sparse fixtures are built in-memory (small blocks, literal-only LZ4).
// No fixtures, no keys.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using Smx.SharpIO.Memory.Buffers;

using Yafex;
using Yafex.FileFormats.MStarPkg;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class MstarSparseCombineTests
{
    private const uint Blk = 512;

    private static byte[] SparseTestImage(uint totalBlks, params (ushort Type, uint Blocks, byte[] Raw, uint Fill)[] chunks)
    {
        var buf = new List<byte>();
        var hdr = new byte[28];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(0, 4), 0xED26FF3A);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(4, 2), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(8, 2), 28);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(10, 2), 12);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(12, 4), Blk);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(16, 4), totalBlks);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(20, 4), (uint)chunks.Length);
        buf.AddRange(hdr);
        foreach (var (type, blocks, raw, fill) in chunks)
        {
            uint totalSz = type == 0xCAC1 ? 12 + blocks * Blk : type == 0xCAC3 ? 12u : 16u;
            var h = new byte[12];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(0, 2), type);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(4, 4), blocks);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(8, 4), totalSz);
            buf.AddRange(h);
            if (type == 0xCAC1) buf.AddRange(raw);
            else if (type == 0xCAC2 || type == 0xCAC4)
            {
                var f = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(f, fill);
                buf.AddRange(f);
            }
        }
        return buf.ToArray();
    }

    private static byte[] RawBlock(byte v)
    {
        var b = new byte[Blk];
        for (int i = 0; i < b.Length; i++) b[i] = (byte)(v + (i & 0xFF));
        return b;
    }

    private const string Script = """
        filepartload 0x36600000 $(UpgradeImage) 0x1000 0x200
        lz4 -d 0x36600000 0x200 0x30200000 0x400
        setenv filesize 0x400
        sparse_write mmc 0x30200000 super $(filesize)
        filepartload 0x30200000 $(UpgradeImage) 0x2000 0x100
        sparse_write mmc 0x30200000 super $(filesize)
        filepartload 0x30000000 $(UpgradeImage) 0x3000 0x50
        mmc write.p 0x30000000 other $(filesize) 1
        filepartload 0x30000000 $(UpgradeImage) 0x4000 0x60
        sparse_write mmc 0x30000000 cache $(filesize)
        """;

    [Test]
    public void Plan_GroupsInOrderWithFlags()
    {
        var groups = MStarFlashPlan.ParseSparseWrites(Script, 0x10000);
        Assert.AreEqual(2, groups.Count);
        Assert.AreEqual("super", groups[0].Target);
        Assert.AreEqual(2, groups[0].Segments.Count);
        Assert.IsTrue(groups[0].Segments[0].Compressed);
        Assert.AreEqual(0x1000, groups[0].Segments[0].FileOffset);
        Assert.AreEqual(0x200, groups[0].Segments[0].StoredSize);
        Assert.AreEqual(0x400, groups[0].Segments[0].DecompressedSize);
        Assert.IsFalse(groups[0].Segments[1].Compressed);
        Assert.AreEqual("cache", groups[1].Target);
        Assert.AreEqual(1, groups[1].Segments.Count);
    }

    [Test]
    public void Plan_StalePendingNotClaimed()
    {
        // filepartload consumed by a plain write must not leak into a later
        // sparse_write to the same load address.
        const string script = """
            filepartload 0x30000000 $(UpgradeImage) 0x5000 0x70
            mmc write.p 0x30000000 stolen $(filesize) 1
            sparse_write mmc 0x30000000 super $(filesize)
            """;
        var groups = MStarFlashPlan.ParseSparseWrites(script, 0x10000);
        Assert.IsEmpty(groups);
    }

    [Test]
    public void Plan_ErasePreservesPending()
    {
        // Erase carries no data: filepartload, lz4, erase, then sparse_write
        // still form one chain (the observed cache/userdata shape).
        const string script = """
            filepartload 0x30220000 $(UpgradeImage) 0x47aa1000 0x3a4
            lz4 -d 0x30220000 0x3a4 0x30200000 0x110ac
            setenv filesize 0x110ac
            mmc erase.p cache
            sparse_write mmc 0x30200000 cache $(filesize)
            """;
        var groups = MStarFlashPlan.ParseSparseWrites(script, 0x50000000L);
        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual("cache", groups[0].Target);
        Assert.AreEqual(1, groups[0].Segments.Count);
        Assert.IsTrue(groups[0].Segments[0].Compressed);
    }

    [Test]
    public void Plan_Lz4MismatchDropsSegment()
    {
        const string script = """
            filepartload 0x36600000 $(UpgradeImage) 0x1000 0x200
            lz4 -d 0x36000000 0x200 0x30200000 0x400
            sparse_write mmc 0x30200000 super $(filesize)
            """;
        Assert.IsEmpty(MStarFlashPlan.ParseSparseWrites(script, 0x10000));
    }

    [Test]
    public void Plan_OutOfRangeDropped()
    {
        const string script = """
            filepartload 0x36600000 $(UpgradeImage) 0x1000 0x200
            sparse_write mmc 0x36600000 super $(filesize)
            """;
        Assert.IsEmpty(MStarFlashPlan.ParseSparseWrites(script, 0x100));
    }

    [Test]
    public void Plan_RealExcerptShapes()
    {
        // Observable Wanbo script facts (offsets/sizes): addresses and the
        // lz4+sparse_write pairing, plus the raw (uncompressed) super part.
        const string excerpt = """
            filepartload 0x36600000 $(UpgradeImage) 0x8a41000 0x34d9fbe
            lz4 -d 0x36600000 0x34d9fbe 0x30200000 0x63ff158
            setenv filesize 0x63ff158
            sparse_write mmc 0x30200000 super $(filesize)
            filepartload 0x30200000 $(UpgradeImage) 0x1a541000 0x61bb078
            sparse_write mmc 0x30200000 super $(filesize)
            """;
        var groups = MStarFlashPlan.ParseSparseWrites(excerpt, 0x50000000L);
        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual("super", groups[0].Target);
        Assert.AreEqual(2, groups[0].Segments.Count);
        Assert.IsTrue(groups[0].Segments[0].Compressed);
        Assert.AreEqual(0x8A41000L, groups[0].Segments[0].FileOffset);
        Assert.AreEqual(0x63FF158L, groups[0].Segments[0].DecompressedSize);
        Assert.IsFalse(groups[0].Segments[1].Compressed);
    }

    [Test]
    public void Overlay_LastWriteWins()
    {
        // A: raw(0)=A-pattern, fill(1); B overlays block 0, skips the rest.
        var imgA = SparseTestImage(4,
            (0xCAC1, 1, RawBlock(0xA0), 0),
            (0xCAC2, 1, [], 0x11223344),
            (0xCAC3, 2, [], 0));
        var imgB = SparseTestImage(4,
            (0xCAC1, 1, RawBlock(0xB0), 0),
            (0xCAC3, 3, [], 0));
        using var buff = new MemoryDataSourceBuffer("t.bin", default(DataSourceFlags));
        long total = MstarSparseCombiner.Overlay(new[] { imgA, imgB }, buff);
        Assert.AreEqual(4L * Blk, total);
        var ds = buff.ToDataSource();
        var raw = ds.Data.ToArray();
        CollectionAssert.AreEqual(RawBlock(0xB0), raw.AsSpan(0, (int)Blk).ToArray());
        var fill = new byte[Blk];
        for (int i = 0; i < fill.Length; i++) fill[i] = new byte[] { 0x44, 0x33, 0x22, 0x11 }[i % 4];
        CollectionAssert.AreEqual(fill, raw.AsSpan((int)Blk, (int)Blk).ToArray());
        CollectionAssert.AreEqual(new byte[2 * Blk], raw.AsSpan((int)(2 * Blk), (int)(2 * Blk)).ToArray());
    }

    [Test]
    public void Overlay_GeometryMismatchThrows()
    {
        var imgA = SparseTestImage(4, (0xCAC3, 4, [], 0));
        var imgB = SparseTestImage(8, (0xCAC3, 8, [], 0));
        using var buff = new MemoryDataSourceBuffer("t.bin", default(DataSourceFlags));
        Assert.Throws<InvalidDataException>(() => MstarSparseCombiner.Overlay(new[] { imgA, imgB }, buff));
    }

    [Test]
    public void Overlay_CorruptThrows()
    {
        using var buff = new MemoryDataSourceBuffer("t.bin", default(DataSourceFlags));
        Assert.Throws<InvalidDataException>(() => MstarSparseCombiner.Overlay(new[] { new byte[] { 1, 2, 3 } }, buff));
    }

    [Test]
    public void Decode_Lz4RoundTrip()
    {
        var sparse = SparseTestImage(2, (0xCAC1, 1, RawBlock(7), 0), (0xCAC3, 1, [], 0));
        var comp = SparseTests.Lz4EncodeLiteral(sparse);
        var image = new byte[0x1000 + comp.Length + 0x100];
        Array.Copy(comp, 0, image, 0x1000, comp.Length);
        var mem = Mem(image, "inner.bin");
        var seg = new FlashSegment
        {
            FileOffset = 0x1000,
            StoredSize = comp.Length,
            Compressed = true,
            DecompressedSize = sparse.Length,
        };
        CollectionAssert.AreEqual(sparse, MstarSparseCombiner.DecodeSegment(mem, seg));
    }

    [Test]
    public void Decode_ShortOutputThrows()
    {
        var sparse = SparseTestImage(2, (0xCAC1, 1, RawBlock(7), 0), (0xCAC3, 1, [], 0));
        var comp = SparseTests.Lz4EncodeLiteral(sparse);
        var image = new byte[0x1000 + comp.Length];
        Array.Copy(comp, 0, image, 0x1000, comp.Length);
        var mem = Mem(image, "inner.bin");
        var seg = new FlashSegment
        {
            FileOffset = 0x1000,
            StoredSize = comp.Length,
            Compressed = true,
            DecompressedSize = sparse.Length + 16,
        };
        Assert.Throws<InvalidDataException>(() => MstarSparseCombiner.DecodeSegment(mem, seg));
    }

    [Test]
    public void Decode_OutOfRangeThrows()
    {
        var mem = Mem(new byte[0x100], "inner.bin");
        var seg = new FlashSegment { FileOffset = 0x80, StoredSize = 0x100, Compressed = false, DecompressedSize = 0x100 };
        Assert.Throws<InvalidDataException>(() => MstarSparseCombiner.DecodeSegment(mem, seg));
    }
}
