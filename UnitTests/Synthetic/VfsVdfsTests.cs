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
// Synthetic VDFS tests: a minimal volume (volume-begins, superblock,
// extended superblock, snapshot base table, one single-leaf catalog tree
// with an inline file and a subdirectory) is built in-memory; probing,
// listing and file reads run without fixtures. Fully in-memory.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NUnit.Framework;

using DiscUtils.Vdfs;

using Yafex.Fuse;

namespace UnitTests;

[TestFixture]
public class VfsVdfsTests
{
    private const int Block = 4096;
    private const int Node = 16384;
    private static readonly byte[] InlineContent = Encoding.ASCII.GetBytes("hello vdfs");

    private static void PutU16(byte[] b, int off, ushort v) =>
        BitConverter.GetBytes(v).CopyTo(b, off);
    private static void PutU32(byte[] b, int off, uint v) =>
        BitConverter.GetBytes(v).CopyTo(b, off);
    private static void PutU64(byte[] b, int off, ulong v) =>
        BitConverter.GetBytes(v).CopyTo(b, off);

    private static byte[] FolderValue(ushort mode)
    {
        var v = new byte[80];
        PutU32(v, 0, 0); // flags
        PutU64(v, 16, 1); // links
        PutU16(v, 40, mode);
        return v;
    }

    private static byte[] FileValue(ushort mode, byte[] inline)
    {
        var v = new byte[312];
        Array.Copy(FolderValue(mode), v, 80);
        PutU64(v, 80, (ulong)inline.Length); // size
        PutU64(v, 88, 0); // total blocks: inline
        Array.Copy(inline, 0, v, 96, inline.Length);
        return v;
    }

    private static byte[] Key(ulong parent, ulong obj, byte type, string name)
    {
        var nb = Encoding.UTF8.GetBytes(name);
        int keyLen = (26 + nb.Length + 7) & ~7;
        var k = new byte[keyLen];
        PutU16(k, 4, (ushort)keyLen);
        PutU64(k, 8, parent);
        PutU64(k, 16, obj);
        k[24] = type;
        k[25] = (byte)nb.Length;
        Array.Copy(nb, 0, k, 26, nb.Length);
        return k;
    }

    private static byte[] LeafNode(params byte[][] records)
    {
        var node = new byte[Node];
        node[0] = (byte)'N'; node[1] = (byte)'D'; node[2] = (byte)'l'; node[3] = (byte)'f';
        PutU16(node, 14, (ushort)records.Length);
        PutU32(node, 16, 1); // node id
        PutU32(node, 28, 2); // leaf
        int at = 32;
        for (int i = 0; i < records.Length; i++)
        {
            Array.Copy(records[i], 0, node, at, records[i].Length);
            PutU32(node, Node - 4 - 4 * (i + 1), (uint)at);
            at += records[i].Length;
        }
        PutU32(node, Node - 4 - 4 * (records.Length + 1), (uint)at);
        return node;
    }

    private static byte[] Cat(params byte[][] parts)
    {
        int len = parts.Sum(p => p.Length);
        var b = new byte[len];
        int at = 0;
        foreach (var p in parts) { Array.Copy(p, 0, b, at, p.Length); at += p.Length; }
        return b;
    }

