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
// Synthetic UBI tests: a minimal image (layout PEB with an ASCII volume
// table plus two data PEBs with a gap) is built in-memory against the
// Yafex-native reader. Fully in-memory, no fixtures.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Ubi;
using Yafex.FileSystems.Ubi;
using Yafex.Fuse;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class UbiTests
{
    internal const int PebSize = 4096;
    internal const int VidOff = 64;
    internal const int DataOff = 512;
    internal const int LebSize = PebSize - DataOff;

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
            table[i] = crc;
        }
        return table;
    }

    private static uint CrcRaw(byte[] data, int offset, int count)
    {
        uint crc = 0xFFFFFFFFu;
        for (int i = offset; i < offset + count; i++)
        {
            crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }

    private static void PutU32BE(byte[] b, int off, uint v)
    {
        b[off] = (byte)(v >> 24);
        b[off + 1] = (byte)(v >> 16);
        b[off + 2] = (byte)(v >> 8);
        b[off + 3] = (byte)v;
    }

    private static byte[] EcHeader()
    {
        var h = new byte[64];
        Encoding.ASCII.GetBytes("UBI#").CopyTo(h, 0);
        h[4] = 1;
        PutU32BE(h, 16, VidOff);
        PutU32BE(h, 20, DataOff);
        PutU32BE(h, 60, CrcRaw(h, 0, 60));
        return h;
    }

    private static byte[] VidHeader(uint vol, uint lnum, byte volType = 0)
    {
        var h = new byte[64];
        Encoding.ASCII.GetBytes("UBI!").CopyTo(h, 0);
        h[4] = 1;
        h[5] = volType;
        PutU32BE(h, 8, vol);
        PutU32BE(h, 12, lnum);
        PutU32BE(h, 60, CrcRaw(h, 0, 60));
        return h;
    }

    private static byte[] Peb(uint vol, uint lnum, byte[] data, byte volType = 0)
    {
        var p = new byte[PebSize];
        Array.Copy(EcHeader(), p, 64);
        Array.Copy(VidHeader(vol, lnum, volType), 0, p, VidOff, 64);
        Array.Copy(data, 0, p, DataOff, Math.Min(data.Length, LebSize));
        return p;
    }

    internal static byte[] BuildImage()
    {
        // layout volume (high id) with one ASCII table record for vol 0
        var table = new byte[LebSize];
        table[12] = 1; // dynamic
        table[15] = 7; // name length
        Encoding.ASCII.GetBytes("testvol").CopyTo(table, 16);
        using var ms = new MemoryStream();
        var layout = Peb(0x7FF0, 0, table, 1);
        ms.Write(layout, 0, layout.Length);
        var hello = new byte[LebSize];
        Encoding.ASCII.GetBytes("HELLO").CopyTo(hello, 0);
        var p0 = Peb(0, 0, hello);
        ms.Write(p0, 0, p0.Length);
        var world = new byte[LebSize];
        Encoding.ASCII.GetBytes("WORLD").CopyTo(world, 0);
        var p1 = Peb(0, 1, world);
        ms.Write(p1, 0, p1.Length);
        // lnum 2 missing on purpose (zero-fill); lnum 3 present
        var bang = new byte[LebSize];
        Encoding.ASCII.GetBytes("BANG").CopyTo(bang, 0);
        var p3 = Peb(0, 3, bang);
        ms.Write(p3, 0, p3.Length);
        return ms.ToArray();
    }

    [Test]
    public void Valid_Detects90()
    {
        var res = (UbiDetectionResult)new UbiAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()));
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual(0, res.Offset);
    }

    [Test]
    public void Embedded_Detects85()
    {
        var inner = BuildImage();
        var buf = new byte[512 + inner.Length];
        new Random(11).NextBytes(buf.AsSpan(0, 512));
        Array.Copy(inner, 0, buf, 512, inner.Length);
        var res = (UbiDetectionResult)new UbiAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(buf));
        Assert.AreEqual(85, res.Confidence);
        Assert.AreEqual(512, res.Offset);
    }

    [Test]
    public void Lister_ShowsVolumes()
    {
        var mem = Mem(BuildImage(), "a.ubi");
        var det = (UbiDetectionResult)new UbiAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var text = Synthetic.ListTree(new UbiAddon(), det, mem);
        StringAssert.Contains("testvol", text);
    }

    [Test]
    public void Extract_EmitsVolumePayload()
    {
        var mem = Mem(BuildImage(), "a.ubi");
        var det = (UbiDetectionResult)new UbiAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new UbiAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("testvol", artifacts[0].Name);
        var payload = artifacts[0].Data.ToArray();
        Assert.AreEqual(4 * LebSize, payload.Length);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("HELLO"), payload[..5]);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("WORLD"), payload[LebSize..(LebSize + 5)]);
        CollectionAssert.AreEqual(new byte[16], payload[(2 * LebSize)..(2 * LebSize + 16)]);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("BANG"), payload[(3 * LebSize)..(3 * LebSize + 4)]);
    }

    [Test]
    public void Expander_MatchesArchiveRejectsGarbage()
    {
        var exp = new UbiExpander();
        Assert.IsTrue(exp.Matches(new MemoryVirtualFile("a.ubi", BuildImage())));
        var rnd = new byte[2048];
        new Random(12).NextBytes(rnd);
        Assert.IsFalse(exp.Matches(new MemoryVirtualFile("x.bin", rnd)));
    }

    [Test]
    public void Garbage_Fails()
    {
        var rnd = new byte[2048];
        new Random(13).NextBytes(rnd);
        var res = new UbiAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(rnd));
        Assert.AreEqual(0, res.Confidence);
    }
}
