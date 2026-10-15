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
// Synthetic gzip tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Gzip;
using Yafex.Support;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class GzipTests
{
    internal static byte[] BuildGzip(byte[] payload, string? fname = null)
    {
        byte[] deflated;
        using (var ms = new MemoryStream())
        {
            using (var df = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                df.Write(payload, 0, payload.Length);
            }
            deflated = ms.ToArray();
        }
        using (var ms = new MemoryStream())
        {
            ms.WriteByte(0x1F);
            ms.WriteByte(0x8B);
            ms.WriteByte(0x08);
            ms.WriteByte(fname != null ? (byte)0x08 : (byte)0x00);
            ms.Write(new byte[6], 0, 6);
            if (fname != null)
            {
                ms.Write(Encoding.ASCII.GetBytes(fname), 0, fname.Length);
                ms.WriteByte(0);
            }
            ms.Write(deflated, 0, deflated.Length);
            uint crc = Crc32.Compute(payload, 0, payload.Length);
            ms.WriteByte((byte)crc);
            ms.WriteByte((byte)(crc >> 8));
            ms.WriteByte((byte)(crc >> 16));
            ms.WriteByte((byte)(crc >> 24));
            uint size = (uint)payload.Length;
            ms.WriteByte((byte)size);
            ms.WriteByte((byte)(size >> 8));
            ms.WriteByte((byte)(size >> 16));
            ms.WriteByte((byte)(size >> 24));
            return ms.ToArray();
        }
    }

    [Test]
    public void Valid_Detects90()
    {
        var res = (GzipDetectionResult)new GzipAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildGzip(new byte[] { 1, 2, 3, 4 })));
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual(4, res.Member.Payload.Length);
    }

    [Test]
    public void Extract_UsesFnameAndChains()
    {
        var inner = new byte[] { 0xAA, 0xBB, 0xCC };
        var mem = Mem(BuildGzip(inner, "uImage"), "kern.pak");
        var det = (GzipDetectionResult)new GzipAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        Assert.AreEqual("uImage", det.Member.FileName);
        var artifacts = new GzipAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("uImage", artifacts[0].Name);
        Assert.IsTrue(artifacts[0].Flags.HasFlag(DataSourceFlags.ProcessFurther));
        CollectionAssert.AreEqual(inner, artifacts[0].Data.ToArray());
    }

    [Test]
    public void Extract_WithoutFname_AppendsUngz()
    {
        var mem = Mem(BuildGzip(new byte[] { 5 }), "kern.pak");
        var det = (GzipDetectionResult)new GzipAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new GzipAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual("kern.pak.ungz", artifacts[0].Name);
    }

    [Test]
    public void BadCrc_Detects0()
    {
        var buf = BuildGzip(new byte[] { 1, 2 });
        buf[buf.Length - 8] ^= 0xFF;
        var res = new GzipAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(buf));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void TrailingGarbage_Detects0()
    {
        var buf = BuildGzip(new byte[] { 1 });
        var bad = new byte[buf.Length + 4];
        Array.Copy(buf, bad, buf.Length);
        var res = new GzipAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(bad));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Truncated_Detects0()
    {
        var res = new GzipAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(new byte[] { 0x1F, 0x8B, 0x08 }));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void CreateLazy_FallsBackToExpander()
    {
        // Gzip has no CreateVfs of its own: the expander registry supplies the tree
        var payload = Encoding.ASCII.GetBytes("inflated payload");
        var mem = Mem(BuildGzip(payload), "blob.gz");
        var addon = new GzipAddon();
        var det = addon.CreateDetector(new Dictionary<string, string>()).Detect(mem);
        Assert.IsNull(addon.CreateVfs(mem, det));
        var vfs = Yafex.Fuse.VfsMount.CreateLazy(addon, det, mem);
        Assert.NotNull(vfs);
        var top = vfs!.Root.Tree["blob.gz"].Node;
        top.EnsureLoaded();
        var file = (Yafex.Fuse.IVfsFile)top.Tree["blob"].Node;
        CollectionAssert.AreEqual(payload, file.Read(0, payload.Length));
    }

    [Test]
    public void CreateLazy_GarbageIsNull()
    {
        var rnd = new byte[4096];
        new Random(5).NextBytes(rnd);
        Assert.IsNull(Yafex.Fuse.VfsMount.CreateLazy(null, null, Mem(rnd)));
    }
}