    private static byte[] BuildImage()
    {
        // Layout (4K blocks): superpage 0 = blocks 0..3 (VB/SB/EXSB),
        // tables = blocks 4..5, meta = blocks 6.. (head page 6..9, node 10..13).
        var img = new byte[14 * Block];

        // Volume begins + superblock + extended superblock
        img[0] = (byte)'V'; img[1] = (byte)'D'; img[2] = (byte)'F'; img[3] = (byte)'S';
        img[4] = (byte)'2'; img[5] = (byte)'0'; img[6] = (byte)'0'; img[7] = (byte)'7';
        var sb = new byte[512];
        sb[0] = (byte)'V'; sb[1] = (byte)'D'; sb[2] = (byte)'F'; sb[3] = (byte)'S';
        sb[4] = (byte)'2'; sb[5] = (byte)'0'; sb[6] = (byte)'0'; sb[7] = (byte)'7';
        sb[0xA4] = 12; // 4K blocks
        sb[0xA5] = 14; // 16K super pages
        Array.Copy(sb, 0, img, 1024, 512);
        var ex = new byte[2560];
        PutU64(ex, 0x48, 4); // tables begin
        PutU64(ex, 0x50, 2); // tables length
        PutU64(ex, 0x58, 6); // meta[0] begin
        PutU64(ex, 0x60, 8); // meta[0] length
        PutU64(ex, 0x668, 14); // volume blocks
        Array.Copy(ex, 0, img, 1536, ex.Length);

        // Snapshot base table at block 4: CAT pages 0..1 mapped, rest empty.
        var bt = new byte[2 * Block];
        bt[0] = (byte)'C'; bt[1] = (byte)'o'; bt[2] = (byte)'W'; bt[3] = (byte)'B';
        PutU64(bt, 24, 1); // CAT last page index
        PutU64(bt, 64, 104); // CAT table offset
        PutU64(bt, 104, 0); // page 0 -> meta iblock 0 (head)
        PutU64(bt, 120, 4); // page 1 -> meta iblock 4 (node 1)
        Array.Copy(bt, 0, img, 4 * Block, bt.Length);

        // Catalog head (meta iblock 0): root node 1, single level.
        var head = new byte[Node];
        head[0] = (byte)'e'; head[1] = (byte)'H'; head[2] = (byte)'N'; head[3] = (byte)'D';
        PutU32(head, 12, 1); // root
        head[16] = 1; // height
        Array.Copy(head, 0, img, 6 * Block, Node);

        // Leaf node 1 (meta iblock 4): root + file + subdir + an orphan
        // record under a dead parent (read-write images accumulate these;
        // traversal must never surface them).
        var root = Cat(Key(0, 1, 1, "root"), FolderValue(0x41ED));
        var file = Cat(Key(1, 100, 2, "hello.txt"), FileValue(0x81A4, InlineContent));
        var sub = Cat(Key(1, 2, 1, "sub"), FolderValue(0x41ED));
        var ghost = Cat(Key(999, 555, 2, "ghost.txt"), FileValue(0x81A4, InlineContent));
        PutU16(root, 6, (ushort)root.Length);
        PutU16(file, 6, (ushort)file.Length);
        PutU16(sub, 6, (ushort)sub.Length);
        PutU16(ghost, 6, (ushort)ghost.Length);
        var leaf = LeafNode(root, file, sub, ghost);
        Array.Copy(leaf, 0, img, 10 * Block, Node);
        return img;
    }

    [Test]
    public void Detect_AcceptsSyntheticVolume()
    {
        using var ms = new MemoryStream(BuildImage(), false);
        Assert.That(VdfsFileSystem.Detect(ms), Is.True);
    }

    [Test]
    public void Root_ListsInlineFileAndSubdir()
    {
        var img = new MemoryVirtualFile("test.vdfs", BuildImage());
        Assert.IsTrue(VfsVdfs.TryOpen(img, new List<object>(), out var fs));
        var tree = VfsExt.BuildLazyTree(fs);
        var names = tree.Select(n => n.Name).OrderBy(n => n).ToArray();
        Assert.That(names, Is.EquivalentTo(new[] { "hello.txt", "sub" }));
    }

    [Test]
    public void InlineFile_ReadsBack()
    {
        using var ms = new MemoryStream(BuildImage(), false);
        using var fs = new VdfsFileSystem(ms);
        using var s = fs.OpenFile("hello.txt", FileMode.Open, FileAccess.Read);
        var buf = new byte[InlineContent.Length];
        int n = s.Read(buf, 0, buf.Length);
        Assert.That(buf[..n], Is.EqualTo(InlineContent));
    }

    [Test]
    public void OrphanRecord_StaysInvisible()
    {
        using var ms = new MemoryStream(BuildImage(), false);
        using var fs = new VdfsFileSystem(ms);
        Assert.That(fs.FileExists("ghost.txt"), Is.False);
        Assert.Throws<System.IO.FileNotFoundException>(
            () => fs.OpenFile("ghost.txt", FileMode.Open, FileAccess.Read));
    }
}
