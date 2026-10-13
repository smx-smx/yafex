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
// Synthetic tar tests (Yafex-native reader). Fully in-memory: no disk.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NUnit.Framework;

using Yafex.FileFormats.Tar;

namespace UnitTests;

[TestFixture]
public class TarTests
{
    internal static byte[] BuildEntry(string name, byte[] payload, bool spacePadded = false)
    {
        var header = new byte[512];
        WriteField(header, 0, 100, name);
        WriteField(header, 100, 8, "000644");
        WriteOctal(header, 124, 12, payload.Length, spacePadded);
        WriteField(header, 257, 6, "ustar");
        WriteField(header, 263, 2, "00");
        header[156] = (byte)'0';
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

    internal static void WriteField(byte[] buf, int offset, int length, string s)
    {
        var b = Encoding.ASCII.GetBytes(s);
        Array.Copy(b, 0, buf, offset, Math.Min(b.Length, length));
    }

    internal static void WriteOctal(byte[] buf, int offset, int length, long value, bool spacePadded = false)
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

    internal static byte[] BuildArchive(bool spacePadded = false)
    {
        using var ms = new MemoryStream();
        var a = BuildEntry("hello.txt", new byte[] { 1, 2, 3, 4, 5 }, spacePadded);
        ms.Write(a, 0, a.Length);
        var b = BuildEntry("empty", Array.Empty<byte>(), spacePadded);
        ms.Write(b, 0, b.Length);
        ms.Write(new byte[1024], 0, 1024);
        return ms.ToArray();
    }

    internal static List<YafexTarEntry> ReadAll(byte[] img)
    {
        using var ms = new MemoryStream(img, false);
        return new YafexTarReader(ms).Entries.ToList();
    }

    [Test]
    public void Entries_YieldNameOffsetLength()
    {
        var entries = ReadAll(BuildArchive());
        Assert.AreEqual(2, entries.Count);
        Assert.AreEqual("hello.txt", entries[0].Name);
        Assert.AreEqual(512, entries[0].Offset);
        Assert.AreEqual(5, entries[0].Length);
        Assert.AreEqual("empty", entries[1].Name);
        Assert.AreEqual(0, entries[1].Length);
    }

    [Test]
    public void Entries_AcceptsSpacePaddedFields()
    {
        var entries = ReadAll(BuildArchive(spacePadded: true));
        Assert.AreEqual(2, entries.Count);
        Assert.AreEqual(5, entries[0].Length);
        Assert.AreEqual("hello.txt", entries[0].Name);
    }

    [Test]
    public void OpenFile_ReturnsBytes()
    {
        var img = BuildArchive();
        using var ms = new MemoryStream(img, false);
        var tar = new YafexTarReader(ms);
        Assert.IsTrue(tar.FileExists("hello.txt"));
        using var s = tar.OpenFile("hello.txt");
        var buf = new byte[(int)s.Length];
        int done = 0;
        while (done < buf.Length)
        {
            int n = s.Read(buf, done, buf.Length - done);
            if (n <= 0)
            {
                break;
            }
            done += n;
        }
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, buf);
    }

    [Test]
    public void CorruptChecksum_Throws()
    {
        var img = BuildArchive();
        img[1024 + 148] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => ReadAll(img));
    }

    [Test]
    public void Truncated_Throws()
    {
        var img = BuildArchive();
        var cut = new byte[512];
        Array.Copy(img, 0, cut, 0, cut.Length);
        Assert.Throws<EndOfStreamException>(() => ReadAll(cut));
    }

    [Test]
    public void IsHeader_AcceptsValidRejectsGarbage()
    {
        var img = BuildArchive();
        Assert.IsTrue(YafexTarReader.IsHeader(img, 0));
        Assert.IsFalse(YafexTarReader.IsHeader(img, 1));
        Assert.IsFalse(YafexTarReader.IsHeader(new byte[1024], 0));
        Assert.IsFalse(YafexTarReader.IsHeader(img, img.Length));
    }
}
