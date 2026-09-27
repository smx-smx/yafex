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
// Synthetic VFS stream tests. Fully in-memory: no disk, no samples.
using System;
using System.IO;
using System.Security.Cryptography;

using NUnit.Framework;

using Yafex;
using Yafex.Fuse;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class VfsStreamsTests
{
    private static byte[] ReadAll(Stream s)
    {
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    [Test]
    public void MemoryFile_RoundTrips()
    {
        var payload = new byte[100];
        new Random(5).NextBytes(payload);
        var file = new MemoryVirtualFile("m.bin", payload);
        Assert.AreEqual(100, file.Length);
        CollectionAssert.AreEqual(payload, ReadAll(file.OpenRead()));
    }

    [Test]
    public void MultiRange_ConcatenatesAndSeeks()
    {
        var raw = new byte[100];
        for (int i = 0; i < raw.Length; i++) raw[i] = (byte)i;
        var ds = Mem(raw);
        var file = new MultiRangeVirtualFile("m.bin", ds,
            new (long, long)[] { (10, 5), (50, 3), (90, 10) });
        Assert.AreEqual(18, file.Length);
        var expect = new byte[] { 10, 11, 12, 13, 14, 50, 51, 52, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99 };
        CollectionAssert.AreEqual(expect, ReadAll(file.OpenRead()));

        using var s = file.OpenRead();
        s.Seek(4, SeekOrigin.Begin);
        var one = new byte[4];
        Assert.AreEqual(4, s.Read(one, 0, 4));
        CollectionAssert.AreEqual(new byte[] { 14, 50, 51, 52 }, one); // straddles ranges
        s.Seek(-2, SeekOrigin.End);
        var tail = new byte[2];
        Assert.AreEqual(2, s.Read(tail, 0, 2));
        CollectionAssert.AreEqual(new byte[] { 98, 99 }, tail);
        Assert.AreEqual(0, s.Read(tail, 0, 2)); // past end
        Assert.Throws<IOException>(() => s.Seek(-1, SeekOrigin.Begin));
    }

    private static (byte[] cipher, byte[] key, byte[] iv) CbcEncrypt(byte[] plain)
    {
        var key = new byte[16];
        var iv = new byte[16];
        new Random(11).NextBytes(key);
        new Random(12).NextBytes(iv);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        // pad plaintext to blocks with zeros (test-only)
        var padded = new byte[(plain.Length + 15) & ~15];
        Array.Copy(plain, padded, plain.Length);
        using var enc = aes.CreateEncryptor();
        return (enc.TransformFinalBlock(padded, 0, padded.Length), key, iv);
    }

    [Test]
    public void AesStream_DecryptsWindowsAndSeeks()
    {
        var plain = new byte[200];
        new Random(13).NextBytes(plain);
        var (cipher, key, iv) = CbcEncrypt(plain);
        var ds = Mem(cipher);
        foreach (var skip in new long[] { 0, 5, 190 })
        {
            var stream = new AesCbcDecryptStream(ds, 0, cipher.Length, skip, plain.Length - skip, key, iv, key);
            var buf = new byte[plain.Length - skip];
            int off = 0;
            var rnd = new Random(14);
            while (off < buf.Length)
            {
                int take = Math.Min(rnd.Next(1, 37), buf.Length - off);
                int n = stream.Read(buf, off, take);
                if (n <= 0) break;
                off += n;
            }
            Assert.AreEqual(buf.Length, off, $"skip={skip}");
            var expect = new byte[buf.Length];
            Array.Copy(plain, skip, expect, 0, expect.Length);
            CollectionAssert.AreEqual(expect, buf, $"skip={skip}");

            using var s2 = new AesCbcDecryptStream(ds, 0, cipher.Length, skip, plain.Length - skip, key, iv, key);
            s2.Seek(7, SeekOrigin.Begin);
            var three = new byte[3];
            Assert.AreEqual(3, s2.Read(three, 0, 3));
            CollectionAssert.AreEqual(expect[7..10], three);
        }
    }

    [Test]
    public void AesStream_TailWithoutKey_ServesRaw()
    {
        var plain = new byte[20];
        new Random(15).NextBytes(plain);
        var (cipher32, key, iv) = CbcEncrypt(plain);
        // stored region itself ends mid-block: last 4 bytes are the tail
        var region = cipher32[..20];
        var ds = Mem(region);
        using var s = new AesCbcDecryptStream(ds, 0, region.Length, 16, 4, key, iv, null);
        var buf = new byte[4];
        Assert.AreEqual(4, s.Read(buf, 0, 4));
        CollectionAssert.AreEqual(region[16..20], buf);
    }
}
