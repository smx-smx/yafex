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
// Synthetic LxSecureBoot tests (old + new variants, no keys).
// Fully in-memory: no disk, no samples.
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.LxBoot;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class LxSecureBootTests
{
    private static byte[] LxBootSynthetic(bool isNew)
    {
        var buf = Zeros(0x600);
        // NOTE: "\x13A0" would parse as U+13A0; write control bytes explicitly.
        buf[0] = (byte)'H'; buf[1] = 0x13; buf[2] = (byte)'A'; buf[3] = (byte)'0';
        buf[4] = (byte)'M'; buf[5] = 0x16; buf[6] = (byte)'P'; buf[7] = (byte)'R';
        WriteAscii(buf, isNew ? 0x240 : 0xD0, "SIC!");
        // descriptors are zeros (LoadAddress 0 -> second stage scan finds nothing -> null)
        return buf;
    }

    [Test]
    public void OldVariant_Detects60()
    {
        var addon = new LxSecureBootAddon();
        var det = addon.CreateDetector(new Dictionary<string, string>());
        var res = (LxSecureBootDetectionResult)det.Detect(Mem(LxBootSynthetic(false)));
        Assert.AreEqual(60, res.Confidence); // 20 base + 20 + 20 known magics
        Assert.NotNull(res.Context);
    }

    [Test]
    public void NewVariant_DetectsPositive()
    {
        var addon = new LxSecureBootAddon();
        var det = addon.CreateDetector(new Dictionary<string, string>());
        var res = (LxSecureBootDetectionResult)det.Detect(Mem(LxBootSynthetic(true)));
        Assert.That(res.Confidence, Is.GreaterThan(0));
        Assert.NotNull(res.Context);
    }

    [Test]
    public void NoSic_Fails()
    {
        var addon = new LxSecureBootAddon();
        var det = addon.CreateDetector(new Dictionary<string, string>());
        var res = det.Detect(Mem(Zeros(0x600)));
        Assert.IsFalse(res.Succeded());
    }

    [Test]
    public void Truncated_FailsGracefully()
    {
        var addon = new LxSecureBootAddon();
        var det = addon.CreateDetector(new Dictionary<string, string>());
        Assert.DoesNotThrow(() =>
        {
            var res = det.Detect(Mem(new byte[4]));
            Assert.IsFalse(res.Succeded());
        });
    }

    private static byte[] BareLlSynthetic()
    {
        var buf = Zeros(64);
        // ARM branch, "LL_B", load, size=48
        buf[0] = 0x06; buf[1] = 0x00; buf[2] = 0x00; buf[3] = 0xEA;
        buf[4] = (byte)'L'; buf[5] = (byte)'L'; buf[6] = (byte)'_'; buf[7] = (byte)'B';
        WriteU32LE(buf, 8, 0x78100000);
        WriteU32LE(buf, 12, 48);
        return buf;
    }

    [Test]
    public void BareLl_Detects60()
    {
        var addon = new LxSecureBootAddon();
        var det = addon.CreateDetector(new Dictionary<string, string>());
        var res = (LxSecureBootDetectionResult)det.Detect(Mem(BareLlSynthetic()));
        Assert.AreEqual(60, res.Confidence);
        Assert.AreEqual(SpbcVariant.Bare, res.Context!.SpbcVariant);
    }

    [Test]
    public void BareLl_ExtractsStage()
    {
        var addon = new LxSecureBootAddon();
        var det = addon.CreateDetector(new Dictionary<string, string>());
        var mem = Mem(BareLlSynthetic(), "lxboot.pak");
        var res = (LxSecureBootDetectionResult)det.Detect(mem);
        var artifacts = new LxSecureBootExtractor(res.Context!).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("lxboot.bin", artifacts[0].Name);
        Assert.AreEqual(48, artifacts[0].Data.ToArray().Length);
    }

    [Test]
    public void BareLl_BadMagic_Fails()
    {
        var buf = BareLlSynthetic();
        buf[4] = (byte)'X';
        var addon = new LxSecureBootAddon();
        var det = addon.CreateDetector(new Dictionary<string, string>());
        var res = det.Detect(Mem(buf));
        Assert.IsFalse(res.Succeded());
    }

    [Test]
    public void Spbc_ExtractsViaFlat()
    {
        // Old-variant synthetic: descriptors zeroed, no second stage found
        var addon = new LxSecureBootAddon();
        var det = addon.CreateDetector(new Dictionary<string, string>());
        var mem = Mem(LxBootSynthetic(false), "boot.pak");
        var res = (LxSecureBootDetectionResult)det.Detect(mem);
        var artifacts = new LxSecureBootExtractor(res.Context!).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("secureboot_0.bin", artifacts[0].Name);
        // writable: file name metadata present (base dir comes from Program)
        Assert.AreEqual("secureboot_0.bin",
            artifacts[0].GetMetadata<Yafex.Metadata.OutputFileName>().First().FileName);
    }
}
