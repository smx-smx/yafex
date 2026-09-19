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
using System.IO;
using System.Linq;

using NUnit.Framework;

using Smx.SharpIO.Extensions;
using Smx.SharpIO.Memory.Buffers;

using Yafex;
using Yafex.Metadata;
using Yafex.Support;

namespace UnitTests;

[TestFixture]
public class SpoolTests
{
    private string _root = "";
    private long _savedThreshold;

    [SetUp]
    public void Setup()
    {
        _savedThreshold = ArtifactSpool.ThresholdBytes;
        _root = Path.Combine(Path.GetTempPath(), $"yafex-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void Teardown()
    {
        ArtifactSpool.ThresholdBytes = _savedThreshold;
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static MemoryDataSource Parent(string dir)
    {
        var parent = new MemoryDataSource(new Memory64<byte>(Array.Empty<byte>()))
        {
            Name = "parent",
            Flags = DataSourceFlags.Input
        };
        parent.AddMetadata(new BaseDirectoryPath(dir));
        return parent;
    }

    [Test]
    public void TestAutoSmall_StaysInMemory()
    {
        ArtifactSpool.ThresholdBytes = 1L << 30;
        var parent = Parent(_root);
        var ds = ArtifactBuilder.FromBytes(parent, _root, "small.bin", new byte[] { 1, 2, 3 });
        Assert.That(ds, Is.TypeOf<MemoryDataSource>());
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, ds.Data.ToArray());
        Assert.AreEqual(0, Directory.GetFiles(_root).Length,
            "no sidecar should exist for memory artifacts");
    }

    [Test]
    public void TestForcedFile_StreamsDirectAndAtomically()
    {
        var parent = Parent(_root);
        var payload = new byte[4096];
        Random.Shared.NextBytes(payload);
        var buff = ArtifactBuilder.BeginBuffer(_root, "big.bin", DataSourceFlags.Output, SpoolHint.File);
        buff.Write(payload.AsSpan(0, 1024).ToArray());
        buff.Write(payload.AsSpan(1024).ToArray());
        var ds = ArtifactBuilder.Complete(buff, parent, _root);

        Assert.That(ds, Is.TypeOf<FileDataSource>());
        var final = Path.Combine(_root, "big.bin");
        Assert.IsTrue(File.Exists(final));
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.partial-*").Length);
        CollectionAssert.AreEqual(payload, ds.Data.ToArray());
        Assert.AreEqual("big.bin", ds.GetMetadata<OutputFileName>().Single().FileName);
        if (ds is IDisposable d) d.Dispose();
    }

    [Test]
    public void TestAutoMigrates_PastThreshold()
    {
        ArtifactSpool.ThresholdBytes = 1024;
        var parent = Parent(_root);
        var payload = new byte[4096];
        Random.Shared.NextBytes(payload);
        var ds = ArtifactBuilder.FromBytes(parent, _root, "mig.bin", payload);
        Assert.That(ds, Is.TypeOf<FileDataSource>());
        CollectionAssert.AreEqual(payload, File.ReadAllBytes(Path.Combine(_root, "mig.bin")));
        CollectionAssert.AreEqual(payload, ds.Data.ToArray());
        if (ds is IDisposable d) d.Dispose();
    }

    [Test]
    public void TestWriteAt_SparseAssembly()
    {
        ArtifactSpool.ThresholdBytes = 16;
        var parent = Parent(_root);
        var buff = ArtifactBuilder.BeginBuffer(_root, "sparse.bin", DataSourceFlags.Output);
        buff.WriteAt(10, new Memory64<byte>(new byte[] { 9, 9 }).AsReadonlySpan());
        var ds = ArtifactBuilder.Complete(buff, parent, _root);
        var data = ds.Data.ToArray();
        Assert.AreEqual(12, data.Length);
        Assert.AreEqual(9, data[10]);
        Assert.AreEqual(9, data[11]);
        Assert.AreEqual(0, data[0]);
        if (ds is IDisposable d) d.Dispose();
    }

    [Test]
    public void TestAbandonedBuffer_DeletesPartial()
    {
        var buff = ArtifactBuilder.BeginBuffer(_root, "ab.bin", DataSourceFlags.Output, SpoolHint.File);
        buff.Write(new byte[] { 1 });
        buff.Dispose();
        Assert.AreEqual(0, Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Length);
    }
}
