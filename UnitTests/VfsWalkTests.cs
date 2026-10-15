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

using Yafex;
using Yafex.Fuse;

namespace UnitTests;

[TestFixture]
public class VfsWalkTests
{
    private static PakVfsFile Text(string name, string content)
        => new(new TextVirtualFile(name, content), Helpers.OctalLiteral(0444));

    /// <summary>root/{b.txt, a/{x.txt, lazy/{y.txt}}, broken/}</summary>
    private static YafexDirectory Sample()
    {
        var root = new YafexDirectory("", Helpers.OctalLiteral(0755));
        root.AddNode(Text("b.txt", "bee"));
        var a = new YafexDirectory("a", Helpers.OctalLiteral(0755));
        a.AddNode(Text("x.txt", "ex"));
        a.AddNode(new LazyVfsDirectory("lazy", Helpers.OctalLiteral(0755),
            () => new IVfsNode[] { Text("y.txt", "why") }));
        root.AddNode(a);
        root.AddNode(new LazyVfsDirectory("broken", Helpers.OctalLiteral(0755),
            () => throw new InvalidDataException("bad image")));
        return root;
    }

    [Test]
    public void Walk_DepthFirstOrdinalAndExpandsLazy()
    {
        var paths = VfsWalk.Walk(Sample()).Select(e => e.Path).ToList();
        CollectionAssert.AreEqual(
            new[] { "a", "a/lazy", "a/lazy/y.txt", "a/x.txt", "b.txt", "broken" }, paths);
    }

    [Test]
    public void Walk_ReportsFailedExpansionAndContinues()
    {
        var broken = VfsWalk.Walk(Sample()).Single(e => e.Path == "broken");
        Assert.That(broken.Error, Is.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void Listing_FlatIsUniform()
    {
        var sw = new StringWriter();
        VfsListing.WriteFlat(Sample(), sw);
        var lines = sw.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines[0], Is.EqualTo($"d {0,12} a/"));
        Assert.That(lines, Does.Contain($"f {3,12} a/lazy/y.txt"));
        Assert.That(lines[^1], Does.StartWith($"d {0,12} broken/  ! bad image"));
    }

    [Test]
    public void Listing_TreeIndentsByDepth()
    {
        var sw = new StringWriter();
        VfsListing.WriteTree(Sample(), sw);
        StringAssert.Contains("    y.txt (3 bytes)", sw.ToString());
    }

    [Test]
    public void OpenRead_FallsBackToChunkedRead()
    {
        var file = new YafexFile(new MemoryDataSource(new Smx.SharpIO.Memory.Buffers.Memory64<byte>(new byte[] { 1, 2, 3, 4 })) { Name = "m" });
        using var s = VfsWalk.OpenRead(file);
        var ms = new MemoryStream();
        s.CopyTo(ms);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, ms.ToArray());
    }

    [Test]
    public void Export_MirrorsTree()
    {
        var dest = Path.Combine(Path.GetTempPath(), $"yafex-export-{Guid.NewGuid():N}");
        try
        {
            int n = VfsExport.Export(Sample(), dest);
            Assert.That(n, Is.EqualTo(3));
            Assert.That(File.ReadAllText(Path.Combine(dest, "a", "lazy", "y.txt")), Is.EqualTo("why"));
            Assert.That(File.ReadAllText(Path.Combine(dest, "b.txt")), Is.EqualTo("bee"));
            Assert.That(Directory.Exists(Path.Combine(dest, "broken")));
        }
        finally
        {
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
        }
    }

    [TestCase("a/../../etc")]
    [TestCase("..")]
    [TestCase("a\\..\\..")]
    [TestCase("a//b")]
    public void SafeTarget_RejectsEscapes(string rel)
    {
        Assert.That(VfsExport.SafeTarget(Path.GetTempPath(), rel), Is.Null);
    }
}
