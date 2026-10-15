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
// Synthetic old-variant MStar tests: a whole-image AES-128-ECB container is
// built in-memory with a throwaway test key (never the staging secrets).
// Fully in-memory, no fixtures.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.MstarEcb;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class MstarEcbTests
{
    // Throwaway test key (16 bytes); unrelated to any staging secret.
    private const string TestKeyHex = "00112233445566778899AABBCCDDEEFF";

    private static readonly byte[] Script =
        Encoding.ASCII.GetBytes("# File Partition: kernel\nfilepartload 0 0 0x100 0x20\n" + new string(' ', 32));

    private static KeysRepository TestKeys() => new KeysRepository(new KeyBundle(new KeyDTO[]
    {
        new Aes128Ecb
        {
            Id = MstarEcb.KeysId,
            KeyMaterial = TestKeyHex,
            Description = "synthetic"
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
        aes.KeySize = 128;
        aes.Mode = CipherMode.ECB;
        aes.Key = key;
        aes.Padding = PaddingMode.None;
        return aes.CreateEncryptor().TransformFinalBlock(plain, 0, plain.Length);
    }

    [Test]
    public void WithoutKey_Detects0()
    {
        var res = new MstarEcbDetector(EmptyKeys()).Detect(Mem(BuildImage()));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void WithKey_Detects95()
    {
        var res = new MstarEcbDetector(TestKeys()).Detect(Mem(BuildImage()));
        Assert.AreEqual(95, res.Confidence);
    }

    [Test]
    public void WrongKey_Detects0()
    {
        var wrong = new byte[16];
        var res = new MstarEcbDetector(TestKeys()).Detect(Mem(BuildImage(key: wrong)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Garbage_Detects0()
    {
        var rnd = new byte[256];
        new Random(11).NextBytes(rnd);
        var res = new MstarEcbDetector(TestKeys()).Detect(Mem(rnd));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Extract_DecryptsInnerImage()
    {
        var img = BuildImage();
        var mem = Mem(img, "test.bin");
        var det = new MstarEcbDetector(TestKeys()).Detect(mem);
        Assert.AreEqual(95, det.Confidence);
        var artifacts = new MstarEcbExtractor(TestKeys()).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("test.decrypted.bin", artifacts[0].Name);
        CollectionAssert.AreEqual(Script, artifacts[0].Data.ToArray().Take(Script.Length).ToArray());
    }

    [Test]
    public void Extract_WithoutKey_Throws()
    {
        var mem = Mem(BuildImage(), "test.bin");
        Assert.Throws<InvalidDataException>(
            () => new MstarEcbExtractor(EmptyKeys()).Extract(mem).ToList());
    }
}
