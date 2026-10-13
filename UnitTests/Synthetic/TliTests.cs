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
// Synthetic TLI tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Tli;
using Yafex.Support;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class TliTests
{
    internal static byte[] BuildTarEntry(string name, byte[] payload, bool spacePadded)
    {
        var header = new byte[512];
        WriteAscii(header, 0, name);
        WriteAscii(header, 100, "000644");
        WriteOctal(header, 124, 12, payload.Length, spacePadded);
        WriteAscii(header, 257, "ustar");
        WriteAscii(header, 262, "00");
        // checksum with the chksum field treated as spaces
        long sum = 0;
        for (int i = 0; i < 512; i++)
        {
            sum += (i >= 148 && i < 156) ? 32 : header[i];
        }
        WriteOctal(header, 148, 8, sum, spacePadded);
        using var ms = new MemoryStream();
        ms.Write(header, 0, header.Length);
        ms.Write(payload, 0, payload.Length);
        int pad = (512 - payload.Length % 512) % 512;
        ms.Write(new byte[pad], 0, pad);
        return ms.ToArray();
    }

    internal static void WriteOctal(byte[] buf, int offset, int length, long value, bool spacePadded)
    {
        string digits = Convert.ToString(value, 8);
        int start = spacePadded ? length - digits.Length - 1 : 0;
        for (int i = 0; i < length; i++)
        {
            buf[offset + i] = (byte)' ';
        }
        for (int i = 0; i < digits.Length && start + i < length; i++)
        {
            buf[offset + start + i] = (byte)digits[i];
        }
        buf[offset + length - 1] = 0;
    }

    internal static byte[] BuildImage(byte[] kernel, byte[] config)
    {
        using var ms = new MemoryStream();
        // descriptor header: tableId + 12-bit length + records + crc32-be
        var header = new byte[15];
        header[0] = 0x4B;
        header[1] = 0x00;
        header[2] = 0x10;
        header[3] = 0x80;
        header[4] = 0x08;
        uint crc = Crc32.Compute(header, 0, header.Length);
        ms.Write(header, 0, header.Length);
        ms.WriteByte((byte)(crc >> 24));
        ms.WriteByte((byte)(crc >> 16));
        ms.WriteByte((byte)(crc >> 8));
        ms.WriteByte((byte)crc);
        // zero gap like production images, then the tar
        ms.Write(new byte[64], 0, 64);
        ms.Write(BuildTarEntry("kernel", kernel, spacePadded: true), 0, BuildTarEntry("kernel", kernel, spacePadded: true).Length);
        var cfgEntry = BuildTarEntry("cfg/opt", config, spacePadded: false);
        ms.Write(cfgEntry, 0, cfgEntry.Length);
        ms.Write(new byte[1024], 0, 1024);
        return ms.ToArray();
    }

    [Test]
    public void Valid_Detects90()
    {
        var mem = Mem(BuildImage(new byte[] { 1, 2, 3 }, new byte[] { 4, 5 }));
        var res = (TliDetectionResult)new TliAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual(0, res.Offset);
        Assert.AreEqual(16, res.HeaderLength);
        Assert.Greater(res.TarStart, res.Offset + res.HeaderLength);
    }

    [Test]
    public void Embedded_DetectsAtOffset()
    {
        var inner = BuildImage(new byte[] { 1 }, new byte[] { 2 });
        var buf = new byte[128 + inner.Length];
        for (int i = 0; i < buf.Length; i++)
        {
            buf[i] = 0xFF;
        }
        Array.Copy(inner, 0, buf, 128, inner.Length);
        var res = (TliDetectionResult)new TliAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(buf));
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual(128, res.Offset);
    }

    [Test]
    public void CorruptCrc_Fails()
    {
        var buf = BuildImage(new byte[] { 1, 2, 3 }, new byte[] { 4, 5 });
        buf[5] ^= 0xFF;
        var res = new TliAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(buf));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Truncated_Fails()
    {
        var buf = BuildImage(new byte[] { 1, 2, 3 }, new byte[] { 4, 5 });
        var cut = new byte[100];
        Array.Copy(buf, 0, cut, 0, cut.Length);
        var res = new TliAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(cut));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void Lister_OutputsImages()
    {
        var kernel = new byte[] { 0xAA, 0xBB, 0xCC };
        var mem = Mem(BuildImage(kernel, new byte[] { 0x11 }), "update.tli");
        var det = (TliDetectionResult)new TliAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var lister = new TliAddon().CreateLister(det);
        Assert.NotNull(lister);
        using var sw = new StringWriter();
        lister!.List(mem, det, sw);
        var text = sw.ToString();
        StringAssert.Contains("Tli", text);
        StringAssert.Contains("kernel", text);
        StringAssert.Contains("cfg/opt", text);
        StringAssert.Contains("size=3", text);
    }

    [Test]
    public void Extract_EmitsEntries()
    {
        var kernel = new byte[] { 0xAA, 0xBB, 0xCC };
        var config = new byte[] { 0x11, 0x22 };
        var mem = Mem(BuildImage(kernel, config), "update.tli");
        var det = (TliDetectionResult)new TliAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new TliAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(2, artifacts.Count);
        Assert.AreEqual("kernel", artifacts[0].Name);
        Assert.AreEqual("cfg_opt", artifacts[1].Name);
        CollectionAssert.AreEqual(kernel, artifacts[0].Data.ToArray());
        CollectionAssert.AreEqual(config, artifacts[1].Data.ToArray());
    }
}
