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
// Synthetic LZ4/LZ4P tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Lz4;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class Lz4pTests
{
    private static byte[] LiteralsOnly(byte[] lit)
    {
        // single literal run then end-of-block (no match part)
        var out_ = new byte[1 + lit.Length];
        out_[0] = (byte)(lit.Length << 4);
        Array.Copy(lit, 0, out_, 1, lit.Length);
        return out_;
    }

    [Test]
    public void Block_LiteralsOnly()
    {
        var lit = Encoding.ASCII.GetBytes("hello!");
        var dst = new byte[64];
        int n = Lz4.DecompressBlock(LiteralsOnly(lit), 0, 1 + lit.Length, dst, 0, 64, out int consumed);
        Assert.AreEqual(lit.Length, n);
        Assert.AreEqual(1 + lit.Length, consumed);
        CollectionAssert.AreEqual(lit, dst[..n]);
    }

    [Test]
    public void Block_Match()
    {
        // "abc" + match(offset=3, len=6) -> "abcabcabc"
        var block = new byte[] { 0x32, (byte)'a', (byte)'b', (byte)'c', 0x03, 0x00 };
        var dst = new byte[64];
        int n = Lz4.DecompressBlock(block, 0, block.Length, dst, 0, 64, out _);
        Assert.AreEqual(9, n);
        Assert.AreEqual("abcabcabc", Encoding.ASCII.GetString(dst, 0, n));
    }

    [Test]
    public void Block_ZeroOffset_Fails()
    {
        var block = new byte[] { 0x10, (byte)'a', 0x00, 0x00 };
        Assert.Throws<System.IO.InvalidDataException>(() =>
            Lz4.DecompressBlock(block, 0, block.Length, new byte[64], 0, 64, out _));
    }

    internal static byte[] BuildLz4p(byte[][] blocks, int chunk)
    {
        var buf = new System.IO.MemoryStream();
        void U32(uint v)
        {
            buf.WriteByte((byte)v);
            buf.WriteByte((byte)(v >> 8));
            buf.WriteByte((byte)(v >> 16));
            buf.WriteByte((byte)(v >> 24));
        }
        buf.Write(Encoding.ASCII.GetBytes("LZ4P"), 0, 4);
        U32(0);
        U32(0);
        U32((uint)chunk);
        U32((uint)blocks.Length);
        U32(0);
        U32(0);
        U32(0);
        foreach (var b in blocks)
        {
            U32((uint)b.Length);
        }
        foreach (var b in blocks)
        {
            buf.Write(b, 0, b.Length);
        }
        return buf.ToArray();
    }

    [Test]
    public void Archive_Detects90AndDecodes()
    {
        var b1 = LiteralsOnly(Encoding.ASCII.GetBytes("AA"));
        var b2 = LiteralsOnly(Encoding.ASCII.GetBytes("BB"));
        var img = BuildLz4p(new[] { b1, b2 }, chunk: 2);
        var res = (Lz4pDetectionResult)new Lz4pAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(img));
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual(2, res.BlockCount);
        var mem = Mem(img, "kern.pak");
        var artifacts = new Lz4pAddon().CreateExtractor(res).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("kern.pak.unlz4", artifacts[0].Name);
        Assert.AreEqual("AABB", Encoding.ASCII.GetString(artifacts[0].Data.ToArray()));
    }

    [Test]
    public void Archive_BadMagic_Detects0()
    {
        var img = BuildLz4p(new[] { LiteralsOnly(new byte[] { 1 }) }, chunk: 1);
        img[0] = (byte)'X';
        var res = new Lz4pAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(img));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Archive_CorruptBlock_Detects0()
    {
        var img = BuildLz4p(new[] { new byte[] { 0x10, (byte)'a', 0x00, 0x00 } }, chunk: 8);
        var res = new Lz4pAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(img));
        Assert.AreEqual(0, res.Confidence);
    }
}
