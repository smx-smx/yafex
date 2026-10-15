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
// Synthetic Android sparse tests: images with every chunk kind are built
// in-memory, plus a literal-only LZ4 encoder for the chained lz4sparse
// expander. No fixtures, no keys.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Sparse;
using Yafex.Fuse;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class SparseTests
{
    private const uint Blk = 4096;

    private sealed class Chunk
    {
        public ushort Type;
        public uint Blocks;
        public byte[] Payload = [];
        public uint Fill;
    }

    private static byte[] BuildImage(Chunk[] chunks, uint totalBlks, uint blkSz = Blk)
    {
        var buf = new List<byte>();
        var hdr = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(0, 4), 0xED26FF3A);
        BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(4, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(8, 2), 28);
        BinaryPrimitives.WriteUInt16LittleEndian(hdr.AsSpan(10, 2), 12);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(12, 4), blkSz);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(16, 4), totalBlks);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(20, 4), (uint)chunks.Length);
        buf.AddRange(hdr);
        foreach (var c in chunks)
        {
            uint totalSz = c.Type switch
            {
                0xCAC1 => 12 + c.Blocks * blkSz,
                0xCAC2 or 0xCAC4 => 16,
                _ => 12,
            };
            var h = new byte[12];
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(0, 2), c.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(4, 4), c.Blocks);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(8, 4), totalSz);
            buf.AddRange(h);
            if (c.Type == 0xCAC1) buf.AddRange(c.Payload);
            else if (c.Type == 0xCAC2 || c.Type == 0xCAC4)
            {
                var f = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(f, c.Fill);
                buf.AddRange(f);
            }
        }
        return buf.ToArray();
    }

    private static byte[] RawOf(byte fill, uint blocks)
    {
        var b = new byte[blocks * Blk];
        for (int i = 0; i < b.Length; i++) b[i] = (byte)(fill + (i & 0xFF));
        return b;
    }

    private static byte[] StandardImage()
    {
        var raw = RawOf(0xA0, 2);
        return BuildImage(new[]
        {
            new Chunk { Type = 0xCAC3, Blocks = 3 },
            new Chunk { Type = 0xCAC1, Blocks = 2, Payload = raw },
            new Chunk { Type = 0xCAC2, Blocks = 1, Fill = 0x11223344 },
            new Chunk { Type = 0xCAC4, Blocks = 0, Fill = 0 },
            new Chunk { Type = 0xCAC3, Blocks = 4 },
        }, totalBlks: 3 + 2 + 1 + 4);
    }

    private static byte[] ExpectedRaw()
    {
        var out_ = new List<byte>();
        out_.AddRange(new byte[3 * Blk]);
        out_.AddRange(RawOf(0xA0, 2));
        // fill word 0x11223344 LE bytes repeated over one block:
        var fill = new List<byte>();
        while (fill.Count < Blk) fill.AddRange(new byte[] { 0x44, 0x33, 0x22, 0x11 });
        out_.AddRange(fill.Take((int)Blk));
        out_.AddRange(new byte[4 * Blk]);
        return out_.ToArray();
    }

    /// <summary>Minimal literal-only LZ4 block encoder (test-local): one
    /// sequence with an extended literal length covering the whole input
    /// (intermediate literal-only sequences would be invalid: every
    /// non-final sequence must end in a match).</summary>
    internal static byte[] Lz4EncodeLiteral(byte[] plain)
    {
        var out_ = new List<byte> { 0xF0 };
        int rest = plain.Length - 15;
        while (rest >= 255) { out_.Add(255); rest -= 255; }
        out_.Add((byte)rest);
        out_.AddRange(plain);
        return out_.ToArray();
    }

    [Test]
    public void Detects90()
    {
        var res = new SparseDetector().Detect(Mem(StandardImage()));
        Assert.AreEqual(90, res.Confidence);
        var layout = ((SparseDetectionResult)res).Layout;
        Assert.AreEqual(10L * Blk, layout.OutputLength);
        Assert.AreEqual(5, layout.Chunks.Count);
    }

    [Test]
    public void Extract_RoundTrips()
    {
        var mem = Mem(StandardImage(), "super.img");
        var det = (SparseDetectionResult)new SparseDetector().Detect(mem);
        var artifacts = new SparseExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("super.raw.bin", artifacts[0].Name);
        CollectionAssert.AreEqual(ExpectedRaw(), artifacts[0].Data.ToArray());
    }

    [Test]
    public void Garbage_Rejects()
    {
        var rnd = new byte[1024];
        new Random(53).NextBytes(rnd);
        Assert.AreEqual(0, new SparseDetector().Detect(Mem(rnd)).Confidence);
    }

    [Test]
    public void TooSmall_Rejects()
    {
        Assert.AreEqual(0, new SparseDetector().Detect(Mem(new byte[27])).Confidence);
    }

    [Test]
    public void BadMagic_Rejects()
    {
        var image = StandardImage();
        image[0] ^= 0xFF;
        Assert.AreEqual(0, new SparseDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void BadVersion_Rejects()
    {
        var image = StandardImage();
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(4, 2), 2);
        Assert.AreEqual(0, new SparseDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void UnknownChunkType_Rejects()
    {
        var image = StandardImage();
        // First chunk type at offset 28 (dontcare 0xCAC3 -> 0xCAC9).
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(28, 2), 0xCAC9);
        Assert.AreEqual(0, new SparseDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void BlockCountMismatch_Rejects()
    {
        var image = StandardImage();
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(16, 4), 999u);
        Assert.AreEqual(0, new SparseDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void TrailingByte_Rejects()
    {
        var image = StandardImage().Concat(new byte[] { 0 }).ToArray();
        Assert.AreEqual(0, new SparseDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void TruncatedPayload_Rejects()
    {
        var image = StandardImage();
        Assert.AreEqual(0, new SparseDetector().Detect(Mem(image[..^100])).Confidence);
    }

    [Test]
    public void Expander_MatchesContainerRejectsGarbage()
    {
        var exp = new SparseExpander();
        Assert.IsTrue(exp.Matches(new MemoryVirtualFile("a.img", StandardImage())));
        var rnd = new byte[2048];
        new Random(7).NextBytes(rnd);
        Assert.IsFalse(exp.Matches(new MemoryVirtualFile("x.bin", rnd)));
        Assert.IsFalse(exp.Matches(new MemoryVirtualFile("short.bin", new byte[16])));
    }

    [Test]
    public void Expander_ListsRawImage()
    {
        var exp = new SparseExpander();
        var node = exp.Expand(new MemoryVirtualFile("a.img", StandardImage()), new List<object>());
        node.EnsureLoaded();
        CollectionAssert.Contains(node.Tree.Keys.ToList(), "a.img.raw");
    }

    [Test]
    public void Expander_RawViewReadsBack()
    {
        var expected = ExpectedRaw();
        var exp = new SparseExpander();
        var keepAlive = new List<object>();
        var raw = exp.ImageFile(new MemoryVirtualFile("a.img", StandardImage()), keepAlive);
        Assert.AreEqual(expected.Length, raw.Length);
        Assert.AreEqual(expected, raw.ReadAvailable());
    }

    [Test]
    public void Lz4Sparse_MatchesAndExpands()
    {
        var comp = Lz4EncodeLiteral(StandardImage());
        var exp = new Lz4SparseExpander();
        var file = new MemoryVirtualFile("super.pak", comp);
        Assert.IsTrue(exp.Matches(file));
        var node = exp.Expand(file, new List<object>());
        node.EnsureLoaded();
        CollectionAssert.Contains(node.Tree.Keys.ToList(), "super.pak.sparse");
    }

    [Test]
    public void Lz4Sparse_ChainsIntoSparse()
    {
        // The decompressed blob must itself satisfy the sparse expander.
        var comp = Lz4EncodeLiteral(StandardImage());
        Assert.IsTrue(Lz4SparseExpander.TryDecompress(
            new MemoryVirtualFile("super.pak", comp), out var image));
        Assert.IsTrue(new SparseExpander().Matches(new MemoryVirtualFile("x", image)));
    }

    [Test]
    public void Lz4Sparse_RejectsGarbageAndPlain()
    {
        var exp = new Lz4SparseExpander();
        var rnd = new byte[4096];
        new Random(11).NextBytes(rnd);
        Assert.IsFalse(exp.Matches(new MemoryVirtualFile("x.bin", rnd)));
        Assert.IsFalse(exp.Matches(new MemoryVirtualFile("short.bin", new byte[16])));
        // Decodable but not sparse.
        var text = Lz4EncodeLiteral(System.Text.Encoding.ASCII.GetBytes(new string('A', 5000)));
        Assert.IsFalse(exp.Matches(new MemoryVirtualFile("t.bin", text)));
    }
}
