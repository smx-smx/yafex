#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
// Synthetic Android boot image tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.AndroidBoot;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class AndroidBootTests
{
    internal static byte[] BuildImage(
        byte[]? kernel = null,
        byte[]? ramdisk = null,
        byte[]? second = null,
        uint pageSize = 2048,
        uint headerVersion = 1,
        uint osVersion = 0x08000000)
    {
        kernel ??= new byte[] { 1, 2, 3, 4 };
        ramdisk ??= new byte[] { 5, 6 };
        second ??= Array.Empty<byte>();
        long Pages(long size) => size == 0 ? 0 : (size + pageSize - 1) / pageSize;
        long kPages = Pages(kernel.Length);
        long rPages = Pages(ramdisk.Length);
        long sPages = Pages(second.Length);
        long total = pageSize + (kPages + rPages + sPages) * pageSize;
        var buf = new byte[total];
        WriteAscii(buf, 0, AndroidBoot.Magic);
        WriteU32LE(buf, 8, (uint)kernel.Length);
        WriteU32LE(buf, 12, 0x10000000);
        WriteU32LE(buf, 16, (uint)ramdisk.Length);
        WriteU32LE(buf, 20, 0x11000000);
        WriteU32LE(buf, 24, (uint)second.Length);
        WriteU32LE(buf, 28, 0x12000000);
        WriteU32LE(buf, 32, 0x10000000);
        WriteU32LE(buf, 36, pageSize);
        WriteU32LE(buf, 40, headerVersion);
        WriteU32LE(buf, 44, osVersion);
        WriteAscii(buf, 48, "test");
        if (headerVersion >= 1)
        {
            WriteU32LE(buf, 1644, AndroidBoot.HeaderV1Size);
        }
        long off = pageSize;
        Array.Copy(kernel, 0, buf, off, kernel.Length);
        off += kPages * pageSize;
        if (ramdisk.Length > 0)
        {
            Array.Copy(ramdisk, 0, buf, off, ramdisk.Length);
            off += rPages * pageSize;
        }
        if (second.Length > 0)
        {
            Array.Copy(second, 0, buf, off, second.Length);
        }
        return buf;
    }

    [Test]
    public void ValidV1_Detects90()
    {
        var res = (AndroidBootDetectionResult)new AndroidBootAddon()
            .CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()));
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual(1u, res.Header.HeaderVersion);
        Assert.AreEqual(2, res.Header.Segments.Count);
    }

    [Test]
    public void ValidV0_Detects90()
    {
        var res = (AndroidBootDetectionResult)new AndroidBootAddon()
            .CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage(headerVersion: 0)));
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual(0u, res.Header.HeaderVersion);
    }

    [Test]
    public void BadMagic_Detects0()
    {
        var buf = BuildImage();
        buf[0] ^= 0xFF;
        var res = new AndroidBootAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(buf));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Truncated_Detects0()
    {
        var res = new AndroidBootAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(Zeros(100)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void BadPageSize_Detects0()
    {
        var buf = BuildImage();
        WriteU32LE(buf, 36, 1000);
        var res = new AndroidBootAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(buf));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void V3_Detects0()
    {
        var buf = BuildImage();
        WriteU32LE(buf, 40, 3);
        var res = new AndroidBootAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(buf));
        Assert.AreEqual(0, res.Confidence);
    }
}
