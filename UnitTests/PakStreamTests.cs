using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;

using NUnit.Framework;

using Smx.SharpIO.Memory.Buffers;

using Yafex;
using Yafex.FileFormats;
using Yafex.FileFormats.Epk;
using Yafex.FileFormats.EpkV2;
using Yafex.Support;

namespace UnitTests;

[TestFixture]
public class PakStreamTests
{
    private const int SIG = EPK_V2_STRUCTURE.SIGNATURE_SIZE; // 0x80

    private static AesDecryptor MakeDecryptor(CipherMode mode)
    {
        var key = new byte[16];
        var iv = new byte[16];
        Random.Shared.NextBytes(key);
        Random.Shared.NextBytes(iv);

        var entry = new KeyEntry
        {
            keyAlgo = CipherAlgorithmType.Aes128,
            keyMode = mode,
            key = key,
            iv = iv,
            comment = "test"
        };
        return new AesDecryptor(entry.GetAes());
    }

    private static byte[] Encrypt(Aes aes, byte[] plain)
    {
        using var ms = new MemoryStream();
        using (var enc = aes.CreateEncryptor())
        using (var cs = new CryptoStream(ms, enc, CryptoStreamMode.Write))
        {
            cs.Write(plain, 0, plain.Length);
            cs.FlushFinalBlock();
        }
        return ms.ToArray();
    }

    internal static byte[] Sequence(int start, int count)
    {
        var buf = new byte[count];
        for (int i = 0; i < count; i++) buf[i] = (byte)((start + i) & 0xFF);
        return buf;
    }

    private static void AssertReadEquals(Stream s, byte[] expected, long offset, int count)
    {
        s.Seek(offset, SeekOrigin.Begin);
        var actual = new byte[count];
        int n = ReadFull(s, actual, count);
        Assert.AreEqual(count, n, $"read length mismatch at offset {offset}");
        CollectionAssert.AreEqual(expected.AsSpan((int)offset, count).ToArray(), actual,
            $"data mismatch at offset {offset}, count {count}");
    }

