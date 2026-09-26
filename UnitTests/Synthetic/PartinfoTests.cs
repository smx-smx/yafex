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
// Synthetic Partinfo tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Partinfo;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class PartinfoTests
{
    [Test]
    public void KnownMagics_Detect30WithType()
    {
        foreach (var (magic, _) in new[] { (0x20081118u, 0), (0x20110729u, 0), (0x20120716u, 0) })
        {
            var buf = Zeros(16);
            WriteU32LE(buf, 0, magic);
            var res = (PartinfoDetectionResult)new PartinfoAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(buf));
            Assert.AreEqual(30, res.Confidence);
            Assert.NotNull(res.Context);
        }
    }

    [Test]
    public void Garbage_Detects0()
    {
        var res = (PartinfoDetectionResult)new PartinfoAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(Zeros(16)));
        Assert.AreEqual(0, res.Confidence);
    }
}
