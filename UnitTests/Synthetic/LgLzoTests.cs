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
// Synthetic LG-LZO tests: streams are built in-memory (stored blocks need no
// encoder; the single LZO1X vector below was produced once from a known 256
// byte "ABCD" pattern with the system LZO library). Fully in-memory.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Lzo;

namespace UnitTests;

[TestFixture]
public class LgLzoTests
{
    // LZO1X ("ABCD" x64, 256 bytes) -> 38 bytes, generated once via lzo1x_1_compress
    private const string LzoAbcdHex =
        "05414243444142434420c31c0000024142434441424344414243444142434441424344110000";

    private static void PutU32Be(List<byte> b, uint v)
    {
        b.Add((byte)(v >> 24));
        b.Add((byte)(v >> 16));
        b.Add((byte)(v >> 8));
        b.Add((byte)v);
    }

    private static byte[] BuildStream(uint flags, byte level, uint blockSize,
        List<(byte[] Payload, bool Stored, uint OutLen)> blocks, uint? checksumOverride = null,
        bool appendChecksum = true)
    {
        var b = new List<byte>();
        b.AddRange(LgLzo.Magic);
        uint total = 0;
        foreach (var blk in blocks) total += blk.OutLen;
        PutU32Be(b, total);
        PutU32Be(b, flags);
        b.Add(1); // method = LZO1X
        b.Add(level);
        PutU32Be(b, blockSize);
        foreach (var blk in blocks)
        {
            PutU32Be(b, blk.OutLen);
            PutU32Be(b, blk.Stored ? blk.OutLen : (uint)blk.Payload.Length);
            b.AddRange(blk.Payload);
        }
        PutU32Be(b, 0); // end marker
        if ((flags & 1) != 0 && appendChecksum)
        {
            if (checksumOverride.HasValue)
            {
                PutU32Be(b, checksumOverride.Value);
            }
            else
            {
                var plain = new List<byte>();
                foreach (var blk in blocks)
                {
                    if (blk.Stored) plain.AddRange(blk.Payload);
                    else Assert.Fail("checksum auto-compute needs decoded bytes");
                }
                PutU32Be(b, LgLzo.Adler32(plain.ToArray(), 0, plain.Count));
            }
        }
        return b.ToArray();
    }

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    [Test]
    public void StoredBlock_RoundTrips()
    {
        var data = BuildStream(0, 1, 0x40000,
            new List<(byte[], bool, uint)> { (Ascii("hello"), true, 5) }, appendChecksum: false);
        CollectionAssert.AreEqual(Ascii("hello"), LgLzo.DecompressAll(data));
    }

    [Test]
    public void LzoBlock_DecodesToAbcdPattern()
    {
        var compressed = Convert.FromHexString(LzoAbcdHex);
        var expect = new byte[256];
        for (int i = 0; i < expect.Length; i++) expect[i] = Ascii("ABCD")[i & 3];
        var data = BuildStream(0, 9, 0x40000,
            new List<(byte[], bool, uint)> { (compressed, false, 256) }, appendChecksum: false);
        CollectionAssert.AreEqual(expect, LgLzo.DecompressAll(data));
    }

    [Test]
    public void FirstByteLiteralRuns_Decode()
    {
        // LZO1X streams usually open with a literal run (first-byte encoding):
        // 0x13 = copy 2 literals, 0x1B = copy 10 literals. A decoder that treats
        // the first byte as a regular match opcode yields garbage here. These
        // micro-streams expand, so they exercise the decoder directly.
        CollectionAssert.AreEqual(
            new byte[] { 0x45, 0x3D },
            Lzo1X.Decompress(Convert.FromHexString("13453d110000"), 2));
        CollectionAssert.AreEqual(
            Ascii("0123456789"),
            Lzo1X.Decompress(Convert.FromHexString("1b30313233343536373839110000"), 10));
    }

    [Test]
    public void Lzo999Block_DecodesCramfsHead()
    {
        // LZO1X-999 (level 9) block, 149 -> 4096 bytes: first block shape of a
        // real LG cramfs payload (round-trip verified with the native library
        // when the vector was made). Pinned by length, adler32 and head bytes.
        var compressed = Convert.FromHexString(Vec2Hex);
        var data = BuildStream(0, 9, 0x40000,
            new List<(byte[], bool, uint)> { (compressed, false, 4096) }, appendChecksum: false);
        var plain = LgLzo.DecompressAll(data);
        Assert.AreEqual(4096, plain.Length);
        Assert.AreEqual(0x426D2B3Eu, LgLzo.Adler32(plain, 0, plain.Length));
        CollectionAssert.AreEqual(
            Convert.FromHexString("453dcd2800b038010300000000000000"),
            plain[..16]);
    }

