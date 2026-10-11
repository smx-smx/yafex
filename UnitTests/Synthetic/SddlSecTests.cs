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
// Synthetic SddlSec tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

using NUnit.Framework;

using Smx.SharpIO;
using Smx.SharpIO.Memory.Buffers;

using Yafex;
using Yafex.FileFormats.SddlSec;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class SddlSecTests
{
    [Test]
    public void Decipher_IsDeterministic()
    {
        var plain = new Memory64<byte>(Encoding.ASCII.GetBytes("HelloSddlSec!!!!!"));
        var a = SddlSecExtractor.Decipher(plain).ToArray();
        var b = SddlSecExtractor.Decipher(plain).ToArray();
        CollectionAssert.AreEqual(a, b);
        Assert.AreEqual(plain.Length, a.Length);
    }

    [Test]
    public void EncipherRoundTripsViaDecipher()
    {
        var plain = Encoding.ASCII.GetBytes("HelloSddlSec!!!!!");
        var back = SddlSecExtractor.Decipher(new Memory64<byte>(SddlEncipher(plain))).ToArray();
        CollectionAssert.AreEqual(plain, back);
    }

    [Test]
    public void DecompressZlib_RoundTrips()
    {
        var raw = Encoding.UTF8.GetBytes(new string('A', 1000));
        // wrap raw deflate in zlib header/adler like DecompressZlib expects (ZLibStream handles it)
        byte[] zlib;
        using (var ms = new MemoryStream())
        {
            using (var zs = new ZLibStream(ms, CompressionMode.Compress))
                zs.Write(raw, 0, raw.Length);
            zlib = ms.ToArray();
        }
        var back = SddlSecExtractor.DecompressZlib(new Memory64<byte>(zlib));
        CollectionAssert.AreEqual(raw, back.ToArray());
    }

    [Test]
    public void SyntheticHeader_Detects100()
    {
        int hdrSize = Marshal.SizeOf<SddlSecHeader>();
        var plain = Zeros(hdrSize + 0x10);
        plain[0] = 0x11; plain[1] = 0x22; plain[2] = 0x33; plain[3] = 0x44;
        WriteAscii(plain, 8, "0001");
        WriteAscii(plain, 12, "0000");
        var enc = SddlEncipher(plain);
        var det = new SddlSecDetector(new Dictionary<string, string>());
        var res = (SddlSecDetectionResult)det.Detect(Mem(enc));
        Assert.AreEqual(100, res.Confidence);
        Assert.NotNull(res.Context);
    }

    [Test]
    public void Garbage_Detects0()
    {
        var det = new SddlSecDetector(new Dictionary<string, string>());
        var res = (SddlSecDetectionResult)det.Detect(Mem(Zeros(64)));
        Assert.AreEqual(0, res.Confidence);
    }

    // Builds one old-framed entry (name[12] + size[12] header, N-field +
    // display run + NoPadding blob) sealed with the given random AES key.
    // No real key material: every key here is random per run.
    private static byte[] BuildOldEntry(byte[] key, byte[] iv, byte[] payload, out uint fileSize)
    {
        var inner = new List<byte>();
        inner.AddRange(Encoding.ASCII.GetBytes("0004"));
        inner.AddRange(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x11, 0x22 });
        inner.AddRange(Encoding.ASCII.GetBytes(payload.Length.ToString("D12")));
        inner.AddRange(payload);
        while (inner.Count % 16 != 0) inner.Add(0);
        var encInner = AesCbcEncryptNone(inner.ToArray(), key, iv);

        var display = Encoding.ASCII.GetBytes("DISPLAY!");
        var blob = new List<byte>();
        blob.AddRange(Encoding.ASCII.GetBytes(display.Length.ToString("D4")));
        blob.AddRange(display);
        blob.AddRange(encInner);
        fileSize = (uint)blob.Count;

        var header = Zeros(32);
        WriteAscii(header, 0, "SDIT.FDI");
        var sizeDigits = fileSize.ToString();
        WriteAscii(header, 12, sizeDigits);
        for (int i = 12 + sizeDigits.Length; i < 24; i++) header[i] = (byte)' ';
        var encHeader = AesCbcEncryptNone(header, key, iv);

        var entry = new List<byte>(encHeader);
        entry.AddRange(blob);
        return entry.ToArray();
    }

    private static SddlSecExtractor OldKeyExtractor(byte[] key, byte[] iv)
    {
        var keys = new KeysRepository(new KeyBundle(new KeyDTO[]
        {
            new RawKeyDTO
            {
                Id = "sddl-sec-old-aes-2009",
                KeyMaterial = Convert.ToHexString(key),
                KeyIV = Convert.ToHexString(iv),
            },
        }));
        return new SddlSecExtractor(new SddlSecContext(), keys);
    }

    [Test]
    public void OldEntry_RoundTripsThroughTrial()
    {
        var key = RandomNumberGenerator.GetBytes(16);
        var iv = RandomNumberGenerator.GetBytes(16);
        var payload = Encoding.ASCII.GetBytes("old-sddl-payload-1234");

        var blob = BuildOldEntry(key, iv, payload, out uint fileSize);
        var extractor = OldKeyExtractor(key, iv);
        var st = new SpanStream(new Memory64<byte>(blob));
        var (entry, data) = extractor.GetFile(st);

        Assert.AreEqual("SDIT.FDI", entry.FileName);
        Assert.AreEqual(fileSize, entry.FileSize);
        Assert.IsTrue(extractor.IsOldFormat);
        CollectionAssert.AreEqual(payload, data.ToArray());
    }

    [Test]
    public void OldEntry_WrongKeyBundle_ThrowsInvalidData()
    {
        var key = RandomNumberGenerator.GetBytes(16);
        var iv = RandomNumberGenerator.GetBytes(16);
        var payload = Encoding.ASCII.GetBytes("old-sddl-payload-1234");

        var blob = BuildOldEntry(key, iv, payload, out _);
        var extractor = OldKeyExtractor(RandomNumberGenerator.GetBytes(16), RandomNumberGenerator.GetBytes(16));
        var st = new SpanStream(new Memory64<byte>(blob));
        Assert.Throws<InvalidDataException>(() => extractor.GetFile(st));
    }
}
