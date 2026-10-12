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
// Synthetic LZHS VFS tests: uncompressed/zero-fill chunks exercise the
// chunk stream without needing real compressed payloads. Fully in-memory.
using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using Yafex;
using Yafex.Fuse;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class LzhsVfsTests
{
    private const int Heading = 0x100000;

    private static void PutU32(byte[] buf, int off, uint v) =>
        BitConverter.GetBytes(v).CopyTo(buf, off);

    private static void PutU16(byte[] buf, int off, ushort v) =>
        BitConverter.GetBytes(v).CopyTo(buf, off);

    // 1MB heading + uncompressed 64B chunk + 32B zero-fill chunk.
    // Outer CompressedSize counts the inner header (16B) + payload.
    private static byte[] FsImage()
    {
        var buf = new byte[Heading + 96 + 16 + 16];
        for (int i = 0; i < 64; i++) buf[i] = (byte)(i & 0xFF); // heading pattern
        int c1 = Heading;
        PutU32(buf, c1, 64);
        PutU32(buf, c1 + 4, 16 + 64);
        PutU16(buf, c1 + 8, 7);
        PutU32(buf, c1 + 16, 64);
        PutU32(buf, c1 + 20, 64);
        PutU16(buf, c1 + 24, 0);
        for (int i = 0; i < 64; i++) buf[c1 + 32 + i] = 0x42;
        int c2 = c1 + 96;
        PutU32(buf, c2, 32);
        PutU32(buf, c2 + 4, 0);
        return buf;
    }

    [Test]
    public void ChunkStream_ReadsHeadingChunksAndZeros()
    {
        var img = FsImage();
        var file = new LzhsFsVirtualFile("fs.bin", new MemoryVirtualFile("fs.bin", img));
        Assert.AreEqual(Heading + 64 + 32, file.Length);
        using var s = file.OpenRead();
        var all = new byte[file.Length];
        int off = 0;
        while (off < all.Length)
        {
            int n = s.Read(all, off, all.Length - off);
            if (n <= 0) break;
            off += n;
        }
        Assert.AreEqual(all.Length, off);
        for (int i = 0; i < 64; i++) Assert.AreEqual((byte)(i & 0xFF), all[i]);
        for (int i = 0; i < 64; i++) Assert.AreEqual(0x42, all[Heading + i]);
        for (int i = 0; i < 32; i++) Assert.AreEqual(0, all[Heading + 64 + i]);
    }

    [Test]
    public void ChunkStream_SeeksBackwardAndForward()
    {
        var img = FsImage();
        var file = new LzhsFsVirtualFile("fs.bin", new MemoryVirtualFile("fs.bin", img));
        using var s = file.OpenRead();
        s.Seek(Heading + 60, SeekOrigin.Begin);
        var buf = new byte[8];
        Assert.AreEqual(8, s.Read(buf, 0, 8));
        for (int i = 0; i < 4; i++) Assert.AreEqual(0x42, buf[i]);
        for (int i = 4; i < 8; i++) Assert.AreEqual(0, buf[i]);
        s.Seek(0, SeekOrigin.Begin); // restart
        var head = new byte[4];
        Assert.AreEqual(4, s.Read(head, 0, 4));
        Assert.AreEqual(0, head[0]);
        Assert.AreEqual(1, head[1]);
        Assert.Throws<IOException>(() => s.Seek(-1, SeekOrigin.Begin));
    }

    [Test]
    public void FsExpander_MatchesContainerRejectsGarbage()
    {
        var exp = new LzhsFsExpander();
        Assert.IsTrue(exp.Matches(new MemoryVirtualFile("fs.bin", FsImage())));
        var rnd = new byte[Heading + 64];
        new Random(31).NextBytes(rnd);
        Assert.IsFalse(exp.Matches(new MemoryVirtualFile("x.bin", rnd)));
        Assert.IsFalse(exp.Matches(new MemoryVirtualFile("short.bin", new byte[16])));
    }

    [Test]
    public void LzhsExpander_MatchesHeaderRejectsGarbage()
    {
        var exp = new LzhsExpander();
        var hdr = new byte[32];
        PutU32(hdr, 0, 100);
        PutU32(hdr, 4, 16);
        Assert.IsTrue(exp.Matches(new MemoryVirtualFile("a.lzhs", hdr)));
        var rnd = new byte[32];
        new Random(32).NextBytes(rnd);
        Assert.IsFalse(exp.Matches(new MemoryVirtualFile("x.bin", rnd)));
        Assert.IsFalse(exp.Matches(new MemoryVirtualFile("short.bin", new byte[4])));
    }

    // Any ELF header parses as LZHS-shaped (e_ident = huge uncomp, small
    // comp, zero spare), e.g. ARM ABL images: mount matching must reject
    // them via checksum verification, like the format detector does.
    private static byte[] ElfHeaded(int length)
    {
        var buf = new byte[length];
        buf[0] = 0x7F;
        buf[1] = (byte)'E';
        buf[2] = (byte)'L';
        buf[3] = (byte)'F';
        buf[4] = 1;
        buf[5] = 1;
        buf[6] = 1;
        new Random(7).NextBytes(new Span<byte>(buf, 16, buf.Length - 16));
        return buf;
    }

    [Test]
    public void LzhsExpander_RejectsElfMagic()
    {
        var exp = new LzhsExpander();
        Assert.IsFalse(exp.Matches(new MemoryVirtualFile("abl.elf", ElfHeaded(70000))));
    }

    [Test]
    public void LzhsExpander_ExpandEmitsNoGarbageOnBadChecksum()
    {
        var exp = new LzhsExpander();
        var dir = exp.Expand(new MemoryVirtualFile("abl.elf", ElfHeaded(70000)), new List<object>());
        dir.EnsureLoaded();
        Assert.IsEmpty(dir.Tree);
    }

    [Test]
    public void Expanders_ReturnLazyDirsWithoutTouchingPayload()
    {
        var img = new MemoryVirtualFile("fs.bin", FsImage());
        var fsDir = new LzhsFsExpander().Expand(img, new List<object>());
        Assert.AreEqual(VfsNodeType.Directory, fsDir.Type);
        var lzhsDir = new LzhsExpander().Expand(img, new List<object>());
        Assert.AreEqual(VfsNodeType.Directory, lzhsDir.Type);
    }
}
