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
// Synthetic LZHS / LZHSFS tests (negative paths; no encoder exists to
// craft positive streams). Fully in-memory: no disk, no samples.
using System;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Lzhs;
using Yafex.FileFormats.LzhsFs;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class LzhsTests
{
    [Test]
    public void Garbage_Detects0NoThrow()
    {
        var buf = Zeros(64);
        new Random(123).NextBytes(buf);
        Assert.DoesNotThrow(() =>
        {
            var res = new LzhsDetector().Detect(Mem(buf));
            Assert.AreEqual(0, res.Confidence);
        });
    }

    [Test]
    public void Truncated_Detects0NoThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            var res = new LzhsDetector().Detect(Mem(new byte[4]));
            Assert.AreEqual(0, res.Confidence);
        });
    }

    [Test]
    public void EmptyStream_Detects0()
    {
        // comp == 0 carries no content: must not vacuously verify via an
        // empty decode with a zero checksum (e.g. lxboot.bin head shape)
        var buf = Zeros(32);
        WriteU32LE(buf, 0, 0x6F225052);
        var res = new LzhsDetector().Detect(Mem(buf));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Fs_Short_Detects0()
    {
        var res = new LzhsFsDetector().Detect(Mem(new byte[8]));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Fs_Garbage_Detects0NoThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            var res = new LzhsFsDetector().Detect(Mem(Zeros(128)));
            Assert.AreEqual(0, res.Confidence);
        });
    }
}
