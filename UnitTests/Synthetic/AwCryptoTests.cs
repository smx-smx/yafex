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
// Synthetic RC6 tests: roundtrips over fixed patterns and region-key
// distinctness. Spec-correctness was verified against a real LiveSuit
// image (header decrypts to the IMAGEWTY magic). Fully in-memory.
using System;

using NUnit.Framework;

using Yafex.FileFormats.AllwinnerImg;

namespace UnitTests;

[TestFixture]
public class AwCryptoTests
{
    private static byte[] Pattern(int len)
    {
        var buf = new byte[len];
        for (int i = 0; i < len; i++) buf[i] = (byte)((i * 31 + 7) & 0xFF);
        return buf;
    }

    [Test]
    public void RegionKeys_AreDistinct()
    {
        var h = AwCrypto.HeaderKey();
        var m = AwCrypto.EntriesKey();
        var g = AwCrypto.PayloadKey();
        Assert.AreEqual(32, h.Length);
        Assert.AreEqual((byte)'i', h[31]);
        Assert.AreEqual((byte)'m', m[31]);
        Assert.AreEqual((byte)'g', g[31]);
        Assert.IsFalse(h.AsSpan().SequenceEqual(m));
        Assert.IsFalse(h.AsSpan().SequenceEqual(g));
    }

    [Test]
    public void Roundtrip_HeaderKey()
    {
        var plain = Pattern(1024);
        var enc = AwCrypto.Encrypt((byte[])plain.Clone(), AwCrypto.HeaderKey());
        Assert.IsFalse(enc.AsSpan().SequenceEqual(plain));
        var dec = AwCrypto.Decrypt(enc, AwCrypto.HeaderKey());
        Assert.IsTrue(dec.AsSpan().SequenceEqual(plain));
    }

    [Test]
    public void Roundtrip_AllRegions()
    {
        var plain = Pattern(256);
        foreach (var key in new[] { AwCrypto.HeaderKey(), AwCrypto.EntriesKey(), AwCrypto.PayloadKey() })
        {
            var dec = AwCrypto.Decrypt(AwCrypto.Encrypt((byte[])plain.Clone(), key), key);
            Assert.IsTrue(dec.AsSpan().SequenceEqual(plain));
        }
    }

    [Test]
    public void WrongKey_DoesNotDecrypt()
    {
        var plain = Pattern(64);
        var enc = AwCrypto.Encrypt((byte[])plain.Clone(), AwCrypto.HeaderKey());
        var dec = AwCrypto.Decrypt(enc, AwCrypto.PayloadKey());
        Assert.IsFalse(dec.AsSpan().SequenceEqual(plain));
    }

    [Test]
    public void ShortTail_LeftUntouched()
    {
        var plain = Pattern(100);
        var enc = AwCrypto.Encrypt((byte[])plain.Clone(), AwCrypto.HeaderKey());
        // full blocks transformed, trailing 4 bytes pass through
        for (int i = 96; i < 100; i++) Assert.AreEqual(plain[i], enc[i]);
        var dec = AwCrypto.Decrypt(enc, AwCrypto.HeaderKey());
        Assert.IsTrue(dec.AsSpan().SequenceEqual(plain));
    }
}