    private static int ReadFull(Stream s, byte[] buf, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = s.Read(buf, total, count - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    [Test]
    public void TestDecryptBlocks_MatchesWholeDecrypt_ECB()
    {
        var aes = MakeDecryptor(CipherMode.ECB);
        var plain = Sequence(0, 6 * 16);
        var cipher = Encrypt(aes.Aes, plain);

        // window over blocks [2..5)
        var outBuf = new byte[4 * 16];
        aes.DecryptBlocks(cipher.Skip(2 * 16).ToArray(), 0, outBuf);
        CollectionAssert.AreEqual(plain.Skip(2 * 16).Take(4 * 16).ToArray(), outBuf);
    }

    [Test]
    public void TestDecryptBlocks_MatchesWholeDecrypt_CBC()
    {
        var aes = MakeDecryptor(CipherMode.CBC);
        var plain = Sequence(0, 6 * 16);
        var cipher = Encrypt(aes.Aes, plain);

        // window over blocks [2..5), needs preceding block for chaining
        var outBuf = new byte[5 * 16];
        aes.DecryptBlocks(cipher.Skip(1 * 16).ToArray(), discardPrefixBlocks: 1, output: outBuf);
        CollectionAssert.AreEqual(plain.Skip(2 * 16).Take(4 * 16).ToArray(), outBuf.AsSpan(0, 4 * 16).ToArray());

        // window starting at block 0 uses the IV
        var outBuf2 = new byte[3 * 16];
        aes.DecryptBlocks(cipher.Take(3 * 16).ToArray(), 0, outBuf2);
        CollectionAssert.AreEqual(plain.Take(3 * 16).ToArray(), outBuf2);
    }

    private static PakImage SingleSegmentImage(byte[] source, PakSegment seg, out MappedBlockSource blockSource)
    {
        blockSource = new MappedBlockSource(new Memory64<byte>(source));
        return new PakImage
        {
            Name = "test.pak",
            Source = blockSource,
            Segments = new List<PakSegment> { seg }
        };
    }

    [Test]
    public void TestPakStream_EncryptedSingleSegment()
    {
        var plain = Sequence(0, 4 * 16);
        var aes = MakeDecryptor(CipherMode.ECB);
        var cipher = Encrypt(aes.Aes, plain);

        var seg = new PakSegment
        {
            PhysicalDataOffset = 0,
            EncryptedSize = plain.Length,
            ContentOffset = 0,
            LogicalSize = plain.Length,
            WasDecrypted = true,
            Decryptor = aes
        };
        var image = SingleSegmentImage(cipher, seg, out _);
        using var s = image.OpenStream();

        Assert.AreEqual(plain.Length, s.Length);
        AssertReadEquals(s, plain, 0, plain.Length);
        AssertReadEquals(s, plain, 0, 4);
        AssertReadEquals(s, plain, 15, 1);
        AssertReadEquals(s, plain, 16, 16);
        AssertReadEquals(s, plain, 17, 8);
        AssertReadEquals(s, plain, 31, 16);
    }

    [Test]
    public void TestPakStream_EncryptedCrossSegment()
    {
        // two encrypted segments (CBC), reads spanning the boundary
        var plainA = Sequence(0, 64);
        var plainB = Sequence(100, 64);
        var aes = MakeDecryptor(CipherMode.CBC);
        var cipherA = Encrypt(aes.Aes, plainA);
        var cipherB = Encrypt(aes.Aes, plainB);

        var expected = plainA.Concat(plainB).ToArray();

        var blockSource = new MappedBlockSource(new Memory64<byte>(cipherA.Concat(cipherB).ToArray()));
        var image = new PakImage
        {
            Name = "test.pak",
            Source = blockSource,
            Segments = new List<PakSegment>
            {
                new PakSegment
                {
                    PhysicalDataOffset = 0,
                    EncryptedSize = plainA.Length,
                    ContentOffset = 0,
                    LogicalSize = plainA.Length,
                    WasDecrypted = true,
                    Decryptor = aes
                },
                new PakSegment
                {
                    PhysicalDataOffset = cipherA.Length,
                    EncryptedSize = plainB.Length,
                    ContentOffset = 0,
                    LogicalSize = plainB.Length,
                    WasDecrypted = true,
                    Decryptor = aes
                }
            }
        };

        using var s = image.OpenStream();
        Assert.AreEqual(expected.Length, s.Length);
        AssertReadEquals(s, expected, 60, 16);   // straddles segment 0/1 boundary
        AssertReadEquals(s, expected, 63, 2);    // exact boundary
        AssertReadEquals(s, expected, 64, 32);   // second segment
        AssertReadEquals(s, expected, 0, expected.Length); // whole file
    }

    [Test]
    public void TestPakStream_PlaintextPassthrough()
    {
        var data = Sequence(0, 200);
        var blockSource = new MappedBlockSource(new Memory64<byte>(data));
        var image = new PakImage
        {
            Name = "test.pak",
            Source = blockSource,
            Segments = new List<PakSegment>
            {
                new PakSegment
                {
                    PhysicalDataOffset = 0,
                    EncryptedSize = 100,
                    ContentOffset = 0,
                    LogicalSize = 100,
                    WasDecrypted = false,
                    Decryptor = null
                },
                new PakSegment
                {
                    PhysicalDataOffset = 100,
                    EncryptedSize = 100,
                    ContentOffset = 0,
                    LogicalSize = 100,
                    WasDecrypted = false,
                    Decryptor = null
                }
            }
        };

        using var s = image.OpenStream();
        Assert.AreEqual(200, s.Length);
        AssertReadEquals(s, data, 90, 20);
        AssertReadEquals(s, data, 199, 1);
    }

    [Test]
    public void TestPakStream_ContentOffset()
    {
        // Epk3-style: encrypted region has a leading 4-byte index, content starts at 4
        var payload = Sequence(0, 60);
        var withIndex = new byte[4 + payload.Length];
        BitConverter.GetBytes(0u).CopyTo(withIndex, 0);
        payload.CopyTo(withIndex, 4);

        var aes = MakeDecryptor(CipherMode.ECB);
        var cipher = Encrypt(aes.Aes, withIndex);

        var seg = new PakSegment
        {
            PhysicalDataOffset = 0,
            EncryptedSize = withIndex.Length,
            ContentOffset = 4,
            LogicalSize = withIndex.Length - 4,
            WasDecrypted = true,
            Decryptor = aes
        };
        var image = SingleSegmentImage(cipher, seg, out _);
        using var s = image.OpenStream();

        Assert.AreEqual(payload.Length, s.Length);
        AssertReadEquals(s, payload, 0, payload.Length);
        AssertReadEquals(s, payload, 15, 16);
        AssertReadEquals(s, payload, 59, 1);
    }

    // ---- synthetic EPK2 walking / extraction ----

    private static void WriteAscii(byte[] buf, int offset, string s)
    {
        Encoding.ASCII.GetBytes(s).CopyTo(buf, offset);
    }

    private static void WriteU32(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)value;
        buf[offset + 1] = (byte)(value >> 8);
        buf[offset + 2] = (byte)(value >> 16);
        buf[offset + 3] = (byte)(value >> 24);
    }

    /// <summary>Builds a PAK_V2_STRUCTURE (signature + header) for one segment.</summary>
    private static byte[] BuildPak2Structure(
        string imageType, uint segmentIndex, uint segmentCount, uint segmentSize)
    {
        var buf = new byte[0x100];
        for (int i = 0; i < SIG; i++) buf[i] = 0xAA; // signature

        int p = SIG;
        WriteAscii(buf, p, imageType); p += 4;
        p += 4; // imageSize
        p += 64; // modelData
        p += 4; // swVersion
        WriteU32(buf, p, 0); p += 4; // swDate
        WriteU32(buf, p, 0); p += 4; // devMode
        WriteU32(buf, p, segmentCount); p += 4;
        WriteU32(buf, p, segmentSize); p += 4;
        WriteU32(buf, p, segmentIndex); p += 4;
        WriteAscii(buf, p, "MPAK"); p += 4;
        p += 24; // reserved
        WriteU32(buf, p, 0); p += 4; // crc32
        return buf;
    }

    /// <summary>
    /// Builds a full EPK2 file with one pak made of the given segment payloads.
    /// Returns the file bytes and the expected extracted pak content.
    /// </summary>
    internal static (byte[] file, byte[] expectedPak) BuildEpk2File(string imageType, params byte[][] segments)
    {
        const int EPK_HEADER_TOTAL = 0x6D4;

        int pakSize = 0;
        foreach (var seg in segments) pakSize += 0x100 + seg.Length;

        var file = new byte[EPK_HEADER_TOTAL + pakSize];
        WriteAscii(file, 0x80, "epak");
        WriteU32(file, 0x84, (uint)(file.Length - 0x80));
        WriteU32(file, 0x88, 1); // fileNum
        WriteAscii(file, 0x8C, "EPK2");
        WriteAscii(file, 0x6B4, "1.0.0.0");
        WriteAscii(file, 0x6C4, "1.0.0.0");

        // imageLocations[0].ImageOffset at 0xB4 (PAK_V2_LOCATION.ImageOffset)
        int pakStart = EPK_HEADER_TOTAL;
        WriteU32(file, 0xB4, (uint)(pakStart - SIG));

        var expected = new List<byte>();
        int pos = pakStart;
        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            var structure = BuildPak2Structure(
                imageType, (uint)i, (uint)segments.Length, (uint)seg.Length);
            structure.CopyTo(file, pos);
            pos += structure.Length;
            seg.CopyTo(file, pos);
            pos += seg.Length;
            expected.AddRange(seg);
        }

        return (file, expected.ToArray());
    }

