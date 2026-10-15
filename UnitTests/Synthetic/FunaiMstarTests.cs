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
// Synthetic Funai MStar wrapper (variant D) tests: leading/trailing info
// blocks with raw payloads are built in-memory. Unencrypted: no keys
// involved. Fully in-memory, no fixtures.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Funai;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class FunaiMstarTests
{
    private static readonly byte[] Soc = Encoding.ASCII.GetBytes("SOC-PAYLOAD-0123456789ABCDEF");
    private static readonly byte[] Frc60 = Encoding.ASCII.GetBytes("FRC60-PAYLOAD-0123456789");
    private static readonly byte[] Frc120 = Encoding.ASCII.GetBytes("FRC120-PAYLOAD-0123456789");

    private static byte[] InfoBlock(long soc, long f60, long f120, string fileCode = "TEST-0A_001_0")
    {
        var text = "#@INFO\n"
            + $"FileCode={fileCode}\n"
            + "BrandName=TestBrand\n"
            + "ModelName=TestModel\n"
            + "SoC_Version=V001_0\n"
            + "FRC_Version=V002_0\n"
            + $"SoC_Size={soc}\n"
            + $"FRC60_Size={f60}\n"
            + $"FRC120_Size={f120}\n"
            + "#@END\n";
        var block = new byte[0x1000];
        Encoding.ASCII.GetBytes(text).CopyTo(block, 0);
        return block;
    }

    private static byte[] LeadingImage(byte[]? soc = null, byte[]? f60 = null, byte[]? f120 = null)
    {
        soc ??= Soc;
        f60 ??= Frc60;
        f120 ??= Frc120;
        var parts = new List<byte> {};
        parts.AddRange(InfoBlock(soc.Length, f60.Length, f120.Length));
        parts.AddRange(soc);
        parts.AddRange(f60);
        parts.AddRange(f120);
        return parts.ToArray();
    }

    private static byte[] TrailingImage(byte[]? soc = null, byte[]? f60 = null, byte[]? f120 = null)
    {
        soc ??= Soc;
        f60 ??= Frc60;
        f120 ??= Frc120;
        var parts = new List<byte> {};
        parts.AddRange(soc);
        parts.AddRange(f60);
        parts.AddRange(f120);
        parts.AddRange(InfoBlock(soc.Length, f60.Length, f120.Length));
        return parts.ToArray();
    }

    [Test]
    public void Leading_Detects96()
    {
        var res = new FunaiMstarDetector().Detect(Mem(LeadingImage()));
        Assert.AreEqual(96, res.Confidence);
        var typed = (FunaiMstarDetectionResult)res;
        Assert.IsTrue(typed.Info.Leading);
        Assert.AreEqual("TEST-0A_001_0", typed.Info.FileCode);
    }

    [Test]
    public void Trailing_Detects96()
    {
        var res = new FunaiMstarDetector().Detect(Mem(TrailingImage()));
        Assert.AreEqual(96, res.Confidence);
        Assert.IsFalse(((FunaiMstarDetectionResult)res).Info.Leading);
    }

    [Test]
    public void LeadingPreferredOverTrailing()
    {
        // Both placements valid: the SoC payload itself is an info block
        // describing the same total. Leading must win (checked first).
        var head = InfoBlock(0x1000, 0, 0);
        var image = head.Concat(InfoBlock(0x1000, 0, 0)).ToArray();
        var res = (FunaiMstarDetectionResult)new FunaiMstarDetector().Detect(Mem(image));
        Assert.AreEqual(96, res.Confidence);
        Assert.IsTrue(res.Info.Leading);
    }

    [Test]
    public void NoInfo_Rejects()
    {
        var rnd = new byte[0x2000];
        new Random(31).NextBytes(rnd);
        Assert.AreEqual(0, new FunaiMstarDetector().Detect(Mem(rnd)).Confidence);
    }

    [Test]
    public void TooSmall_Rejects()
    {
        Assert.AreEqual(0, new FunaiMstarDetector().Detect(Mem(new byte[0xFFF])).Confidence);
    }

    [Test]
    public void MissingKey_Rejects()
    {
        var block = new byte[0x1000];
        Encoding.ASCII.GetBytes("#@INFO\nFileCode=X\n#@END\n").CopyTo(block, 0);
        var image = block.Concat(new byte[0x1000]).ToArray();
        Assert.AreEqual(0, new FunaiMstarDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void UnparsableSize_Rejects()
    {
        var text = "#@INFO\nFileCode=X\nBrandName=B\nModelName=M\nSoC_Version=V\nFRC_Version=V\n"
            + "SoC_Size=abc\nFRC60_Size=0\nFRC120_Size=0\n#@END\n";
        var block = new byte[0x1000];
        Encoding.ASCII.GetBytes(text).CopyTo(block, 0);
        var image = block.Concat(new byte[100]).ToArray();
        Assert.AreEqual(0, new FunaiMstarDetector().Detect(Mem(image)).Confidence);
    }

    [Test]
    public void LayoutMismatch_Rejects()
    {
        // Declared SoC size does not reach EOF.
        var image = LeadingImage();
        var res = new FunaiMstarDetector().Detect(Mem(image[..^100]));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Truncated_Rejects()
    {
        var image = TrailingImage();
        Assert.AreEqual(0, new FunaiMstarDetector().Detect(Mem(image[..0x1000])).Confidence);
    }

    [Test]
    public void Extract_Leading_RoundTrips()
    {
        var mem = Mem(LeadingImage(), "test.upg");
        var det = (FunaiMstarDetectionResult)new FunaiMstarDetector().Detect(mem);
        Assert.AreEqual(96, det.Confidence);
        var artifacts = new FunaiMstarExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(3, artifacts.Count);
        Assert.AreEqual("SoC.bin", artifacts[0].Name);
        Assert.AreEqual("FRC60.bin", artifacts[1].Name);
        Assert.AreEqual("FRC120.bin", artifacts[2].Name);
        CollectionAssert.AreEqual(Soc, artifacts[0].Data.ToArray());
        CollectionAssert.AreEqual(Frc60, artifacts[1].Data.ToArray());
        CollectionAssert.AreEqual(Frc120, artifacts[2].Data.ToArray());
        // SoC is nested (secure-MStar probe); FRC payloads are terminal.
        Assert.IsTrue(artifacts[0].Flags.HasFlag(DataSourceFlags.ProcessFurther));
        Assert.IsFalse(artifacts[1].Flags.HasFlag(DataSourceFlags.ProcessFurther));
        Assert.IsFalse(artifacts[2].Flags.HasFlag(DataSourceFlags.ProcessFurther));
    }

    [Test]
    public void Extract_Trailing_RoundTrips()
    {
        var mem = Mem(TrailingImage(), "test.upg");
        var det = (FunaiMstarDetectionResult)new FunaiMstarDetector().Detect(mem);
        var artifacts = new FunaiMstarExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(3, artifacts.Count);
        CollectionAssert.AreEqual(Soc, artifacts[0].Data.ToArray());
        CollectionAssert.AreEqual(Frc60, artifacts[1].Data.ToArray());
        CollectionAssert.AreEqual(Frc120, artifacts[2].Data.ToArray());
    }

    [Test]
    public void Extract_ZeroLengths_Skipped()
    {
        var soc = Encoding.ASCII.GetBytes("ONLY-SOC");
        var mem = Mem(LeadingImage(soc, Array.Empty<byte>(), Array.Empty<byte>()), "test.upg");
        var det = (FunaiMstarDetectionResult)new FunaiMstarDetector().Detect(mem);
        var artifacts = new FunaiMstarExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("SoC.bin", artifacts[0].Name);
        CollectionAssert.AreEqual(soc, artifacts[0].Data.ToArray());
    }

    [Test]
    public void Extract_PayloadPastEof_Throws()
    {
        var mem = Mem(LeadingImage(), "test.upg");
        var det = (FunaiMstarDetectionResult)new FunaiMstarDetector().Detect(mem);
        // Shrink the data source below the declared layout after detection.
        var cut = Mem(mem.Data.ToArray()[..(0x1000 + Soc.Length - 10)], "test.upg");
        Assert.Throws<InvalidDataException>(() => new FunaiMstarExtractor(det).Extract(cut).ToList());
    }
}
