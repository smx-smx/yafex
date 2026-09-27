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
// Synthetic squashfs3 tests: a minimal 3.1 image (superblock, one compressed
// inode metablock, stored dir metablocks, data blocks both compressed and
// stored, one fragment, symlink, empty file, subdir) is built in-memory
// against the DiscUtils driver. Fully in-memory, no fixtures.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using NUnit.Framework;

using DiscUtils;
using DiscUtils.SquashFs3;

using Yafex;
using Yafex.FileFormats.Lzo;

namespace UnitTests;

[TestFixture]
public class Squashfs3Tests
{
    private const int Blog = 12;
    private const int BlockSize = 1 << Blog;

    private static readonly byte[] Hello = Encoding.ASCII.GetBytes("hello squashfs3");
    private static readonly byte[] Big = Enumerable.Range(0, 6000).Select(i => (byte)(i * 13)).ToArray();
    private static readonly byte[] FragData = Encoding.ASCII.GetBytes("fragment tail");
    private static readonly byte[] LinkTarget = Encoding.ASCII.GetBytes("a.txt");

    private static byte[] ZlibWrap(byte[] plain)
    {
        using var deflated = new MemoryStream();
        using (var def = new DeflateStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
        {
            def.Write(plain, 0, plain.Length);
        }
        var body = deflated.ToArray();
        var block = new byte[2 + body.Length + 4];
        block[0] = 0x78;
        block[1] = 0x9C;
        Array.Copy(body, 0, block, 2, body.Length);
        uint adler = LgLzo.Adler32(plain, 0, plain.Length);
        block[^4] = (byte)(adler >> 24);
        block[^3] = (byte)(adler >> 16);
        block[^2] = (byte)(adler >> 8);
        block[^1] = (byte)adler;
        return block;
    }

    private static void PutU16(List<byte> b, ushort v) => b.AddRange(BitConverter.GetBytes(v));
    private static void PutU32(List<byte> b, uint v) => b.AddRange(BitConverter.GetBytes(v));
    private static void PutU64(List<byte> b, ulong v) => b.AddRange(BitConverter.GetBytes(v));

    private static void PutBase(List<byte> b, int type, ushort mode)
    {
        PutU32(b, (uint)(type | (mode << 4))); // uid/guid idx 0
        PutU32(b, 0x4F791449); // mtime
        PutU32(b, 1); // number
    }

    private static void PatchU32(List<byte> b, int at, uint v)
    {
        var bytes = BitConverter.GetBytes(v);
        for (int i = 0; i < 4; i++) b[at + i] = bytes[i];
    }

    private static void PatchU64(List<byte> b, int at, ulong v)
    {
        var bytes = BitConverter.GetBytes(v);
        for (int i = 0; i < 8; i++) b[at + i] = bytes[i];
    }

    private static List<byte> BuildDirContent(List<(string Name, int Type, int InodeOff)> entries, uint blk)
    {
        var c = new List<byte>();
        c.Add((byte)(entries.Count - 1));
        PutU32(c, blk);
        PutU32(c, 0);
        foreach (var e in entries)
        {
            PutU16(c, (ushort)((e.InodeOff & 0x1FFF) | (e.Type << 13)));
            c.Add((byte)(Encoding.ASCII.GetByteCount(e.Name) - 1));
            PutU16(c, 0);
            c.AddRange(Encoding.ASCII.GetBytes(e.Name));
        }
        return c;
    }

    private static byte[] BuildImage()
    {
        var helloBlk = ZlibWrap(Hello);
        var big0Blk = ZlibWrap(Big[..BlockSize]);
        var big1 = Big[BlockSize..];
        var fragBlk = ZlibWrap(FragData);

        // ---- inode table (uncompressed image; offsets tracked) ----
        var itab = new List<byte>();
        var inodeOff = new Dictionary<string, int>();
        var patchDataStart = new Dictionary<string, int>();

        // root dir
        inodeOff["root"] = itab.Count;
        PutBase(itab, 1, 0x1ED);
        PutU32(itab, 2);
        int rootSizeAt = itab.Count; PutU32(itab, 0);
        int rootStartAt = itab.Count; PutU32(itab, 0);
        PutU32(itab, 1);

        void Reg(string name, uint size, ulong dataStart, uint frag, uint fragOff, params uint[] blocks)
        {
            inodeOff[name] = itab.Count;
            PutBase(itab, 2, 0x1A4);
            patchDataStart[name] = itab.Count; PutU64(itab, dataStart);
            PutU32(itab, frag);
            PutU32(itab, fragOff);
            PutU32(itab, size);
            foreach (var bl in blocks) PutU32(itab, bl);
        }

        // placeholders: data starts + block sizes patched after layout
        Reg("a.txt", (uint)Hello.Length, 0, 0xFFFFFFFF, 0, 0);
        int aListAt = itab.Count - 4;
        Reg("big.txt", (uint)Big.Length, 0, 0xFFFFFFFF, 0, 0, 0);
        int bigListAt = itab.Count - 8;
        Reg("frag.txt", (uint)FragData.Length, 0, 0, 0);
        // link
        inodeOff["link"] = itab.Count;
        PutBase(itab, 3, 0x1FF);
        PutU32(itab, 1);
        PutU16(itab, (ushort)LinkTarget.Length);
        itab.AddRange(LinkTarget);
        // empty
        Reg("empty", 0, 0, 0xFFFFFFFF, 0);
        // sub dir
        inodeOff["sub"] = itab.Count;
        PutBase(itab, 1, 0x1ED);
        PutU32(itab, 2);
        int subSizeAt = itab.Count; PutU32(itab, 0);
        int subStartAt = itab.Count; PutU32(itab, 0);
        PutU32(itab, 1);
        // b.txt
        Reg("b.txt", (uint)Hello.Length, 0, 0xFFFFFFFF, 0, 0);
        int bListAt = itab.Count - 4;

        // ---- dir contents ----
        var rootEntries = new List<(string, int, int)>
        {
            ("a.txt", 2, inodeOff["a.txt"]),
            ("big.txt", 2, inodeOff["big.txt"]),
            ("frag.txt", 2, inodeOff["frag.txt"]),
            ("link", 3, inodeOff["link"]),
            ("empty", 2, inodeOff["empty"]),
            ("sub", 1, inodeOff["sub"]),
        };
        var subEntries = new List<(string, int, int)> { ("b.txt", 2, inodeOff["b.txt"]) };
        var rootContent = BuildDirContent(rootEntries, 0);
        var subContent = BuildDirContent(subEntries, 0);
        int rootMetaLen = 2 + rootContent.Count;

        // ---- assembly ----
        var img = new List<byte>();
        img.AddRange(new byte[119]); // superblock placeholder

        long aStart = img.Count; img.AddRange(helloBlk);
        long bigStart = img.Count; img.AddRange(big0Blk); img.AddRange(big1);
        long bStart = img.Count; img.AddRange(helloBlk);
        long fragStart = img.Count; img.AddRange(fragBlk);

        // patch reg data starts + block lists
        PatchU64(itab, patchDataStart["a.txt"], (ulong)aStart);
        PatchU32(itab, aListAt, (uint)helloBlk.Length);
        PatchU64(itab, patchDataStart["big.txt"], (ulong)bigStart);
        PatchU32(itab, bigListAt, (uint)big0Blk.Length);
        PatchU32(itab, bigListAt + 4, (uint)(big1.Length | (1 << 24))); // stored
        PatchU64(itab, patchDataStart["b.txt"], (ulong)bStart);
        PatchU32(itab, bListAt, (uint)helloBlk.Length);

        // patch dir inodes
        PatchU32(itab, rootSizeAt, (uint)(rootContent.Count + 3));
        PatchU32(itab, rootStartAt, 0);
        PatchU32(itab, subSizeAt, (uint)(subContent.Count + 3));
        PatchU32(itab, subStartAt, (uint)rootMetaLen);

        // itab metablock (compressed, like real images)
        long itabOff = img.Count;
        var itabZ = ZlibWrap(itab.ToArray());
        PutU16(img, (ushort)itabZ.Length);
        img.AddRange(itabZ);

        // dtab metablocks (stored)
        long dtabOff = img.Count;
        PutU16(img, (ushort)(rootContent.Count | (1 << 15)));
        img.AddRange(rootContent);
        PutU16(img, (ushort)(subContent.Count | (1 << 15)));
        img.AddRange(subContent);

        // fragment entry metablock (compressed)
        long fragMetaPos = img.Count;
        var fragEntry = new List<byte>();
        PutU64(fragEntry, (ulong)fragStart);
        PutU32(fragEntry, (uint)fragBlk.Length);
        PutU32(fragEntry, 0);
        var fragEntryZ = ZlibWrap(fragEntry.ToArray());
        PutU16(img, (ushort)fragEntryZ.Length);
        img.AddRange(fragEntryZ);

        // fragment index, lookup index (unused), ids
        long ftabOff = img.Count;
        PutU64(img, (ulong)fragMetaPos);
        long lookupOff = img.Count;
        PutU64(img, 0);
        long uidOff = img.Count;
        PutU32(img, 1000);
        long guidOff = img.Count;
        PutU32(img, 100);

        // ---- superblock ----
        var sb = new List<byte>();
        PutU32(sb, 0x73717368);
        PutU32(sb, 8); // inodes
        PutU32(sb, 0); PutU32(sb, 0); PutU32(sb, 0); PutU32(sb, 0); PutU32(sb, 0); // compat
        sb.AddRange(new byte[] { 3, 0, 1, 0 }); // major/minor
        PutU16(sb, (ushort)BlockSize); // block_size_1 (low word)
        PutU16(sb, Blog); // block_log
        sb.Add(0); sb.Add(1); sb.Add(1); // flags/uids/guids
        PutU32(sb, 0x4F791449); // mkfs
        PutU64(sb, 0); // root (start 0, off 0)
        PutU32(sb, (uint)BlockSize);
        PutU32(sb, 1); // fragments
        PutU32(sb, 0); // fragment_table_start_2 (1.x/2.x compat)
        PutU64(sb, 0); // bytes_used (patched)
        PutU64(sb, (ulong)uidOff);
        PutU64(sb, (ulong)guidOff);
        PutU64(sb, (ulong)itabOff);
        PutU64(sb, (ulong)dtabOff);
        PutU64(sb, (ulong)ftabOff);
        PutU64(sb, (ulong)lookupOff);
        if (sb.Count != 119) throw new Exception($"superblock size {sb.Count}");
        for (int i = 0; i < 119; i++) img[i] = sb[i];
        var used = BitConverter.GetBytes((ulong)img.Count);
        for (int i = 0; i < 8; i++) img[63 + i] = used[i];

        return img.ToArray();
    }

    private static Squashfs3FileSystem Open(byte[] image)
    {
        return new Squashfs3FileSystem(new MemoryStream(image, writable: false));
    }

    private static byte[] ReadAll(DiscFileInfo f)
    {
        using var s = f.OpenRead();
        var buf = new byte[s.Length];
        int off = 0;
        while (off < buf.Length)
        {
            int n = s.Read(buf, off, buf.Length - off);
            if (n <= 0) throw new InvalidDataException("short read");
            off += n;
        }
        return buf;
    }

    [Test]
    public void Root_ListsEntries()
    {
        using var fs = Open(BuildImage());
        var names = fs.Root.GetDirectories().Select(d => d.Name)
            .Concat(fs.Root.GetFiles().Select(f => f.Name)).ToList();
        CollectionAssert.AreEquivalent(
            new[] { "sub", "a.txt", "big.txt", "frag.txt", "link", "empty" }, names);
    }

    [Test]
    public void CompressedBlock_Reads()
    {
        using var fs = Open(BuildImage());
        var f = fs.Root.GetFiles().First(x => x.Name == "a.txt");
        CollectionAssert.AreEqual(Hello, ReadAll(f));
    }

    [Test]
    public void MixedBlocks_Read()
    {
        using var fs = Open(BuildImage());
        var f = fs.Root.GetFiles().First(x => x.Name == "big.txt");
        Assert.AreEqual(6000, f.Length);
        CollectionAssert.AreEqual(Big, ReadAll(f));
    }

    [Test]
    public void FragmentFile_Reads()
    {
        using var fs = Open(BuildImage());
        var f = fs.Root.GetFiles().First(x => x.Name == "frag.txt");
        CollectionAssert.AreEqual(FragData, ReadAll(f));
    }

    [Test]
    public void NestedFile_Reads()
    {
        using var fs = Open(BuildImage());
        var sub = fs.Root.GetDirectories().First(x => x.Name == "sub");
        var b = sub.GetFiles().First(x => x.Name == "b.txt");
        CollectionAssert.AreEqual(Hello, ReadAll(b));
    }

    [Test]
    public void EmptyFile_ReadsZero()
    {
        using var fs = Open(BuildImage());
        var f = fs.Root.GetFiles().First(x => x.Name == "empty");
        Assert.AreEqual(0, f.Length);
    }

    [Test]
    public void Detector_HitsAndMisses()
    {
        var hit = new MemoryDataSource(BuildImage()) { Name = "t.sqsh" };
        var result = new Yafex.FileFormats.Squashfs3.Squashfs3Detector().Detect(hit);
        Assert.IsTrue(result.Succeded());
        Assert.AreEqual(90, result.Confidence);
        var miss = new MemoryDataSource(Encoding.ASCII.GetBytes("definitely not squashfs........")) { Name = "x" };
        Assert.IsFalse(new Yafex.FileFormats.Squashfs3.Squashfs3Detector().Detect(miss).Succeded());
    }

    [Test]
    public void BadMagic_Rejected()
    {
        var img = BuildImage();
        img[0] = 0xFF;
        Assert.Throws<IOException>(() => Open(img));
    }
}
