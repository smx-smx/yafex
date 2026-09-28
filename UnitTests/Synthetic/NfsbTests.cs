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
// Synthetic NFSB tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Nfsb;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class NfsbTests
{
    private static byte[] BuildNfsb(byte[] inner, string algo = "md5", int algoOff = 0x0E)
    {
        var buf = new byte[Nfsb.HeaderSize + inner.Length];
        Encoding.ASCII.GetBytes("NFSB").CopyTo(buf, 0);
        Encoding.ASCII.GetBytes(algo).CopyTo(buf, algoOff);
        Array.Copy(inner, 0, buf, Nfsb.HeaderSize, inner.Length);
        return buf;
    }

    [Test]
    public void ValidMd5_Detects90()
    {
        var res = (NfsbDetectionResult)new NfsbAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildNfsb(new byte[] { 1, 2, 3 })));
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual(3, res.InnerSize);
    }

    [Test]
    public void ValidSha256At1A_Detects90()
    {
        var res = (NfsbDetectionResult)new NfsbAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildNfsb(new byte[] { 9 }, "sha256", 0x1A)));
        Assert.AreEqual(90, res.Confidence);
    }

    [Test]
    public void Extract_StripsHeader()
    {
        var inner = new byte[] { 0x68, 0x73, 0x71, 0x73 };
        var mem = Mem(BuildNfsb(inner), "base.pak");
        var det = (NfsbDetectionResult)new NfsbAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var extractor = new NfsbAddon().CreateExtractor(det);
        var artifacts = extractor.Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("base.pak.unnfsb", artifacts[0].Name);
        Assert.IsTrue(artifacts[0].Flags.HasFlag(DataSourceFlags.ProcessFurther));
        CollectionAssert.AreEqual(inner, artifacts[0].Data.ToArray());
    }

    [Test]
    public void Garbage_Detects0()
    {
        var res = new NfsbAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(Zeros(0x1010)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void MissingMarker_Detects0()
    {
        var buf = BuildNfsb(new byte[] { 1 });
        Array.Clear(buf, 0x0E, 3);
        Array.Clear(buf, 0x1A, 6);
        var res = new NfsbAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(buf));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Truncated_Detects0()
    {
        var res = new NfsbAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildNfsb(Array.Empty<byte>())[..Nfsb.HeaderSize]));
        Assert.AreEqual(0, res.Confidence);
    }
}
