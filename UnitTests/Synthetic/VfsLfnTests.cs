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
// Synthetic VFAT long-name tests: a minimal FAT16 image (boot sector, one
// FAT, root dir with a 2-slot LFN entry, one data cluster) is built
// in-memory; listing prefers the long name and lookup opens by it.
// Fully in-memory.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex.FileSystems.Fat;

using Yafex.Fuse;

namespace UnitTests;

[TestFixture]
public class VfsLfnTests
{
    private const string LongName = "HelloWorldLongName.txt";
    private static readonly byte[] Content = Encoding.ASCII.GetBytes("Hello, FAT!\n");

    private static byte LfnChecksum(byte[] shortName)
    {
        byte sum = 0;
        for (int i = 0; i < 11; i++)
            sum = (byte)(((sum & 1) << 7) + (sum >> 1) + shortName[i]);
        return sum;
    }

    private static void PutU16(byte[] buf, int off, ushort v) =>
        BitConverter.GetBytes(v).CopyTo(buf, off);

    // 11 sectors: boot, FAT, root dir, 8 data clusters (1 sector each)
    private static byte[] FatImage()
    {
        var img = new byte[11 * 512];
        // boot sector / BPB
        PutU16(img, 11, 512);
        img[13] = 1;
        PutU16(img, 14, 1);
        img[16] = 1;
        PutU16(img, 17, 16);
        PutU16(img, 19, 11);
        img[21] = 0xF8;
        PutU16(img, 22, 1);
        img[510] = 0x55; img[511] = 0xAA;
        // FAT: media, EOF, cluster 2 = EOF
        img[512] = 0xF8; img[513] = 0xFF; img[514] = 0xFF; img[515] = 0xFF;
        img[516] = 0xFF; img[517] = 0xFF;
        // root dir at sector 2: 2 LFN slots + short entry
        byte[] shortName = Encoding.ASCII.GetBytes("HELLO~1 TXT");
        byte ck = LfnChecksum(shortName);
        // slot for chars 13..21 ("gName.txt"), seq 2|0x40, comes first
        var slot1 = new byte[32];
        slot1[0] = 0x42;
        WriteLfnChars(slot1, LongName.Substring(13));
        slot1[11] = 0x0F; slot1[12] = 0; slot1[13] = ck;
        // slot for chars 0..12 ("HelloWorldLon"), seq 1
        var slot0 = new byte[32];
        slot0[0] = 0x01;
        WriteLfnChars(slot0, LongName.Substring(0, 13));
        slot0[11] = 0x0F; slot0[12] = 0; slot0[13] = ck;
        Array.Copy(slot1, 0, img, 1024, 32);
        Array.Copy(slot0, 0, img, 1056, 32);
        // short entry
        Array.Copy(shortName, 0, img, 1088, 11);
        img[1088 + 11] = 0x20;
        PutU16(img, 1088 + 26, 2);
        BitConverter.GetBytes(Content.Length).CopyTo(img, 1088 + 28);
        // data cluster 2 at sector 3
        Array.Copy(Content, 0, img, 1536, Content.Length);
        return img;
    }

    private static void WriteLfnChars(byte[] slot, string text)
    {
        var chars = new char[13];
        for (int i = 0; i < 13; i++) chars[i] = '\uFFFF';
        for (int i = 0; i < text.Length && i < 13; i++) chars[i] = text[i];
        if (text.Length < 13) chars[text.Length] = '\0';
        for (int i = 0; i < 13; i++)
        {
            int off = i < 5 ? 1 + i * 2 : i < 11 ? 14 + (i - 5) * 2 : 28 + (i - 11) * 2;
            slot[off] = (byte)chars[i];
            slot[off + 1] = (byte)(chars[i] >> 8);
        }
    }

    [Test]
    public void Listing_PrefersLongName()
    {
        var img = new MemoryVirtualFile("lfn.bin", FatImage());
        Assert.IsTrue(VfsFat.TryOpen(img, new List<object>(), out var fs));
        var tree = VfsExt.BuildLazyTree(fs);
        Assert.AreEqual(1, tree.Count);
        Assert.AreEqual(LongName, tree[0].Name);
    }

    [Test]
    public void Lookup_OpensByLongName()
    {
        using var s = new MemoryStream(FatImage());
        var fs = new FatFileSystem(s);
        using var f = fs.OpenFile(LongName, FileMode.Open, FileAccess.Read);
        var buf = new byte[Content.Length];
        int n = f.Read(buf, 0, buf.Length);
        Assert.AreEqual(Content.Length, n);
        Assert.IsTrue(buf.AsSpan().SequenceEqual(Content));
    }

    [Test]
    public void Lookup_StillOpensByShortName()
    {
        using var s = new MemoryStream(FatImage());
        var fs = new FatFileSystem(s);
        using var f = fs.OpenFile("HELLO~1.TXT", FileMode.Open, FileAccess.Read);
        Assert.AreEqual(Content.Length, f.Length);
    }
}
