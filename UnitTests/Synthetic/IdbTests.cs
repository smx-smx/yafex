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
// Synthetic IDB tests. Fully in-memory: no disk, no samples.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex;
using Yafex.FileFormats.Idb;

using static UnitTests.Synthetic;

namespace UnitTests;

[TestFixture]
public class IdbTests
{
    internal static byte[] BuildImage()
    {
        var img = new List<byte>();
        img.AddRange(Encoding.ASCII.GetBytes("IDB_VERSION=1.1\n"));
        img.AddRange(Encoding.ASCII.GetBytes("IDB_NUM_IMG=3\n"));
        img.AddRange(Encoding.ASCII.GetBytes("IDB_HDR_ENDMARK\n"));
        int tableStart = img.Count;
        var blobs = new[] { new byte[] { 1, 2, 3 }, Array.Empty<byte>(), new byte[] { 4, 5 } };
        int dataStart = tableStart + 3 * 16;
        int off = dataStart;
        foreach (var b in blobs)
        {
            img.AddRange(new byte[8]); // unk1/unk2
            img.AddRange(BitConverter.GetBytes(b.Length));
            img.AddRange(BitConverter.GetBytes(b.Length == 0 ? 0 : off));
            off += b.Length;
        }
        foreach (var b in blobs)
        {
            img.AddRange(b);
        }
        return img.ToArray();
    }

