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
// Synthetic JFFS2 tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Jffs2;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class Jffs2Tests
{
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
        uint a = 1, bb = 0;
        foreach (byte x in plain)
        {
            a = (a + x) % 65521;
            bb = (bb + a) % 65521;
        }
        uint adler = (bb << 16) | a;
        block[^4] = (byte)(adler >> 24);
        block[^3] = (byte)(adler >> 16);
        block[^2] = (byte)(adler >> 8);
        block[^1] = (byte)adler;
        return block;
    }

    private static void Pad4(List<byte> b)
    {
        while (b.Count % 4 != 0) b.Add(0);
    }

    private static void PutDirent(List<byte> img, uint pino, uint ino, string name, byte dtype)
    {
        var nameBytes = Encoding.ASCII.GetBytes(name);
        PutU16(img, 0x1985);
        PutU16(img, 0xE001); // dirent + compat bits (as seen in the wild)
        int totPos = img.Count;
        PutU32(img, 0);
        PutU32(img, 0); // hdr_crc (unenforced)
        PutU32(img, pino);
        PutU32(img, 1); // version
        PutU32(img, ino);
        PutU32(img, 0); // mctime
        img.Add((byte)nameBytes.Length);
        img.Add(dtype);
        img.Add(0);
        img.Add(0);
        PutU32(img, 0); // node_crc
        PutU32(img, 0); // name_crc
        img.AddRange(nameBytes);
        Pad4(img);
        uint tot = (uint)(img.Count - totPos + 4);
        var tb = BitConverter.GetBytes(tot);
        for (int i = 0; i < 4; i++) img[totPos + i] = tb[i];
    }

    private static void PutInode(List<byte> img, uint ino, byte[] stored, uint size, byte compr)
    {
        PutU16(img, 0x1985);
        PutU16(img, 0xE002); // inode + compat bits
        int totPos = img.Count;
        PutU32(img, 0);
        PutU32(img, 0); // hdr_crc
        PutU32(img, ino);
        PutU32(img, 1); // version
        PutU32(img, 0x81A4); // mode
        PutU16(img, 0);
        PutU16(img, 0);
        PutU32(img, size); // isize
        PutU32(img, 0);
        PutU32(img, 0);
        PutU32(img, 0);
        PutU32(img, 0); // offset
        PutU32(img, (uint)stored.Length); // csize
        PutU32(img, size); // dsize
        img.Add(compr);
        img.Add(0);
        PutU16(img, 0);
        PutU32(img, 0); // data_crc
        PutU32(img, 0); // node_crc
        img.AddRange(stored);
        Pad4(img);
        uint tot = (uint)(img.Count - totPos + 4);
        var tb = BitConverter.GetBytes(tot);
        for (int i = 0; i < 4; i++) img[totPos + i] = tb[i];
    }

    internal static byte[] BuildImage()
    {
        var img = new List<byte>();
        // cleanmarker
        PutU16(img, 0x1985);
        PutU16(img, 0x2003);
        PutU32(img, 12);
        PutU32(img, 0);
        var hello = Encoding.ASCII.GetBytes("hello jffs2");
        var raw = Encoding.ASCII.GetBytes("raw-bytes");
        PutDirent(img, 1, 2, "a.txt", 8);
        PutInode(img, 2, ZlibWrap(hello), (uint)hello.Length, 6);
        PutDirent(img, 1, 3, "b.bin", 8);
        PutInode(img, 3, raw, (uint)raw.Length, 0);
        while (img.Count < 256) img.Add(0xFF); // empty flash
        return img.ToArray();
    }

    [Test]
    public void Valid_Detects90()
    {
        var res = (Jffs2DetectionResult)new Jffs2Addon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()));
        Assert.AreEqual(90, res.Confidence);
    }

    [Test]
    public void Extract_ReadsBothFiles()
    {
        var mem = Mem(BuildImage(), "mode.pak");
        var det = (Jffs2DetectionResult)new Jffs2Addon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new Jffs2Addon().CreateExtractor(det).Extract(mem)
            .OrderBy(a => a.Name).ToList();
        Assert.AreEqual(2, artifacts.Count);
        Assert.AreEqual("a.txt", artifacts[0].Name);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("hello jffs2"), artifacts[0].Data.ToArray());
        Assert.AreEqual("b.bin", artifacts[1].Name);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("raw-bytes"), artifacts[1].Data.ToArray());
    }

    [Test]
    public void Garbage_Detects0()
    {
        var res = new Jffs2Addon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(Encoding.ASCII.GetBytes("definitely not jffs2.......")));
        Assert.AreEqual(0, res.Confidence);
    }
}
