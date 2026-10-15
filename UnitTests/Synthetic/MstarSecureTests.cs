#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
// Synthetic MStar secure-container tests: a minimal container (chunk table
// at 0x500, one AES-CBC segment) is built in-memory with a throwaway test
// key (never the staging secrets). Fully in-memory, no fixtures.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.MstarSecure;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class MstarSecureTests
{
    // Throwaway test key (32 bytes); unrelated to any staging secret.
    private const string TestKeyHex =
        "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF";

    private static readonly byte[] Script =
        Encoding.ASCII.GetBytes("filepartload 81100000 usb.bin 2000 100\nmmc write.p 81100000 KL 100\n");

    private static KeysRepository TestKeys() => new KeysRepository(new KeyBundle(new KeyDTO[]
    {
        new KeyCollectionDTO
        {
            Id = MstarSecure.KeysId,
            Keys = new List<KeyDTO>
            {
                new Aes256Cbc
                {
                    Id = "test",
                    KeyMaterial = TestKeyHex,
                    KeyIV = new string('0', 64),
                    Description = "synthetic"
                }
            }
        }
    }));

    private static byte[] BuildImage(byte[]? payload = null, byte[]? key = null)
    {
        payload ??= Script;
        key ??= Convert.FromHexString(TestKeyHex);
        int padded = (payload.Length + 15) & ~15;
        var plain = new byte[padded];
        Array.Copy(payload, plain, payload.Length);
        using var aes = Aes.Create();
        aes.BlockSize = 128;
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.IV = new byte[16];
        aes.Key = key;
        aes.Padding = PaddingMode.None;
        var cipher = aes.CreateEncryptor().TransformFinalBlock(plain, 0, plain.Length);

        var img = new byte[0x1000 + cipher.Length];
        var head = Encoding.ASCII.GetBytes("MTK.....");
        Array.Copy(head, 0, img, 0x500, head.Length);
        void PutU32(int off, uint v) => Array.Copy(BitConverter.GetBytes(v), 0, img, off, 4);
        PutU32(0x508, 64); // segment_size
        PutU32(0x50C, 0x1000); // data_offset
        PutU32(0x510, (uint)cipher.Length); // data_length
        var tail = Encoding.ASCII.GetBytes(".....mtk");
        Array.Copy(tail, 0, img, 0x500 + 120, tail.Length);
        Array.Copy(cipher, 0, img, 0x1000, cipher.Length);
        return img;
    }

    [Test]
    public void ShapeWithoutKeys_Detects85()
    {
        var res = new MstarSecureDetector(EmptyKeys()).Detect(Mem(BuildImage()));
        Assert.AreEqual(85, res.Confidence);
    }

    [Test]
    public void WithKey_Detects95()
    {
        var res = new MstarSecureDetector(TestKeys()).Detect(Mem(BuildImage()));
        Assert.AreEqual(95, res.Confidence);
    }

    [Test]
    public void WrongKey_Detects85()
    {
        var wrong = new byte[32];
        var res = new MstarSecureDetector(TestKeys()).Detect(Mem(BuildImage(key: wrong)));
        Assert.AreEqual(85, res.Confidence);
    }

    [Test]
    public void Garbage_Detects0()
    {
        var rnd = new byte[0x600];
        new Random(3).NextBytes(rnd);
        var res = new MstarSecureDetector(TestKeys()).Detect(Mem(rnd));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Extract_DecryptsInnerImage()
    {
        var img = BuildImage();
        var mem = Mem(img, "test.pkg");
        var det = (MstarSecureDetectionResult)new MstarSecureDetector(TestKeys()).Detect(mem);
        Assert.AreEqual(95, det.Confidence);
        var artifacts = new MstarSecureExtractor(TestKeys(), det.Chunk!).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("test.decrypted.bin", artifacts[0].Name);
        var inner = artifacts[0].Data.ToArray();
        CollectionAssert.AreEqual(Script, inner.Skip(0x1000).Take(Script.Length).ToArray());
    }

    [Test]
    public void Extract_WithoutKey_Throws()
    {
        var img = BuildImage();
        var mem = Mem(img, "test.pkg");
        var det = (MstarSecureDetectionResult)new MstarSecureDetector(EmptyKeys()).Detect(mem);
        Assert.AreEqual(85, det.Confidence);
        Assert.Throws<InvalidDataException>(
            () => new MstarSecureExtractor(EmptyKeys(), det.Chunk!).Extract(mem).ToList());
    }
}
