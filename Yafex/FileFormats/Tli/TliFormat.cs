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

using DiscUtils.Archives;

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
            if (TarFile.IsHeader(image, (int)pos))
            {
                tarStart = pos;
                return true;
            }
        }
        return false;
    }

    /// <summary>Open the tar archive at an absolute image offset.</summary>
    public static TarFile OpenTar(byte[] image, long tarStart)
    {
        var stream = new MemoryStream(image, (int)tarStart, image.Length - (int)tarStart, false);
        return new TarFile(stream);
    }

    public static int CountEntries(TarFile tar)
    {
        int count = 0;
        foreach (var _ in tar.Entries)
        {
            count++;
        }
        return count;
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
                int members;
                try
                {
                    members = TliParser.CountEntries(TliParser.OpenTar(bytes, tarStart));
                }
                catch (Exception)
                {
                    continue;
                }
                if (members == 0)
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
        var tar = TliParser.OpenTar(_image, _tarStart);
        int count = TliParser.CountEntries(tar);
        output.WriteLine($"Tli ({source.Name}, {count} images, header {_headerLength} bytes at 0x{_offset:X}):");
        foreach (var e in tar.Entries)
        {
            output.WriteLine($"  f {e.Name} size={e.Length}");
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
        var tar = TliParser.OpenTar(_image, _tarStart);
        foreach (var e in tar.Entries)
        {
            string flat = TliParser.SafeName(e.Name);
            if (flat.Length == 0 || e.Length > int.MaxValue)
            {
                continue;
            }
            var content = new byte[(int)e.Length];
            using (var s = tar.OpenFile(e.Name))
            {
                int done = 0;
                while (done < content.Length)
                {
                    int n = s.Read(content, done, content.Length - done);
                    if (n <= 0)
                    {
                        break;
                    }
                    done += n;
                }
                if (done != content.Length)
                {
                    continue;
                }
            }
            yield return ArtifactBuilder.Flat(source, flat, content);
        }
    }
}