    private const string Vec2Hex =
        "1b453dcd2800b038010300a0000002436f6d7072657373656420524f4d46538108ce9c7d02024d00" +
        "042b7c00bc0506ed41c9064c0000c8c05203ed830c020019608a38c98209000052454c4541534500" +
        "a481c906100100c804008a136369706c75735f7075622e70656d00286d000f0c060467656e657261" +
        "6c2870002000000000000000e6000020000000000000004d0000110000";

    [Test]
    public void Checksum_AcceptedWhenCorrect()
    {
        Assert.AreEqual(0x022E00F3u, LgLzo.Adler32(Ascii("hi!"), 0, 3));
        var data = BuildStream(1, 1, 0x40000,
            new List<(byte[], bool, uint)> { (Ascii("hi!"), true, 3) });
        CollectionAssert.AreEqual(Ascii("hi!"), LgLzo.DecompressAll(data));
    }

    [Test]
    public void Checksum_RejectedWhenWrong()
    {
        var data = BuildStream(1, 1, 0x40000,
            new List<(byte[], bool, uint)> { (Ascii("hi!"), true, 3) },
            checksumOverride: 0xDEADBEEFu);
        Assert.Throws<InvalidDataException>(() => LgLzo.DecompressAll(data));
    }

    [Test]
    public void BadMagic_Rejected()
    {
        var data = BuildStream(0, 1, 0x40000,
            new List<(byte[], bool, uint)> { (Ascii("hello"), true, 5) }, appendChecksum: false);
        data[0] = 0xFF;
        Assert.Throws<InvalidDataException>(() => LgLzo.DecompressAll(data));
    }

    [Test]
    public void BadBlockSize_Rejected()
    {
        var data = BuildStream(0, 1, 512,
            new List<(byte[], bool, uint)> { (Ascii("hello"), true, 5) }, appendChecksum: false);
        Assert.Throws<InvalidDataException>(() => LgLzo.DecompressAll(data));
    }

    [Test]
    public void InLenGreaterThanOutLen_Rejected()
    {
        var b = new List<byte>();
        b.AddRange(LgLzo.Magic);
        PutU32Be(b, 5);
        PutU32Be(b, 0);
        b.Add(1); b.Add(1);
        PutU32Be(b, 0x40000);
        PutU32Be(b, 5); // out
        PutU32Be(b, 6); // in > out: corrupt
        b.AddRange(Ascii("hello!"));
        PutU32Be(b, 0);
        Assert.Throws<InvalidDataException>(() => LgLzo.DecompressAll(b.ToArray()));
    }

    [Test]
    public void TruncatedBlock_Rejected()
    {
        var data = BuildStream(0, 1, 0x40000,
            new List<(byte[], bool, uint)> { (Ascii("hello"), true, 5) }, appendChecksum: false);
        var cut = new byte[data.Length - 2];
        Array.Copy(data, cut, cut.Length);
        Assert.Throws<InvalidDataException>(() => LgLzo.DecompressAll(cut));
    }

    [Test]
    public void VersionVariantHeader_Accepted()
    {
        // method position holds 0x00 (first byte of version==1) -> version path
        var b = new List<byte>();
        b.AddRange(LgLzo.Magic);
        PutU32Be(b, 5);
        PutU32Be(b, 0);
        PutU32Be(b, 1); // version word at 16..19 (byte16 doubles as method!=1)
        b.Add(1); // method at 20
        b.Add(1); // level at 21
        PutU32Be(b, 0x40000); // block size at 22
        PutU32Be(b, 5);
        PutU32Be(b, 5);
        b.AddRange(Ascii("hello"));
        PutU32Be(b, 0);
        CollectionAssert.AreEqual(Ascii("hello"), LgLzo.DecompressAll(b.ToArray()));
    }

    [Test]
    public void Detector_HitsSyntheticStream()
    {
        var data = BuildStream(0, 1, 0x40000,
            new List<(byte[], bool, uint)> { (Ascii("hello"), true, 5) }, appendChecksum: false);
        var source = new MemoryDataSource(data) { Name = "test.lzo" };
        var result = (LgLzoDetectionResult)new LgLzoDetector().Detect(source);
        Assert.IsTrue(result.Succeded());
        Assert.AreEqual(90, result.Confidence);
        Assert.AreEqual(1, result.Context!.BlockCount);
        Assert.AreEqual(5, result.Context.TotalOut);
    }

    [Test]
    public void Detector_MissesPlainData()
    {
        var source = new MemoryDataSource(Ascii("definitely not lzo data.....")) { Name = "x.bin" };
        Assert.IsFalse(new LgLzoDetector().Detect(source).Succeded());
    }
}
