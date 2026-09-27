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
// Synthetic cramfs tests: a minimal image (superblock, root dir, a zlib file,
// a symlink, a subdir, an XIP raw file, an empty file) is built in-memory.
// Fully in-memory, no fixtures.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Cramfs;
using Yafex.FileFormats.Lzo;

namespace UnitTests;

[TestFixture]
public class CramfsTests
{
    private static readonly byte[] Hello = Encoding.ASCII.GetBytes("hello");
    private static readonly byte[] LinkTarget = Encoding.ASCII.GetBytes("a.txt");
    private static readonly byte[] Big = Enumerable.Range(0, 3000).Select(i => (byte)('A' + (i % 26))).ToArray();
    private static readonly byte[] Raw = Enumerable.Range(0, 6000).Select(i => (byte)(i * 7)).ToArray();

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

    private sealed class Entry
    {
        public ushort Mode;
        public uint Size;
        public byte Gid = 5;
        public string Name = "";
        public long DataOffset;
    }

    private static void PutU32(List<byte> b, uint v) => b.AddRange(BitConverter.GetBytes(v));

    private static void PutInode(List<byte> b, ushort mode, uint size, byte gid, string name, long dataOffset)
    {
        int nl = (Encoding.ASCII.GetByteCount(name) + 3) / 4;
        PutU32(b, (uint)(mode | (0 << 16)));
        PutU32(b, (size & 0xFFFFFF) | ((uint)gid << 24));
        PutU32(b, (uint)nl | (uint)((dataOffset >> 2) << 6));
        var nameBytes = new byte[nl * 4];
        Encoding.ASCII.GetBytes(name).CopyTo(nameBytes, 0);
        b.AddRange(nameBytes);
    }

    private static long Align(long v, long a) => (v + a - 1) & ~(a - 1);

    private static byte[] BuildImage()
    {
        var helloBlk = ZlibWrap(Hello);
        var linkBlk = ZlibWrap(LinkTarget);
        var bigBlk = ZlibWrap(Big);

        // layout pass: data region starts after root entries
        var rootEntries = new List<Entry>
        {
            new() { Mode = 0x81A4, Size = (uint)Hello.Length, Name = "a.txt" },
            new() { Mode = 0xA1FF, Size = (uint)LinkTarget.Length, Name = "link" },
            new() { Mode = 0x41ED, Size = 20, Name = "sub" },
            new() { Mode = 0x83ED, Size = (uint)Raw.Length, Name = "raw.bin" },
            new() { Mode = 0x81A4, Size = 0, Name = "empty" },
        };
        long rootSize = rootEntries.Sum(e => 12 + ((Encoding.ASCII.GetByteCount(e.Name) + 3) / 4) * 4);
        long cursor = 76 + rootSize;

        var blocks = new Dictionary<string, byte[]>
        {
            ["a.txt"] = helloBlk,
            ["link"] = linkBlk,
            ["b.txt"] = bigBlk,
        };
        var offsets = new Dictionary<string, long>();
        foreach (var name in new[] { "a.txt", "link" })
        {
            offsets[name] = cursor;
            cursor += 4 + blocks[name].Length; // 1 pointer + block
            cursor = Align(cursor, 4);
        }
        // sub dir entries
        long subOff = cursor;
        cursor += 20; // single b.txt entry
        // sub file data
        offsets["b.txt"] = cursor;
        cursor += 4 + bigBlk.Length;
        cursor = Align(cursor, 4);
        // raw file: record unaligned offset, store page-aligned
        long rawOff = cursor;
        long rawAligned = Align(cursor, 4096);
        cursor = rawAligned + Raw.Length;

        var img = new List<byte>();
        // superblock
        PutU32(img, CramfsReader.Magic);
        PutU32(img, 0); // size patched later
        PutU32(img, 3); // flags FSIDv2|SORTED
        PutU32(img, 0); // future
        img.AddRange(Encoding.ASCII.GetBytes("Compressed ROMFS"));
        img.AddRange(new byte[16]); // fsid
        var nm = new byte[16];
        Encoding.ASCII.GetBytes("test").CopyTo(nm, 0);
        img.AddRange(nm);
        // root inode
        PutU32(img, 0x41ED);
        PutU32(img, (uint)rootSize | (5u << 24));
        PutU32(img, (uint)(76 >> 2) << 6);
        // root entries
        foreach (var e in rootEntries)
        {
            long off = e.Name == "sub" ? subOff : e.Name == "raw.bin" ? rawOff : e.Name == "empty" ? 0 : offsets[e.Name];
            PutInode(img, e.Mode, e.Size, e.Gid, e.Name, off);
        }
        // file data
        while (img.Count < offsets["a.txt"]) img.Add(0);
        foreach (var name in new[] { "a.txt", "link" })
        {
            while (img.Count < offsets[name]) img.Add(0);
            PutU32(img, (uint)(offsets[name] + 4 + blocks[name].Length)); // END offset
            img.AddRange(blocks[name]);
        }
        while (img.Count < subOff) img.Add(0);
        PutInode(img, 0x81A4, (uint)Big.Length, 5, "b.txt", offsets["b.txt"]);
        while (img.Count < offsets["b.txt"]) img.Add(0);
        PutU32(img, (uint)(offsets["b.txt"] + 4 + bigBlk.Length));
        img.AddRange(bigBlk);
        while (img.Count < rawAligned) img.Add(0);
        img.AddRange(Raw);

        var bytes = img.ToArray();
        Array.Copy(BitConverter.GetBytes((uint)bytes.Length), 0, bytes, 4, 4);
        return bytes;
    }

