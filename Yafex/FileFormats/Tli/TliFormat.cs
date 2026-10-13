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
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Yafex.FileFormats.Tli;

/// <summary>
/// TLI firmware update container (Loewe TV software-update images, ".tli").
/// Layout: a short descriptor header followed by a standard ustar archive
/// holding one entry per firmware image, then zero padding.
/// Header: [tableId:8][length:12][descriptor records...][crc32-be:32],
/// where length covers the header bytes and the CRC stored at
/// [length-1..length+2] spans the preceding bytes (IEEE CRC32).
/// Only plaintext headers are recognized.
/// </summary>
public static class TliParser
{
    public const byte TableId = 0x4B;
    public const int MaxHeaderLength = 65535;
    public const int MaxEntries = 10000;

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
            table[i] = crc;
        }
        return table;
    }

    public static uint Crc32(byte[] data, int offset, int count)
    {
        uint crc = 0xFFFFFFFFu;
        int end = offset + count;
        for (int i = offset; i < end; i++)
        {
            crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFFu;
    }

    public sealed class Header
    {
        public long Offset;
        public int Length;
    }

    public sealed class Entry
    {
        public string Name = string.Empty;
        public long DataOffset;
        public long Size;
    }

    /// <summary>Validate the header at offset; returns its length on success.</summary>
    public static bool TryParseHeader(byte[] image, long offset, out int headerLength)
    {
        headerLength = 0;
        if (offset < 0 || offset + 12 > image.Length)
        {
            return false;
        }
        if (image[offset] != TableId)
        {
            return false;
        }
        int length = ((image[offset + 1] & 0x0F) << 8) | image[offset + 2];
        if (length <= 8 || length > MaxHeaderLength)
        {
            return false;
        }
        if (offset + length + 3 > image.Length)
        {
            return false;
        }
        uint stored = (uint)((image[offset + length - 1] << 24)
            | (image[offset + length] << 16)
            | (image[offset + length + 1] << 8)
            | image[offset + length + 2]);
        if (stored == 0)
        {
            return false;
        }
        if (Crc32(image, (int)offset, length - 1) != stored)
        {
            return false;
        }
        headerLength = length;
        return true;
    }

    /// <summary>Flatten tar member names for artifact output.</summary>
    public static string SafeName(string name)
    {
        string flat = name.Replace('/', '_').Replace('\\', '_').Trim();
        return flat;
    }

    /// <summary>Locate the tar archive following the header.</summary>
    public static bool TryFindTar(byte[] image, long headerEnd, out long tarStart)
    {
        tarStart = 0;
        long limit = Math.Min(image.Length - 512, headerEnd + 65536);
        for (long pos = headerEnd; pos < limit; pos++)
        {
            if (IsTarHeader(image, pos))
            {
                tarStart = pos;
                return true;
            }
        }
        return false;
    }

    private static bool IsTarHeader(byte[] image, long pos)
    {
        if (pos < 0 || pos + 512 > image.Length)
        {
            return false;
        }
        if (image[pos + 257] != (byte)'u'
            || image[pos + 258] != (byte)'s'
            || image[pos + 259] != (byte)'t'
            || image[pos + 260] != (byte)'a'
            || image[pos + 261] != (byte)'r')
        {
            return false;
        }
        return TarChecksum(image, pos, out _);
    }

    private static bool TarChecksum(byte[] image, long pos, out long size)
    {
        size = 0;
        long sum = 0;
        for (int i = 0; i < 512; i++)
        {
            byte b = image[pos + i];
            sum += (i >= 148 && i < 156) ? 32 : b;
        }
        if (!ParseOctal(image, pos + 148, 8, out long stored))
        {
            return false;
        }
        if (sum != stored)
        {
            return false;
        }
        return ParseOctal(image, pos + 124, 12, out size);
    }

    /// <summary>Tar numeric field: leading spaces/nulls, octal digits,
    /// terminated by space/null. Both paddings occur in the wild.</summary>
    private static bool ParseOctal(byte[] image, long pos, int length, out long value)
    {
        value = 0;
        int i = 0;
        while (i < length && (image[pos + i] == (byte)' ' || image[pos + i] == 0))
        {
            i++;
        }
        if (i >= length)
        {
            return false;
        }
        int digits = 0;
        while (i < length)
        {
            byte b = image[pos + i];
            if (b == 0 || b == (byte)' ')
            {
                break;
            }
            if (b < (byte)'0' || b > (byte)'7')
            {
                return false;
            }
            value = (value << 3) + (b - (byte)'0');
            digits++;
            i++;
        }
        return digits > 0;
    }

    private static bool IsZeroBlock(byte[] image, long pos)
    {
        if (pos < 0 || pos + 512 > image.Length)
        {
            return true;
        }
        for (int i = 0; i < 512; i++)
        {
            if (image[pos + i] != 0)
            {
                return false;
            }
        }
        return true;
    }

    private static string ReadName(byte[] image, long pos)
    {
        int nameLen = 0;
        while (nameLen < 100 && image[pos + nameLen] != 0)
        {
            nameLen++;
        }
        int prefixLen = 0;
        while (prefixLen < 155 && image[pos + 345 + prefixLen] != 0)
        {
            prefixLen++;
        }
        string name = Encoding.ASCII.GetString(image, (int)pos, nameLen);
        if (prefixLen > 0)
        {
            string prefix = Encoding.ASCII.GetString(image, (int)pos + 345, prefixLen);
            name = prefix + "/" + name;
        }
        return name;
    }

    /// <summary>Walk the tar entries; stops at the end marker or first fault.</summary>
    public static List<Entry> ListEntries(byte[] image, long tarStart)
    {
        var entries = new List<Entry>();
        long pos = tarStart;
        while (pos + 512 <= image.Length && entries.Count < MaxEntries)
        {
            if (IsZeroBlock(image, pos))
            {
                if (IsZeroBlock(image, pos + 512))
                {
                    break;
                }
                pos += 512;
                continue;
            }
            if (!IsTarHeader(image, pos) || !TarChecksum(image, pos, out long size))
            {
                break;
            }
            if (size < 0 || pos + 512 + size > image.Length)
            {
                break;
            }
            entries.Add(new Entry
            {
                Name = ReadName(image, pos),
                DataOffset = pos + 512,
                Size = size,
            });
            pos += 512 + ((size + 511) / 512) * 512;
        }
        return entries;
    }
}

