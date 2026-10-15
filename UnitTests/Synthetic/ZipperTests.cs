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
// Synthetic zipper tests: a minimal header (duplicated magic blocks plus a
// name table) with fake squashfs payloads is built in-memory. Payloads carry
// only a valid superblock head (magic, version, bytes_used); full filesystem
// parsing stays covered by the squashfs suites. No fixtures, no keys.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Zipper;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class ZipperTests
{
    private const long HeaderSize = 0x100000;

    private static byte[] FakeSquash(int length, byte fill)
    {
        var img = new byte[length];
        for (int i = 0; i < length; i++) img[i] = fill;
        BinaryPrimitives.WriteUInt32LittleEndian(img.AsSpan(0, 4), 0x73717368);
        BinaryPrimitives.WriteUInt32LittleEndian(img.AsSpan(12, 4), 4096);
        BinaryPrimitives.WriteUInt16LittleEndian(img.AsSpan(28, 2), 4);
        BinaryPrimitives.WriteInt64LittleEndian(img.AsSpan(40, 8), length);
        return img;
    }

    private static byte[] PlaBlock(string[] names)
    {
        var blk = new byte[4096];
        Encoding.ASCII.GetBytes("0PLA\n\0\0\0").CopyTo(blk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(blk.AsSpan(8, 4), 128);
        for (int i = 0; i < 32; i++) blk[12 + i] = (byte)(0xA0 + i);
        BinaryPrimitives.WriteUInt32LittleEndian(blk.AsSpan(84, 4), (uint)names.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(blk.AsSpan(88, 4), 52);
        BinaryPrimitives.WriteUInt32LittleEndian(blk.AsSpan(92, 4), (uint)(names.Length * 52));
        for (int i = 0; i < names.Length; i++)
        {
            int baseOff = 96 + i * 52;
            BinaryPrimitives.WriteUInt32LittleEndian(blk.AsSpan(baseOff, 4), (uint)i);
            var nb = Encoding.ASCII.GetBytes(names[i]);
            Array.Copy(nb, 0, blk, baseOff + 32, Math.Min(nb.Length, 20));
        }
        return blk;
    }

    private static byte[] BuildImage(string[] names, byte[][] payloads, int gap = 0)
    {
        var buf = new byte[HeaderSize + payloads.Sum(p => p.Length) + gap * Math.Max(0, payloads.Length - 1)];
        var g = new byte[64];
        Encoding.ASCII.GetBytes("gDla4\0\0\0").CopyTo(g, 0);
        for (int i = 8; i < 64; i++) g[i] = (byte)i;
        Array.Copy(g, 0, buf, 0x1000, 64);
        Array.Copy(g, 0, buf, 0x2000, 64);
        var pla = PlaBlock(names);
        foreach (long off in new long[] { 0x3000, 0x13000, 0x23000, 0x33000 })
        {
            Array.Copy(pla, 0, buf, off, pla.Length);
        }
        long pos = HeaderSize;
        for (int i = 0; i < payloads.Length; i++)
        {
            Array.Copy(payloads[i], 0, buf, pos, payloads[i].Length);
            pos += payloads[i].Length + (i + 1 < payloads.Length ? gap : 0);
        }
        return buf;
    }

    private static byte[] StandardImage() => BuildImage(
        new[] { "rootfs", "fonts" },
        new[] { FakeSquash(512, 0x11), FakeSquash(300, 0x22) },
        gap: 64);

    [Test]
    public void Detects96_WithTableNames()
    {
        var res = new ZipperDetector().Detect(Mem(StandardImage()));
        Assert.AreEqual(96, res.Confidence);
        var cuts = ((ZipperDetectionResult)res).Cuts;
        Assert.AreEqual(2, cuts.Count);
        Assert.AreEqual("rootfs", cuts[0].Name);
        Assert.AreEqual("fonts", cuts[1].Name);
        Assert.AreEqual(HeaderSize, cuts[0].Offset);
        Assert.AreEqual(512, cuts[0].Size);
        Assert.AreEqual(HeaderSize + 512 + 64, cuts[1].Offset);
        Assert.AreEqual(300, cuts[1].Size);
    }

    [Test]
    public void NameCountMismatch_FallsBackToNumbered()
    {
        var image = BuildImage(
            new[] { "only" },
            new[] { FakeSquash(256, 0x11), FakeSquash(256, 0x22) });
        var res = (ZipperDetectionResult)new ZipperDetector().Detect(Mem(image));
        Assert.AreEqual(96, res.Confidence);
        Assert.AreEqual("part-0", res.Cuts[0].Name);
        Assert.AreEqual("part-1", res.Cuts[1].Name);
    }

    [Test]
    public void Garbage_Rejects()
    {
        var rnd = new byte[0x100100];
        new Random(41).NextBytes(rnd);
        Assert.AreEqual(0, new ZipperDetector().Detect(Mem(rnd)).Confidence);
    }

    [Test]
    public void TooSmall_Rejects()
    {
        Assert.AreEqual(0, new ZipperDetector().Detect(Mem(new byte[100])).Confidence);
    }

    [Test]
    public void BadMagic_Rejects()
    {
        var image = StandardImage();
        image[0x1000] ^= 0xFF;
        Assert.AreEqual(0, new ZipperDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void NonZeroPrefix_Rejects()
    {
        var image = StandardImage();
        image[0x500] = 1;
        Assert.AreEqual(0, new ZipperDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void GCopyMismatch_Rejects()
    {
        var image = StandardImage();
        image[0x2000 + 10] ^= 0xFF;
        Assert.AreEqual(0, new ZipperDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void PlaCopyMismatch_Rejects()
    {
        var image = StandardImage();
        image[0x23000 + 100] ^= 0xFF;
        Assert.AreEqual(0, new ZipperDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void NoPayloadAtBase_Rejects()
    {
        var image = StandardImage();
        for (long i = HeaderSize; i < HeaderSize + 512; i++) image[i] = 0;
        Assert.AreEqual(0, new ZipperDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void BadSquashVersion_Rejects()
    {
        var image = StandardImage();
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan((int)HeaderSize + 28, 2), 3);
        Assert.AreEqual(0, new ZipperDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void BytesUsedPastEof_Rejects()
    {
        var image = StandardImage();
        BinaryPrimitives.WriteInt64LittleEndian(image.AsSpan((int)HeaderSize + 40, 8), image.Length + 1000L);
        Assert.AreEqual(0, new ZipperDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void TruncatedHeader_Rejects()
    {
        var image = StandardImage();
        Assert.AreEqual(0, new ZipperDetector().Detect(Mem(image[..0x20000])).Confidence);
    }

    [Test]
    public void StrayMagicInsideData_Skipped()
    {
        // A bogus hsqs (bad version) embedded in a gap must not abort the split.
        var first = FakeSquash(512, 0x11);
        var second = FakeSquash(300, 0x22);
        var gap = new byte[128];
        Encoding.ASCII.GetBytes("hsqs").CopyTo(gap, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(gap.AsSpan(28, 2), 3); // bad major
        var buf = new byte[HeaderSize + first.Length + gap.Length + second.Length];
        var g = new byte[64];
        Encoding.ASCII.GetBytes("gDla4\0\0\0").CopyTo(g, 0);
        Array.Copy(g, 0, buf, 0x1000, 64);
        Array.Copy(g, 0, buf, 0x2000, 64);
        var pla = PlaBlock(new[] { "a", "b" });
        foreach (long off in new long[] { 0x3000, 0x13000, 0x23000, 0x33000 })
        {
            Array.Copy(pla, 0, buf, off, pla.Length);
        }
        Array.Copy(first, 0, buf, HeaderSize, first.Length);
        Array.Copy(gap, 0, buf, HeaderSize + first.Length, gap.Length);
        Array.Copy(second, 0, buf, HeaderSize + first.Length + gap.Length, second.Length);
        var res = (ZipperDetectionResult)new ZipperDetector().Detect(Mem(buf));
        Assert.AreEqual(96, res.Confidence);
        Assert.AreEqual(2, res.Cuts.Count);
        Assert.AreEqual("a", res.Cuts[0].Name);
        Assert.AreEqual("b", res.Cuts[1].Name);
        Assert.AreEqual(512, res.Cuts[0].Size);
        Assert.AreEqual(300, res.Cuts[1].Size);
    }

    [Test]
    public void Extract_RoundTrips()
    {
        var payloads = new[] { FakeSquash(512, 0x11), FakeSquash(300, 0x22) };
        var mem = Mem(BuildImage(new[] { "rootfs", "fonts" }, payloads, gap: 64), "super.pak");
        var det = (ZipperDetectionResult)new ZipperDetector().Detect(mem);
        Assert.AreEqual(96, det.Confidence);
        var artifacts = new ZipperExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(2, artifacts.Count);
        Assert.AreEqual("rootfs.bin", artifacts[0].Name);
        Assert.AreEqual("fonts.bin", artifacts[1].Name);
        CollectionAssert.AreEqual(payloads[0], artifacts[0].Data.ToArray());
        CollectionAssert.AreEqual(payloads[1], artifacts[1].Data.ToArray());
    }
}
