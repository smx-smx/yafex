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
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileSystems.Squashfs3;
using Yafex.Fuse;

namespace UnitTests;

/// <summary>Content-expander sniffing and nested-member layout.</summary>
[TestFixture]
public class NestedExpansionTests
{
    /// <summary>Tar with a member past the 128 KiB sniff window and a nested tar.gz.</summary>
    internal static byte[] BuildTar()
    {
        var inner = new MemoryStream();
        using (var gz = new GZipStream(inner, CompressionLevel.Fastest, leaveOpen: true))
        using (var tw = new TarWriter(gz, TarEntryFormat.Ustar, leaveOpen: true))
        {
            AddFile(tw, "README.txt", Encoding.ASCII.GetBytes("inner readme"));
        }
        var outer = new MemoryStream();
        using (var tw = new TarWriter(outer, TarEntryFormat.Ustar, leaveOpen: true))
        {
            tw.WriteEntry(new UstarTarEntry(TarEntryType.Directory, "fw/"));
            AddFile(tw, "fw/README.txt", Encoding.ASCII.GetBytes("outer readme"));
            AddFile(tw, "fw/big.bin", new byte[200_000]);
            AddFile(tw, "fw/extra.tar.gz", inner.ToArray());
        }
        return outer.ToArray();
    }

    private static void AddFile(TarWriter tw, string name, byte[] data)
    {
        tw.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(data) });
    }

    [Test]
    public void TarExpander_MatchesArchivesLargerThanTheSniffWindow()
    {
        var tar = new MemoryVirtualFile("a.tar", BuildTar());
        Assert.That(tar.Length, Is.GreaterThan(128 * 1024));
        Assert.IsInstanceOf<TarExpander>(ContentExpanders.FindMatch(tar));
    }

    [Test]
    public void NestedMembers_ExpandUnderDotD_WithoutNameCollisions()
    {
        var keepAlive = new List<object>();
        var dir = new TarExpander().Expand(new MemoryVirtualFile("a.tar", BuildTar()), keepAlive);
        var paths = VfsWalk.Walk(dir, VfsWalk.AllNested).Select(e => e.Path).ToList();
        CollectionAssert.Contains(paths, "fw/README.txt");
        CollectionAssert.Contains(paths, "fw/extra.tar.gz");
        // gzip member -> its inflated tar -> that tar's members, each under .d/
        CollectionAssert.Contains(paths, "fw/extra.tar.gz.d/extra.tar.d/README.txt");
        var outer = VfsWalk.Walk(dir).Single(e => e.Path == "fw/README.txt");
        using var r = new StreamReader(VfsWalk.OpenRead(outer.Node));
        Assert.AreEqual("outer readme", r.ReadToEnd());
    }

    [TestCase((ushort)3, true)]
    [TestCase((ushort)4, false)]
    public void Squashfs3_SniffRequiresMajorVersion3(ushort major, bool expected)
    {
        var head = new byte[32];
        Encoding.ASCII.GetBytes("hsqs").CopyTo(head, 0);
        BitConverter.GetBytes(major).CopyTo(head, 28);
        Assert.AreEqual(expected, Squashfs3FileSystem.IsVersion3(head));
        Assert.AreEqual(expected, new Squashfs3Expander().Matches(new MemoryVirtualFile("s.img", head)));
    }

    [Test]
    public void NestedViews_AreNotSniffedWhileBuildingAndHiddenByDefault()
    {
        var keepAlive = new List<object>();
        var dir = new TarExpander().Expand(new MemoryVirtualFile("a.tar", BuildTar()), keepAlive);
        dir.EnsureLoaded();
        var fw = dir.Tree["fw"].Node;
        fw.EnsureLoaded();
        // every member gets a view; none has been sniffed or expanded
        var views = VfsWalk.NestedViewsOf(fw);
        CollectionAssert.AreEquivalent(new[] { "README.txt.d", "big.bin.d", "extra.tar.gz.d" }, views.Select(v => v.Name));
        Assert.IsTrue(views.All(v => v.Tree.Count == 0));

        // depth 0: own structure only; views are not listed as children either
        CollectionAssert.AreEquivalent(new[] { "fw", "fw/README.txt", "fw/big.bin", "fw/extra.tar.gz" },
            VfsWalk.Walk(dir).Select(e => e.Path));
        Assert.IsFalse(VfsWalk.Children(fw).Any(n => n is NestedVfsDirectory));

        // depth 1: only views whose sniff finds content
        var depth1 = VfsWalk.Walk(dir, 1).Select(e => e.Path).ToList();
        CollectionAssert.Contains(depth1, "fw/extra.tar.gz.d/extra.tar");
        Assert.IsFalse(depth1.Any(p => p.StartsWith("fw/big.bin.d")));
        Assert.IsFalse(depth1.Any(p => p.Contains("extra.tar.d/")), "depth 1 stops before the inner tar");
    }

    [Test]
    public void MountHidesViewsWithoutContentOrWhenDisabled()
    {
        var keepAlive = new List<object>();
        var dir = new TarExpander().Expand(new MemoryVirtualFile("a.tar", BuildTar()), keepAlive);
        dir.EnsureLoaded();
        var fw = dir.Tree["fw"].Node;
        fw.EnsureLoaded();
        var gz = (NestedVfsDirectory)fw.Tree["extra.tar.gz.d"].Node;
        var big = (NestedVfsDirectory)fw.Tree["big.bin.d"].Node;
        try
        {
            Assert.IsFalse(NestedViews.IsHiddenInMount(gz));
            Assert.IsTrue(NestedViews.IsHiddenInMount(big));
            NestedViews.ShowInMount = false;
            Assert.IsTrue(NestedViews.IsHiddenInMount(gz));
        }
        finally
        {
            NestedViews.ShowInMount = true;
        }
    }

    [Test]
    public void HeadCachedFile_ServesHeadOnceAndTailFromSource()
    {
        var data = Enumerable.Range(0, HeadCachedFile.HeadSize + 1000).Select(i => (byte)i).ToArray();
        var counting = new CountingFile(new MemoryVirtualFile("x", data));
        var cached = new HeadCachedFile(counting);
        for (int i = 0; i < 5; i++)
        {
            Assert.AreEqual(data.Take(64), cached.ReadHead(64));
        }
        Assert.AreEqual(1, counting.Opens, "head read once");
        using var s = cached.OpenRead();
        s.Seek(HeadCachedFile.HeadSize + 10, SeekOrigin.Begin);
        var buf = new byte[4];
        s.ReadExactly(buf);
        CollectionAssert.AreEqual(data.Skip(HeadCachedFile.HeadSize + 10).Take(4), buf);
    }

    private sealed class CountingFile(IVirtualFile inner) : IVirtualFile
    {
        public int Opens;
        public string Name => inner.Name;
        public long Length => inner.Length;
        public Stream OpenRead()
        {
            Opens++;
            return inner.OpenRead();
        }
    }
}
