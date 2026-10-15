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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Yafex.FileFormats.Zipper;

/// <summary>LG "zipper" super container (observed in recent webOS EPK3
/// super.pak): a 0x100000-byte header holding duplicated magic blocks and a
/// partition-name table, followed by raw concatenated squashfs payloads.
/// The table carries verification metadata (digests), not data offsets, so
/// payload boundaries come from validated squashfs superblocks and names
/// come from the table in order.</summary>
public sealed class ZipperCut
{
    public string Name = "";
    public long Offset;
    public long Size;
}

public record ZipperDetectionResult(
    int Confidence,
    List<ZipperCut> Cuts) : DetectionResult(Confidence);

public static class ZipperParser
{
    public const long HeaderSize = 0x100000;

    private static readonly byte[] GMagic = "gDla4\0\0\0"u8.ToArray();
    private static readonly byte[] PMagic = "0PLA\n\0\0\0"u8.ToArray();

    private static readonly long[] GOffsets = [0x1000, 0x2000];
    private static readonly long[] POffsets = [0x3000, 0x13000, 0x23000, 0x33000];

    public const int PlaEntrySize = 52;
    public const int PlaNameSize = 20;
    public const int PlaNameOffset = 32;

    private const uint SquashMagic = 0x73717368;

    private static bool MagicOk(byte[] image, long off, byte[] magic)
    {
        if (off < 0 || off + magic.Length > image.Length) return false;
        for (int i = 0; i < magic.Length; i++)
        {
            if (image[off + i] != magic[i]) return false;
        }
        return true;
    }

    private static bool BlockIsZeroPadded(byte[] image, long off, int used)
    {
        for (long i = off + used; i < off + 4096 && i < image.Length; i++)
        {
            if (image[i] != 0) return false;
        }
        return true;
    }

    /// <summary>Validates the duplicated magic blocks (two identical gDla4
    /// blocks, four identical PLA blocks). Returns the table names.</summary>
    public static List<string>? ParseNames(byte[] image)
    {
        if (image.Length < HeaderSize) return null;
        // Leading area must be padding up to the first magic block.
        for (long i = 0; i < GOffsets[0]; i++)
        {
            if (image[i] != 0) return null;
        }
        byte[]? firstG = null;
        foreach (long off in GOffsets)
        {
            if (!MagicOk(image, off, GMagic)) return null;
            var blk = new byte[64];
            Array.Copy(image, off, blk, 0, 64);
            firstG ??= blk;
            if (!blk.AsSpan().SequenceEqual(firstG)) return null;
            if (!BlockIsZeroPadded(image, off, 64)) return null;
        }
        byte[]? firstP = null;
        List<string>? names = null;
        foreach (long off in POffsets)
        {
            if (!MagicOk(image, off, PMagic)) return null;
            // Whole 4K copies must be byte-identical.
            var blk = new byte[4096];
            Array.Copy(image, off, blk, 0, 4096);
            firstP ??= blk;
            if (!blk.AsSpan().SequenceEqual(firstP)) return null;
            names ??= ParseTable(blk);
            if (names == null) return null;
        }
        return names;
    }

    internal static List<string>? ParseTable(byte[] blk)
    {
        if (blk.Length < 4096) return null;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(blk.AsSpan(84, 4));
        uint entrySize = BinaryPrimitives.ReadUInt32LittleEndian(blk.AsSpan(88, 4));
        uint tableSize = BinaryPrimitives.ReadUInt32LittleEndian(blk.AsSpan(92, 4));
        if (count == 0 || count > 100) return null;
        if (entrySize != PlaEntrySize) return null;
        if (tableSize != count * entrySize) return null;
        if (96 + tableSize > blk.Length) return null;
        var names = new List<string>();
        for (long i = 0; i < count; i++)
        {
            long baseOff = 96 + i * entrySize;
            var nameBytes = blk.AsSpan((int)baseOff + PlaNameOffset, PlaNameSize);
            int nul = nameBytes.IndexOf((byte)0);
            var slice = nul >= 0 ? nameBytes.Slice(0, nul) : nameBytes;
            if (slice.IsEmpty) return null;
            foreach (byte b in slice)
            {
                if (b < 0x20 || b > 0x7E) return null;
            }
            names.Add(Encoding.ASCII.GetString(slice));
        }
        return names;
    }

