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
// Synthetic Funai UPG (variant A) tests: minimal interleaved containers are
// built in-memory with a throwaway test key (never the staging secrets).
// Fully in-memory, no fixtures.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Funai;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class FunaiUpgTests
{
    // Throwaway test key (4 bytes LE); unrelated to any staging secret.
    private const uint TestKey = 0xA1B2C3D4;
    private const string TestKeyHex = "D4C3B2A1";
    private const uint WrongKey = 0x01020304;

    private static KeysRepository TestKeys() => new KeysRepository(new KeyBundle(new KeyDTO[]
    {
        new KeyCollectionDTO
        {
            Id = FunaiUpgDetector.KeysId,
            Keys = new List<KeyDTO>
            {
                new RawKeyDTO { KeyMaterial = TestKeyHex, Description = "synthetic" },
            },
        },
    }));

    private static KeysRepository WrongKeys() => new KeysRepository(new KeyBundle(new KeyDTO[]
    {
        new KeyCollectionDTO
        {
            Id = FunaiUpgDetector.KeysId,
            Keys = new List<KeyDTO>
            {
                new RawKeyDTO { KeyMaterial = "04030201", Description = "synthetic wrong" },
            },
        },
    }));

    private static byte[] BuildImage(
        (ushort Type, byte[] Plain, bool Encrypted)[] entries,
        uint key = TestKey)
    {
        using var ms = new System.IO.MemoryStream();
        ms.Write("UPG\0\0\0"u8.ToArray(), 0, 6);
        var n = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(n, (ushort)entries.Length);
        ms.Write(n, 0, 2);
        ms.Write(new byte[4], 0, 4); // total size placeholder (informational)
        foreach (var (type, plain, encrypted) in entries)
        {
            byte[] stored = encrypted ? FunaiCrypto.Encrypt(plain, key) : plain;
            var hdr = new byte[8];
            BinaryPrimitives.WriteUInt16LittleEndian(hdr, type);
            BinaryPrimitives.WriteUInt32LittleEndian(hdr.AsSpan(2), (uint)(stored.Length + 70));
            hdr[6] = 3;
            hdr[7] = (byte)(encrypted ? 1 : 0);
            ms.Write(hdr, 0, 8);
            ms.Write(stored, 0, stored.Length);
            ms.Write(new byte[4], 0, 4); // CRC (ignored)
            ms.Write(new byte[64], 0, 64); // hash (ignored)
        }
        var image = ms.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(8, 4), (uint)(image.Length - 12));
        return image;
    }

    private static readonly byte[] Descriptor =
        Encoding.ASCII.GetBytes("TEST-10_001_0\0\0\0");

    private static byte[] StandardImage(uint key = TestKey) => BuildImage(new[]
    {
        (Type: (ushort)0, Plain: Descriptor, Encrypted: true),
        (Type: (ushort)3, Plain: Enumerable.Range(0, 100).Select(i => (byte)i).ToArray(), Encrypted: true),
        // Odd length exercises the trailing passthrough (< 8 bytes tail).
        (Type: (ushort)4, Plain: Enumerable.Range(0, 13).Select(i => (byte)(0xA0 + i)).ToArray(), Encrypted: false),
    }, key);

    [Test]
    public void Crypto_RoundTrip()
    {
        var plain = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        CollectionAssert.AreEqual(plain, FunaiCrypto.Decrypt(FunaiCrypto.Encrypt(plain, TestKey), TestKey));
        // Trailing 1-7 bytes pass through unchanged.
        var odd = Enumerable.Range(0, 13).Select(i => (byte)(0xA0 + i)).ToArray();
        CollectionAssert.AreEqual(odd, FunaiCrypto.Decrypt(FunaiCrypto.Encrypt(odd, TestKey), TestKey));
        // The F-function quirk: LSB of F is always 0.
        Assert.AreEqual(0u, FunaiCrypto.F(0x12345678, TestKey) & 1u);
    }

    [Test]
    public void EncryptedWithKey_Detects95()
    {
        var res = new FunaiUpgDetector(TestKeys(), new Dictionary<string, string>()).Detect(Mem(StandardImage()));
        Assert.AreEqual(95, res.Confidence);
    }

    [Test]
    public void EncryptedWithoutKeys_Detects85()
    {
        var res = new FunaiUpgDetector(EmptyKeys(), new Dictionary<string, string>()).Detect(Mem(StandardImage()));
        Assert.AreEqual(85, res.Confidence);
    }

    [Test]
    public void WrongKey_Detects85()
    {
        var res = new FunaiUpgDetector(WrongKeys(), new Dictionary<string, string>()).Detect(Mem(StandardImage()));
        Assert.AreEqual(85, res.Confidence);
        Assert.IsTrue(WrongKey != TestKey);
    }

    [Test]
    public void PlainFile_Detects85()
    {
        var image = BuildImage(new[]
        {
            (Type: (ushort)0, Plain: Descriptor, Encrypted: false),
            (Type: (ushort)3, Plain: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, Encrypted: false),
        });
        var res = new FunaiUpgDetector(EmptyKeys(), new Dictionary<string, string>()).Detect(Mem(image));
        Assert.AreEqual(85, res.Confidence);
    }

    [Test]
    public void PhlMagic_Detects0()
    {
        // UPG-PHL shares the 6-byte magic but has entry count 0: not UPG.
        var image = StandardImage();
        image[6] = 0;
        image[7] = 0;
        var res = new FunaiUpgDetector(TestKeys(), new Dictionary<string, string>()).Detect(Mem(image));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Garbage_Detects0()
    {
        var rnd = new byte[256];
        new Random(7).NextBytes(rnd);
        var res = new FunaiUpgDetector(TestKeys(), new Dictionary<string, string>()).Detect(Mem(rnd));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Truncated_Detects0()
    {
        var image = StandardImage();
        var cut = image[..^10];
        var res = new FunaiUpgDetector(TestKeys(), new Dictionary<string, string>()).Detect(Mem(cut));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void EntrySizeTooSmall_Detects0()
    {
        var image = StandardImage();
        // First entry header ES at offset 14: force below the 70-byte minimum.
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(14, 4), 10u);
        var res = new FunaiUpgDetector(TestKeys(), new Dictionary<string, string>()).Detect(Mem(image));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void BadEncryptionFlag_Detects0()
    {
        var image = StandardImage();
        image[19] = 2; // enc flag must be 0 or 1
        var res = new FunaiUpgDetector(TestKeys(), new Dictionary<string, string>()).Detect(Mem(image));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Extract_DecryptsPayloads()
    {
        var payloads = new[]
        {
            (Type: (ushort)0, Plain: Descriptor, Encrypted: true),
            (Type: (ushort)3, Plain: Enumerable.Range(0, 100).Select(i => (byte)i).ToArray(), Encrypted: true),
            (Type: (ushort)4, Plain: Enumerable.Range(0, 13).Select(i => (byte)(0xA0 + i)).ToArray(), Encrypted: false),
        };
        var mem = Mem(BuildImage(payloads), "test.upg");
        var det = (FunaiUpgDetectionResult)new FunaiUpgDetector(TestKeys(), new Dictionary<string, string>()).Detect(mem);
        Assert.AreEqual(95, det.Confidence);
        var artifacts = new FunaiUpgExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(3, artifacts.Count);
        Assert.AreEqual("0.bin", artifacts[0].Name);
        Assert.AreEqual("3.bin", artifacts[1].Name);
        Assert.AreEqual("4.bin", artifacts[2].Name);
        CollectionAssert.AreEqual(payloads[0].Plain, artifacts[0].Data.ToArray());
        CollectionAssert.AreEqual(payloads[1].Plain, artifacts[1].Data.ToArray());
        CollectionAssert.AreEqual(payloads[2].Plain, artifacts[2].Data.ToArray());
    }

    [Test]
    public void Extract_WithoutKey_PreservesCipher()
    {
        var mem = Mem(StandardImage(), "test.upg");
        var det = (FunaiUpgDetectionResult)new FunaiUpgDetector(EmptyKeys(), new Dictionary<string, string>()).Detect(mem);
        Assert.AreEqual(85, det.Confidence);
        var artifacts = new FunaiUpgExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(3, artifacts.Count);
        // Still encrypted: differs from the plaintext descriptor.
        CollectionAssert.AreNotEqual(Descriptor, artifacts[0].Data.ToArray());
    }

    [Test]
    public void Extract_DuplicateType_LastWins()
    {
        var first = Encoding.ASCII.GetBytes("AAAAAAAA");
        var second = Encoding.ASCII.GetBytes("BBBBBBBB");
        var mem = Mem(BuildImage(new[]
        {
            (Type: (ushort)0, Plain: Descriptor, Encrypted: true),
            (Type: (ushort)5, Plain: first, Encrypted: false),
            (Type: (ushort)5, Plain: second, Encrypted: false),
        }), "test.upg");
        var det = (FunaiUpgDetectionResult)new FunaiUpgDetector(TestKeys(), new Dictionary<string, string>()).Detect(mem);
        var artifacts = new FunaiUpgExtractor(det).Extract(mem).ToList();
        var dup = artifacts.Where(a => a.Name == "5.bin").ToList();
        Assert.AreEqual(1, dup.Count);
        CollectionAssert.AreEqual(second, dup[0].Data.ToArray());
    }
}
