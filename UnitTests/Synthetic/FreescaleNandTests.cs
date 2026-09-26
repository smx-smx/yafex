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
// Synthetic FreescaleNand tests. Fully in-memory: no disk, no samples.
using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.FreescaleNand;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class FreescaleNandTests
{
    [Test]
    public void FcbMagic_Detects100()
    {
        var buf = Zeros(0x40);
        WriteAscii(buf, 0x10, "FCB ");
        var res = new FreescaleNandDetector().Detect(Mem(buf));
        Assert.AreEqual(100, res.Confidence);
    }

    [Test]
    public void Garbage_Fails()
    {
        var res = new FreescaleNandDetector().Detect(Mem(Zeros(0x40)));
        Assert.IsFalse(res.Succeded());
    }
}
