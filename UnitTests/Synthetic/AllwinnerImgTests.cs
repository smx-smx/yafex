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
// Synthetic AllwinnerImg tests: a tiny v1 image is built in-memory with the
// module's own crypto, then detected, listed and extracted. Fully in-memory.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.AllwinnerImg;

namespace UnitTests;

[TestFixture]
public class AllwinnerImgTests
{
    private static void PutU32(byte[] buf, int off, uint v) =>
        BitConverter.GetBytes(v).CopyTo(buf, off);

    private static void PutAscii(byte[] buf, int off, string s, int len)
    {
        var b = Encoding.ASCII.GetBytes(s);
        Array.Copy(b, 0, buf, off, Math.Min(b.Length, len));
    }

    // header + 2 entries + 2 payloads, all RC6-encrypted with region keys
    private static byte[] TinyImage(out byte[] pay0, out byte[] pay1)
    {
        pay0 = new byte[48];
        pay1 = new byte[64];
        for (int i = 0; i < pay0.Length; i++) pay0[i] = (byte)(i + 1);
        for (int i = 0; i < pay1.Length; i++) pay1[i] = (byte)(0x80 + i);

        var head = new byte[1024];
        PutAscii(head, 0, "IMAGEWTY", 8);
        PutU32(head, 8, 0x100);
        PutU32(head, 12, 80);
        PutU32(head, 32 + 6 * 4, 2); // num_files
        var encHead = AwCrypto.Encrypt(head, AwCrypto.HeaderKey());

        var table = new byte[2048];
        // entry 0: SYS_CONFIG
        PutU32(table, 0, 256);
        PutU32(table, 4, 1024);
        PutAscii(table, 8, "COMMON", 8);
        PutAscii(table, 16, "SYS_CONFIG", 16);
        PutU32(table, 32 + 4, 48);   // stored
        PutU32(table, 32 + 8, 48);   // original
        PutU32(table, 32 + 12, 3072);// offset
        PutAscii(table, 32 + 20, "sys_config.fex", 256);
        // entry 1: ROOTFS
        int b = 1024;
        PutU32(table, b, 256);
        PutU32(table, b + 4, 1024);
        PutAscii(table, b + 8, "RFSFAT16", 8);
        PutAscii(table, b + 16, "ROOTFS", 16);
        PutU32(table, b + 32 + 4, 64);
        PutU32(table, b + 32 + 8, 64);
        PutU32(table, b + 32 + 12, 3120);
        PutAscii(table, b + 32 + 20, "rootfs.fex", 256);
        var encTable = AwCrypto.Encrypt(table, AwCrypto.EntriesKey());

        var img = new byte[3120 + 64];
        Array.Copy(encHead, 0, img, 0, 1024);
        Array.Copy(encTable, 0, img, 1024, 2048);
        Array.Copy(AwCrypto.Encrypt(pay0, AwCrypto.PayloadKey()), 0, img, 3072, 48);
        Array.Copy(AwCrypto.Encrypt(pay1, AwCrypto.PayloadKey()), 0, img, 3120, 64);
        return img;
    }

    [Test]
    public void Detect_FindsSyntheticImage()
    {
        var img = TinyImage(out _, out _);
        var ds = new MemoryDataSource(img) { Name = "tiny.img" };
        var det = new AllwinnerImgDetector();
        var res = (AllwinnerImgDetectionResult)det.Detect(ds);
        Assert.AreEqual(100, res.Confidence);
        Assert.IsNotNull(res.Context);
        Assert.AreEqual(2u, res.Context!.NumFiles);
    }

    [Test]
    public void Detect_RejectsRandom()
    {
        var rnd = new byte[4096];
        for (int i = 0; i < rnd.Length; i++) rnd[i] = (byte)((i * 37 + 11) & 0xFF);
        var ds = new MemoryDataSource(rnd) { Name = "rnd.bin" };
        var res = (AllwinnerImgDetectionResult)new AllwinnerImgDetector().Detect(ds);
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Extract_RoundtripsPayloads()
    {
        var img = TinyImage(out var pay0, out var pay1);
        var ds = new MemoryDataSource(img) { Name = "tiny.img" };
        ds.AddMetadata(new Yafex.Metadata.BaseDirectoryPath("tiny"));
        var det = (AllwinnerImgDetectionResult)new AllwinnerImgDetector().Detect(ds);
        var ext = new AllwinnerImgExtractor(det);
        var names = new List<string>();
        var blobs = new List<byte[]>();
        foreach (var art in ext.Extract(ds))
        {
            names.Add(art.Name ?? "?");
            blobs.Add(art.Data.ToArray());
        }
        Assert.AreEqual(2, names.Count);
        Assert.IsTrue(names[0].EndsWith("sys_config.fex"));
        Assert.IsTrue(names[1].EndsWith("rootfs.fex"));
        Assert.IsTrue(blobs[0].AsSpan().SequenceEqual(pay0));
        Assert.IsTrue(blobs[1].AsSpan().SequenceEqual(pay1));
    }

    [Test]
    public void List_ShowsEntries()
    {
        var img = TinyImage(out _, out _);
        var ds = new MemoryDataSource(img) { Name = "tiny.img" };
        var det = (AllwinnerImgDetectionResult)new AllwinnerImgDetector().Detect(ds);
        var lister = new AllwinnerImgLister(det);
        using var sw = new StringWriter();
        lister.List(ds, det, sw);
        var text = sw.ToString();
        Assert.IsTrue(text.Contains("SYS_CONFIG"));
        Assert.IsTrue(text.Contains("ROOTFS"));
    }
}
