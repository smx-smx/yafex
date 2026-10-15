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
// Synthetic UBIFS tests: a minimal volume (superblock, root/file/subdir/
// symlink inodes, R5-keyed dentries, raw + zlib data nodes across two
// LEBs) is built in-memory against the Yafex-native driver.
// Fully in-memory, no fixtures.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Ubifs;
using Yafex.Fuse;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class UbifsTests
{
    internal const int LebSize = 4096;
    internal static readonly byte[] Hello = Encoding.ASCII.GetBytes("hi-ubi");
    internal static readonly byte[] Big = Enumerable.Range(0, 300).Select(i => (byte)(i * 7 + 1)).ToArray();
    internal static readonly byte[] LinkTarget = Encoding.ASCII.GetBytes("hello.txt");

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
            table[i] = crc;
        }
        return table;
    }

    private static uint R5(byte[] name)
    {
        uint a = 0;
        foreach (byte c in name)
        {
            a += (uint)((c << 4) + (c >> 4));
            a *= 11;
        }
        return a;
    }

    private static void PutU16(List<byte> b, ushort v)
    {
        b.Add((byte)v);
        b.Add((byte)(v >> 8));
    }

    private static void PutU32(List<byte> b, uint v)
    {
        b.Add((byte)v);
        b.Add((byte)(v >> 8));
        b.Add((byte)(v >> 16));
        b.Add((byte)(v >> 24));
    }

    private static void PutU64(List<byte> b, ulong v)
    {
        PutU32(b, (uint)v);
        PutU32(b, (uint)(v >> 32));
    }

    private static byte[] Node(int type, ulong sqnum, byte[] body)
    {
        var n = new List<byte>();
        PutU32(n, 0x06101831);
        PutU32(n, 0); // crc placeholder
        PutU64(n, sqnum);
        PutU32(n, (uint)(24 + body.Length));
        PutU32(n, (uint)type);
        n.AddRange(body);
        var arr = n.ToArray();
        uint crc = 0xFFFFFFFFu;
        for (int i = 8; i < arr.Length; i++)
        {
            crc = CrcTable[(crc ^ arr[i]) & 0xFF] ^ (crc >> 8);
        }
        var cb = BitConverter.GetBytes(crc);
        Array.Copy(cb, 0, arr, 4, 4);
        // nodes sit 8-aligned in a LEB; pad with zeros like real images
        if (arr.Length % 8 != 0)
        {
            var padded = new byte[arr.Length + (8 - arr.Length % 8)];
            Array.Copy(arr, padded, arr.Length);
            return padded;
        }
        return arr;
    }

    private static byte[] Ino(uint inum, ulong sqnum, uint mode, ulong size, byte[] inlineData)
    {
        var b = new List<byte>();
        PutU32(b, inum);
        PutU32(b, 0); // INO key
        b.AddRange(new byte[16]); // creat + padding
        PutU64(b, size); // abs 48
        b.AddRange(new byte[32]); // times
        PutU32(b, 0); // uid, abs 88
        PutU32(b, 0); // gid, abs 92
        b.AddRange(new byte[8]); // reserved, abs 96
        PutU32(b, mode); // abs 104
        PutU32(b, 1); // nlink, abs 108
        while (b.Count < 136) b.Add(0); // fixed part is 160 total with header
        b.AddRange(inlineData);
        return Node(0, sqnum, b.ToArray());
    }

    private static byte[] Dent(uint parent, string name, uint child, ulong sqnum)
    {
        var nb = Encoding.UTF8.GetBytes(name);
        var b = new List<byte>();
        PutU32(b, parent);
        PutU32(b, (2u << 29) | (R5(nb) & 0x1FFFFFFFu));
        b.AddRange(new byte[8]); // reserved
        PutU64(b, child);
        b.Add(0); // pad
        b.Add(0); // type unknown
        b.Add((byte)nb.Length);
        b.Add(0); // pad
        b.AddRange(nb);
        return Node(2, sqnum, b.ToArray());
    }

    private static byte[] Data(uint inum, uint block, byte[] payload, uint compr, ulong sqnum)
    {
        var b = new List<byte>();
        PutU32(b, inum);
        PutU32(b, (1u << 29) | block);
        b.AddRange(new byte[8]); // reserved
        PutU32(b, (uint)payload.Length);
        PutU32(b, compr);
        b.AddRange(payload);
        return Node(1, sqnum, b.ToArray());
    }

    private static byte[] ZlibWrap(byte[] plain)
    {
        using var ms = new MemoryStream();
        using (var def = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            def.Write(plain, 0, plain.Length);
        }
        return ms.ToArray();
    }

    internal static byte[] BuildImage()
    {
        var leb0 = new List<byte>();
        var leb1 = new List<byte>();
        ulong sq = 1;
        // superblock (leb_size lives at +36)
        var sb = new List<byte>();
        sb.AddRange(new byte[8]);
        sb.AddRange(new byte[4]);
        PutU32(sb, (uint)LebSize);
        while (sb.Count < 72) sb.Add(0);
        leb0.AddRange(Node(6, sq++, sb.ToArray()));
        // root dir + entries
        leb0.AddRange(Ino(1, sq++, 0x41ED, 0, Array.Empty<byte>()));
        leb0.AddRange(Dent(1, "hello.txt", 2, sq++));
        leb0.AddRange(Dent(1, "sub", 3, sq++));
        leb0.AddRange(Dent(1, "link", 5, sq++));
        leb0.AddRange(Ino(2, sq++, 0x81A4, (ulong)Hello.Length, Array.Empty<byte>()));
        leb0.AddRange(Data(2, 0, Hello, 0, sq++));
        leb0.AddRange(Ino(3, sq++, 0x41ED, 0, Array.Empty<byte>()));
        // second LEB: subdir content + compressed file + symlink
        leb1.AddRange(Dent(3, "deep.bin", 4, sq++));
        leb1.AddRange(Ino(4, sq++, 0x81A4, (ulong)Big.Length, Array.Empty<byte>()));
        leb1.AddRange(Data(4, 0, ZlibWrap(Big), 2, sq++));
        leb1.AddRange(Ino(5, sq++, 0xA1FF, (ulong)LinkTarget.Length, LinkTarget));
        var img0 = new byte[LebSize];
        Array.Copy(leb0.ToArray(), img0, Math.Min(leb0.Count, LebSize));
        var img1 = new byte[LebSize];
        Array.Copy(leb1.ToArray(), img1, Math.Min(leb1.Count, LebSize));
        using var ms = new MemoryStream();
        ms.Write(img0, 0, img0.Length);
        ms.Write(img1, 0, img1.Length);
        return ms.ToArray();
    }

    [Test]
    public void Valid_Detects90()
    {
        var res = (UbifsDetectionResult)new UbifsAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()));
        Assert.AreEqual(90, res.Confidence);
    }

    [Test]
    public void Lister_ShowsTree()
    {
        var mem = Mem(BuildImage(), "v.ubifs");
        var det = (UbifsDetectionResult)new UbifsAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var text = Synthetic.ListTree(new UbifsAddon(), det, mem);
        StringAssert.Contains("hello.txt", text);
        StringAssert.Contains("sub/deep.bin", text);
        StringAssert.Contains("link", text);
    }

    [Test]
    public void Extract_ReadsFilesAndSymlink()
    {
        var mem = Mem(BuildImage(), "v.ubifs");
        var det = (UbifsDetectionResult)new UbifsAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new UbifsAddon().CreateExtractor(det).Extract(mem)
            .OrderBy(a => a.Name).ToList();
        var names = artifacts.Select(a => a.Name).ToList();
        CollectionAssert.Contains(names, "hello.txt");
        CollectionAssert.Contains(names, "sub_deep.bin");
        CollectionAssert.Contains(names, "link");
        CollectionAssert.AreEqual(Hello, artifacts.First(a => a.Name == "hello.txt").Data.ToArray());
        CollectionAssert.AreEqual(Big, artifacts.First(a => a.Name == "sub_deep.bin").Data.ToArray());
        CollectionAssert.AreEqual(LinkTarget, artifacts.First(a => a.Name == "link").Data.ToArray());
    }

    [Test]
    public void Expander_ListsMembers()
    {
        var exp = new UbifsExpander();
        Assert.IsTrue(exp.Matches(new MemoryVirtualFile("v.ubifs", BuildImage())));
        var node = exp.Expand(new MemoryVirtualFile("v.ubifs", BuildImage()), new List<object>());
        node.EnsureLoaded();
        var names = node.Tree.Keys.ToList();
        CollectionAssert.Contains(names, "hello.txt");
        CollectionAssert.Contains(names, "sub");
    }

    [Test]
    public void Garbage_Fails()
    {
        var rnd = new byte[256];
        new Random(21).NextBytes(rnd);
        var res = new UbifsAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(rnd));
        Assert.AreEqual(0, res.Confidence);
    }
}