public record TliDetectionResult(int Confidence, byte[]? Image, long Offset, int HeaderLength, long TarStart) : DetectionResult(Confidence);

public class TliDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 512)
            {
                return DetectionResult.Fail;
            }
            var bytes = data.ToArray();
            int found = 0;
            for (long off = 0; off + 12 <= bytes.Length && found < 64; off++)
            {
                if (bytes[off] != TliParser.TableId)
                {
                    continue;
                }
                found++;
                if (!TliParser.TryParseHeader(bytes, off, out int headerLength))
                {
                    continue;
                }
                if (!TliParser.TryFindTar(bytes, off + headerLength + 3, out long tarStart))
                {
                    continue;
                }
                var entries = TliParser.ListEntries(bytes, tarStart);
                if (entries.Count == 0)
                {
                    continue;
                }
                return new TliDetectionResult(90, bytes, off, headerLength, tarStart);
            }
            return DetectionResult.Fail;
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class TliLister : IFormatLister
{
    private readonly byte[] _image;
    private readonly long _offset;
    private readonly int _headerLength;
    private readonly long _tarStart;

    public TliLister(TliDetectionResult result)
    {
        _image = result.Image!;
        _offset = result.Offset;
        _headerLength = result.HeaderLength;
        _tarStart = result.TarStart;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        var entries = TliParser.ListEntries(_image, _tarStart);
        output.WriteLine($"Tli ({source.Name}, {entries.Count} images, header {_headerLength} bytes at 0x{_offset:X}):");
        foreach (var e in entries)
        {
            output.WriteLine($"  f {e.Name} size={e.Size}");
        }
    }
}

public class TliExtractor : IFormatExtractor
{
    private readonly byte[] _image;
    private readonly long _tarStart;

    public TliExtractor(TliDetectionResult result)
    {
        _image = result.Image!;
        _tarStart = result.TarStart;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        foreach (var e in TliParser.ListEntries(_image, _tarStart))
        {
            string flat = TliParser.SafeName(e.Name);
            if (flat.Length == 0)
            {
                continue;
            }
            var content = new byte[e.Size];
            Array.Copy(_image, e.DataOffset, content, 0, (int)e.Size);
            yield return ArtifactBuilder.Flat(source, flat, content);
        }
    }
}
