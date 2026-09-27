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
// Synthetic MtkPartFile tests: lazy decrypt windows, meta strip, raw
// fallback. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.Fuse;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class MtkPartFileTests
{
    private static byte[] RandomBytes(int n, int seed)
    {
        var b = new byte[n];
        new Random(seed).NextBytes(b);
        return b;
    }

    private static byte[] AesCbc(byte[] plain, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var enc = aes.CreateEncryptor();
        return enc.TransformFinalBlock(plain, 0, plain.Length);
    }

    private static KeysRepository CollectionKeys(byte[] key, byte[] iv)
    {
        return new KeysRepository(new KeyBundle(new KeyDTO[]
        {
            new KeyCollectionDTO
            {
                Id = "mtkpkg-keys",
                Keys = new List<KeyDTO>
                {
                    new Aes128Cbc { KeyMaterial = Convert.ToHexString(key), KeyIV = Convert.ToHexString(iv) },
                },
            },
        }));
    }

    private static byte[] ReadAll(MtkPartFile file)
    {
        using var s = file.OpenRead();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    [Test]
    public void EncryptedNew_DecryptsWindows()
    {
        var key = RandomBytes(16, 21);
        var iv = RandomBytes(16, 22);
        var payload = RandomBytes(208, 23);
        var region = new byte[48 + payload.Length];
        Encoding.ASCII.GetBytes("reserved mtk inc").CopyTo(region, 0);
        Array.Copy(payload, 0, region, 48, payload.Length);
        var cipher = AesCbc(region, key, iv);
        var ds = Mem(cipher);
        var ks = new MtkKeyState(CollectionKeys(key, iv));
        var file = new MtkPartFile("p.pak", ds, 0, cipher.Length, encrypted: true, oldVariant: false, ks);
        Assert.AreEqual(payload.Length, file.Length);
        CollectionAssert.AreEqual(payload, ReadAll(file));

        using var s = file.OpenRead();
        s.Seek(190, SeekOrigin.Begin);
        var tail = new byte[10];
        Assert.AreEqual(10, s.Read(tail, 0, 10));
        CollectionAssert.AreEqual(payload[190..200], tail);
    }

    [Test]
    public void PlaintextNew_StripsMeta()
    {
        var payload = RandomBytes(64, 24);
        var region = new byte[48 + 8 + 4 + payload.Length];
        Encoding.ASCII.GetBytes("iMtK").CopyTo(region, 48);
        BitConverter.GetBytes(4).CopyTo(region, 52);
        Encoding.ASCII.GetBytes("v1.0").CopyTo(region, 56);
        Array.Copy(payload, 0, region, 60, payload.Length);
        var ds = Mem(region);
        var file = new MtkPartFile("p.pak", ds, 0, region.Length,
            encrypted: false, oldVariant: false, new MtkKeyState(EmptyKeys()));
        Assert.AreEqual(payload.Length, file.Length);
        CollectionAssert.AreEqual(payload, ReadAll(file));
    }

    [Test]
    public void EncryptedWithoutKeys_ServesRaw()
    {
        var cipher = RandomBytes(80, 25);
        var ds = Mem(cipher);
        var file = new MtkPartFile("p.pak", ds, 0, cipher.Length,
            encrypted: true, oldVariant: false, new MtkKeyState(EmptyKeys()));
        Assert.AreEqual(cipher.Length, file.Length);
        CollectionAssert.AreEqual(cipher, ReadAll(file));
    }

    [Test]
    public void PlaintextOld_StripsMeta()
    {
        var payload = RandomBytes(32, 26);
        var region = new byte[8 + 4 + payload.Length];
        Encoding.ASCII.GetBytes("iMtK").CopyTo(region, 0);
        BitConverter.GetBytes(4).CopyTo(region, 4);
        Encoding.ASCII.GetBytes("v9").CopyTo(region, 8);
        Array.Copy(payload, 0, region, 12, payload.Length);
        var ds = Mem(region);
        var file = new MtkPartFile("p.pak", ds, 0, region.Length,
            encrypted: false, oldVariant: true, new MtkKeyState(EmptyKeys()));
        Assert.AreEqual(payload.Length, file.Length);
        CollectionAssert.AreEqual(payload, ReadAll(file));
    }
}
