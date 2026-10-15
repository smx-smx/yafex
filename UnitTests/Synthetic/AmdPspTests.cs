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
// Synthetic AMD PSP tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.AmdPsp;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class AmdPspTests
{
    internal static byte[] BuildPspDir((byte Type, uint Size, ulong Addr)[] entries)
    {
        var buf = new byte[16 + entries.Length * 16];
        WriteAscii(buf, 0, "$PSP");
        WriteU32LE(buf, 8, (uint)entries.Length);
        for (int i = 0; i < entries.Length; i++)
        {
            int o = 16 + i * 16;
            buf[o] = entries[i].Type;
            WriteU32LE(buf, o + 4, entries[i].Size);
            var a = entries[i].Addr;
            for (int b = 0; b < 8; b++)
            {
                buf[o + 8 + b] = (byte)(a >> (8 * b));
            }
        }
        uint cksum = AmdPsp.Fletcher32(buf, 8, buf.Length - 8);
        WriteU32LE(buf, 4, cksum);
        return buf;
    }

    internal static byte[] BuildImageWithEfs(byte[] dir, int efsOffset = 0x100, int dirOffset = 0x1000)
    {
        var buf = new byte[dirOffset + dir.Length];
        WriteU32LE(buf, efsOffset, AmdPsp.EfsSignature);
        WriteU32LE(buf, efsOffset + 0x14, (uint)dirOffset);
        Array.Copy(dir, 0, buf, dirOffset, dir.Length);
        return buf;
    }

    [Test]
    public void ValidEfsAndPsp_Detects95()
    {
        var payload = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var dir = BuildPspDir(new[] { ((byte)0x04, (uint)payload.Length, (ulong)0x500 | (1UL << 62)) });
        var full = BuildImageWithEfs(dir);
        Array.Copy(payload, 0, full, 0x500, payload.Length);
        var res = (AmdPspDetectionResult)new AmdPspAddon()
            .CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(full));
        Assert.AreEqual(95, res.Confidence);
        Assert.AreEqual(1, res.Directories.Count);
        Assert.AreEqual(1, res.EfsList.Count);
    }

    [Test]
    public void BarePspDir_Detects90()
    {
        var dir = BuildPspDir(new[] { ((byte)0x04, (uint)4, (ulong)0x30 | (1UL << 62)) });
        var buf = new byte[0x100];
        Array.Copy(dir, buf, dir.Length);
        new byte[] { 1, 2, 3, 4 }.CopyTo(buf, 0x30);
        var res = (AmdPspDetectionResult)new AmdPspAddon()
            .CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(buf));
        Assert.AreEqual(90, res.Confidence);
    }

    [Test]
    public void BadChecksum_Detects0()
    {
        var dir = BuildPspDir(new[] { ((byte)0x04, (uint)4, (ulong)0x30 | (1UL << 62)) });
        dir[20] ^= 0xFF;
        var res = new AmdPspAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(dir));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Garbage_Detects0()
    {
        var res = new AmdPspAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(Zeros(0x100)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Truncated_Detects0()
    {
        var res = new AmdPspAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(Zeros(10)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void RandomMagicWithoutChecksum_Detects0()
    {
        // byte-hit without a valid checksum (like $PSP inside ARM blobs)
        var buf = Zeros(0x100);
        WriteAscii(buf, 0x40, "$PSP");
        buf[0x40 + 8] = 4;
        var res = new AmdPspAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(buf));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Extract_CarvesEntries()
    {
        var k0 = new byte[] { 0xAA, 0xBB, 0xCC };
        var k1 = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        var dir = BuildPspDir(new[]
        {
            ((byte)0x04, (uint)k0.Length, (ulong)0x500 | (1UL << 62)),
            ((byte)0x25, (uint)k1.Length, (ulong)0x40 | (2UL << 62)), // mode 2: dir+0x40
        });
        var full = BuildImageWithEfs(dir);
        Array.Resize(ref full, 0x1100);
        Array.Copy(k0, 0, full, 0x500, k0.Length);
        Array.Copy(k1, 0, full, 0x1000 + 0x40, k1.Length);
        var mem = Mem(full, "flash.bin");
        var det = (AmdPspDetectionResult)new AmdPspAddon()
            .CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new AmdPspAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(2, artifacts.Count);
        Assert.AreEqual("psp0_smu_offchip_fw_00.bin", artifacts[0].Name);
        Assert.AreEqual("psp0_psp_mp2_fw_01.bin", artifacts[1].Name);
        Assert.IsTrue(artifacts[0].Flags.HasFlag(DataSourceFlags.Output));
        Assert.IsTrue(artifacts[0].Flags.HasFlag(DataSourceFlags.ProcessFurther));
        CollectionAssert.AreEqual(k0, artifacts[0].Data.ToArray());
        CollectionAssert.AreEqual(k1, artifacts[1].Data.ToArray());
    }

    [Test]
    public void Extract_SkipsEmptyAndUnresolvable()
    {
        var k0 = new byte[] { 0xAA };
        var dir = BuildPspDir(new[]
        {
            ((byte)0x04, (uint)k0.Length, (ulong)0x500 | (1UL << 62)),
            ((byte)0x48, (uint)0, (ulong)0x600 | (1UL << 62)), // empty slot
            ((byte)0x25, (uint)0x10, (ulong)0xFFFFFF00 | (1UL << 62)), // past EOF
        });
        var full = BuildImageWithEfs(dir);
        Array.Copy(k0, 0, full, 0x500, k0.Length);
        var mem = Mem(full, "flash.bin");
        var det = (AmdPspDetectionResult)new AmdPspAddon()
            .CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new AmdPspAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        CollectionAssert.AreEqual(k0, artifacts[0].Data.ToArray());
    }

    [Test]
    public void Extract_ClampsOversize()
    {
        var dir = BuildPspDir(new[]
        {
            ((byte)0x04, (uint)0x10000, (ulong)0x500 | (1UL << 62)),
        });
        var full = BuildImageWithEfs(dir);
        var mem = Mem(full, "flash.bin");
        var det = (AmdPspDetectionResult)new AmdPspAddon()
            .CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new AmdPspAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual(full.Length - 0x500, artifacts[0].Data.Length);
    }

    [Test]
    public void Overlaps_DetectsCover()
    {
        Assert.IsTrue(AmdPspExtractor.Overlaps(0x1D02000, 0x2FE000, 0x1D00000, 0x300000));
        Assert.IsFalse(AmdPspExtractor.Overlaps(0x21000, 0x20000, 0x1D00000, 0x300000));
        Assert.IsFalse(AmdPspExtractor.Overlaps(0x100, 0x100, 0x200, 0x100));
    }

    [Test]
    public void Lister_OutputsDirsAndEntries()
    {
        var dir = BuildPspDir(new[] { ((byte)0x04, (uint)4, (ulong)0x500 | (1UL << 62)) });
        var full = BuildImageWithEfs(dir);
        var mem = Mem(full, "flash.bin");
        var det = (AmdPspDetectionResult)new AmdPspAddon()
            .CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var text = Synthetic.ListTree(new AmdPspAddon(), det, mem);
        StringAssert.Contains("smu_offchip_fw", text);
    }

    [Test]
    public void Expander_MatchesAndExpands()
    {
        var k0 = new byte[] { 0xAA, 0xBB };
        var dir = BuildPspDir(new[] { ((byte)0x04, (uint)k0.Length, (ulong)0x500 | (1UL << 62)) });
        var full = BuildImageWithEfs(dir);
        Array.Copy(k0, 0, full, 0x500, k0.Length);
        var mem = Mem(full, "flash.bin");
        var file = new Yafex.Fuse.RangeVirtualFile("flash.bin", mem, 0, full.Length);
        var expander = new Yafex.Fuse.AmdPspExpander();
        Assert.IsTrue(expander.Matches(file));
        Assert.IsFalse(new Yafex.Fuse.MtkBootExpander().Matches(file));
        var keepAlive = new List<object>();
        var node = expander.Expand(file, keepAlive);
        node.EnsureLoaded();
        var names = node.Tree.Keys.OrderBy(k => k).ToList();
        CollectionAssert.Contains(names, "psp0_smu_offchip_fw_00.bin");
        var fnode = (Yafex.Fuse.IVfsFile)node.Tree["psp0_smu_offchip_fw_00.bin"].Node;
        CollectionAssert.AreEqual(k0, fnode.Read(0, k0.Length)!);
    }
}
