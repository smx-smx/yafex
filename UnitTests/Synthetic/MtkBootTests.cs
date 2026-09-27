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
// Synthetic MTK boot tests. Fully in-memory: no disk, no samples.
// (LZHS payload decode is verified against real samples + the reference.)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.MtkBoot;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class MtkBootTests
{
    internal static byte[] BuildImage(string magic = null)
    {
        magic ??= MtkBoot.PblMagic;
        var img = Zeros(MtkBoot.PblSize + 16);
        Encoding.ASCII.GetBytes(magic).CopyTo(img, MtkBoot.MagicOffset);
        return img;
    }

    [Test]
    public void Pbl_Detects90()
    {
        var res = (MtkBootDetectionResult)new MtkBootAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()));
        Assert.AreEqual(90, res.Confidence);
        Assert.IsTrue(res.IsPbl);
    }

    [Test]
    public void NoSegments_ExtractsOnlyFirstStage()
    {
        var mem = Mem(BuildImage(), "boot.pak");
        var det = (MtkBootDetectionResult)new MtkBootAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new MtkBootAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("mtk_1bl.bin", artifacts[0].Name);
        Assert.AreEqual(MtkBoot.PblSize, artifacts[0].Data.ToArray().Length);
    }

    [Test]
    public void Garbage_Detects0()
    {
        var res = new MtkBootAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(Zeros(MtkBoot.PblSize)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Truncated_Detects0()
    {
        var res = new MtkBootAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()[..(MtkBoot.PblSize - 1)]));
        Assert.AreEqual(0, res.Confidence);
    }
}
