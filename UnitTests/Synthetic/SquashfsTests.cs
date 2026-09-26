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
// Synthetic Squashfs tests: the detector is disk-only, so assert it fails
// gracefully on in-memory sources without touching disk.
using System;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Squashfs;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class SquashfsTests
{
    [Test]
    public void MemorySource_ThrowsNotSupported()
    {
        var det = new SquashfsDetector();
        Assert.Throws<NotSupportedException>(() => det.Detect(Mem(Zeros(256))));
    }
}
