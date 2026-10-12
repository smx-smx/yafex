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
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Smx.SharpIO.Memory.Buffers;

using Yafex;
using Yafex.FileFormats;
using Yafex.FileFormats.EpkV2;
using Yafex.FileFormats.LgUp;
using Yafex.Fuse;
using Yafex.Support;

namespace UnitTests;

[TestFixture]
public class VfsTests
{
    private string _root = "";

    [SetUp]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"yafex-vfstest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void Teardown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static List<string> ChildNames(IVfsNode dir)
    {
        dir.EnsureLoaded();
        return dir.Tree.Keys.OrderBy(k => k).ToList();
    }

    [Test]
    public void TestLazyDirectory_LoadsOnce()
    {
        var calls = 0;
        var dir = new LazyVfsDirectory("lazy", Helpers.OctalLiteral(0755), () =>
        {
            calls++;
            return new IVfsNode[] { new YafexDirectory("child", Helpers.OctalLiteral(0755)) };
        });
        Assert.AreEqual(0, dir.Tree.Count);
        dir.EnsureLoaded();
        dir.EnsureLoaded();
        Assert.AreEqual(1, calls);
        Assert.IsTrue(dir.Tree.ContainsKey("child"));
    }

    [Test]
    public void TestRawPakStream_MatchesStoredBytes()
    {
        var stored = PakStreamTests.Sequence(0, 300);
        var source = new MappedBlockSource(new Memory64<byte>(stored));
        var image = new PakImage
        {
            Name = "test.pak",
            Source = source,
            Segments = new List<PakSegment>
            {
                new() { PhysicalDataOffset = 0, EncryptedSize = 100, ContentOffset = 0, LogicalSize = 100, WasDecrypted = false },
                new() { PhysicalDataOffset = 100, EncryptedSize = 200, ContentOffset = 0, LogicalSize = 200, WasDecrypted = false },
            }
        };
        Assert.AreEqual(300, image.RawLength);
        using var s = image.OpenRawStream();
        var buf = new byte[300];
        var n = 0;
        while (n < buf.Length)
        {
            var r = s.Read(buf, n, buf.Length - n);
            if (r <= 0) break;
            n += r;
        }
        Assert.AreEqual(300, n);
        CollectionAssert.AreEqual(stored, buf);
        // straddling read across the segment boundary
        s.Seek(90, SeekOrigin.Begin);
        var win = new byte[20];
        Assert.AreEqual(20, s.Read(win, 0, 20));
        CollectionAssert.AreEqual(stored.AsSpan(90, 20).ToArray(), win);
    }

    [Test]
    public void TestGzipExpander_RoundTrip()
    {
        var payload = Encoding.ASCII.GetBytes(new string('a', 1000) + new string('b', 1000));
        byte[] gzipped;
        using (var ms = new MemoryStream())
        {
            using (var z = new GZipStream(ms, CompressionLevel.Fastest))
                z.Write(payload, 0, payload.Length);
            gzipped = ms.ToArray();
        }
        var image = new PakImage
        {
            Name = "data.gz",
            Source = new MappedBlockSource(new Memory64<byte>(gzipped)),
            Segments = new List<PakSegment>
            {
                new() { PhysicalDataOffset = 0, EncryptedSize = gzipped.Length, ContentOffset = 0, LogicalSize = gzipped.Length, WasDecrypted = false },
            }
        };

        var expander = new GzipExpander();
        Assert.IsTrue(expander.Matches(image));
        Assert.IsFalse(new SquashfsExpander().Matches(image));

        var keepAlive = new List<object>();
        var dir = expander.Expand(image, keepAlive);
        dir.EnsureLoaded();
        Assert.AreEqual(1, dir.Tree.Count);
        var node = (PakVfsFile)dir.Tree.Values.First().Node;
        Assert.AreEqual("data", node.Name);
        Assert.AreEqual(payload.Length, node.Size);
        var chunk = ((IVfsFile)node).Read(0, payload.Length);
        Assert.NotNull(chunk);
        CollectionAssert.AreEqual(payload, chunk!);
        using var s = ((IVirtualFile)new GzipVirtualFile("data", image)).OpenRead();
        Assert.AreEqual(payload.Length, s.Length);
        var outBuf = new byte[payload.Length];
        var n = 0;
        while (n < outBuf.Length)
        {
            var r = s.Read(outBuf, n, outBuf.Length - n);
            if (r <= 0) break;
            n += r;
        }
        CollectionAssert.AreEqual(payload, outBuf);
        // backward seek replays the inflater
        s.Seek(0, SeekOrigin.Begin);
        Assert.AreEqual((int)payload[0], s.ReadByte());
    }

