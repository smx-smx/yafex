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
// Synthetic MtkBdp tests: a minimal update image (two partitions, one
// split across two chunks, PITIT/PIT/BIT tables) built in-memory.
// Fully in-memory, no fixtures.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.MtkBdp;
using Yafex.Fuse;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class MtkBdpTests
{
    internal static readonly byte[] BootA = Encoding.ASCII.GetBytes("HELLO-");
    internal static readonly byte[] BootB = Encoding.ASCII.GetBytes("WORLD");
    internal static readonly byte[] Root = Encoding.ASCII.GetBytes("root-payload!");

    private static void PutU32(List<byte> b, uint v)
    {
        b.Add((byte)v);
        b.Add((byte)(v >> 8));
        b.Add((byte)(v >> 16));
        b.Add((byte)(v >> 24));
    }

    private static void PutName(List<byte> b, string name)
    {
        var nb = Encoding.ASCII.GetBytes(name);
        if (nb.Length > 16) throw new ArgumentException(nameof(name));
        int start = b.Count;
        b.AddRange(nb);
        while (b.Count - start < 16) b.Add(0);
    }

    internal static byte[] BuildImage()
    {
        // chunk payloads first so offsets are known
        var img = new List<byte>();
        int bootAOff = 0;
        img.AddRange(BootA);
        int bootBOff = img.Count;
        img.AddRange(BootB);
        int rootOff = img.Count;
        img.AddRange(Root);
        // PIT: magic x2 + unk + first + esize + ecount + entries
        int pitOff = img.Count;
        img.AddRange(new byte[] { 0xDC, 0xEA, 0x30, 0x85, 0xDC, 0xEA, 0x30, 0x85 });
        PutU32(img, 1);
        PutU32(img, 24);
        PutU32(img, 64);
        PutU32(img, 2);
        PutName(img, "boot_1");
        PutU32(img, 1);
        PutU32(img, 0);
        PutU32(img, 0x00000000);
        PutU32(img, 0x00200000);
        while (img.Count < pitOff + 24 + 64) img.Add(0);
        PutName(img, "rootfs");
        PutU32(img, 2);
        PutU32(img, 0);
        PutU32(img, 0x02A00000);
        PutU32(img, 0x02C00000);
        while (img.Count < pitOff + 24 + 128) img.Add(0);
        // BIT: magic x5 + entries + END
        int bitOff = img.Count;
        for (int k = 0; k < 5; k++) img.AddRange(new byte[] { 0xCD, 0xAB, 0x30, 0x85 });
        void Bit(uint id, int off, byte[] payload, uint inpart)
        {
            PutU32(img, id);
            PutU32(img, (uint)off);
            PutU32(img, (uint)payload.Length);
            PutU32(img, inpart);
            PutU32(img, 0);
        }
        Bit(1, bootAOff, BootA, 0);
        Bit(1, bootBOff, BootB, (uint)BootA.Length);
        Bit(2, rootOff, Root, 0);
        PutU32(img, 0x8530EFEF);
        PutU32(img, 0);
        PutU32(img, 0);
        PutU32(img, 0);
        PutU32(img, 0);
        // PITIT: magic x2 + flags + one entry (size = exact table length)
        int pititOff = img.Count;
        img.AddRange(new byte[] { 0x69, 0x54, 0x49, 0x50, 0x69, 0x54, 0x49, 0x50 });
        PutU32(img, 1);
        PutU32(img, 0);
        PutU32(img, 0x08000000);
        PutU32(img, (uint)pitOff);
        PutU32(img, (uint)(bitOff - pitOff));
        PutU32(img, 0);
        return img.ToArray();
    }

    [Test]
    public void Valid_Detects90()
    {
        var res = (MtkBdpDetectionResult)new MtkBdpAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()));
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual(2, res.Parts.Count);
    }

    [Test]
    public void Lister_ShowsPartitions()
    {
        var mem = Mem(BuildImage(), "f.bin");
        var det = (MtkBdpDetectionResult)new MtkBdpAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var lister = new MtkBdpAddon().CreateLister(det);
        Assert.NotNull(lister);
        using var sw = new StringWriter();
        lister!.List(mem, det, sw);
        var text = sw.ToString();
        StringAssert.Contains("boot_1", text);
        StringAssert.Contains("rootfs", text);
    }

    [Test]
    public void Extract_AssemblesChunksInOrder()
    {
        var mem = Mem(BuildImage(), "f.bin");
        var det = (MtkBdpDetectionResult)new MtkBdpAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new MtkBdpAddon().CreateExtractor(det).Extract(mem)
            .OrderBy(a => a.Name).ToList();
        Assert.AreEqual(2, artifacts.Count);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("HELLO-WORLD"), artifacts.First(a => a.Name == "boot_1.bin").Data.ToArray());
        CollectionAssert.AreEqual(Root, artifacts.First(a => a.Name == "rootfs.bin").Data.ToArray());
    }

    [Test]
    public void Expander_ListsPartitions()
    {
        var exp = new MtkBdpExpander();
        var img = new MemoryVirtualFile("f.bin", BuildImage());
        Assert.IsTrue(exp.Matches(img));
        var node = exp.Expand(img, new List<object>());
        node.EnsureLoaded();
        var names = node.Tree.Keys.ToList();
        CollectionAssert.Contains(names, "boot_1.bin");
        CollectionAssert.Contains(names, "rootfs.bin");
    }

    [Test]
    public void Garbage_Fails()
    {
        var rnd = new byte[8192];
        new Random(51).NextBytes(rnd);
        var res = new MtkBdpAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(rnd));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void TablesWithoutCrossMatch_Fail()
    {
        // PIT names no BIT entry: no extractable partition, no detection
        var img = BuildImage();
        // corrupt the BIT magic
        for (int i = 0; i < img.Length - 20; i++)
        {
            if (img[i] == 0xCD && img[i + 1] == 0xAB)
            {
                img[i] = 0x00;
                break;
            }
        }
        var res = new MtkBdpAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(img));
        Assert.AreEqual(0, res.Confidence);
    }
}
