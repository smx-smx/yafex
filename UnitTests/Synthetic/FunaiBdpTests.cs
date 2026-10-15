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
// Synthetic Funai BDP (variant C) tests: a minimal indexed container (plain,
// cmpr and _image_rom partitions) is built in-memory with a throwaway test
// key (never the staging secrets). Fully in-memory, no fixtures.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Funai;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class FunaiBdpTests
{
    // Throwaway test key (4 bytes LE); unrelated to any staging secret.
    private const uint TestKey = 0x0BADF00D;
    private const string TestKeyHex = "0DF0AD0B";

    private static KeysRepository TestKeys() => new KeysRepository(new KeyBundle(new KeyDTO[]
    {
        new KeyCollectionDTO
        {
            Id = FunaiBdpDetector.KeysId,
            Keys = new List<KeyDTO>
            {
                new RawKeyDTO { KeyMaterial = TestKeyHex, Description = "synthetic" },
            },
        },
    }));

    private static byte[] Deflate(byte[] plain)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionMode.Compress))
        {
            z.Write(plain, 0, plain.Length);
        }
        return ms.ToArray();
    }

    private static void WriteName(byte[] buf, int off, int len, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name);
        Array.Copy(bytes, 0, buf, off, Math.Min(bytes.Length, len));
    }

    private static byte[] PlainBlob(string name, byte[] payload)
    {
        var blob = new byte[0x20 + payload.Length];
        WriteName(blob, 0, 0x20, name);
        Array.Copy(payload, 0, blob, 0x20, payload.Length);
        return blob;
    }

    private static byte[] CmprBlob(string name, (uint Mode, byte[] Raw, byte Fill)[] blocks)
    {
        // data_size covers stored stream bytes (raw blocks only); fill
        // blocks emit without consuming the stream.
        uint dataSize = 0;
        foreach (var (mode, raw, _) in blocks)
        {
            if (mode == 0) dataSize += (uint)raw.Length;
        }
        var blob = new List<byte>();
        var head = new byte[0x20];
        WriteName(head, 0, 0x20, name);
        blob.AddRange(head);
        var hdr = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(4, 4), (uint)blocks.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(8, 4), dataSize);
        blob.AddRange(hdr);
        foreach (var (mode, raw, fill) in blocks)
        {
            var d = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(0, 4), (uint)raw.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(4, 4), mode);
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(8, 4), fill);
            blob.AddRange(d);
        }
        foreach (var (mode, raw, fill) in blocks)
        {
            if (mode == 0) blob.AddRange(raw);
        }
        return blob.ToArray();
    }

    private static byte[] ImageRomBlob(string name, byte[][] chunks)
    {
        var comped = chunks.Select(Deflate).ToArray();
        var headerLen = 0x20 + 4 + 4 + 8 + 20 + 12 + 4 + 12;
        uint dataBase = (uint)(headerLen + chunks.Length * 64);
        var blob = new List<byte>();
        var head = new byte[0x20];
        WriteName(head, 0, 0x20, name);
        blob.AddRange(head);
        var hdr = new byte[4 + 4 + 8 + 20 + 12 + 4 + 12];
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(0, 4), (uint)chunks.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(4 + 4 + 8 + 20 + 12, 4), dataBase);
        blob.AddRange(hdr);
        uint running = 0;
        var descs = new List<byte[]>();
        foreach (var comp in comped)
        {
            var d = new byte[64];
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(20 + 20 + 8, 4), running);
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(20 + 20 + 8 + 4, 4), (uint)comp.Length);
            descs.Add(d);
            running += (uint)comp.Length;
        }
        foreach (var d in descs) blob.AddRange(d);
        foreach (var comp in comped) blob.AddRange(comp);
        return blob.ToArray();
    }

    private static byte[] BuildImage(
        (string IndexName, byte[] Blob)[] parts,
        uint key = TestKey,
        int trailing = 0)
    {
        int indexLen = 0x20 + 4 + parts.Length * 44;
        int off = indexLen;
        var entries = new List<(string, int, int)>();
        foreach (var (name, blob) in parts)
        {
            entries.Add((name, off, blob.Length));
            // Blobs are packed back-to-back; the file stays 8-aligned here
            // because every test blob is a multiple of 8 except the checked
            // trailing tail below.
            off += blob.Length;
        }
        var plain = new List<byte>();
        var magic = new byte[16];
        Encoding.ASCII.GetBytes("index_table").CopyTo(magic, 0);
        plain.AddRange(magic);
        plain.AddRange(new byte[16]); // padding / unknown
        var count = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(count, (uint)parts.Length);
        plain.AddRange(count);
        foreach (var (name, o, len) in entries)
        {
            var e = new byte[44];
            WriteName(e, 0, 32, name);
            BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(32, 4), (uint)o);
            BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(36, 4), (uint)len);
            plain.AddRange(e);
        }
        // Pad the index area to the first blob offset (blobs are packed).
        while (plain.Count < entries[0].Item2) plain.Add(0);
        foreach (var (_, blob) in parts) plain.AddRange(blob);
        // Optional trailing passthrough tail (< 8 bytes at EOF).
        for (int i = 0; i < trailing; i++) plain.Add((byte)(0xA0 + i));
        return FunaiCrypto.Encrypt(plain.ToArray(), key);
    }

    private static readonly byte[] KernelPayload = Encoding.ASCII.GetBytes("KERNEL-PAYLOAD-0123456789ABCDEF");
    private static readonly byte[] RawBlock = Encoding.ASCII.GetBytes("HELLO");

    private static byte[] StandardImage(uint key = TestKey) => BuildImage(new[]
    {
        (IndexName: "kernel", Blob: PlainBlob("kernel", KernelPayload)),
        (IndexName: "fe_rom", Blob: CmprBlob("fe_rom",
            new (uint, byte[], byte)[] { (0, RawBlock, 0), (1, new byte[16], 0xFF) })),
        (IndexName: "nand_image_rom", Blob: ImageRomBlob("nand_image_rom",
            new[] { Encoding.ASCII.GetBytes("CHUNK-ONE-0123456789"), Encoding.ASCII.GetBytes("CHUNK-TWO-9876543210") })),
    }, key);

    private static byte[] ExpectedCmpr() =>
        RawBlock.Concat(Enumerable.Repeat((byte)0xFF, 16)).ToArray();

    [Test]
    public void WithKey_Detects95()
    {
        var res = new FunaiBdpDetector(TestKeys(), new Dictionary<string, string>()).Detect(Mem(StandardImage()));
        Assert.AreEqual(95, res.Confidence);
    }

    [Test]
    public void WithoutKeys_Rejects()
    {
        var res = new FunaiBdpDetector(EmptyKeys(), new Dictionary<string, string>()).Detect(Mem(StandardImage()));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void WrongKey_Rejects()
    {
        var keys = new KeysRepository(new KeyBundle(new KeyDTO[]
        {
            new KeyCollectionDTO
            {
                Id = FunaiBdpDetector.KeysId,
                Keys = new List<KeyDTO> { new RawKeyDTO { KeyMaterial = "01020304", Description = "wrong" } },
            },
        }));
        var res = new FunaiBdpDetector(keys, new Dictionary<string, string>()).Detect(Mem(StandardImage()));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Garbage_Rejects()
    {
        var rnd = new byte[256];
        new Random(21).NextBytes(rnd);
        var res = new FunaiBdpDetector(TestKeys(), new Dictionary<string, string>()).Detect(Mem(rnd));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void TruncatedIndex_Rejects()
    {
        var image = StandardImage();
        var cut = image[..64];
        var res = new FunaiBdpDetector(TestKeys(), new Dictionary<string, string>()).Detect(Mem(cut));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void ImplausibleCount_Rejects()
    {
        var parts = new[] { (IndexName: "kernel", Blob: PlainBlob("kernel", KernelPayload)) };
        var image = BuildImage(parts);
        // The count lives in the second half of file block [32..40), i.e.
        // ciphertext bytes 36..39 (first-half bytes decrypt to the block's
        // second half). Corrupt those so the count becomes implausible.
        image[36] ^= 0xFF;
        image[37] ^= 0xFF;
        var res = new FunaiBdpDetector(TestKeys(), new Dictionary<string, string>()).Detect(Mem(image));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Extract_RoundTripsAllKinds()
    {
        var mem = Mem(StandardImage(), "test.bin");
        var det = (FunaiBdpDetectionResult)new FunaiBdpDetector(TestKeys(), new Dictionary<string, string>()).Detect(mem);
        Assert.AreEqual(95, det.Confidence);
        var artifacts = new FunaiBdpExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(3, artifacts.Count);
        Assert.AreEqual("kernel.bin", artifacts[0].Name);
        Assert.AreEqual("fe_rom.bin", artifacts[1].Name);
        Assert.AreEqual("nand_image_rom.bin", artifacts[2].Name);
        CollectionAssert.AreEqual(KernelPayload, artifacts[0].Data.ToArray());
        CollectionAssert.AreEqual(ExpectedCmpr(), artifacts[1].Data.ToArray());
        CollectionAssert.AreEqual(
            Encoding.ASCII.GetBytes("CHUNK-ONE-0123456789CHUNK-TWO-9876543210"),
            artifacts[2].Data.ToArray());
    }

    [Test]
    public void Extract_TrailingTail_PassesThrough()
    {
        var parts = new[] { (IndexName: "kernel", Blob: PlainBlob("kernel", KernelPayload)) };
        var mem = Mem(BuildImage(parts, trailing: 3), "test.bin");
        var det = (FunaiBdpDetectionResult)new FunaiBdpDetector(TestKeys(), new Dictionary<string, string>()).Detect(mem);
        Assert.AreEqual(95, det.Confidence);
        var artifacts = new FunaiBdpExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        CollectionAssert.AreEqual(KernelPayload, artifacts[0].Data.ToArray());
    }

    [Test]
    public void Extract_UnknownCmprMode_Throws()
    {
        var bad = CmprBlob("fe_rom", new (uint, byte[], byte)[] { (2, new byte[4], 0) });
        var mem = Mem(BuildImage(new[] { (IndexName: "fe_rom", Blob: bad) }), "test.bin");
        var det = (FunaiBdpDetectionResult)new FunaiBdpDetector(TestKeys(), new Dictionary<string, string>()).Detect(mem);
        Assert.AreEqual(95, det.Confidence);
        Assert.Throws<InvalidDataException>(() => new FunaiBdpExtractor(det).Extract(mem).ToList());
    }

    [Test]
    public void Extract_PartitionPastEof_Rejects()
    {
        var parts = new[] { (IndexName: "kernel", Blob: PlainBlob("kernel", KernelPayload)) };
        var image = BuildImage(parts);
        // Entry 0 size (transformed bytes 0x48..0x4B) decrypts from
        // ciphertext bytes 0x4C..0x4F; corrupt one so the blob runs past EOF.
        image[0x4C] ^= 0xFF;
        var res = new FunaiBdpDetector(TestKeys(), new Dictionary<string, string>()).Detect(Mem(image));
        Assert.AreEqual(0, res.Confidence);
    }
}
