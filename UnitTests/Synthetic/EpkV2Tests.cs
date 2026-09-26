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
// Synthetic EpkV2 / EpkV2Beta tests. Fully in-memory: no disk, no samples.
using System;
using System.IO;
using System.Runtime.InteropServices;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.EpkV2;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class EpkV2Tests
{
    [Test]
    public void Beta_PlainHeader_Detects100()
    {
        var buf = Zeros(0x400);
        WriteAscii(buf, 0, "epak");
        WriteAscii(buf, 12, "EPK2");
        var det = new Epk2BetaDetector(EmptyKeys());
        var res = (Epk2DetectionResult<EPK_V2_BETA_HEADER>)det.Detect(Mem(buf));
        Assert.AreEqual(100, res.Confidence);
        Assert.NotNull(res.Context);
    }

    [Test]
    public void Beta_Garbage_Detects0()
    {
        var det = new Epk2BetaDetector(EmptyKeys());
        var res = (Epk2DetectionResult<EPK_V2_BETA_HEADER>)det.Detect(Mem(Zeros(0x400)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void PlainWithVersions_Detects180()
    {
        int hdrSize = Marshal.SizeOf<EPK_V2_HEADER>();
        int total = EPK_V2_STRUCTURE.SIGNATURE_SIZE + hdrSize + 256 + 32 + 0x10;
        var buf = Zeros(total);
        int hdrOff = EPK_V2_STRUCTURE.SIGNATURE_SIZE;
        WriteAscii(buf, hdrOff, "epak");
        WriteAscii(buf, hdrOff + 12, "EPK2");
        int verOff = hdrOff + hdrSize + 256;
        WriteAscii(buf, verOff, "06.01.46.01");
        WriteAscii(buf, verOff + 16, "05.05.90.01");
        var det = new Epk2Detector(EmptyKeys());
        DetectionResult res = det.Detect(Mem(buf));
        Assert.AreEqual(180, res.Confidence);
    }

    [Test]
    public void EncryptedWithoutKeys_Detects0Gracefully()
    {
        var buf = Zeros(0x800);
        new Random(42).NextBytes(buf);
        var det = new Epk2Detector(EmptyKeys());
        DetectionResult res = det.Detect(Mem(buf));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Beta_Lister_OutputsHeader()
    {
        var buf = Zeros(0x400);
        WriteAscii(buf, 0, "epak");
        WriteAscii(buf, 12, "EPK2");
        var ds = Mem(buf);
        var det = (Epk2DetectionResult<EPK_V2_BETA_HEADER>)new Epk2BetaDetector(EmptyKeys()).Detect(ds);
        var lister = new Epk2BetaAddon(EmptyKeys()).CreateLister(det);
        Assert.NotNull(lister);
        var sw = new StringWriter();
        lister!.List(ds, det, sw);
        Assert.That(sw.ToString().Length, Is.GreaterThan(0));
    }
}
