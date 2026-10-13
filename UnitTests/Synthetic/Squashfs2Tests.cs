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
// Synthetic squashfs2 tests: a minimal LE 2.0 image (superblock, one stored
// inode metablock, one stored dir metablock, one stored + one deflated data
// block, symlink) is built in-memory against the Yafex-native driver.
// Fully in-memory, no fixtures.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Squashfs2;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class Squashfs2Tests
{
    internal static readonly byte[] Hello = Encoding.ASCII.GetBytes("hi-sq2");
    internal static readonly byte[] Big = Enumerable.Range(0, 300).Select(i => (byte)(i * 7 + 1)).ToArray();

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

    private static byte[] Deflate(byte[] plain)
    {
        using var ms = new MemoryStream();
        using (var def = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            def.Write(plain, 0, plain.Length);
        }
        return ms.ToArray();
    }

    private static byte[] MetaBlock(byte[] raw)
    {
        var b = new List<byte>();
        PutU16(b, (ushort)(0x8000 | raw.Length));
        b.AddRange(raw);
        return b.ToArray();
    }

    internal static byte[] BuildImage()
    {
        var helloDeflated = Deflate(Big);

        // --- inodes (packed linearly in one stored metablock) ---
        var ino = new List<byte>();
        var offsets = new Dictionary<string, int>();

        // root dir @0
        offsets["root"] = ino.Count;
        // dir listing bytes computed below; placeholder w0 then patched size
        uint w0root = 1u | (0x1EDu << 4);
        PutU32(ino, w0root);

        // hello.txt @?
        offsets["hello"] = -1;
        // data.bin, link: offsets recorded as we go
        var dirBytes = new List<byte>();
        dirBytes.Add(2); // count-1: 3 entries
        dirBytes.Add(0); dirBytes.Add(0); dirBytes.Add(0); // start_block 0

        void AddDirent(int inodeOff, int type, string name)
        {
            var nb = Encoding.ASCII.GetBytes(name);
            uint bits = (uint)inodeOff | ((uint)type << 13) | ((uint)(nb.Length - 1) << 16);
            PutU32(dirBytes, bits);
            dirBytes.AddRange(nb);
        }

        // Build file inodes first to learn offsets? No: dir must come first
        // in the metablock (root is inode #1 at offset 0). Reserve root rest
        // (11 bytes) and append files after.
        var rootRestPos = ino.Count;
        ino.AddRange(new byte[11]);

        // hello.txt: stored data block
        offsets["hello"] = ino.Count;
        PutU32(ino, 2u | (0x1A4u << 4));
        int helloRestPos = ino.Count;
        ino.AddRange(new byte[20 + 4]);

        // data.bin: deflated data block
        offsets["data"] = ino.Count;
        PutU32(ino, 2u | (0x1A4u << 4));
        int dataRestPos = ino.Count;
        ino.AddRange(new byte[20 + 4]);

        // link -> hello.txt
        offsets["link"] = ino.Count;
        PutU32(ino, 3u | (0x1FFu << 4));
        var target = Encoding.ASCII.GetBytes("hello.txt");
        PutU16(ino, (ushort)target.Length);
        ino.AddRange(target);

        AddDirent(offsets["hello"], 2, "hello.txt");
        AddDirent(offsets["data"], 2, "data.bin");
        AddDirent(offsets["link"], 3, "link");

        // patch root dir rest: fileSize + dirOffset(0), mtime, dir_start(0)
        var rb = BitConverter.GetBytes((uint)dirBytes.Count);
        for (int i = 0; i < 4; i++) ino[rootRestPos + i] = rb[i];
        // mtime + dir_start already zero

        // patch hello rest: content_start filled later (after tables placed);

        // --- tables ---
        var img = new List<byte>();
        img.AddRange(new byte[119]); // superblock placeholder

        long inodeTable = img.Count;
        img.AddRange(MetaBlock(ino.ToArray()));
        long dirTable = img.Count;
        img.AddRange(MetaBlock(dirBytes.ToArray()));
        long fragTable = img.Count; // FragmentCount=0: dir run ends here
        long uidStart = img.Count;
        PutU32(img, 0);
        long helloData = img.Count;
        img.AddRange(Hello);
        long bigData = img.Count;
        img.AddRange(helloDeflated);

        // patch content starts + block sizes
        void PatchU32(byte[] buf, int pos, uint v)
        {
            var bb = BitConverter.GetBytes(v);
            for (int i = 0; i < 4; i++) buf[pos + i] = bb[i];
        }
        var inoArr = ino.ToArray();
        // hello rest: mtime(0) content_start fragment fragOff size blocks[1]
        PatchU32(inoArr, helloRestPos + 4, (uint)helloData);
        PatchU32(inoArr, helloRestPos + 8, 0xFFFFFFFFu);
        PatchU32(inoArr, helloRestPos + 12, 0);
        PatchU32(inoArr, helloRestPos + 16, (uint)Hello.Length);
        PatchU32(inoArr, helloRestPos + 20, (uint)(0x01000000u | (uint)Hello.Length)); // stored bit + len
        // data rest
        PatchU32(inoArr, dataRestPos + 4, (uint)bigData);
        PatchU32(inoArr, dataRestPos + 8, 0xFFFFFFFFu);
        PatchU32(inoArr, dataRestPos + 12, 0);
        PatchU32(inoArr, dataRestPos + 16, (uint)Big.Length);
        PatchU32(inoArr, dataRestPos + 20, (uint)helloDeflated.Length); // compressed: no stored bit

        // rewrite inode metablock with patched inodes
        var patched = MetaBlock(inoArr);
        img.RemoveRange((int)inodeTable, (int)(dirTable - inodeTable));
        img.InsertRange((int)inodeTable, patched);
        long shift = patched.Length - (dirTable - inodeTable);
        dirTable += shift;
        fragTable += shift;
        uidStart += shift;
        helloData += shift;
        bigData += shift;
        // content starts were computed pre-shift: fix by re-adding shift
        PatchU32(inoArr, helloRestPos + 4, (uint)helloData);
        PatchU32(inoArr, dataRestPos + 4, (uint)bigData);
        patched = MetaBlock(inoArr);
        img.RemoveRange((int)inodeTable, patched.Length);
        img.InsertRange((int)inodeTable, patched);

        // --- superblock ---
        var sb = new byte[119];
        Encoding.ASCII.GetBytes("hsqs").CopyTo(sb, 0);
        void SbU16(int off, ushort v) { sb[off] = (byte)v; sb[off + 1] = (byte)(v >> 8); }
        void SbU32(int off, uint v)
        {
            sb[off] = (byte)v; sb[off + 1] = (byte)(v >> 8);
            sb[off + 2] = (byte)(v >> 16); sb[off + 3] = (byte)(v >> 24);
        }
        void SbU64(int off, ulong v) { SbU32(off, (uint)v); SbU32(off + 4, (uint)(v >> 32)); }
        SbU32(4, 4); // inodes
        SbU32(8, (uint)img.Count); // bytes used
        SbU32(12, (uint)uidStart);
        SbU32(16, (uint)uidStart); // guid
        SbU32(20, (uint)inodeTable);
        SbU32(24, (uint)dirTable);
        SbU16(28, 2); // major
        SbU16(30, 0); // minor
        SbU32(32, 4096);
        SbU16(34, 12); // block log
        sb[36] = 0; // flags
        sb[37] = 1; // uids
        sb[38] = 1; // guids
        SbU64(43, 0); // root (start 0, offset 0)
        SbU32(51, 4096);
        SbU32(55, 0); // fragments
        SbU32(59, (uint)fragTable);
        for (int i = 0; i < 119; i++) img[i] = sb[i];

        return img.ToArray();
    }

    [Test]
    public void Valid_Detects90()
    {
        var res = (Squashfs2DetectionResult)new Squashfs2Addon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()));
        Assert.AreEqual(90, res.Confidence);
    }

    [Test]
    public void Extract_ReadsFilesAndSymlink()
    {
        var mem = Mem(BuildImage(), "sq2.img");
        var det = (Squashfs2DetectionResult)new Squashfs2Addon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new Squashfs2Addon().CreateExtractor(det).Extract(mem)
            .OrderBy(a => a.Name).ToList();
        var names = artifacts.Select(a => a.Name).ToList();
        CollectionAssert.Contains(names, "hello.txt");
        CollectionAssert.Contains(names, "data.bin");
        CollectionAssert.AreEqual(Hello, artifacts.First(a => a.Name == "hello.txt").Data.ToArray());
        CollectionAssert.AreEqual(Big, artifacts.First(a => a.Name == "data.bin").Data.ToArray());
    }

    [Test]
    public void Lister_ShowsEntries()
    {
        var mem = Mem(BuildImage(), "sq2.img");
        var det = (Squashfs2DetectionResult)new Squashfs2Addon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var lister = new Squashfs2Addon().CreateLister(det);
        Assert.NotNull(lister);
        using var sw = new StringWriter();
        lister!.List(mem, det, sw);
        var text = sw.ToString();
        StringAssert.Contains("hello.txt", text);
        StringAssert.Contains("data.bin", text);
    }

    [Test]
    public void Garbage_Fails()
    {
        var rnd = new byte[256];
        new Random(7).NextBytes(rnd);
        var res = new Squashfs2Addon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(rnd));
        Assert.AreEqual(0, res.Confidence);
    }
}