    private static bool ValidSquash(long bytesUsed, uint blockSize, ushort major, long remaining)
    {
        if (major != 4) return false;
        if (blockSize != 4096 && blockSize != 8192 && blockSize != 16384
            && blockSize != 65536 && blockSize != 131072) return false;
        return bytesUsed >= 96 && bytesUsed <= remaining;
    }

    /// <summary>Splits the payload (after the header) on validated squashfs
    /// superblocks. Names come from the table when counts match, else
    /// numbered fallbacks. Cuts never overlap. Stray magic occurrences
    /// inside compressed data fail superblock validation and are skipped.</summary>
    public static List<ZipperCut>? SplitPayload(byte[] image, List<string> names)
    {
        var starts = new List<long>();
        long pos = HeaderSize;
        while (true)
        {
            long found = IndexOf(image, SquashMagic, pos);
            if (found < 0) break;
            if (found + 96 > image.Length) break;
            uint blockSize = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)found + 12, 4));
            ushort major = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan((int)found + 28, 2));
            long bytesUsed = BinaryPrimitives.ReadInt64LittleEndian(image.AsSpan((int)found + 40, 8));
            if (!ValidSquash(bytesUsed, blockSize, major, image.Length - found))
            {
                pos = found + 1;
                continue;
            }
            starts.Add(found);
            pos = found + bytesUsed;
        }
        if (starts.Count == 0) return null;
        if (starts[0] != HeaderSize) return null;
        var cuts = new List<ZipperCut>();
        for (int i = 0; i < starts.Count; i++)
        {
            long end = image.Length;
            // Re-read this cut's length (validated above).
            long bytesUsed = BinaryPrimitives.ReadInt64LittleEndian(image.AsSpan((int)starts[i] + 40, 8));
            end = starts[i] + bytesUsed;
            if (i + 1 < starts.Count && end > starts[i + 1]) return null;
            string name = (starts.Count == names.Count) ? names[i] : $"part-{i}";
            cuts.Add(new ZipperCut { Name = name, Offset = starts[i], Size = end - starts[i] });
        }
        return cuts;
    }

    private static long IndexOf(byte[] image, uint magicLe, long from)
    {
        byte b0 = (byte)(magicLe & 0xFF);
        for (long i = from; i + 4 <= image.Length; i++)
        {
            if (image[i] != b0) continue;
            if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)i, 4)) == magicLe) return i;
        }
        return -1;
    }

    public static List<ZipperCut>? Open(byte[] image)
    {
        var names = ParseNames(image);
        if (names == null) return null;
        return SplitPayload(image, names);
    }
}

public class ZipperDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < ZipperParser.HeaderSize + 96) return DetectionResult.Fail;
            // Header window only for the magic/table walk; the payload scan
            // below pages through the (already mapped) image.
            var image = data.ToArray();
            var cuts = ZipperParser.Open(image);
            if (cuts == null || cuts.Count == 0) return DetectionResult.Fail;
            // Exact structural fit (duplicated magic blocks, consistent
            // table, superblock-validated cuts): outranks whole-image
            // heuristics that might also match the raw payload bytes.
            return new ZipperDetectionResult(96, cuts);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class ZipperExtractor : IFormatExtractor
{
    private readonly List<ZipperCut> _cuts;

    public ZipperExtractor(ZipperDetectionResult result)
    {
        _cuts = result.Cuts;
    }

    public static string SafeName(string name)
    {
        string flat = name.Replace('/', '_').Replace('\\', '_').Trim();
        return flat.Length == 0 ? "part" : flat;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var image = source.Data.ToArray();
        var cuts = ZipperParser.Open(image)
            ?? throw new InvalidDataException("Zipper payload no longer splits cleanly");
        foreach (var cut in cuts)
        {
            var bytes = new byte[cut.Size];
            Array.Copy(image, cut.Offset, bytes, 0, bytes.Length);
            var artifact = ArtifactBuilder.Flat(source, SafeName(cut.Name) + ".bin", bytes);
            artifact.Flags |= DataSourceFlags.ProcessFurther;
            yield return artifact;
        }
    }
}
