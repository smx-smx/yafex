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
// Synthetic CDTM (Linksys Titan) tests: a minimal header with two entries
// is built in-memory. Entry/trailer checksums are stored but never verified
// (unidentified algorithm); payloads are opaque blobs. No fixtures.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Cdtm;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class CdtmTests
{
    private static byte[] BuildImage(
        (uint Idx, string Name, byte[] Blob)[] entries,
        uint flags = 0xFFFFFFFF,
        string product = "CYWM",
        string fileName = "wrt-11.1.1-r0707.img",
        bool trailer = true)
    {
        const int tableBase = 0xBC;
        const int tableOff = 0xB0;
        const int entrySize = 40;
        long blobAt = 0x400;
        var blobs = new List<(long Off, byte[] Data)>();
        foreach (var (_, _, blob) in entries)
        {
            blobs.Add((blobAt, blob));
            blobAt += blob.Length;
        }
        long total = blobAt + (trailer ? 8 : 0);
        var image = new byte[total];
        Encoding.ASCII.GetBytes("CDTM").CopyTo(image, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(4, 4), 0x00010000);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(8, 4), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(12, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(16, 4), (uint)(tableBase + entries.Length * entrySize));
        Encoding.ASCII.GetBytes(product).CopyTo(image, 0x14);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x18, 4), 0x10203040);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x1C, 4), 0x0B010000);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x20, 4), (uint)(trailer ? total - 8 : total));
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x24, 4), 0x30);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x28, 4), (uint)tableOff);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x2C, 4), 0xFFFFFFFF);
        Encoding.ASCII.GetBytes(fileName).CopyTo(image, 0x70);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(tableOff, 4), (uint)entries.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(tableOff + 4, 4), entrySize);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(tableOff + 8, 4), (uint)tableBase);
        for (int i = 0; i < entries.Length; i++)
        {
            int baseOff = tableBase + i * entrySize;
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(baseOff, 4), (uint)blobs[i].Off);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(baseOff + 4, 4), (uint)blobs[i].Data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(baseOff + 8, 4), (uint)blobs[i].Data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(baseOff + 12, 4), 0xFFFFFFFF);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(baseOff + 16, 4), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(baseOff + 20, 4), entries[i].Idx);
            Encoding.ASCII.GetBytes(entries[i].Name).CopyTo(image, baseOff + 24);
        }
        foreach (var (off, blob) in blobs)
        {
            Array.Copy(blob, 0, image, off, blob.Length);
        }
        if (trailer)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(image.Length - 8, 4), 0xC453DE23);
            BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(image.Length - 4, 4),
                Yafex.FileFormats.Cdtm.CdtmChecksum.Compute(image.AsSpan(0, image.Length - 8)));
        }
        return image;
    }

    private static readonly byte[] Kernel = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
    private static readonly byte[] Root = Enumerable.Range(0, 100).Select(i => (byte)(0xFF - i)).ToArray();

    private static byte[] StandardImage() => BuildImage(new[]
    {
        (Idx: 1u, Name: "kernel", Blob: Kernel),
        (Idx: 2u, Name: "root", Blob: Root),
    });

    [Test]
    public void Detects90_WithEntries()
    {
        var res = new CdtmDetector().Detect(Mem(StandardImage()));
        Assert.AreEqual(90, res.Confidence);
        var det = (CdtmDetectionResult)res;
        Assert.AreEqual("wrt-11.1.1-r0707.img", det.FileName);
        Assert.AreEqual(2, det.Entries.Count);
        Assert.AreEqual("kernel", det.Entries[0].Name);
        Assert.AreEqual(0x400, det.Entries[0].Offset);
        Assert.AreEqual(256, det.Entries[0].Size);
        Assert.AreEqual("root", det.Entries[1].Name);
    }

    [Test]
    public void BadFlags_Rejects()
    {
        var image = BuildImage(new[] { (1u, "kernel", Kernel) }, flags: 0x12345678);
        Assert.AreEqual(0, new CdtmDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void Garbage_Rejects()
    {
        var rnd = new byte[2048];
        new Random(61).NextBytes(rnd);
        Assert.AreEqual(0, new CdtmDetector().Detect(Mem(rnd)).Confidence);
    }

    [Test]
    public void TooSmall_Rejects()
    {
        Assert.AreEqual(0, new CdtmDetector().Detect(Mem(new byte[100])).Confidence);
    }

    [Test]
    public void BadMagic_Rejects()
    {
        var image = StandardImage();
        image[0] ^= 0xFF;
        Assert.AreEqual(0, new CdtmDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void BadVersion_Rejects()
    {
        var image = StandardImage();
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(12, 4), 2u);
        Assert.AreEqual(0, new CdtmDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void BadProductId_Rejects()
    {
        var image = StandardImage();
        image[0x14] = 0x01;
        Assert.AreEqual(0, new CdtmDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void BadTrailerMagic_Rejects()
    {
        var image = StandardImage();
        image[image.Length - 8] ^= 0xFF;
        Assert.AreEqual(0, new CdtmDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void ZeroCount_Rejects()
    {
        var image = BuildImage([]);
        Assert.AreEqual(0, new CdtmDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void OverlappingEntries_Reject()
    {
        // Hand-built overlap: second entry starts inside the first blob.
        var image = StandardImage();
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0xBC + 40 + 4, 4), 0x410u);
        Assert.AreEqual(0, new CdtmDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void EntryPastTrailer_Rejects()
    {
        var image = StandardImage();
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0xBC + 4, 4), 0x10000u);
        Assert.AreEqual(0, new CdtmDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void Truncated_Rejects()
    {
        var image = StandardImage();
        Assert.AreEqual(0, new CdtmDetector().Detect(Mem(image[..^10])).Confidence);
    }

    [Test]
    public void Extract_RoundTrips()
    {
        var mem = Mem(StandardImage(), "wrt.img");
        var det = (CdtmDetectionResult)new CdtmDetector().Detect(mem);
        Assert.AreEqual(90, det.Confidence);
        var artifacts = new CdtmExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(2, artifacts.Count);
        Assert.AreEqual("kernel.bin", artifacts[0].Name);
        Assert.AreEqual("root.bin", artifacts[1].Name);
        CollectionAssert.AreEqual(Kernel, artifacts[0].Data.ToArray());
        CollectionAssert.AreEqual(Root, artifacts[1].Data.ToArray());
    }

    [Test]
    public void Checksum_GoldenVectors()
    {
        Assert.AreEqual(0xFFFFFFFFu, Yafex.FileFormats.Cdtm.CdtmChecksum.Compute([]));
        Assert.AreEqual(0x377A6011u, Yafex.FileFormats.Cdtm.CdtmChecksum.Compute("123456789"u8.ToArray()));
        Assert.AreEqual(0x27CFA612u, Yafex.FileFormats.Cdtm.CdtmChecksum.Compute("kernel"u8.ToArray()));
        Assert.AreEqual(0xCE1A9ADEu, Yafex.FileFormats.Cdtm.CdtmChecksum.Compute(Enumerable.Repeat((byte)0xAA, 256).ToArray()));
    }

    [Test]
    public void CorruptTrailerChecksum_Rejects()
    {
        var image = StandardImage();
        image[image.Length - 1] ^= 0xFF;
        Assert.AreEqual(0, new CdtmDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void FlashDirect_NoTrailer_Detects()
    {
        var payloads = new[] { (1u, "kernel", Kernel), (2u, "root", Root) };
        var web = BuildImage(payloads);
        // Same bytes minus the trailer, flash flags, file size covering all.
        var image = web[..^8].ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(8, 4), 0x17FFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x20, 4), (uint)image.Length);
        Assert.AreEqual(90, new CdtmDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void FileSizeMismatch_Rejects()
    {
        var image = StandardImage();
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x20, 4), 1234u);
        Assert.AreEqual(0, new CdtmDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void TableOffsetMismatch_Rejects()
    {
        var image = StandardImage();
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(0x28, 4), 0xC0u);
        Assert.AreEqual(0, new CdtmDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void FlashFlags_WithTrailer_Rejects()
    {
        // Inconsistent combination: flash flags but a web trailer present.
        var image = StandardImage();
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(8, 4), 0x17FFFFFF);
        Assert.AreEqual(0, new CdtmDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void Vfs_ListsEntries()
    {
        var mem = Mem(StandardImage(), "wrt.img");
        var addon = new CdtmAddon();
        var det = addon.CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var text = ListTree(addon, det, mem);
        StringAssert.Contains("kernel.bin", text);
        StringAssert.Contains("root.bin", text);
    }
}
