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
// Synthetic MStarPkg tests. Fully in-memory: no disk, no samples.
using System.Linq;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.MStarPkg;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class MStarPkgTests
{
    [Test]
    public void WithMagic_Detects100()
    {
        var buf = Zeros(0x4000);
        WriteAscii(buf, 0, "xxx setenv MstarUpgrade_complete yyy");
        var res = new MStarPkgDetector().Detect(Mem(buf));
        Assert.AreEqual(100, res.Confidence);
    }

    [Test]
    public void WithoutMagic_Detects0()
    {
        var res = new MStarPkgDetector().Detect(Mem(Zeros(0x4000)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void WithFilepartload_Detects100()
    {
        // mboot-style scripts (Seiki/hisense/toshiba) have no setenv marker.
        var buf = Zeros(0x4000);
        WriteAscii(buf, 0, "xxx filepartload 81100000 usb.bin 2000 100 yyy");
        var res = new MStarPkgDetector().Detect(Mem(buf));
        Assert.AreEqual(100, res.Confidence);
    }

    [Test]
    public void Extract_BareFallback_YieldsOffsetNames()
    {
        var buf = Zeros(0x5000);
        var script =
            "filepartload 81100000 usb.bin 2000 100\n" + // bare hex
            "filepartload 0x20200000 $(UpgradeImage) 0x4000 0x100\n" + // 0x prefix
            "filepartload 81100000 usb.bin 2000 100\n" + // duplicate: skipped
            "filepartload 81100000 usb.bin 400 0\n" + // zero size: skipped
            "filepartload 81100000 usb.bin 6000 100\n"; // out of range: skipped
        WriteAscii(buf, 0, script);
        var artifacts = new MStarPkgExtractor().Extract(Mem(buf, "test.bin")).ToList();
        Assert.AreEqual(2, artifacts.Count);
        Assert.AreEqual("img_2000.pak", artifacts[0].Name);
        Assert.AreEqual("img_4000.pak", artifacts[1].Name);
        Assert.AreEqual(0x100, artifacts[0].Data.ToArray().Length);
    }

    [Test]
    public void Extract_NamedSectionsBeatBareLines()
    {
        var buf = Zeros(0x5000);
        var script =
            "# File Partition: part1\nfilepartload 0 0 0x4000 0x100\n" +
            "filepartload 81100000 usb.bin 2000 100\n"; // stray: ignored
        WriteAscii(buf, 0, script);
        var artifacts = new MStarPkgExtractor().Extract(Mem(buf, "test.bin")).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("part1.pak", artifacts[0].Name);
    }

    [Test]
    public void Extract_BareLinesTakeNameFromConsumer()
    {
        var buf = Zeros(0x6000);
        var script =
            "filepartload 20200000 MstarUpgrade.bin 4000 100\n" +
            "mmc write.p 20200000 recovery 100 1\n" +
            "filepartload 81100000 usb.bin 1000 100\n" +
            "ubi write 80100000 RFS 100 100\n" +
            "filepartload 81100000 usb.bin 2000 100\n" +
            "nand write.e 80100000 KL 100\n" +
            "filepartload 81100000 usb.bin 3000 100\n" +
            "store_secure_info kernelSign 81100000\n" +
            "filepartload 81100000 usb.bin 4100 100\n" +
            "mmc write.boot 1 80100000 0 100\n" +
            "filepartload 81100000 usb.bin 1000 100\n"; // same offset: deduped
        WriteAscii(buf, 0, script);
        var artifacts = new MStarPkgExtractor().Extract(Mem(buf, "test.bin")).ToList();
        var names = artifacts.Select(a => a.Name).ToList();
        CollectionAssert.AreEqual(
            new[] { "recovery.pak", "RFS.pak", "KL.pak", "kernel.pak", "boot.pak" },
            names);
    }

    [Test]
    public void Extract_RepeatNamesUniquified()
    {
        var buf = Zeros(0x6000);
        var script =
            "filepartload 81100000 usb.bin 1000 100\n" +
            "ubi write 80100000 APP 100 100\n" +
            "filepartload 81100000 usb.bin 2000 100\n" +
            "ubi write 80100000 APP 100 100\n";
        WriteAscii(buf, 0, script);
        var artifacts = new MStarPkgExtractor().Extract(Mem(buf, "test.bin")).ToList();
        var names = artifacts.Select(a => a.Name).ToList();
        CollectionAssert.AreEqual(new[] { "APP.pak", "APP_2.pak" }, names);
    }

    [Test]
    public void Short_Detects0()
    {
        var res = new MStarPkgDetector().Detect(Mem(new byte[16]));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Extract_EmitsWritableArtifacts()
    {
        var buf = Zeros(0x5000);
        var script = "# File Partition: part1\nfilepartload 0 0 0x4000 0x100\n";
        WriteAscii(buf, 0, script);
        for (int i = 0; i < 0x100; i++) buf[0x4000 + i] = (byte)i;
        var mem = Mem(buf, "test.bin");
        var artifacts = new MStarPkgExtractor().Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("part1.pak", artifacts[0].Name);
        Assert.IsTrue(artifacts[0].Flags.HasFlag(DataSourceFlags.ProcessFurther));
        var fileName = artifacts[0].GetMetadata<Yafex.Metadata.OutputFileName>().First().FileName;
        Assert.AreEqual("part1.pak", fileName);
        Assert.IsNotEmpty(artifacts[0].GetMetadata<Yafex.Metadata.OutputDirectoryName>());
        Assert.AreEqual(0x100, artifacts[0].Data.ToArray().Length);
    }
}
