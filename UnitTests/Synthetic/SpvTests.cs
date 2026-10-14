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
// Synthetic Spv tests: a minimal flash image (header, two populated bins,
// two empty slots, build strings) built in-memory. Fully in-memory.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Spv;
using Yafex.Fuse;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class SpvTests
{
    internal static readonly byte[] CpuPayload = Encoding.ASCII.GetBytes("riscv-payload-aaa");
    internal static readonly byte[] ModPayload = Encoding.ASCII.GetBytes("mod-part");

    private static void PutU32(byte[] b, int off, uint v)
    {
        b[off] = (byte)v;
        b[off + 1] = (byte)(v >> 8);
        b[off + 2] = (byte)(v >> 16);
        b[off + 3] = (byte)(v >> 24);
    }

    private static uint NameRaw(string name)
    {
        var b = new byte[4];
        var nb = Encoding.ASCII.GetBytes(name);
        Array.Copy(nb, b, Math.Min(nb.Length, 4));
        return BitConverter.ToUInt32(b, 0);
    }

    internal static byte[] BuildImage()
    {
        const uint flashBase = 0x48000000;
        var date = Encoding.ASCII.GetBytes("May 30 2025\0");
        var time = Encoding.ASCII.GetBytes("18:16:27\0");
        // payloads first so addresses are known
        int cpuOff = 96;
        int modOff = cpuOff + CpuPayload.Length;
        var img = new List<byte>();
        img.AddRange(new byte[96]);
        img.AddRange(CpuPayload);
        img.AddRange(ModPayload);
        // build strings ride after the payloads
        int dateOff = img.Count;
        img.AddRange(date);
        int timeOff = img.Count;
        img.AddRange(time);
        var buf = img.ToArray();
        PutU32(buf, 0, 0x0A40006F);
        PutU32(buf, 4, 0x40565053);
        PutU32(buf, 20, flashBase + (uint)dateOff);
        PutU32(buf, 24, flashBase + (uint)timeOff);
        PutU32(buf, 28, 4);
        void Bin(int i, string name, uint fileOff, byte[] payload, uint size)
        {
            int at = 32 + i * 16;
            PutU32(buf, at, NameRaw(name));
            PutU32(buf, at + 4, flashBase + fileOff);
            PutU32(buf, at + 8, size);
            PutU32(buf, at + 12, 0);
        }
        // bin 0 spans file offset zero (header included, like live images);
        // other bins point at their payload offsets
        Bin(0, "CPU", 0, CpuPayload, (uint)(96 + CpuPayload.Length));
        Bin(1, "MOD", (uint)modOff, ModPayload, (uint)ModPayload.Length);
        // real checksums over each span (checksum fields zero while summing)
        for (int i = 0; i < 2; i++)
        {
            int at = 32 + i * 16;
            uint addr = BitConverter.ToUInt32(buf, at + 4);
            uint size = BitConverter.ToUInt32(buf, at + 8);
            long spanStart = addr - flashBase;
            long spanLength = Math.Min(size, buf.Length - (int)spanStart);
            uint crc = Yafex.FileFormats.Spv.SpvParser.ComputeChecksum(buf, spanStart, spanLength);
            PutU32(buf, at + 12, crc);
        }
        return buf;
    }

    [Test]
    public void Valid_Detects90()
    {
        var res = (SpvDetectionResult)new SpvAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()));
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual(2, res.Bins.Count);
    }

    [Test]
    public void Lister_ShowsBinsAndBuild()
    {
        var mem = Mem(BuildImage(), "f.spv");
        var det = (SpvDetectionResult)new SpvAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var lister = new SpvAddon().CreateLister(det);
        Assert.NotNull(lister);
        using var sw = new StringWriter();
        lister!.List(mem, det, sw);
        var text = sw.ToString();
        StringAssert.Contains("CPU.bin", text);
        StringAssert.Contains("MOD.bin", text);
        StringAssert.Contains("May 30 2025", text);
        StringAssert.Contains("18:16:27", text);
    }

    [Test]
    public void Extract_EmitsPayloads()
    {
        var mem = Mem(BuildImage(), "f.spv");
        var det = (SpvDetectionResult)new SpvAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new SpvAddon().CreateExtractor(det).Extract(mem)
            .OrderBy(a => a.Name).ToList();
        Assert.AreEqual(2, artifacts.Count);
        CollectionAssert.AreEqual(ModPayload, artifacts.First(a => a.Name == "MOD.bin").Data.ToArray());
        // CPU spans offset zero, so the header itself is not emitted
        var cpu = artifacts.First(a => a.Name == "CPU.bin").Data.ToArray();
        CollectionAssert.AreEqual(CpuPayload, cpu);
    }

    [Test]
    public void Expander_ListsBins()
    {
        var exp = new SpvExpander();
        var img = new MemoryVirtualFile("f.spv", BuildImage());
        Assert.IsTrue(exp.Matches(img));
        var node = exp.Expand(img, new List<object>());
        node.EnsureLoaded();
        var names = node.Tree.Keys.ToList();
        CollectionAssert.Contains(names, "CPU.bin");
        CollectionAssert.Contains(names, "MOD.bin");
    }

    [Test]
    public void Garbage_Fails()
    {
        var rnd = new byte[256];
        new Random(31).NextBytes(rnd);
        var res = new SpvAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(rnd));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void WrongMagic_Fails()
    {
        var img = BuildImage();
        img[4] = 0x00;
        var res = new SpvAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(img));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void WrongChecksum_Fails()
    {
        var img = BuildImage();
        img[100] ^= 0xFF; // corrupt a payload byte: checksum must reject
        var res = new SpvAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(img));
        Assert.AreEqual(0, res.Confidence);
    }
}