    [Test]
    public void Valid_Detects90()
    {
        var res = (IdbDetectionResult)new IdbAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(BuildImage()));
        Assert.AreEqual(90, res.Confidence);
        Assert.AreEqual(3, res.Table.ImageCount);
    }

    [Test]
    public void Extract_SkipsZeroAndNumbersOutputs()
    {
        var mem = Mem(BuildImage(), "IDB_X.lr");
        var det = (IdbDetectionResult)new IdbAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new IdbAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(2, artifacts.Count);
        Assert.AreEqual("1.bin", artifacts[0].Name);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, artifacts[0].Data.ToArray());
        Assert.AreEqual("2.bin", artifacts[1].Name);
        CollectionAssert.AreEqual(new byte[] { 4, 5 }, artifacts[1].Data.ToArray());
    }

    [Test]
    public void Garbage_Detects0()
    {
        var res = new IdbAddon().CreateDetector(new Dictionary<string, string>())
            .Detect(Mem(Encoding.ASCII.GetBytes("definitely not an image database....")));
        Assert.AreEqual(0, res.Confidence);
    }

    [Test]
    public void BadOffset_Detects0()
    {
        var img = BuildImage();
        // corrupt first entry offset past EOF (table starts at 46)
        Array.Copy(BitConverter.GetBytes(0x7FFFFFFF), 0, img, 46 + 12, 4);
        var res = new IdbAddon().CreateDetector(new Dictionary<string, string>()).Detect(Mem(img));
        Assert.AreEqual(0, res.Confidence);
    }

    internal static byte[] BuildLgi1(int width, int height, byte imageBpp, byte format,
        byte[][] palette, byte[] pixels, int entrySize = 4, int paletteBpp = 32)
    {
        var img = new List<byte>();
        img.AddRange(Encoding.ASCII.GetBytes("LGI1"));
        img.Add(1); // version
        img.Add((byte)paletteBpp);
        img.Add(imageBpp);
        img.Add(format);
        img.Add((byte)(width >> 8));
        img.Add((byte)width);
        img.Add((byte)(height >> 8));
        img.Add((byte)height);
        if ((format & 0x80) != 0)
        {
            img.Add((byte)(palette.Length >> 8));
            img.Add((byte)palette.Length);
            img.Add((byte)entrySize);
            img.Add(0); // pad
            img.Add((byte)(palette.Length * entrySize >> 24));
            img.Add((byte)(palette.Length * entrySize >> 16));
            img.Add((byte)(palette.Length * entrySize >> 8));
            img.Add((byte)(palette.Length * entrySize));
            foreach (var e in palette)
            {
                img.AddRange(e);
            }
        }
        img.Add((byte)(pixels.Length >> 24));
        img.Add((byte)(pixels.Length >> 16));
        img.Add((byte)(pixels.Length >> 8));
        img.Add((byte)pixels.Length);
        img.AddRange(pixels);
        return img.ToArray();
    }

    [Test]
    public void Lgi1_IndexedRaw()
    {
        var pal = new[] { new byte[] { 0xFF, 0x10, 0x20, 0x30 }, new byte[] { 0x80, 0xA0, 0xB0, 0xC0 } };
        var blob = BuildLgi1(2, 1, 8, 0x80, pal, new byte[] { 0, 1 });
        var img = Lgi1.Decode(blob);
        Assert.AreEqual(2, img.Width);
        Assert.AreEqual(1, img.Height);
        // ARGB -> RGBA rotation
        CollectionAssert.AreEqual(
            new byte[] { 0x10, 0x20, 0x30, 0xFF, 0xA0, 0xB0, 0xC0, 0x80 }, img.Rgba);
    }

    [Test]
    public void Lgi1_RleWithExtendedCount()
    {
        var pal = new[] { new byte[] { 0xFF, 0x01, 0x02, 0x03 }, new byte[] { 0xFF, 0x04, 0x05, 0x06 } };
        // marker 0x07, run (count 1 -> 2px idx1), extended run (0x80 0x02 -> 3px idx0), literal idx1
        var blob = BuildLgi1(2, 3, 8, 0xC0, pal,
            new byte[] { 0x07, 0x07, 0x01, 0x01, 0x07, 0x80, 0x02, 0x00, 0x01 });
        var img = Lgi1.Decode(blob);
        Assert.AreEqual(2, img.Width);
        Assert.AreEqual(3, img.Height);
        CollectionAssert.AreEqual(
            new byte[]
            {
                0x04, 0x05, 0x06, 0xFF, 0x04, 0x05, 0x06, 0xFF,
                0x01, 0x02, 0x03, 0xFF, 0x01, 0x02, 0x03, 0xFF,
                0x01, 0x02, 0x03, 0xFF, 0x04, 0x05, 0x06, 0xFF,
            }, img.Rgba);
    }

    [Test]
    public void Lgi1_Indexed16AndRgbPalette()
    {
        var pal = new[] { new byte[] { 0x11, 0x22, 0x33 } };
        var blob = BuildLgi1(2, 1, 16, 0x80, pal, new byte[] { 0, 0, 0, 0 }, 3, 24);
        var img = Lgi1.Decode(blob);
        CollectionAssert.AreEqual(
            new byte[] { 0x11, 0x22, 0x33, 0xFF, 0x11, 0x22, 0x33, 0xFF }, img.Rgba);
    }

    [Test]
    public void Lgi1_DirectRgb()
    {
        var blob = BuildLgi1(1, 2, 24, 0x00, Array.Empty<byte[]>(),
            new byte[] { 1, 2, 3, 4, 5, 6 });
        var img = Lgi1.Decode(blob);
        CollectionAssert.AreEqual(
            new byte[] { 1, 2, 3, 0xFF, 4, 5, 6, 0xFF }, img.Rgba);
    }

    [Test]
    public void Lgi1_RejectsBadIndex()
    {
        var pal = new[] { new byte[] { 0xFF, 0x01, 0x02, 0x03 } };
        var blob = BuildLgi1(1, 1, 8, 0x80, pal, new byte[] { 7 });
        Assert.Throws<InvalidDataException>(() => Lgi1.Decode(blob));
    }

    [Test]
    public void PngWriter_RoundTrip()
    {        var png = PngWriter.EncodeRgba(1, 1, new byte[] { 9, 8, 7, 6 });
        Assert.AreEqual(0x89, png[0]);
        Assert.AreEqual("IHDR", Encoding.ASCII.GetString(png, 12, 4));
        Assert.AreEqual(1, (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19]);
        Assert.AreEqual(6, png[25]); // truecolor+alpha
        Assert.AreEqual("IEND", Encoding.ASCII.GetString(png, png.Length - 8, 4));
    }

    [Test]
    public void Extract_DecodesLgi1ToPng()
    {
        var pal = new[] { new byte[] { 0xFF, 0x01, 0x02, 0x03 } };
        var lgi = BuildLgi1(1, 1, 8, 0x80, pal, new byte[] { 0 });
        var img = new List<byte>();
        img.AddRange(Encoding.ASCII.GetBytes("IDB_VERSION=1.1\n"));
        img.AddRange(Encoding.ASCII.GetBytes("IDB_NUM_IMG=2\n"));
        img.AddRange(Encoding.ASCII.GetBytes("IDB_HDR_ENDMARK\n"));
        int tableStart = img.Count;
        int dataStart = tableStart + 2 * 16;
        img.AddRange(new byte[8]);
        img.AddRange(BitConverter.GetBytes(lgi.Length));
        img.AddRange(BitConverter.GetBytes(dataStart));
        img.AddRange(new byte[8]);
        img.AddRange(BitConverter.GetBytes(3));
        img.AddRange(BitConverter.GetBytes(dataStart + lgi.Length));
        img.AddRange(lgi);
        img.AddRange(new byte[] { 9, 9, 9 });
        var mem = Mem(img.ToArray(), "IDB_X.lr");
        var det = (IdbDetectionResult)new IdbAddon().CreateDetector(new Dictionary<string, string>()).Detect(mem);
        var artifacts = new IdbAddon().CreateExtractor(det).Extract(mem).ToList();
        Assert.AreEqual(2, artifacts.Count);
        Assert.AreEqual("1.png", artifacts[0].Name);
        var png = artifacts[0].Data.ToArray();
        Assert.AreEqual(0x89, png[0]);
        Assert.AreEqual("IHDR", Encoding.ASCII.GetString(png, 12, 4));
        Assert.AreEqual("2.bin", artifacts[1].Name);
    }

    internal static byte[] BuildEu16(int width, int height, ushort[] palette, byte[] runs)
    {
        var img = new List<byte>();
        img.AddRange(Encoding.ASCII.GetBytes("16"));
        img.Add(0); // version
        img.Add((byte)width);
        img.Add(0);
        img.Add((byte)height);
        img.Add(0);
        img.Add((byte)palette.Length);
        foreach (var p in palette)
        {
            img.Add((byte)p);
            img.Add((byte)(p >> 8));
        }
        int pixels = width * height;
        img.Add((byte)(pixels >> 24));
        img.Add((byte)(pixels >> 16));
        img.Add((byte)(pixels >> 8));
        img.Add((byte)pixels);
        img.AddRange(runs);
        return img.ToArray();
    }

    [Test]
    public void Eu16_IndexedRuns()
    {
        // RGB565 red + green; 3x1: 2 red, 1 green
        var blob = BuildEu16(3, 1, new ushort[] { 0xF800, 0x07E0 }, new byte[] { 2, 0, 1, 1 });
        var img = Eu16.Decode(blob);
        Assert.AreEqual(3, img.Width);
        CollectionAssert.AreEqual(
            new byte[] { 255, 0, 0, 255, 255, 0, 0, 255, 0, 255, 0, 255 }, img.Rgba);
    }

    [Test]
    public void Eu16_RejectsBadIndex()
    {
        var blob = BuildEu16(1, 1, new ushort[] { 0xF800 }, new byte[] { 1, 4 });
        Assert.Throws<InvalidDataException>(() => Eu16.Decode(blob));
    }
}