    private static CramfsReader Open()
    {
        return new CramfsReader(BuildImage());
    }

    private static CramfsInode Find(CramfsReader fs, CramfsInode dir, string name)
    {
        return fs.ReadDirectory(dir).First(e => e.Name == name);
    }

    [Test]
    public void Superblock_Parses()
    {
        var fs = Open();
        Assert.AreEqual(3u, fs.Flags);
        Assert.AreEqual("test", fs.VolumeName);
        Assert.IsTrue(fs.Root.IsDirectory);
        Assert.AreEqual(5, fs.RootGid);
    }

    [Test]
    public void Root_ListsFiveEntries()
    {
        var fs = Open();
        var names = fs.ReadDirectory(fs.Root).Select(e => e.Name).ToList();
        CollectionAssert.AreEquivalent(new[] { "a.txt", "link", "sub", "raw.bin", "empty" }, names);
    }

    [Test]
    public void ZlibFile_Reads()
    {
        var fs = Open();
        var a = Find(fs, fs.Root, "a.txt");
        Assert.IsTrue(a.IsRegular);
        CollectionAssert.AreEqual(Hello, fs.ReadFile(a));
    }

    [Test]
    public void NestedFile_Reads()
    {
        var fs = Open();
        var sub = Find(fs, fs.Root, "sub");
        Assert.IsTrue(sub.IsDirectory);
        var b = Find(fs, sub, "b.txt");
        CollectionAssert.AreEqual(Big, fs.ReadFile(b));
    }

    [Test]
    public void Symlink_Resolves()
    {
        var fs = Open();
        var link = Find(fs, fs.Root, "link");
        Assert.IsTrue(link.IsSymlink);
        Assert.AreEqual("a.txt", fs.ReadSymlink(link));
    }

    [Test]
    public void RawFile_ReadsUnalignedPayload()
    {
        var fs = Open();
        var raw = Find(fs, fs.Root, "raw.bin");
        Assert.IsTrue(raw.IsRaw);
        CollectionAssert.AreEqual(Raw, fs.ReadFile(raw));
    }

    [Test]
    public void EmptyFile_ReadsZero()
    {
        var fs = Open();
        var empty = Find(fs, fs.Root, "empty");
        Assert.AreEqual(0, fs.ReadFile(empty).Length);
    }

    [Test]
    public void LgSizeExtension_MatchesReleaseValues()
    {
        // RELEASE in lgap.pak: size24 0x388A60, gid 0xC9, root gid 0xC8
        Assert.AreEqual(20482656u, CramfsReader.ExtendSize(0x388A60, 0xC9, 0xC8));
        Assert.AreEqual(272u, CramfsReader.ExtendSize(272, 0xC8, 0xC8));
    }

    [Test]
    public void Detector_HitsAndMisses()
    {
        var hit = new MemoryDataSource(BuildImage()) { Name = "t.cramfs" };
        // detector comes with the format wiring commit; magic probe here
        Assert.AreEqual(CramfsReader.Magic, BitConverter.ToUInt32(hit.Data.ToArray(), 0));
        var miss = new MemoryDataSource(Encoding.ASCII.GetBytes("definitely not cramfs........")) { Name = "x" };
        Assert.AreNotEqual(CramfsReader.Magic, BitConverter.ToUInt32(miss.Data.ToArray(), 0));
    }

    [Test]
    public void BadMagic_Rejected()
    {
        var img = BuildImage();
        img[0] = 0xFF;
        Assert.Throws<InvalidDataException>(() => new CramfsReader(img));
    }
}
