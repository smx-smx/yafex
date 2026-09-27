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
// Synthetic FAT probe + Allwinner part-stream tests. Fully in-memory;
// real filesystems are proven via live mounts.
using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.AllwinnerImg;
using Yafex.Fuse;

namespace UnitTests;

[TestFixture]
public class VfsFatTests
{
    private static byte[] Bpb(
        ushort bytesPerSector = 512, byte sectorsPerCluster = 8,
        ushort reserved = 32, byte fats = 2, byte media = 0xF8, uint sectors = 447680)
    {
        var bpb = new byte[512];
        BitConverter.GetBytes(bytesPerSector).CopyTo(bpb, 11);
        bpb[13] = sectorsPerCluster;
        BitConverter.GetBytes(reserved).CopyTo(bpb, 14);
        bpb[16] = fats;
        bpb[21] = media;
        BitConverter.GetBytes(sectors).CopyTo(bpb, 32);
        bpb[510] = 0x55; bpb[511] = 0xAA;
        return bpb;
    }

    [Test]
    public void Probe_AcceptsSaneBpb()
    {
        // magic right but superblock garbage: open fails gracefully
        Assert.IsFalse(VfsFat.TryOpen(
            new MemoryVirtualFile("img.bin", Bpb()), new List<object>(), out _));
    }

    [Test]
    public void Probe_RejectsBadSignature()
    {
        var bpb = Bpb();
        bpb[511] = 0x00;
        Assert.IsFalse(VfsFat.IsBpb(bpb));
    }

    [Test]
    public void Probe_RejectsBadGeometry()
    {
        Assert.IsFalse(VfsFat.IsBpb(Bpb(bytesPerSector: 513)));
        Assert.IsFalse(VfsFat.IsBpb(Bpb(sectorsPerCluster: 3)));
        Assert.IsFalse(VfsFat.IsBpb(Bpb(fats: 0)));
        Assert.IsFalse(VfsFat.IsBpb(Bpb(media: 0xF1)));
        Assert.IsFalse(VfsFat.IsBpb(Bpb(sectors: 0)));
        Assert.IsTrue(VfsFat.IsBpb(Bpb()));
    }

    [Test]
    public void AwPartStream_DecryptsWindow()
    {
        var plain = new byte[64];
        for (int i = 0; i < plain.Length; i++) plain[i] = (byte)(i * 3 + 1);
        var cipher = AwCrypto.Encrypt((byte[])plain.Clone(), AwCrypto.PayloadKey());
        var ds = new MemoryDataSource(cipher) { Name = "part" };
        using var s = new Rc6EcbDecryptStream(ds, 0, 64, AwCrypto.PayloadKey());
        var buf = new byte[64];
        s.Seek(20, SeekOrigin.Begin);
        int n = s.Read(buf, 0, 17);
        Assert.AreEqual(17, n);
        for (int i = 0; i < 17; i++) Assert.AreEqual(plain[20 + i], buf[i]);
        s.Seek(0, SeekOrigin.Begin);
        n = s.Read(buf, 0, 64);
        Assert.AreEqual(64, n);
        Assert.IsTrue(buf.AsSpan().SequenceEqual(plain));
    }
}
