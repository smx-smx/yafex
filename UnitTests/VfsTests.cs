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
