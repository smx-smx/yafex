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
// Synthetic squashfs4 tests: a minimal LE 4.0 image (superblock, one stored
// inode metablock, one stored dir metablock, one stored data block) is built
// in-memory against the Yafex-native driver. Fully in-memory, no fixtures.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Squashfs;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class Squashfs4Tests
{
    internal static readonly byte[] Hello = Encoding.ASCII.GetBytes("hi-sq4");

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

    private static byte[] MetaBlock(byte[] raw)
    {
        var b = new List<byte>();
        PutU16(b, (ushort)(0x8000 | raw.Length));
        b.AddRange(raw);
        return b.ToArray();
    }

    internal static byte[] BuildImage()
    {
        var name = Encoding.ASCII.GetBytes("hello.txt");

        // --- dir listing (header + one record) ---
        var dir = new List<byte>();
        PutU32(dir, 0); // count - 1
        PutU32(dir, 0); // start block (inode metablock 0)
        PutU32(dir, 1); // inode number base
        PutU16(dir, 32); // record: inode offset in metablock
        PutU16(dir, 1); // inode delta (file is inode 2)
        PutU16(dir, 2); // type: regular file
        PutU16(dir, (ushort)(name.Length - 1));
        dir.AddRange(name);

        // --- inodes (root dir @0, file @32, block length follows) ---
        var ino = new List<byte>();
        // root dir inode (32 bytes)
        PutU16(ino, 1); // type dir
        PutU16(ino, 0x1ED); // mode
        PutU16(ino, 0); // uid
        PutU16(ino, 0); // gid
        PutU32(ino, 0); // mtime
        PutU32(ino, 1); // inode number
        PutU32(ino, 0); // start block (dir metablock 0)
        PutU32(ino, 2); // nlink
        PutU16(ino, (ushort)(dir.Count + 3)); // file size
        PutU16(ino, 0); // offset
        PutU32(ino, 1); // parent
        // file inode (32 bytes) at offset 32
        PutU16(ino, 2); // type file
        PutU16(ino, 0x1A4); // mode
        PutU16(ino, 0);
        PutU16(ino, 0);
        PutU32(ino, 0);
        PutU32(ino, 2);
        int fileStartPos = ino.Count; // start_block field index placeholder
        PutU32(ino, 0); // start block (patched later)
        PutU32(ino, 0xFFFFFFFFu); // no fragment
        PutU32(ino, 0); // fragment offset
        PutU32(ino, (uint)Hello.Length);
        PutU32(ino, 0x01000000u | (uint)Hello.Length); // stored data block length

        // --- tables ---
        var img = new List<byte>();
        img.AddRange(new byte[96]); // superblock placeholder
        long inodeTable = img.Count;
        img.AddRange(MetaBlock(ino.ToArray()));
        long dirTable = img.Count;
        img.AddRange(MetaBlock(dir.ToArray()));
        long dataStart = img.Count;
        img.AddRange(Hello);

        // patch file start block (absolute file offset)
        var sb4 = BitConverter.GetBytes((uint)dataStart);
        for (int i = 0; i < 4; i++) img[(int)inodeTable + 2 + fileStartPos + i] = sb4[i];

        // --- superblock ---
        var sb = new byte[96];
        void SbU16(int off, ushort v) { sb[off] = (byte)v; sb[off + 1] = (byte)(v >> 8); }
        void SbU32(int off, uint v)
        {
            sb[off] = (byte)v; sb[off + 1] = (byte)(v >> 8);
            sb[off + 2] = (byte)(v >> 16); sb[off + 3] = (byte)(v >> 24);
        }
        void SbU64(int off, ulong v) { SbU32(off, (uint)v); SbU32(off + 4, (uint)(v >> 32)); }
        Encoding.ASCII.GetBytes("hsqs").CopyTo(sb, 0);
        SbU32(4, 2); // inodes
        SbU32(12, 4096); // block size
        SbU32(16, 0); // fragments
        SbU16(20, 1); // compression: deflate
        SbU16(22, 12); // block log
        SbU16(26, 1); // one uid
        SbU16(28, 4); // major
        SbU16(30, 0); // minor
        SbU64(32, 0); // root inode ref (metablock 0, offset 0)
        SbU64(40, (ulong)img.Count); // bytes used (fixed up below)
        SbU64(48, 0xFFFFFFFFFFFFFFFFul); // no uid table (never resolved for list/extract)
        SbU64(56, 0xFFFFFFFFFFFFFFFFul); // no xattrs
        SbU64(64, (ulong)inodeTable);
        SbU64(72, (ulong)dirTable);
        SbU64(80, 0xFFFFFFFFFFFFFFFFul); // no fragments
        for (int i = 0; i < 96; i++) img[i] = sb[i];

        // fix bytes-used now that the image is complete
        var bu = BitConverter.GetBytes((ulong)img.Count);
        for (int i = 0; i < 8; i++) img[40 + i] = bu[i];

        return img.ToArray();
    }

    [Test]
    public void Valid_Detects90()
    {
        var res = (Squashfs4DetectionResult)new Squashfs4Addon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()));
        Assert.AreEqual(90, res.Confidence);
    }

    [Test]
    public void Lister_ShowsFile()
    {
        var mem = Mem(BuildImage(), "sq4.img");
        var det = (Squashfs4DetectionResult)new Squashfs4Addon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var text = Synthetic.ListTree(new Squashfs4Addon(), det, mem);
        StringAssert.Contains("hello.txt", text);
        StringAssert.Contains($"f {6,12} ", text);
    }

    [Test]
    public void CreateVfs_MountsTreeLazily()
    {
        var mem = Mem(BuildImage(), "sq4.img");
        var addon = new Squashfs4Addon();
        var det = addon.CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var vfs = addon.CreateVfs(mem, det);
        Assert.NotNull(vfs);
        var top = vfs!.Root.Tree["sq4.img"].Node;
        top.EnsureLoaded();
        var file = top.Tree["hello.txt"].Node;
        Assert.AreEqual(6, file.Size);
        CollectionAssert.AreEqual(
            ((Yafex.Fuse.IVfsFile)file).Read(0, 6),
            new Squashfs4Addon().CreateExtractor(det).Extract(mem).Single().Data.ToArray());
    }

    [Test]
    public void Extract_ReadsFile()
    {
        var mem = Mem(BuildImage(), "sq4.img");
        var det = (Squashfs4DetectionResult)new Squashfs4Addon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new Squashfs4Addon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("hello.txt", artifacts[0].Name);
        CollectionAssert.AreEqual(Hello, artifacts[0].Data.ToArray());
    }

    [Test]
    public void Garbage_Fails()
    {
        var rnd = new byte[256];
        new Random(9).NextBytes(rnd);
        var res = new Squashfs4Addon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(rnd));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void XattrsPresent_StillOpens()
    {
        // images carrying an extended-attribute table (e.g. Novatek
        // vendor images) must list/extract; xattr metadata is ignored
        var img = BuildImage();
        var sb = new byte[96];
        Array.Copy(img, sb, 96);
        // point the xattr table somewhere plausible instead of -1
        sb[56] = 0x01;
        Array.Copy(sb, img, 96);
        var mem = Mem(img, "sq4x.img");
        var det = (Squashfs4DetectionResult)new Squashfs4Addon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        Assert.AreEqual(90, det.Confidence);
        var artifacts = new Squashfs4Addon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        CollectionAssert.AreEqual(Hello, artifacts[0].Data.ToArray());
    }
}