    [Test]
    public void TestEpkVfsTree_OptInViews()
    {
        var seg = PakStreamTests.Sequence(0, 0x40);
        var (file, _) = PakStreamTests.BuildEpk2File("root", seg);
        var path = Path.Combine(_root, "t.epk");
        File.WriteAllBytes(path, file);

        using var mf = new MFile(path);
        var vfs = new EpkVfs(mf, new KeysRepository(null!));
        var names = ChildNames(vfs.Root);
        Assert.IsTrue(names.Contains("t"), string.Join(",", names));
        var fw = vfs.Root.Tree["t"].Node;
        var paks = ChildNames(fw);
        Assert.IsTrue(paks.Contains("root.pak"), string.Join(",", paks));
        Assert.IsTrue(paks.Contains("root.pak.raw"), string.Join(",", paks));
        Assert.IsTrue(paks.Contains("root.pak.meta"), string.Join(",", paks));

        // raw view == stored segment bytes, decrypted view == plaintext (no keys needed)
        var rawNode = (IVfsFile)fw.Tree["root.pak.raw"].Node;
        var raw = rawNode.Read(0, seg.Length);
        Assert.NotNull(raw);
        CollectionAssert.AreEqual(seg, raw!);

        var metaNode = (IVfsFile)fw.Tree["root.pak.meta"].Node;
        var meta = Encoding.ASCII.GetString(metaNode.Read(0, 4096)!);
        StringAssert.Contains("root", meta);
    }

