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
// Synthetic BroadcomImg tests: a JFFS2 boot region (reused builder) plus a
// UBI region (reused builder) at a PEB-style offset. Fully in-memory.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.BroadcomImg;
using Yafex.Fuse;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class BroadcomImgTests
{
    internal static byte[] BuildImage()
    {
        var jffs = Jffs2Tests.BuildImage();
        var ubi = UbiTests.BuildImage();
        const int ubiStart = 65536;
        var img = new byte[ubiStart + ubi.Length];
        for (int i = jffs.Length; i < ubiStart; i++) img[i] = 0xFF;
        Array.Copy(jffs, img, jffs.Length);
        Array.Copy(ubi, 0, img, ubiStart, ubi.Length);
        return img;
    }

    [Test]
    public void Valid_Detects95()
    {
        var res = (BroadcomImgDetectionResult)new BroadcomImgAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()));
        Assert.AreEqual(95, res.Confidence);
        Assert.AreEqual(65536, res.UbiStart);
    }

    [Test]
    public void Lister_ShowsBothRegions()
    {
        var mem = Mem(BuildImage(), "f.imgbin");
        var det = (BroadcomImgDetectionResult)new BroadcomImgAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var text = Synthetic.ListTree(new BroadcomImgAddon(), det, mem);
        StringAssert.Contains("a.txt", text);
        StringAssert.Contains("testvol", text);
    }

    [Test]
    public void Extract_EmitsBothRegions()
    {
        var mem = Mem(BuildImage(), "f.imgbin");
        var det = (BroadcomImgDetectionResult)new BroadcomImgAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new BroadcomImgAddon().CreateExtractor(det).Extract(mem).ToList();
        var names = artifacts.Select(a => a.Name).ToList();
        CollectionAssert.Contains(names, "a.txt");
        CollectionAssert.Contains(names, "testvol");
    }

    [Test]
    public void Jffs2Only_Fails()
    {
        var res = new BroadcomImgAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(Jffs2Tests.BuildImage()));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Expander_MatchesLayout()
    {
        var exp = new BroadcomImgExpander();
        Assert.IsTrue(exp.Matches(new MemoryVirtualFile("f.imgbin", BuildImage())));
        var node = exp.Expand(new MemoryVirtualFile("f.imgbin", BuildImage()), new List<object>());
        node.EnsureLoaded();
        var names = node.Tree.Keys.ToList();
        CollectionAssert.Contains(names, "boot.jffs2");
        CollectionAssert.Contains(names, "root.ubi");
    }
}
