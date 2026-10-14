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
using System.Linq;
using System.Text;

namespace Yafex.FileFormats.Spv;

/// <summary>
/// SpaceTouch SG2 flash image (e.g. spvxx sp2cw3, Andes RISC-V): a 96-byte
/// header (entry jump, "SPV@" tag, build-string addresses, up to 4 bin
/// descriptors with flash address, size, and checksum) followed by the bin
/// payloads. Bin addresses map to file offsets relative to the lowest
/// populated bin address; the header itself is metadata, never payload.
/// Per-bin checksums are surfaced, not verified (algorithm unknown).
/// </summary>
public static class SpvParser
{
    public const uint TagMagic = 0x40565053; // "SPV@"
    public const int HeaderSize = 96;
    public const int MaxBins = 4;
    public const int BinRecordSize = 16;

    public sealed record BinEntry(string Name, uint Addr, uint Size, uint Checksum, long SpanStart, long Offset);

    public static uint ReadU32LE(byte[] image, long offset)
    {
        return (uint)(image[offset] | (image[offset + 1] << 8)
            | (image[offset + 2] << 16) | (image[offset + 3] << 24));
    }

    public static string DecodeName(uint raw)
    {
        var bytes = new[] { (byte)raw, (byte)(raw >> 8), (byte)(raw >> 16), (byte)(raw >> 24) };
        int len = Array.IndexOf(bytes, (byte)0);
        if (len < 0) len = bytes.Length;
        return Encoding.ASCII.GetString(bytes, 0, len);
    }

    public static bool ValidName(string name)
    {
        return name.Length > 0 && name.Length <= 4
            && name.All(c => c >= 0x20 && c < 0x7F);
    }

    /// <summary>Validate the header; returns the populated bins on success.</summary>
    public static bool TryParseHeader(byte[] image, out List<BinEntry> bins)
    {
        bins = new List<BinEntry>();
        if (image.Length < HeaderSize) return false;
        if (ReadU32LE(image, 4) != TagMagic) return false;
        uint total = ReadU32LE(image, 28);
        if (total < 1 || total > MaxBins) return false;
        uint floor = uint.MaxValue;
        var raw = new List<(string Name, uint Addr, uint Size, uint Checksum)>();
        for (int i = 0; i < total; i++)
        {
            long at = 32 + i * BinRecordSize;
            string name = DecodeName(ReadU32LE(image, at));
            uint addr = ReadU32LE(image, at + 4);
            uint size = ReadU32LE(image, at + 8);
            uint checksum = ReadU32LE(image, at + 12);
            if (size == 0) continue; // empty slot
            if (!ValidName(name) || addr == 0 || size > image.Length) return false;
            raw.Add((name, addr, size, checksum));
            if (addr < floor) floor = addr;
        }
        if (raw.Count == 0) return false;
        foreach (var (name, addr, size, checksum, index) in raw.Select((r, i) => (r.Name, r.Addr, r.Size, r.Checksum, i)))
        {
            long spanStart = (long)addr - floor;
            if (spanStart < 0 || spanStart >= image.Length) return false;
            // the header is metadata: a span covering offset zero starts
            // its payload after the header
            long start = spanStart < HeaderSize ? HeaderSize : spanStart;
            if (start > image.Length) return false;
            long spanLength = Math.Min((long)size, image.Length - spanStart);
            if (spanLength <= 0 || start - spanStart >= spanLength) return false;
            if (!VerifyChecksum(image, spanStart, spanLength, index)) return false;
            bins.Add(new BinEntry(name, addr, size, checksum, spanStart, start));
        }
        return bins.Count > 0;
    }

    /// <summary>
    /// Additive checksum over the bin span with any checksum fields inside
    /// it zeroed (verified against live SpaceTouch images).
    /// </summary>
    public static uint ComputeChecksum(byte[] image, long spanStart, long spanLength)
    {
        uint total = ReadU32LE(image, 28);
        ulong sum = 0;
        for (long i = spanStart; i < spanStart + spanLength; i++)
        {
            bool inField = false;
            for (int b = 0; b < total; b++)
            {
                long field = 32 + b * BinRecordSize + 12;
                if (i >= field && i < field + 4) { inField = true; break; }
            }
            if (!inField) sum += image[i];
        }
        return (uint)(sum & 0xFFFFFFFFu);
    }