    [Test]
    public void TestIdbExpander_DecodesPng()
    {
        var pal = new[] { new byte[] { 0xFF, 0x01, 0x02, 0x03 } };
        var lgi = IdbTests.BuildLgi1(1, 1, 8, 0x80, pal, new byte[] { 0 });
        var img = new List<byte>();
        img.AddRange(Encoding.ASCII.GetBytes("IDB_VERSION=1.1\n"));
        img.AddRange(Encoding.ASCII.GetBytes("IDB_NUM_IMG=1\n"));
        img.AddRange(Encoding.ASCII.GetBytes("IDB_HDR_ENDMARK\n"));
        int tableStart = img.Count;
        int dataStart = tableStart + 16;
        img.AddRange(new byte[8]);
        img.AddRange(BitConverter.GetBytes(lgi.Length));
        img.AddRange(BitConverter.GetBytes(dataStart));
        img.AddRange(lgi);
        var path = Path.Combine(_root, "t.lr");
        File.WriteAllBytes(path, img.ToArray());

        using var mf = new MFile(path);
        var image = new RangeVirtualFile("t.lr", mf, 0, mf.Data.Length);
        var expander = new IdbExpander();
        Assert.IsTrue(expander.Matches(image));
        var keepAlive = new List<object>();
        var dir = expander.Expand(image, keepAlive);
        var names = ChildNames(dir);
        Assert.AreEqual(new[] { "1.png" }, names);
        var node = (IVfsFile)dir.Tree["1.png"].Node;
        var head = node.Read(0, 8);
        Assert.NotNull(head);
        CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, head!);
    }

    [Test]
    public void TestUfsExpander_RoundTrip()
    {
        var bytes = UfsTests.BuildUfs1();
        var path = Path.Combine(_root, "part.bin");
        File.WriteAllBytes(path, bytes);

        using var mf = new MFile(path);
        var image = new RangeVirtualFile("part.bin", mf, 0, mf.Data.Length);
        var expander = new UfsExpander();
        Assert.IsTrue(expander.Matches(image));
        Assert.IsFalse(new Jffs2Expander().Matches(image));
        var keepAlive = new List<object>();
        var dir = expander.Expand(image, keepAlive);
        var names = ChildNames(dir);
        CollectionAssert.AreEquivalent(new[] { "hello.txt", "link" }, names);
        var node = (IVfsFile)dir.Tree["hello.txt"].Node;
        var content = node.Read(0, 12);
        Assert.NotNull(content);
        CollectionAssert.AreEqual(Encoding.ASCII.GetBytes("hello, ufs1!"), content!);
    }

    [Test]
    public void TestAndroidBootExpander_RoundTrip()
    {
        var kernel = new byte[] { 0xAA, 0xBB, 0xCC };
        var ramdisk = new byte[] { 0x11, 0x22 };
        var bytes = AndroidBootTests.BuildImage(kernel, ramdisk);
        var path = Path.Combine(_root, "boot.img");
        File.WriteAllBytes(path, bytes);

        using var mf = new MFile(path);
        var image = new RangeVirtualFile("boot.img", mf, 0, mf.Data.Length);
        var expander = new AndroidBootExpander();
        Assert.IsTrue(expander.Matches(image));
        Assert.IsFalse(new MtkBootExpander().Matches(image));
        var keepAlive = new List<object>();
        var dir = expander.Expand(image, keepAlive);
        var names = ChildNames(dir);
        CollectionAssert.Contains(names, "kernel.bin");
        CollectionAssert.Contains(names, "ramdisk.bin");
        var node = (IVfsFile)dir.Tree["kernel.bin"].Node;
        CollectionAssert.AreEqual(kernel, node.Read(0, kernel.Length)!);

        var det = (Yafex.FileFormats.AndroidBoot.AndroidBootDetectionResult)
            new Yafex.FileFormats.AndroidBoot.AndroidBootAddon()
                .CreateDetector(new Dictionary<string, string>()).Detect(mf);
        var vfs = new Yafex.FileFormats.AndroidBoot.AndroidBootAddon()
            .CreateVfs(mf, det);
        Assert.NotNull(vfs);
        var top = ChildNames(vfs!.Root);
        Assert.IsTrue(top.Contains("boot"), string.Join(",", top));
        var fw = vfs.Root.Tree["boot"].Node;
        var segs = ChildNames(fw);
        CollectionAssert.Contains(segs, "kernel.bin");
        CollectionAssert.Contains(segs, "ramdisk.bin");
    }

    [Test]
    public void TestAmdPspVfsTree_RoundTrip()
    {
        var k0 = new byte[] { 0xAA, 0xBB, 0xCC };
        var dir = AmdPspTests.BuildPspDir(new[] { ((byte)0x04, (uint)k0.Length, (ulong)0x500 | (1UL << 62)) });
        var full = AmdPspTests.BuildImageWithEfs(dir);
        Array.Copy(k0, 0, full, 0x500, k0.Length);
        var path = Path.Combine(_root, "flash.bin");
        File.WriteAllBytes(path, full);

        using var mf = new MFile(path);
        var image = new RangeVirtualFile("flash.bin", mf, 0, mf.Data.Length);
        var expander = new AmdPspExpander();
        Assert.IsTrue(expander.Matches(image));
        var keepAlive = new List<object>();
        var expandDir = expander.Expand(image, keepAlive);
        var expandNames = ChildNames(expandDir);
        CollectionAssert.Contains(expandNames, "psp0_smu_offchip_fw_00.bin");

        var det = (Yafex.FileFormats.AmdPsp.AmdPspDetectionResult)
            new Yafex.FileFormats.AmdPsp.AmdPspAddon()
                .CreateDetector(new Dictionary<string, string>()).Detect(mf);
        var vfs = new Yafex.FileFormats.AmdPsp.AmdPspAddon()
            .CreateVfs(mf, det);
        Assert.NotNull(vfs);
        var top = ChildNames(vfs!.Root);
        Assert.IsTrue(top.Contains("flash"), string.Join(",", top));
        var fw = vfs.Root.Tree["flash"].Node;
        var segs = ChildNames(fw);
        CollectionAssert.Contains(segs, "psp0_smu_offchip_fw_00.bin");
        var node = (IVfsFile)fw.Tree["psp0_smu_offchip_fw_00.bin"].Node;
        CollectionAssert.AreEqual(k0, node.Read(0, k0.Length)!);
    }

    [Test]
    public void TestLgUpVfsTree_LazyUnits()
    {
        var key = LgUpTests.RandomKey();
        var content = new byte[64];
        Random.Shared.NextBytes(content);
        var file = LgUpTests.BuildPackage(key, content, out var expected);

        var mf = new MemoryDataSource(new Memory64<byte>(file))
            { Name = "test.up", Flags = DataSourceFlags.Input };
        var det = new LgUpDetector(new KeysRepository(null!),
            new Dictionary<string, string> { { "key", Convert.ToHexString(key) } });
        var res = (LgUpDetectionResult)det.Detect(mf);
        Assert.AreEqual(100, res.Confidence);

        var vfs = new LgUpVfs(res.Context!, mf);
        var top = ChildNames(vfs.Root);
        Assert.IsTrue(top.Contains("test.up"), string.Join(",", top));
        var fw = vfs.Root.Tree["test.up"].Node;
        var secs = ChildNames(fw);
        Assert.IsTrue(secs.Contains("00_test"), string.Join(",", secs));
        var sec = fw.Tree["00_test"].Node;
        var units = ChildNames(sec);
        Assert.IsTrue(units.Contains("unit_0000.data"), string.Join(",", units));
        Assert.IsTrue(units.Contains("unit_0000.data.raw"), string.Join(",", units));
        Assert.IsTrue(units.Contains("unit_0000.data.meta"), string.Join(",", units));

        // payload untouched until first read; then memoized decrypt+decompress
        var unitNode = (IVfsFile)sec.Tree["unit_0000.data"].Node;
        var out0 = unitNode.Read(0, expected.Length);
        Assert.NotNull(out0);
        CollectionAssert.AreEqual(expected, out0!);
        var out1 = unitNode.Read(10, 20);
        Assert.NotNull(out1);
        CollectionAssert.AreEqual(expected.AsSpan(10, 20).ToArray(), out1!);
    }
}
