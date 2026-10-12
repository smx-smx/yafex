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
using System.Linq;
using System.Runtime.CompilerServices;
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

    // SDDL integers are big-endian (see 2013.SEC: SDIT bytes 00 53 are
    // 83 segments, matching the 85-file header count as 83+1+1). Guards
    // the SharpIO endianness behavior the whole format depends on.
    [Test]
    public void SditSegmentCount_IsBigEndian()
    {
        Assert.AreEqual(44, Unsafe.SizeOf<SditModuleEntry>());
        var buf = Zeros(8 + 4 + 44);
        buf[0] = 0x11; buf[1] = 0x22; buf[2] = 0x33; buf[3] = 0x44;
        buf[4] = 1; // one group
        buf[8] = 0; buf[9] = 1; // group 0, one entry
        int e = 12;
        buf[e + 24] = 1; // module type
        buf[e + 26] = 0x00; buf[e + 27] = 0x53; // BE 83
        WriteAscii(buf, e + 36, "PEAKS");
        var ext = new SddlSecExtractor(new SddlSecContext(), EmptyKeys());
        var modules = ext.ParseSditToModules(new Memory64<byte>(buf));
        Assert.AreEqual(1, modules.Count);
        Assert.AreEqual("PEAKS", modules[0].ModuleName);
        Assert.AreEqual(83, modules[0].SegmentCount);
    }

    private static byte[] AesCbcEncryptPkcs7(byte[] plain, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var enc = aes.CreateEncryptor();
        return enc.TransformFinalBlock(plain, 0, plain.Length);
    }

    // Builds one new-framed entry (name[12] + size[12] header, AES-CBC-PKCS7
    // payload) sealed with the given random AES key. No real key material.
    private static byte[] BuildNewEntry(byte[] key, byte[] iv, string name, byte[] payload, out uint fileSize)
    {
        var encPayload = AesCbcEncryptPkcs7(payload, key, iv);
        fileSize = (uint)encPayload.Length;
        var header = Zeros(24);
        WriteAscii(header, 0, name);
        WriteAscii(header, 12, fileSize.ToString());
        var encHeader = AesCbcEncryptPkcs7(header, key, iv);
        var entry = new List<byte>(encHeader);
        entry.AddRange(encPayload);
        return entry.ToArray();
    }

    // Full production wiring for Extract-path tests: detect first so the
    // context carries the deciphered header, like FileFormatRepository flow.
    private static SddlSecExtractor NewKeyExtractorFor(byte[] key, byte[] iv, byte[] blob, out MemoryDataSource mem)
    {
        var keys = new KeysRepository(new KeyBundle(new KeyDTO[]
        {
            new Aes128Cbc
            {
                Id = "sddl-sec-key",
                KeyMaterial = Convert.ToHexString(key),
                KeyIV = Convert.ToHexString(iv),
            },
        }));
        mem = Mem(blob);
        var det = (SddlSecDetectionResult)new SddlSecAddon(keys)
            .CreateDetector(new Dictionary<string, string>()).Detect(mem);
        Assert.AreEqual(100, det.Confidence);
        return new SddlSecExtractor(det.Context!, keys);
    }

    private static byte[] BuildModuleHeader(uint storedBe, byte flags = 0)
    {
        Assert.AreEqual(48, Unsafe.SizeOf<ModuleHeader>());
        var buf = Zeros(48);
        buf[0] = 0x11; buf[1] = 0x22; buf[2] = 0x33; buf[3] = 0x44;
        buf[34] = flags;
        buf[36] = (byte)(storedBe >> 24);
        buf[37] = (byte)(storedBe >> 16);
        buf[38] = (byte)(storedBe >> 8);
        buf[39] = (byte)storedBe;
        return buf;
    }

    // Builds a minimal new-format container: obfuscated header + SDIT.FDI
    // describing one single-segment module + its segment entry.
    private static byte[] BuildNewContainer(byte[] key, byte[] iv, string module, byte[] segPayload)
    {
        var sdit = new List<byte>();
        sdit.AddRange(new byte[] { 0x11, 0x22, 0x33, 0x44, 1, 0, 0, 0 });
        sdit.AddRange(new byte[] { 0, 1, 0, 0 });
        var entry = Zeros(44);
        entry[24] = 1;
        entry[26] = 0x00; entry[27] = 0x01; // BE 1 segment
        WriteAscii(entry, 36, module);
        sdit.AddRange(entry);
        var sditEntry = BuildNewEntry(key, iv, "SDIT.FDI", sdit.ToArray(), out _);
        var segEntry = BuildNewEntry(key, iv, module + ".F00", segPayload, out _);
        var header = Zeros(Unsafe.SizeOf<SddlSecHeader>());
        header[0] = 0x11; header[1] = 0x22; header[2] = 0x33; header[3] = 0x44;
        WriteAscii(header, 8, "0000");
        WriteAscii(header, 12, "0001");
        var blob = new List<byte>(SddlEncipher(header));
        blob.AddRange(sditEntry);
        blob.AddRange(segEntry);
        return blob.ToArray();
    }

    [Test]
    public void NewSegment_OversizeStored_ThrowsInvalidData()
    {
        var key = RandomNumberGenerator.GetBytes(16);
        var iv = RandomNumberGenerator.GetBytes(16);
        // valid magic, plain flags, stored size far past the 16 payload bytes
        var seg = new List<byte>(BuildModuleHeader(0x01000000));
        seg.AddRange(Zeros(16));
        var blob = BuildNewContainer(key, iv, "TST", seg.ToArray());
        var extractor = NewKeyExtractorFor(key, iv, blob, out var mem);
        var ex = Assert.Throws<InvalidDataException>(() => extractor.Extract(mem).ToList());
        StringAssert.Contains("stored size", ex!.Message);
    }

    [Test]
    public void NewSegment_OversizeContent_ThrowsInvalidData()
    {
        var key = RandomNumberGenerator.GetBytes(16);
        var iv = RandomNumberGenerator.GetBytes(16);
        // stored size covers the 32 content bytes; the content header
        // inside claims 16MB from offset 0
        var content = Zeros(32);
        content[9] = 0x01; content[10] = 0x00; content[11] = 0x00; content[12] = 0x00;
        var seg = new List<byte>(BuildModuleHeader(32));
        seg.AddRange(content);
        var blob = BuildNewContainer(key, iv, "TST", seg.ToArray());
        var extractor = NewKeyExtractorFor(key, iv, blob, out var mem);
        var ex = Assert.Throws<InvalidDataException>(() => extractor.Extract(mem).ToList());
        StringAssert.Contains("content range", ex!.Message);
    }
}
