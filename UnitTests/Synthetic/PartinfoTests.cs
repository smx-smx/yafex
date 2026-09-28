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
// Synthetic Partinfo tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Partinfo;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class PartinfoTests
{
    [Test]
    public void KnownMagics_Detect30WithType()
    {
        foreach (var (magic, _) in new[] { (0x20081118u, 0), (0x20110729u, 0), (0x20120716u, 0) })
        {
            var buf = Zeros(16);
            WriteU32LE(buf, 0, magic);
            var res = (PartinfoDetectionResult)new PartinfoAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(buf));
            Assert.AreEqual(30, res.Confidence);
            Assert.NotNull(res.Context);
        }
    }

    [Test]
    public void Garbage_Detects0()
    {
        var res = (PartinfoDetectionResult)new PartinfoAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(Zeros(16)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void V1OldestMagic_Detects30()
    {
        // 0x20110620 (2011-06-20) predates V2 (20110729): age-ordered versioning.
        var buf = Zeros(16);
        WriteU32LE(buf, 0, 0x20110620u);
        var res = (PartinfoDetectionResult)new PartinfoAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(buf));
        Assert.AreEqual(30, res.Confidence);
        Assert.AreEqual(PartinfoType.PartinfoV1, res.Context!.PartinfoType);
    }

    private static byte[] BuildV1(params (string name, uint off, uint size, string file)[] entries)
    {
        var buf = Zeros(72 + 88 * entries.Length);
        WriteU32LE(buf, 0, 0x20110620u);
        WriteU32LE(buf, 12, (uint)entries.Length);
        WriteAscii(buf, 16, "testdev");
        for (int i = 0; i < entries.Length; i++)
        {
            int b = 72 + i * 88;
            WriteAscii(buf, b, entries[i].name);
            WriteU32LE(buf, b + 32, entries[i].off);
            WriteU32LE(buf, b + 36, entries[i].size);
            WriteAscii(buf, b + 40, entries[i].file);
            buf[b + 80] = 1;
            buf[b + 81] = 1;
        }
        return buf;
    }

    [Test]
    public void V1_ParsesEntries()
    {
        var tbl = PartinfoTable.Parse(Mem(BuildV1(("boot", 0, 0x100000, "boot.bin"))).Data.Span, PartinfoType.PartinfoV1);
        Assert.AreEqual(1, tbl.partitions.Count);
        Assert.AreEqual("boot", tbl.partitions[0].name);
        Assert.AreEqual(0x100000u, tbl.partitions[0].size);
        Assert.AreEqual("boot.bin", tbl.partitions[0].filename);
        Assert.AreEqual("testdev", tbl.devices[0].name);
    }

    private static byte[] BuildMtdinfo(params (string name, uint off, uint size)[] entries)
    {
        var buf = Zeros(0xF0 + 84 * entries.Length);
        WriteU32LE(buf, 0, 0x20090729u);
        buf[12] = 1;
        buf[13] = (byte)entries.Length;
        WriteAscii(buf, 16, "test_map0");
        for (int i = 0; i < entries.Length; i++)
        {
            int b = 0xF0 + i * 84;
            WriteAscii(buf, b, entries[i].name);
            WriteU32LE(buf, b + 32, entries[i].off);
            WriteU32LE(buf, b + 36, entries[i].size);
            buf[b + 80] = 1;
            buf[b + 81] = 1;
        }
        return buf;
    }

    [Test]
    public void Mtdinfo_Detects30AndParses()
    {
        var buf = BuildMtdinfo(("boot", 0x20000, 0x80000));
        var res = (PartinfoDetectionResult)new PartinfoAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(buf));
        Assert.AreEqual(30, res.Confidence);
        Assert.AreEqual(PartinfoType.MtdinfoFile, res.Context!.PartinfoType);

        var tbl = PartinfoTable.Parse(Mem(buf).Data.Span, PartinfoType.MtdinfoFile);
        Assert.AreEqual(1, tbl.partitions.Count);
        Assert.AreEqual("boot", tbl.partitions[0].name);
        Assert.AreEqual(0x20000u, tbl.partitions[0].offset);
    }

    [Test]
    public void Mtdinfo_BadLength_Detects0()
    {
        // Shape requires (len - 0xF0) % 84 == 0.
        var buf = BuildMtdinfo(("boot", 0x20000, 0x80000));
        var bad = new byte[buf.Length - 1];
        Array.Copy(buf, bad, bad.Length);
        var res = (PartinfoDetectionResult)new PartinfoAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(bad));
        Assert.AreEqual(0, res.Confidence);
    }
}
