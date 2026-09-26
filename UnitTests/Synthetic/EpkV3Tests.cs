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
// Synthetic EpkV3 tests. Fully in-memory: no disk, no samples.
using System.Runtime.InteropServices;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.EpkV3;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class EpkV3Tests
{
    [Test]
    public void New_PlainVersions_Detects80()
    {
        int headSize = Marshal.SizeOf<EPK_V3_NEW_HEAD_STRUCTURE>();
        int hdrSize = Marshal.SizeOf<EPK_V3_NEW_HEADER>();
        var buf = Zeros(headSize + 0x300);
        WriteAscii(buf, 256, "EPK3");
        WriteAscii(buf, 256 + hdrSize, "03.53.45.01");
        WriteAscii(buf, 256 + hdrSize + 16, "05.05.90.01");
        var det = new Epk3NewDetector(EmptyKeys());
        var res = (Epk3NewDetectionResult)det.Detect(Mem(buf));
        Assert.AreEqual(80, res.Confidence);
    }

    [Test]
    public void New_Garbage_Detects0()
    {
        var det = new Epk3NewDetector(EmptyKeys());
        var res = (Epk3NewDetectionResult)det.Detect(Mem(Zeros(0x800)));
        Assert.AreEqual(0, res.Confidence);
    }
}
