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
// Synthetic AdbImg tests: a minimal container (outer + inner headers around
// a JFFS2 payload, signature tail) built in-memory. Fully in-memory.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.AdbImg;
using Yafex.Fuse;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class AdbImgTests
{
    internal static readonly byte[] SigTail = Encoding.ASCII.GetBytes("fake-signature");

    private static void PutAscii(byte[] buf, int off, string s)
    {
        var b = Encoding.ASCII.GetBytes(s);
        Array.Copy(b, 0, buf, off, Math.Min(b.Length, buf.Length - off));
    }

    internal static byte[] BuildImage()
    {
        var jffs = Jffs2Tests.BuildImage();
        var outer = new byte[256];
        PutAscii(outer, 0, "yIMG1");
        PutAscii(outer, 6, "MULTI_BOARDS_ID");
        PutAscii(outer, 0x18, "dlink");
        PutAscii(outer, 0x30, "TESTBOARD");
        PutAscii(outer, 0x40, "2024-01-02 03:04");
        var inner = new byte[256];
        PutAscii(inner, 0, "mIMG1");
        int dataLength = inner.Length + jffs.Length;
        PutAscii(outer, 0x58, dataLength.ToString());
        using var ms = new MemoryStream();
        ms.Write(outer, 0, outer.Length);
        ms.Write(inner, 0, inner.Length);
        ms.Write(jffs, 0, jffs.Length);
        ms.Write(SigTail, 0, SigTail.Length);
        return ms.ToArray();
    }

    [Test]
    public void Valid_Detects90()
    {
        var res = (AdbImgDetectionResult)new AdbImgAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()));
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual(512, res.PayloadStart);
    }

    [Test]
    public void Lister_ShowsBoardAndMembers()
    {
        var mem = Mem(BuildImage(), "f.sig");
        var det = (AdbImgDetectionResult)new AdbImgAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var text = Synthetic.ListTree(new AdbImgAddon(), det, mem);
        StringAssert.Contains("a.txt", text);
        StringAssert.Contains("signature.bin", text);
    }

    [Test]
    public void Extract_EmitsMembersAndSig()
    {
        var mem = Mem(BuildImage(), "f.sig");
        var det = (AdbImgDetectionResult)new AdbImgAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new AdbImgAddon().CreateExtractor(det).Extract(mem)
            .OrderBy(a => a.Name).ToList();
        var names = artifacts.Select(a => a.Name).ToList();
        CollectionAssert.Contains(names, "a.txt");
        CollectionAssert.Contains(names, "signature.bin");
        CollectionAssert.AreEqual(SigTail, artifacts.First(a => a.Name == "signature.bin").Data.ToArray());
    }

    [Test]
    public void Expander_MatchesLayout()
    {
        var exp = new AdbImgExpander();
        var img = new MemoryVirtualFile("f.sig", BuildImage());
        Assert.IsTrue(exp.Matches(img));
        var node = exp.Expand(img, new List<object>());
        node.EnsureLoaded();
        var names = node.Tree.Keys.ToList();
        CollectionAssert.Contains(names, "payload.jffs2");
        CollectionAssert.Contains(names, "signature.bin");
    }

    [Test]
    public void Garbage_Fails()
    {
        var rnd = new byte[2048];
        new Random(41).NextBytes(rnd);
        var res = new AdbImgAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(rnd));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void MissingInner_Fails()
    {
        var img = BuildImage();
        img[0x100] = (byte)'X';
        var res = new AdbImgAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(img));
        Assert.AreEqual(0, res.Confidence);
    }
}
