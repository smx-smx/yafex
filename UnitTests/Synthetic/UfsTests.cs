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
// Synthetic UFS tests. Fully in-memory: no disk, no samples. Minimal
// single-group UFS1/UFS2 images (either endianness) built field by field
// per FreeBSD sys/ufs/ffs/fs.h, dinode.h and dir.h numeric layouts.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Ufs;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class UfsTests
{
    private const uint Mtime = 1700000000;

    private class Writer
    {
        private readonly List<byte> _b;
        private readonly bool _be;
        public Writer(List<byte> b, bool be) { _b = b; _be = be; }
        public void U16(ushort v)
        {
            if (_be) { _b.Add((byte)(v >> 8)); _b.Add((byte)v); }
            else { _b.Add((byte)v); _b.Add((byte)(v >> 8)); }
        }
        public void U32(uint v)
        {
            if (_be) { _b.Add((byte)(v >> 24)); _b.Add((byte)(v >> 16)); _b.Add((byte)(v >> 8)); _b.Add((byte)v); }
            else { _b.Add((byte)v); _b.Add((byte)(v >> 8)); _b.Add((byte)(v >> 16)); _b.Add((byte)(v >> 24)); }
        }
        public void U64(ulong v)
        {
            if (_be) { U32((uint)(v >> 32)); U32((uint)v); }
            else { U32((uint)v); U32((uint)(v >> 32)); }
        }
        public void Bytes(byte[] data) { _b.AddRange(data); }
        public void Zeros(int n) { for (int i = 0; i < n; i++) _b.Add(0); }
    }

    private static void PutDirent(List<byte> img, uint ino, byte dtype, string name, bool be = false)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        int reclen = (8 + nameBytes.Length + 1 + 3) & ~3;
        int start = img.Count;
        if (be)
        {
            img.Add((byte)(ino >> 24)); img.Add((byte)(ino >> 16)); img.Add((byte)(ino >> 8)); img.Add((byte)ino);
            img.Add((byte)(reclen >> 8)); img.Add((byte)reclen);
        }
        else
        {
            img.Add((byte)ino); img.Add((byte)(ino >> 8)); img.Add((byte)(ino >> 16)); img.Add((byte)(ino >> 24));
            img.Add((byte)reclen); img.Add((byte)(reclen >> 8));
        }
        img.Add(dtype);
        img.Add((byte)nameBytes.Length);
        img.AddRange(nameBytes);
        while (img.Count - start < reclen) img.Add(0);
    }

    // Minimal UFS1 image: root + 13-byte file (frag tail) + short symlink.
    internal static byte[] BuildUfs1(bool be = false)
    {
        int fsize = 1024, bsize = 4096;
        var img = new List<byte>(new byte[64 * 1024]);
        var w = new Writer(img, be);
        // superblock at byte 8192
        int sb = 8192;
        Action<int, uint> at32 = (off, v) =>
        {
            var tmp = new List<byte>(); new Writer(tmp, be).U32(v);
            for (int i = 0; i < 4; i++) img[sb + off + i] = tmp[i];
        };
        Action<int, ushort> at16 = (off, v) =>
        {
            var tmp = new List<byte>(); new Writer(tmp, be).U16(v);
            for (int i = 0; i < 2; i++) img[sb + off + i] = tmp[i];
        };
        Action<int, ulong> at64 = (off, v) =>
        {
            var tmp = new List<byte>(); new Writer(tmp, be).U64(v);
            for (int i = 0; i < 8; i++) img[sb + off + i] = tmp[i];
        };
        at32(8, 8); at32(12, 16); at32(16, 24); at32(20, 32);
        at32(24, 0); at32(28, 0);
        at32(32, Mtime); at32(36, 64); at32(40, 60); at32(44, 1);
        at32(48, (uint)bsize); at32(52, (uint)fsize); at32(56, 4); at32(60, 8);
        at32(64, 0); at32(68, 60);
        at32(72, 0xFFFFF000); at32(76, 0xFFFFFC00);
        at32(80, 12); at32(84, 10);
        at32(88, 1); at32(92, 16); at32(96, 2); at32(100, 1); at32(104, 8192);
        at32(116, 1024); at32(120, 32); at32(124, 8); at32(128, 0);
        at32(144, 0x12345678); at32(148, 0x9ABCDEF0);
        at32(160, 128); at32(184, 16); at32(188, 64);
        at32(1312, 0); at32(1320, 60); at32(1324, 2);
        at64(1336, 0xFFFFFFFFFFFFF000UL); at64(1344, 0xFFFFFFFFFFFFFC00UL);
        at32(1352, 0); at32(1372, 0x00019554);
        // inodes at frag 24 (byte 24576), 128 bytes each
        Action<uint, ushort, ulong, uint> inode = (ino, mode, size, db0) =>
        {
            int pos = 24576 + (int)(ino % 32) * 128;
            var tmp = new List<byte>(); var tw = new Writer(tmp, be);
            tw.U16(mode); tw.U16(ino == 2 ? (ushort)2 : (ushort)1);
            tw.U32(0); // dirdepth/freelink
            tw.U64(size);
            tw.U32(Mtime); tw.U32(0); tw.U32(Mtime); tw.U32(0); tw.U32(Mtime); tw.U32(0);
            for (int i = 0; i < 12; i++) tw.U32(i == 0 ? db0 : 0);
            for (int i = 0; i < 3; i++) tw.U32(0);
            while (tmp.Count < 128) tmp.Add(0);
            for (int i = 0; i < 128; i++) img[pos + i] = tmp[i];
        };
        inode(2, 0x41ED, 512, 32);
        inode(3, 0x81A4, 12, 33);
        // symlink ino 4 -> "hello.txt" via shortlink
        {
            int pos = 24576 + 4 * 128;
            var tmp = new List<byte>(); var tw = new Writer(tmp, be);
            tw.U16(0xA1FF); tw.U16(1); tw.U32(0); tw.U64(9);
            tw.U32(Mtime); tw.U32(0); tw.U32(Mtime); tw.U32(0); tw.U32(Mtime); tw.U32(0);
            var link = Encoding.ASCII.GetBytes("hello.txt");
            tmp.AddRange(link);
            while (tmp.Count < 128) tmp.Add(0);
            for (int i = 0; i < 128; i++) img[pos + i] = tmp[i];
        }
        // directory block at frag 32
        {
            var dir = new List<byte>();
            PutDirent(dir, 2, 4, ".", be);
            PutDirent(dir, 2, 4, "..", be);
            PutDirent(dir, 3, 8, "hello.txt", be);
            PutDirent(dir, 4, 10, "link", be);
            while (dir.Count < 512) dir.Add(0);
            for (int i = 0; i < 512; i++) img[32768 + i] = dir[i];
        }
        // file payload at frag 33
        {
            var payload = Encoding.ASCII.GetBytes("hello, ufs1!");
            for (int i = 0; i < payload.Length; i++) img[33792 + i] = payload[i];
        }
        return img.ToArray();
    }

    // Minimal UFS2 image with a 12-block + tail file (single-indirect path).
    // fsize == bsize here (frag=1); frag tails are covered by the UFS1 test.
    internal static byte[] BuildUfs2()
    {
        int fsize = 8192, bsize = 8192;
        var img = new List<byte>(new byte[32 * 8192]);
        int sb = 65536;
        Action<int, uint> at32 = (off, v) =>
        {
            img[sb + off] = (byte)v; img[sb + off + 1] = (byte)(v >> 8);
            img[sb + off + 2] = (byte)(v >> 16); img[sb + off + 3] = (byte)(v >> 24);
        };
        Action<int, ulong> at64 = (off, v) =>
        {
            at32(off, (uint)v); at32(off + 4, (uint)(v >> 32));
        };
        at32(8, 8); at32(12, 9); at32(16, 10); at32(20, 11);
        at32(44, 1);
        at32(48, (uint)bsize); at32(52, (uint)fsize); at32(56, 1); at32(60, 8);
        at32(68, 60);
        at32(72, 0xFFFFE000); at32(76, 0xFFFFE000);
        at32(80, 13); at32(84, 13);
        at32(88, 1); at32(92, 16); at32(96, 0); at32(100, 4); at32(104, 8192);
        at32(116, 1024); at32(120, 32); at32(124, 16); at32(128, 0);
        at32(144, 0x12345678); at32(148, 0x9ABCDEF0);
        at32(160, 256); at32(184, 32); at32(188, 32);
        at64(1072, Mtime); at64(1080, 32);
        at32(1312, 0); at32(1320, 60); at32(1324, 2);
        at64(1336, 0xFFFFFFFFFFFFE000UL); at64(1344, 0xFFFFFFFFFFFFFC00UL);
        at32(1352, 0); at32(1372, 0x19540119);
        // inodes at frag 10 (byte 81920), 256 bytes each
        long fileSize = 12L * bsize + 500;
        Action<uint, ushort, ulong> inode = (ino, mode, size) =>
        {
            int pos = 81920 + (int)(ino % 32) * 256;
            var tmp = new List<byte>();
            tmp.Add((byte)mode); tmp.Add((byte)(mode >> 8));
            tmp.Add((byte)(ino == 2 ? 2 : 1)); tmp.Add(0);
            for (int i = 0; i < 12; i++) tmp.Add(0); // uid/gid/blksize @4..15
            Action<ulong> u64 = v =>
            {
                tmp.Add((byte)v); tmp.Add((byte)(v >> 8)); tmp.Add((byte)(v >> 16)); tmp.Add((byte)(v >> 24));
                tmp.Add((byte)(v >> 32)); tmp.Add((byte)(v >> 40)); tmp.Add((byte)(v >> 48)); tmp.Add((byte)(v >> 56));
            };
            u64(size); // @16
            u64(0); // blocks
            u64(Mtime); u64(Mtime); u64(Mtime); u64(Mtime); // times @32..63
            for (int i = 0; i < 4; i++) tmp.Add(0); // nsec @64..79? (4x u32)
            for (int i = 0; i < 4; i++) tmp.Add(0);
            for (int i = 0; i < 4; i++) tmp.Add(0);
            for (int i = 0; i < 4; i++) tmp.Add(0);
            while (tmp.Count < 80) tmp.Add(0);
            // gen/kernflags/flags/extsize @80..95
            while (tmp.Count < 96) tmp.Add(0);
            // extb[2] @96..111
            while (tmp.Count < 112) tmp.Add(0);
            if (ino == 3)
            {
                for (int i = 0; i < 12; i++) u64((ulong)(12 + i)); // db @112
                u64(24); u64(0); u64(0); // ib @208
            }
            else if (ino == 4)
            {
                var link = Encoding.ASCII.GetBytes("hello.txt");
                tmp.AddRange(link);
                while (tmp.Count < 232) tmp.Add(0);
            }
            else
            {
                u64(11); // db0 -> dir frag
                while (tmp.Count < 232) tmp.Add(0);
            }
            while (tmp.Count < 256) tmp.Add(0);
            for (int i = 0; i < 256; i++) img[pos + i] = tmp[i];
        };
        inode(2, 0x41ED, 512);
        inode(3, 0x81A4, (ulong)fileSize);
        inode(4, 0xA1FF, 9);
        // directory block at frag 11
        {
            var dir = new List<byte>();
            PutDirent(dir, 2, 4, ".");
            PutDirent(dir, 2, 4, "..");
            PutDirent(dir, 3, 8, "big.bin");
            PutDirent(dir, 4, 10, "link");
            while (dir.Count < 512) dir.Add(0);
            for (int i = 0; i < 512; i++) img[11 * 8192 + i] = dir[i];
        }
        // direct data frags 12..23 + indirect block 24 -> frag 25
        for (int lbn = 0; lbn < 12; lbn++)
        {
            byte fill = (byte)(lbn + 1);
            for (int i = 0; i < bsize; i++) img[(12 + lbn) * 8192 + i] = fill;
        }
        for (int i = 0; i < 500; i++) img[25 * 8192 + i] = 0x5A;
        // single-indirect pointer block at frag 24
        img[24 * 8192] = 25; // frag 25 LE u64 (rest zero)
        return img.ToArray();
    }

    [Test]
    public void Ufs1_Detects90()
    {
        var res = (UfsDetectionResult)new UfsAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildUfs1()));
        Assert.AreEqual(90, res.Confidence);
    }

    [Test]
    public void Ufs1BE_Detects90()
    {
        var res = (UfsDetectionResult)new UfsAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildUfs1(be: true)));
        Assert.AreEqual(90, res.Confidence);
    }

    [Test]
    public void Ufs2_Detects90()
    {
        var res = (UfsDetectionResult)new UfsAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildUfs2()));
        Assert.AreEqual(90, res.Confidence);
    }

    [Test]
    public void Ufs1_Extract_ReadsFiles()
    {
        var mem = Mem(BuildUfs1(), "part.bin");
        var det = (UfsDetectionResult)new UfsAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new UfsAddon().CreateExtractor(det).Extract(mem)
            .OrderBy(a => a.Name).ToList();
        Assert.AreEqual(2, artifacts.Count);
        Assert.AreEqual("hello.txt", artifacts[0].Name);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("hello, ufs1!"), artifacts[0].Data.ToArray());
        Assert.AreEqual("link", artifacts[1].Name);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("hello.txt"), artifacts[1].Data.ToArray());
    }

    [Test]
    public void Ufs2_ReadsIndirectBlocks()
    {
        using var stream = new MemoryStream(BuildUfs2(), writable: false);
        var ctx = DiscUtils.Ufs.UfsContext.Open(stream);
        var file = ctx.ReadFile(3);
        Assert.AreEqual(12 * 8192 + 500, file.Length);
        for (int lbn = 0; lbn < 12; lbn++)
        {
            Assert.AreEqual((byte)(lbn + 1), file[lbn * 8192]);
            Assert.AreEqual((byte)(lbn + 1), file[lbn * 8192 + 8191]);
        }
        for (int i = 0; i < 500; i++)
        {
            Assert.AreEqual(0x5A, file[12 * 8192 + i]);
        }
        var link = ctx.ReadSymlink(4);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("hello.txt"), link);
    }

    [Test]
    public void Garbage_Detects0()
    {
        var res = new UfsAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(Encoding.ASCII.GetBytes("definitely not ufs, padding to 9568 bytes minimum length......".PadRight(9600, '.'))));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Truncated_Detects0()
    {
        var full = BuildUfs1();
        var cut = new byte[8192];
        Array.Copy(full, cut, cut.Length);
        var res = new UfsAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(cut));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void BadMagic_Detects0()
    {
        var img = BuildUfs2();
        img[65536 + 1372] ^= 0xFF;
        var res = new UfsAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(img));
        Assert.AreEqual(0, res.Confidence);
    }
}