    private static Epk2Context PlainContext(EPK_V2_HEADER hdr)
    {
        var keysRepo = new KeysRepository(null!);
        return new Epk2Context(
            new EpkServicesFactory(keysRepo),
            new EpkServices(),
            hdr
        );
    }

    [Test]
    public void TestEpk2SegmentWalker_AndPakStream()
    {
        var segA = Sequence(0, 0x90);
        var segB = Sequence(0x200, 0x30);

        var (file, expected) = BuildEpk2File("root", segA, segB);

        var hdr = new EPK_V2_HEADER();
        hdr.imageLocations = new PAK_V2_LOCATION[64];
        // pakLoc = GetImageOffset(0) + 1*SIG, pak starts right after the EPK header
        hdr.imageLocations[0].ImageOffset = 0x6D4u - (uint)SIG;
        hdr.fileNum = 1;

        var ctx = PlainContext(hdr);
        var span = new Memory64<byte>(file).AsReadonlySpan();
        var images = Epk2SegmentWalker.BuildPakImages(
            span, ctx, hdr
        );

        Assert.AreEqual(1, images.Count);
        var pak = images[0];
        Assert.AreEqual("root.pak", pak.Image.Name);
        Assert.AreEqual(2, pak.Image.Segments.Count);
        Assert.AreEqual(expected.Length, pak.Image.Length);

        pak.Image.Source = new MappedBlockSource(new Memory64<byte>(file));
        using var s = pak.Image.OpenStream();

        var buf = new byte[expected.Length];
        Assert.AreEqual(expected.Length, ReadFull(s, buf, expected.Length));
        CollectionAssert.AreEqual(expected, buf);

        AssertReadEquals(s, expected, 0, 16);
        AssertReadEquals(s, expected, 0x80, 16);
        AssertReadEquals(s, expected, 0x8F, 4);   // straddles the segment boundary (segA = 0x90)
        AssertReadEquals(s, expected, 0x90, 0x20);
        AssertReadEquals(s, expected, expected.Length - 1, 1);
    }

    [Test]
    public void TestEpk2Extractor_PlainPaks()
    {
        var segA = Sequence(0, 0x90);
        var segB = Sequence(0x200, 0x30);
        var (file, expected) = BuildEpk2File("root", segA, segB);

        var mf = new MemoryDataSource(new Memory64<byte>(file)) { Name = "test", Flags = DataSourceFlags.Input };

        var keysRepo = new KeysRepository(null!);
        var detector = new Epk2Detector(keysRepo);
        var result = (Epk2DetectionResult<EPK_V2_HEADER>)detector.Detect(mf);
        Assert.IsTrue(result.Succeded(), "EPK2 detection failed");
        Assert.NotNull(result.Context);

        var extractor = new Epk2Extractor(result);
        var paks = extractor.Extract(mf).ToList();
        Assert.AreEqual(1, paks.Count);

        var actual = paks[0].Data.ToArray();
        CollectionAssert.AreEqual(expected, actual);
    }
}
