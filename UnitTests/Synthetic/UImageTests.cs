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
// Synthetic uImage tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.UImage;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class UImageTests
{
    private static void WriteU32BE(byte[] buf, int off, uint v)
    {
        buf[off] = (byte)(v >> 24);
        buf[off + 1] = (byte)(v >> 16);
        buf[off + 2] = (byte)(v >> 8);
        buf[off + 3] = (byte)v;
    }

    internal static byte[] BuildUImage(byte[] payload, string name = "test")
    {
        var buf = new byte[UImage.HeaderSize + payload.Length];
        WriteU32BE(buf, 0, UImage.Magic);
        WriteU32BE(buf, 8, 0x12345678);
        WriteU32BE(buf, 12, (uint)payload.Length);
        WriteU32BE(buf, 16, 0x80000000);
        buf[28] = 5;
        buf[29] = 5;
        buf[30] = 2;
        Encoding.ASCII.GetBytes(name).CopyTo(buf, 32);
        Array.Copy(payload, 0, buf, UImage.HeaderSize, payload.Length);
        WriteU32BE(buf, 4, UImage.Crc32(buf, 0, UImage.HeaderSize));
        return buf;
    }

    [Test]
    public void Valid_Detects90()
    {
        var res = (UImageDetectionResult)new UImageAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildUImage(new byte[] { 1, 2, 3 })));
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual("test", res.Header.Name);
        Assert.AreEqual(3u, res.Header.Size);
    }

    [Test]
    public void Extract_StripsHeader()
    {
        var inner = new byte[] { 0xAA, 0xBB };
        var mem = Mem(BuildUImage(inner), "kern.pak");
        var det = (UImageDetectionResult)new UImageAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new UImageAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("kern.pak.unpaked", artifacts[0].Name);
        Assert.IsTrue(artifacts[0].Flags.HasFlag(DataSourceFlags.ProcessFurther));
        CollectionAssert.AreEqual(inner, artifacts[0].Data.ToArray());
    }

    [Test]
    public void BadCrc_Detects0()
    {
        var buf = BuildUImage(new byte[] { 1 });
        buf[10] ^= 0xFF;
        var res = new UImageAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(buf));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Truncated_Detects0()
    {
        var res = new UImageAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(Zeros(63)));
        Assert.AreEqual(0, res.Confidence);
    }
}
