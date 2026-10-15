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
// Synthetic Funai UPG-PHL (variant B) tests: a fixed 8-slot container is
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
public class FunaiPhlTests
{
    // Throwaway test key (4 bytes LE); unrelated to any staging secret.
    private const uint TestKey = 0x55AA55AA;
    private const string TestKeyHex = "AA55AA55";

    private static KeysRepository TestKeys() => new KeysRepository(new KeyBundle(new KeyDTO[]
    {
        new KeyCollectionDTO
        {
            Id = FunaiPhlDetector.KeysId,
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
            Id = FunaiPhlDetector.KeysId,
            Keys = new List<KeyDTO>
            {
                new RawKeyDTO { KeyMaterial = "78563412", Description = "synthetic wrong" },
            },
        },
    }));

    private static readonly byte[] Descriptor =
        Encoding.ASCII.GetBytes("PHL-TEST_001_0\0\0\0" + "20240101-000001\0");

    private static byte[] BuildImage(
        (ushort Type, byte[] Plain)[] bodies,
        uint key = TestKey)
    {
        // Fixed 8 slots: listed bodies first, remainder skipped (FFFF/0).
        var header = new byte[64];
        Encoding.ASCII.GetBytes("UPG\0\0\0\0\0").CopyTo(header, 0);
        for (int i = 0; i < 8; i++)
        {
            int off = 6 + i * 6;
            if (i < bodies.Length)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(off, 2), bodies[i].Type);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(off + 2, 4), (uint)bodies[i].Plain.Length);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(off, 2), 0xFFFF);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(off + 2, 4), 0);
            }
        }
        using var ms = new System.IO.MemoryStream();
        ms.Write(header, 0, header.Length);
        foreach (var (_, plain) in bodies)
        {
            ms.Write(FunaiCrypto.Encrypt(plain, key), 0, plain.Length);
        }
        return ms.ToArray();
    }

    private static byte[] StandardImage(uint key = TestKey) => BuildImage(new[]
    {
        (Type: (ushort)0, Plain: Descriptor),
        (Type: (ushort)1, Plain: Enumerable.Range(0, 64).Select(i => (byte)i).ToArray()),
        (Type: (ushort)2, Plain: Enumerable.Range(0, 40).Select(i => (byte)(0xFF - i)).ToArray()),
        (Type: (ushort)3, Plain: new byte[64]),
    }, key);

    [Test]
    public void WithKey_Detects95()
    {
        var res = new FunaiPhlDetector(TestKeys(), new Dictionary<string, string>()).Detect(Mem(StandardImage()));
        Assert.AreEqual(95, res.Confidence);
    }

    [Test]
    public void WithoutKeys_Detects85()
    {
        var res = new FunaiPhlDetector(EmptyKeys(), new Dictionary<string, string>()).Detect(Mem(StandardImage()));
        Assert.AreEqual(85, res.Confidence);
    }

    [Test]
    public void WrongKey_Detects85()
    {
        var res = new FunaiPhlDetector(WrongKeys(), new Dictionary<string, string>()).Detect(Mem(StandardImage()));
        Assert.AreEqual(85, res.Confidence);
    }

    [Test]
    public void UpgMagic_Detects0()
    {
        // Variable-count UPG shares the 6-byte magic but not the 8-byte
        // fixed-slot header: entry count 8 looks like slot data, and the
        // layout walk must reject it.
        var image = StandardImage();
        image[6] = 8;
        image[7] = 0;
        var res = new FunaiPhlDetector(TestKeys(), new Dictionary<string, string>()).Detect(Mem(image));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Garbage_Detects0()
    {
        var rnd = new byte[256];
        new Random(13).NextBytes(rnd);
        var res = new FunaiPhlDetector(TestKeys(), new Dictionary<string, string>()).Detect(Mem(rnd));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Truncated_Detects0()
    {
        var image = StandardImage();
        var cut = image[..^10];
        var res = new FunaiPhlDetector(TestKeys(), new Dictionary<string, string>()).Detect(Mem(cut));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void SlotSizeOverflow_Detects0()
    {
        var image = StandardImage();
        // Slot 1 size at offset 14: force past EOF.
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(14, 4), (uint)(image.Length + 100));
        var res = new FunaiPhlDetector(TestKeys(), new Dictionary<string, string>()).Detect(Mem(image));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Extract_DecryptsBodies()
    {
        var bodies = new[]
        {
            (Type: (ushort)0, Plain: Descriptor),
            (Type: (ushort)1, Plain: Enumerable.Range(0, 64).Select(i => (byte)i).ToArray()),
            (Type: (ushort)3, Plain: new byte[64]),
        };
        var mem = Mem(BuildImage(bodies), "test.upg");
        var det = (FunaiPhlDetectionResult)new FunaiPhlDetector(TestKeys(), new Dictionary<string, string>()).Detect(mem);
        Assert.AreEqual(95, det.Confidence);
        var artifacts = new FunaiPhlExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(3, artifacts.Count);
        Assert.AreEqual("0.bin", artifacts[0].Name);
        Assert.AreEqual("1.bin", artifacts[1].Name);
        Assert.AreEqual("3.bin", artifacts[2].Name);
        CollectionAssert.AreEqual(bodies[0].Plain, artifacts[0].Data.ToArray());
        CollectionAssert.AreEqual(bodies[1].Plain, artifacts[1].Data.ToArray());
        CollectionAssert.AreEqual(bodies[2].Plain, artifacts[2].Data.ToArray());
        // Descriptor display: identifier plus date/info strings.
        Assert.AreEqual("PHL-TEST_001_0", FunaiCrypto.DisplayString(artifacts[0].Data.ToArray().AsSpan(0, 16)));
    }

    [Test]
    public void Extract_WithoutKey_PreservesCipher()
    {
        var mem = Mem(StandardImage(), "test.upg");
        var det = (FunaiPhlDetectionResult)new FunaiPhlDetector(EmptyKeys(), new Dictionary<string, string>()).Detect(mem);
        Assert.AreEqual(85, det.Confidence);
        var artifacts = new FunaiPhlExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(4, artifacts.Count);
        CollectionAssert.AreNotEqual(Descriptor, artifacts[0].Data.ToArray());
    }
}
