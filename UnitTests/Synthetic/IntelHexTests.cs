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
// Synthetic Intel HEX tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.IntelHex;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class IntelHexTests
{
    private static string Rec(byte type, int addr, byte[] data)
    {
        var raw = new byte[4 + data.Length + 1];
        raw[0] = (byte)data.Length;
        raw[1] = (byte)(addr >> 8);
        raw[2] = (byte)addr;
        raw[3] = type;
        Array.Copy(data, 0, raw, 4, data.Length);
        int sum = 0;
        foreach (byte b in raw) sum += b;
        raw[^1] = (byte)((256 - (sum & 0xFF)) & 0xFF);
        var sb = new StringBuilder(":");
        foreach (byte b in raw) sb.Append(b.ToString("X2"));
        return sb.ToString();
    }

    internal static byte[] BuildImage()
    {
        var sb = new StringBuilder();
        sb.AppendLine("3008:0400000300000000F9");
        sb.AppendLine(Rec(0x00, 0x0000, new byte[] { 1, 2, 3, 4 }));
        sb.AppendLine(Rec(0x02, 0x0000, new byte[] { 0x10, 0x00 })); // base 0x10000
        sb.AppendLine(Rec(0x00, 0x0010, new byte[] { 5, 6 }));
        sb.AppendLine(Rec(0x01, 0x0000, Array.Empty<byte>()));
        sb.AppendLine();
        sb.Append("\x1A\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    [Test]
    public void Valid_Detects90()
    {
        var res = (IntelHexDetectionResult)new IntelHexAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()));
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual("3008", res.Image.HeaderCode);
        Assert.AreEqual(2, res.Image.Segments.Count);
    }

    [Test]
    public void Extract_FlattenAndGapFills()
    {
        var mem = Mem(BuildImage(), "mico.pak");
        var det = (IntelHexDetectionResult)new IntelHexAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new IntelHexAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(1, artifacts.Count);
        Assert.AreEqual("mico.bin", artifacts[0].Name);
        var flat = artifacts[0].Data.ToArray();
        Assert.AreEqual(0x10012, flat.Length);
        Assert.AreEqual(1, flat[0]);
        Assert.AreEqual(4, flat[3]);
        Assert.AreEqual(0xFF, flat[4]); // gap fill
        Assert.AreEqual(5, flat[0x10010]);
        Assert.AreEqual(6, flat[0x10011]);
    }

    [Test]
    public void BadChecksum_Detects0()
    {
        var img = BuildImage();
        // corrupt a checksum byte (last data record's checksum)
        img[img.Length - 8] ^= 0xFF;
        var res = new IntelHexAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(img));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void MissingEof_Detects0()
    {
        var text = Encoding.ASCII.GetString(BuildImage());
        var cut = text[..text.IndexOf(":00000001FF", StringComparison.Ordinal)];
        var res = new IntelHexAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(Encoding.ASCII.GetBytes(cut)));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void BinaryGarbage_Detects0()
    {
        var res = new IntelHexAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(new byte[] { 0x00, 0x01, 0x02, 0x85, 0x19, 0x00 }));
        Assert.AreEqual(0, res.Confidence);
    }
}
