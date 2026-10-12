#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
// Synthetic Android boot image tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.AndroidBoot;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class AndroidBootTests
{
    internal static byte[] BuildImage(
        byte[]? kernel = null,
        byte[]? ramdisk = null,
        byte[]? second = null,
        uint pageSize = 2048,
        uint headerVersion = 1,
        uint osVersion = 0x08000000)
    {
        kernel ??= new byte[] { 1, 2, 3, 4 };
        ramdisk ??= new byte[] { 5, 6 };
        second ??= Array.Empty<byte>();
        long Pages(long size) => size == 0 ? 0 : (size + pageSize - 1) / pageSize;
        long kPages = Pages(kernel.Length);
        long rPages = Pages(ramdisk.Length);
        long sPages = Pages(second.Length);
        long total = pageSize + (kPages + rPages + sPages) * pageSize;
        var buf = new byte[total];
        WriteAscii(buf, 0, AndroidBoot.Magic);
        WriteU32LE(buf, 8, (uint)kernel.Length);
        WriteU32LE(buf, 12, 0x10000000);
        WriteU32LE(buf, 16, (uint)ramdisk.Length);
        WriteU32LE(buf, 20, 0x11000000);
        WriteU32LE(buf, 24, (uint)second.Length);
        WriteU32LE(buf, 28, 0x12000000);
        WriteU32LE(buf, 32, 0x10000000);
        WriteU32LE(buf, 36, pageSize);
        WriteU32LE(buf, 40, headerVersion);
        WriteU32LE(buf, 44, osVersion);
        WriteAscii(buf, 48, "test");
        if (headerVersion >= 1)
        {
            WriteU32LE(buf, 1644, AndroidBoot.HeaderV1Size);
        }
        long off = pageSize;
        Array.Copy(kernel, 0, buf, off, kernel.Length);
        off += kPages * pageSize;
        if (ramdisk.Length > 0)
        {
            Array.Copy(ramdisk, 0, buf, off, ramdisk.Length);
            off += rPages * pageSize;
        }
        if (second.Length > 0)
        {
            Array.Copy(second, 0, buf, off, second.Length);
        }
        return buf;
    }

    [Test]
    public void ValidV1_Detects90()
    {
        var res = (AndroidBootDetectionResult)new AndroidBootAddon()
            .CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()));
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual(1u, res.Header.HeaderVersion);
        Assert.AreEqual(2, res.Header.Segments.Count);
    }

    [Test]
    public void ValidV0_Detects90()
    {
        var res = (AndroidBootDetectionResult)new AndroidBootAddon()
            .CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage(headerVersion: 0)));
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual(0u, res.Header.HeaderVersion);
    }

    [Test]
    public void BadMagic_Detects0()
    {
        var buf = BuildImage();
        buf[0] ^= 0xFF;
        var res = new AndroidBootAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(buf));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Truncated_Detects0()
    {
        var res = new AndroidBootAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(Zeros(100)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void BadPageSize_Detects0()
    {
        var buf = BuildImage();
        WriteU32LE(buf, 36, 1000);
        var res = new AndroidBootAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(buf));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void V3_Detects0()
    {
        var buf = BuildImage();
        WriteU32LE(buf, 40, 3);
        var res = new AndroidBootAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(buf));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Extract_EmitsKernelRamdisk()
    {
        var kernel = new byte[] { 0xAA, 0xBB, 0xCC };
        var ramdisk = new byte[] { 0x11, 0x22 };
        var mem = Mem(BuildImage(kernel, ramdisk), "boot.img");
        var det = (AndroidBootDetectionResult)new AndroidBootAddon()
            .CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new AndroidBootAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(2, artifacts.Count);
        Assert.AreEqual("kernel.bin", artifacts[0].Name);
        Assert.AreEqual("ramdisk.bin", artifacts[1].Name);
        Assert.IsTrue(artifacts[0].Flags.HasFlag(DataSourceFlags.Output));
        Assert.IsTrue(artifacts[0].Flags.HasFlag(DataSourceFlags.ProcessFurther));
        CollectionAssert.AreEqual(kernel, artifacts[0].Data.ToArray());
        CollectionAssert.AreEqual(ramdisk, artifacts[1].Data.ToArray());
    }

    [Test]
    public void Lister_OutputsSegments()
    {
        var mem = Mem(BuildImage(), "boot.img");
        var det = (AndroidBootDetectionResult)new AndroidBootAddon()
            .CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var lister = new AndroidBootAddon().CreateLister(det);
        Assert.NotNull(lister);
        using var sw = new StringWriter();
        lister!.List(mem, det, sw);
        var text = sw.ToString();
        StringAssert.Contains("AndroidBoot", text);
        StringAssert.Contains("kernel", text);
        StringAssert.Contains("ramdisk", text);
    }

    internal static byte[] BuildDtb(int totalSize = 64)
    {
        var buf = new byte[totalSize];
        buf[0] = 0xD0;
        buf[1] = 0x0D;
        buf[2] = 0xFE;
        buf[3] = 0xED;
        buf[4] = (byte)(totalSize >> 24);
        buf[5] = (byte)(totalSize >> 16);
        buf[6] = (byte)(totalSize >> 8);
        buf[7] = (byte)totalSize;
        return buf;
    }

    [Test]
    public void FindAppendedDtb_FindsOffset()
    {
        var prefix = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var dtb = BuildDtb(64);
        var kernel = prefix.Concat(dtb).ToArray();
        Assert.AreEqual(prefix.Length, AndroidBoot.FindAppendedDtb(kernel));
    }

    [Test]
    public void FindAppendedDtb_NoneReturnsMinusOne()
    {
        Assert.AreEqual(-1, AndroidBoot.FindAppendedDtb(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public void Extract_KernelAppendedDtbSplits()
    {
        var prefix = new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0x01, 0x02, 0x03, 0x04 };
        var dtb = BuildDtb(64);
        var kernel = prefix.Concat(dtb).ToArray();
        var mem = Mem(BuildImage(kernel, new byte[] { 0x0A }), "edk.img");
        var det = (AndroidBootDetectionResult)new AndroidBootAddon()
            .CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new AndroidBootAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(3, artifacts.Count);
        Assert.AreEqual("kernel.bin", artifacts[0].Name);
        Assert.AreEqual("kernel.dtb", artifacts[1].Name);
        Assert.AreEqual("ramdisk.bin", artifacts[2].Name);
        CollectionAssert.AreEqual(prefix, artifacts[0].Data.ToArray());
        CollectionAssert.AreEqual(dtb, artifacts[1].Data.ToArray());
    }

    [Test]
    public void Expander_MatchesAndExpands()
    {
        var kernel = new byte[] { 0xAA, 0xBB };
        var bytes = BuildImage(kernel, new byte[] { 0x11 });
        var mem = Mem(bytes, "boot.img");
        var file = new Yafex.Fuse.RangeVirtualFile("boot.img", mem, 0, bytes.Length);
        var expander = new Yafex.Fuse.AndroidBootExpander();
        Assert.IsTrue(expander.Matches(file));
        Assert.IsFalse(new Yafex.Fuse.MtkBootExpander().Matches(file));
        var keepAlive = new List<object>();
        var dir = expander.Expand(file, keepAlive);
        dir.EnsureLoaded();
        var names = dir.Tree.Keys.OrderBy(k => k).ToList();
        CollectionAssert.Contains(names, "kernel.bin");
        CollectionAssert.Contains(names, "ramdisk.bin");
        var node = (Yafex.Fuse.IVfsFile)dir.Tree["kernel.bin"].Node;
        CollectionAssert.AreEqual(kernel, node.Read(0, kernel.Length)!);
    }
}
