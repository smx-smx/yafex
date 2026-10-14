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
// Synthetic ext tests: magic accept/reject and graceful failure on
// truncated or corrupt images, plus a minimal in-memory ext2 image
// (superblock, one group, root dir, one direct-block file) proving
// round-trip reads through the Yafex-native driver. Fully in-memory.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex.FileSystems.Ext;
using Yafex.Fuse;

namespace UnitTests;

[TestFixture]
public class VfsExtTests
{
    private static MemoryVirtualFile Image(byte[] content) =>
        new("img.bin", content);

    [Test]
    public void Probe_RejectsWrongMagic()
    {
        var buf = new byte[2048];
        buf[1080] = 0x51; buf[1081] = 0x41;
        Assert.IsFalse(VfsExt.TryOpen(Image(buf), new List<object>(), out _));
    }

    [Test]
    public void Probe_RejectsTruncatedImage()
    {
        Assert.IsFalse(VfsExt.TryOpen(Image(new byte[100]), new List<object>(), out _));
    }

    [Test]
    public void Probe_RejectsEmptyImage()
    {
        Assert.IsFalse(VfsExt.TryOpen(Image(new byte[4096]), new List<object>(), out _));
    }

    [Test]
    public void Probe_FailsGracefullyOnCorruptSuperblock()
    {
        // right magic, garbage superblock: open must fail, not throw
        var buf = new byte[4096];
        buf[1080] = 0x53; buf[1081] = 0xEF;
        Assert.IsFalse(VfsExt.TryOpen(Image(buf), new List<object>(), out _));
    }

    internal static readonly byte[] ExtContent = Encoding.ASCII.GetBytes("hello ext2");

    private static void PutU16(byte[] b, int off, ushort v)
    {
        b[off] = (byte)v;
        b[off + 1] = (byte)(v >> 8);
    }

    private static void PutU32(byte[] b, int off, uint v)
    {
        b[off] = (byte)v;
        b[off + 1] = (byte)(v >> 8);
        b[off + 2] = (byte)(v >> 16);
        b[off + 3] = (byte)(v >> 24);
    }

    private static void PutInode(byte[] img, int table, int ino, ushort mode, uint size, uint block)
    {
        int off = table + (ino - 1) * 128;
        PutU16(img, off + 0, mode);
        PutU16(img, off + 26, (ushort)(mode == 0x41ED ? 3 : 1)); // links
        PutU32(img, off + 4, size);
        PutU32(img, off + 28, 2); // 512-byte sectors
        PutU32(img, off + 40, block); // direct[0]
    }

    internal static byte[] BuildExt2()
    {
        // 8 x 1K blocks: 0 boot, 1 superblock, 2 GDT, 3 bbitmap,
        // 4 ibitmap, 5 inode table, 6 root dir, 7 file data.
        var img = new byte[8192];
        PutU32(img, 1024 + 0, 8); // inodes
        PutU32(img, 1024 + 4, 8); // blocks
        PutU32(img, 1024 + 16, 5); // free inodes
        PutU32(img, 1024 + 20, 1); // first data block
        PutU32(img, 1024 + 32, 8); // blocks per group
        PutU32(img, 1024 + 40, 8); // inodes per group
        PutU16(img, 1024 + 56, 0xEF53); // magic
        PutU32(img, 1024 + 76, 1); // dynamic revision
        PutU16(img, 1024 + 88, 128); // inode size
        // group descriptor at block 2
        PutU32(img, 2048 + 0, 3); // block bitmap
        PutU32(img, 2048 + 4, 4); // inode bitmap
        PutU32(img, 2048 + 8, 5); // inode table
        // bitmaps: blocks 0..7 used, inodes 2..3 used
        img[3072] = 0xFF;
        img[4096] = 0x06;
        // inodes: 2 = root dir, 3 = file
        PutInode(img, 5120, 2, 0x41ED, 1024, 6);
        PutInode(img, 5120, 3, 0x81A4, (uint)ExtContent.Length, 7);
        // root dir block: '.', '..', 'hello.txt'
        void Dirent(int at, uint ino, string name, byte type, ushort recLen)
        {
            var nb = Encoding.ASCII.GetBytes(name);
            PutU32(img, at + 0, ino);
            PutU16(img, at + 4, recLen);
            img[at + 6] = (byte)nb.Length;
            img[at + 7] = type;
            nb.CopyTo(img, at + 8);
        }
        Dirent(6144, 2, ".", 2, 12);
        Dirent(6156, 2, "..", 2, 12);
        Dirent(6168, 3, "hello.txt", 1, 1000);
        ExtContent.CopyTo(img, 7168);
        return img;
    }

    [Test]
    public void RoundTrip_ListsFile()
    {
        var img = new MemoryVirtualFile("ext2.img", BuildExt2());
        Assert.IsTrue(VfsExt.TryOpen(img, new List<object>(), out var fs));
        var tree = VfsExt.BuildLazyTree(fs);
        Assert.AreEqual(1, tree.Count);
        Assert.AreEqual("hello.txt", tree[0].Name);
    }

    [Test]
    public void RoundTrip_ReadsFile()
    {
        using var s = new MemoryStream(BuildExt2(), writable: false);
        using var fs = new ExtFileSystem(s);
        using var f = fs.OpenFile("hello.txt", FileMode.Open, FileAccess.Read);
        var buf = new byte[ExtContent.Length];
        int n = f.Read(buf, 0, buf.Length);
        Assert.AreEqual(ExtContent.Length, n);
        CollectionAssert.AreEqual(ExtContent, buf);
    }

    [Test]
    public void RoundTrip_RejectsEmptyDir()
    {
        // zeroed dir block: no entries must surface, no throw
        var img = BuildExt2();
        for (int i = 6144; i < 7168; i++) img[i] = 0;
        Assert.IsTrue(VfsExt.TryOpen(new MemoryVirtualFile("ext2.img", img), new List<object>(), out var fs));
        var tree = VfsExt.BuildLazyTree(fs);
        Assert.AreEqual(0, tree.Count);
    }
}
