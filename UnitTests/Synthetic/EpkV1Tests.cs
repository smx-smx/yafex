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
// Synthetic EpkV1 tests. Fully in-memory: no disk, no samples.
using System.IO;
using System.Runtime.InteropServices;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.EpkV1;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class EpkV1Tests
{
    [Test]
    public void PlainMagic_Detects99()
    {
        var buf = Zeros(0x400);
        WriteAscii(buf, 0, "epak");
        var res = (Epk1DetectionResult)new Epk1Detector().Detect(Mem(buf));
        Assert.AreEqual(99, res.Confidence);
    }

    [Test]
    public void Garbage_Detects0()
    {
        var res = (Epk1DetectionResult)new Epk1Detector().Detect(Mem(Zeros(0x400)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Lister_OnePak_OutputsPakLine()
    {
        int hdrSize = Marshal.SizeOf<Epk1Header>();
        var buf = Zeros(hdrSize + Marshal.SizeOf<PakHeader>() + 0x10);
        WriteAscii(buf, 0, "epak");
        WriteU32LE(buf, 8, 1); // pakCount = 1 -> Old
        // pakRecs[0] starts at offset 12: offset, size
        WriteU32LE(buf, 12, (uint)hdrSize);
        WriteU32LE(buf, 16, 0x100);
        WriteAscii(buf, hdrSize, "kern");
        WriteU32LE(buf, hdrSize + 4, 0x100);
        var ds = Mem(buf);
        var det = (Epk1DetectionResult)new Epk1Detector().Detect(ds);
        Assert.AreEqual(99, det.Confidence);
        var lister = new Epk1Addon().CreateLister(det);
        Assert.NotNull(lister);
        var sw = new StringWriter();
        lister!.List(ds, det, sw);
        var text = sw.ToString();
        Assert.That(text, Does.Contain("EPKv1"));
        Assert.That(text, Does.Contain("kern.pak"));
    }
}
