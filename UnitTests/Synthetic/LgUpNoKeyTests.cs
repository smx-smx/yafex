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
// Synthetic LgUp no-keys path (positives live in LgUpTests).
// Fully in-memory: no disk, no samples.
using System.Collections.Generic;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.LgUp;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class LgUpNoKeyTests
{
    [Test]
    public void MagicWithoutKeys_Detects30NullCtx()
    {
        var buf = Zeros(0x200);
        WriteAscii(buf, 0x100 + 4, "EPKG");
        var det = new LgUpDetector(EmptyKeys(), new Dictionary<string, string>());
        var res = (LgUpDetectionResult)det.Detect(Mem(buf));
        Assert.AreEqual(30, res.Confidence);
        Assert.IsNull(res.Context);
    }

    [Test]
    public void NoMagic_Detects0()
    {
        var det = new LgUpDetector(EmptyKeys(), new Dictionary<string, string>());
        var res = (LgUpDetectionResult)det.Detect(Mem(Zeros(0x200)));
        Assert.AreEqual(0, res.Confidence);
    }
}