    public static bool VerifyChecksum(byte[] image, long spanStart, long spanLength, int binIndex)
    {
        uint stored = ReadU32LE(image, 32 + binIndex * BinRecordSize + 12);
        return ComputeChecksum(image, spanStart, spanLength) == stored;
    }

    /// <summary>Payload length: span length minus any header overlap.</summary>
    public static long PayloadLength(byte[] image, BinEntry bin)
    {
        long spanLength = Math.Min((long)bin.Size, image.Length - bin.SpanStart);
        return Math.Min(spanLength - (bin.Offset - bin.SpanStart), image.Length - bin.Offset);
    }

    /// <summary>Read a NUL-terminated build string via its flash address.</summary>
    public static string ReadBuildString(byte[] image, uint addr, uint floor)
    {
        try
        {
            long offset = (long)addr - floor;
            if (offset < 0 || offset >= image.Length) return string.Empty;
            int len = 0;
            while (len < 64 && offset + len < image.Length && image[offset + len] != 0) len++;
            string s = Encoding.ASCII.GetString(image, (int)offset, len);
            if (s.Length == 0 || s.Any(c => c < 0x20 || c >= 0x7F)) return string.Empty;
            return s;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public static uint FloorAddress(byte[] image)
    {
        uint floor = uint.MaxValue;
        uint total = ReadU32LE(image, 28);
        for (int i = 0; i < total; i++)
        {
            long at = 32 + i * BinRecordSize;
            uint size = ReadU32LE(image, at + 8);
            if (size == 0) continue;
            uint addr = ReadU32LE(image, at + 4);
            if (addr < floor) floor = addr;
        }
        return floor;
    }

    public static string SafeName(string name)
    {
        string flat = name.Replace('/', '_').Replace('\\', '_').Trim();
        return flat.Length == 0 ? "bin" : flat;
    }
}

public record SpvDetectionResult(int Confidence, byte[]? Image, List<SpvParser.BinEntry> Bins) : DetectionResult(Confidence);

public class SpvDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < SpvParser.HeaderSize) return DetectionResult.Fail;
            var bytes = data.ToArray();
            if (!SpvParser.TryParseHeader(bytes, out var bins)) return DetectionResult.Fail;
            return new SpvDetectionResult(90, bytes, bins);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class SpvLister : IFormatLister
{
    private readonly byte[] _image;
    private readonly List<SpvParser.BinEntry> _bins;

    public SpvLister(SpvDetectionResult result)
    {
        _image = result.Image!;
        _bins = result.Bins;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        output.WriteLine($"Spv ({source.Name}, {_bins.Count} bins):");
        uint floor = SpvParser.FloorAddress(_image);
        string date = SpvParser.ReadBuildString(_image, SpvParser.ReadU32LE(_image, 20), floor);
        string time = SpvParser.ReadBuildString(_image, SpvParser.ReadU32LE(_image, 24), floor);
        if (date.Length > 0 || time.Length > 0)
        {
            output.WriteLine($"  build {date} {time}".TrimEnd());
        }
        foreach (var b in _bins)
        {
            long length = SpvParser.PayloadLength(_image, b);
            output.WriteLine($"  f {b.Name}.bin addr=0x{b.Addr:X} size={length} checksum=0x{b.Checksum:X8}");
        }
    }
}

public class SpvExtractor : IFormatExtractor
{
    private readonly byte[] _image;
    private readonly List<SpvParser.BinEntry> _bins;

    public SpvExtractor(SpvDetectionResult result)
    {
        _image = result.Image!;
        _bins = result.Bins;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        foreach (var b in _bins)
        {
            long length = SpvParser.PayloadLength(_image, b);
            if (length <= 0) continue;
            var content = new byte[length];
            Array.Copy(_image, b.Offset, content, 0, length);
            yield return ArtifactBuilder.Flat(source, SpvParser.SafeName(b.Name) + ".bin", content);
        }
    }
}
