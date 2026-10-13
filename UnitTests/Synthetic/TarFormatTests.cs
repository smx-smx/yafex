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
// Synthetic tar-format tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Tar;
using Yafex.Fuse;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class TarFormatTests
{
    [Test]
    public void Valid_Detects85()
    {
        var mem = Mem(TarTests.BuildArchive());
        var res = (TarDetectionResult)new TarAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        Assert.AreEqual(85, res.Confidence);
        Assert.AreEqual(0, res.Offset);
    }

    [Test]
    public void Embedded_DetectsAtOffset()
    {
        var inner = TarTests.BuildArchive();
        var buf = new byte[512 + inner.Length];
        new Random(3).NextBytes(buf.AsSpan(0, 512));
        Array.Copy(inner, 0, buf, 512, inner.Length);
        var res = (TarDetectionResult)new TarAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(buf));
        Assert.AreEqual(85, res.Confidence);
        Assert.AreEqual(512, res.Offset);
    }

    [Test]
    public void Garbage_Fails()
    {
        var rnd = new byte[2048];
        new Random(4).NextBytes(rnd);
        var res = new TarAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(rnd));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Lister_OutputsMembers()
    {
        var mem = Mem(TarTests.BuildArchive(), "a.tar");
        var det = (TarDetectionResult)new TarAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var lister = new TarAddon().CreateLister(det);
        Assert.NotNull(lister);
        using var sw = new StringWriter();
        lister!.List(mem, det, sw);
        var text = sw.ToString();
        StringAssert.Contains("Tar", text);
        StringAssert.Contains("hello.txt", text);
        StringAssert.Contains("size=5", text);
    }

    [Test]
    public void Extract_EmitsMembers()
    {
        var mem = Mem(TarTests.BuildArchive(), "a.tar");
        var det = (TarDetectionResult)new TarAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new TarAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(2, artifacts.Count);
        Assert.AreEqual("hello.txt", artifacts[0].Name);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, artifacts[0].Data.ToArray());
    }

    [Test]
    public void Expander_MatchesArchiveRejectsGarbage()
    {
        var exp = new TarExpander();
        Assert.IsTrue(exp.Matches(new MemoryVirtualFile("a.tar", TarTests.BuildArchive())));
        var rnd = new byte[2048];
        new Random(5).NextBytes(rnd);
        Assert.IsFalse(exp.Matches(new MemoryVirtualFile("x.bin", rnd)));
        Assert.IsFalse(exp.Matches(new MemoryVirtualFile("short.bin", new byte[16])));
    }

    [Test]
    public void Expander_ListsMembers()
    {
        var exp = new TarExpander();
        var node = exp.Expand(new MemoryVirtualFile("a.tar", TarTests.BuildArchive()), new List<object>());
        node.EnsureLoaded();
        var names = node.Tree.Keys.ToList();
        CollectionAssert.Contains(names, "hello.txt");
        CollectionAssert.Contains(names, "empty");
    }
}
