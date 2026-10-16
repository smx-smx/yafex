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

namespace Yafex.FileFormats.Cdtm;

/// <summary>Linksys WRTP54G (TI AR7 "Titan", PSPBoot) web firmware image: a
/// <c>CDTM</c> header with a ProductID and an entry table (kernel/rootfs
/// blobs), plus an 8-byte trailer (magic + checksum). Entry checksums and
/// the trailer checksum use an unidentified algorithm and are read, never
/// verified. Payload codecs are opaque blobs to this layer.</summary>
public sealed class CdtmEntry
{
    public uint Index;
    public string Name = "";
    public long Offset;
    public long Size;
}

public record CdtmDetectionResult(
    int Confidence,
    string FileName,
    List<CdtmEntry> Entries) : DetectionResult(Confidence);

public static class CdtmParser
{
    public const int TrailerSize = 8;
    public const uint TrailerMagic = 0xC453DE23;
    public const int TableOffset = 0xBC;
    public const int EntrySize = 40;
    public const int EntryIndexOffset = 20;
    public const int EntryNameSize = 16;
    public const int EntryNameOffset = 24;

    private static readonly byte[] Magic = "CDTM"u8.ToArray();

    private static bool MagicOk(byte[] image, long off, byte[] magic)
    {
        if (off < 0 || off + magic.Length > image.Length) return false;
        for (int i = 0; i < magic.Length; i++)
        {
            if (image[off + i] != magic[i]) return false;
        }
        return true;
    }

    private static bool Printable(ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty) return false;
        foreach (byte b in span)
        {
            if (b != 0 && (b < 0x20 || b > 0x7E)) return false;
        }
        return true;
    }

    public static string DisplayName(byte[] image, int off, int len)
    {
        var span = image.AsSpan(off, len);
        int nul = span.IndexOf((byte)0);
        return Encoding.ASCII.GetString(nul >= 0 ? span.Slice(0, nul) : span);
    }

    /// <summary>Validates the header, entry table and trailer. Returns the
    /// file name plus entries, or null. All multi-byte integers are
    /// little-endian.</summary>
    public static (string, List<CdtmEntry>)? Open(byte[] image)
    {
        if (image.Length < TableOffset + 8 + TrailerSize) return null;
        if (!MagicOk(image, 0, Magic)) return null;
        // Flags: 0xFFFFFFFF for web images, 0x17FFFFFF high byte for
        // flash-direct images (documented web/flash distinction).
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(8, 4));
        if (flags != 0xFFFFFFFF && flags != 0x17FFFFFF) return null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(12, 4)) != 1) return null;
        uint headerLen = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(16, 4));
        if (!Printable(image.AsSpan(0x14, 4))) return null;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(0xB0, 4));
        uint entrySize = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(0xB4, 4));
        if (count == 0 || count > 64) return null;
        if (entrySize != EntrySize) return null;
        long tableEnd = (long)TableOffset + count * entrySize;
        if (tableEnd > image.Length) return null;
        if (headerLen != tableEnd) return null;

        string fileName = DisplayName(image, 0x70, 0x40);
        if (fileName.Length == 0 || !Printable(image.AsSpan(0x70, 0x40))) return null;

        var entries = new List<CdtmEntry>();
        for (long i = 0; i < count; i++)
        {
            long baseOff = TableOffset + i * entrySize;
            uint index = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)baseOff + EntryIndexOffset, 4));
            var nameSpan = image.AsSpan((int)baseOff + EntryNameOffset, EntryNameSize);
            if (!Printable(nameSpan)) return null;
            string name = DisplayName(image, (int)baseOff + EntryNameOffset, EntryNameSize);
            if (name.Length == 0 || index == 0 || index > 999) return null;
            long off = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)baseOff, 4));
            long size = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)baseOff + 4, 4));
            long content = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)baseOff + 8, 4));
            if (size <= 0) return null;
            if (off < tableEnd) return null;
            if (off + size > image.Length - TrailerSize) return null;
            if (content > size) return null;
            entries.Add(new CdtmEntry { Index = index, Name = name, Offset = off, Size = size });
        }
        if (entries.Count == 0) return null;
        // Entries must not overlap, and the image ends with payload plus
        // the 8-byte trailer (exact fit).
        var ordered = new List<CdtmEntry>(entries);
        ordered.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Offset < ordered[i - 1].Offset + ordered[i - 1].Size) return null;
        }
        long lastEnd = ordered[^1].Offset + ordered[^1].Size;
        if (lastEnd != image.Length - TrailerSize) return null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(image.Length - 8, 4)) != TrailerMagic) return null;
        // Trailer checksum at -4 and per-entry checksums use an
        // unidentified algorithm (not IEEE CRC32/adler/CRC-32C/MPEG-2/JAM,
        // sums, Fletcher, or MD5/SHA truncations): read, never verified.
        return (fileName, entries);
    }
}

public class CdtmDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < CdtmParser.TableOffset + 8 + CdtmParser.TrailerSize) return DetectionResult.Fail;
            var parsed = CdtmParser.Open(data.ToArray());
            if (parsed == null) return DetectionResult.Fail;
            return new CdtmDetectionResult(90, parsed.Value.Item1, parsed.Value.Item2);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class CdtmExtractor : IFormatExtractor
{
    private readonly string _fileName;
    private readonly List<CdtmEntry> _entries;

    public CdtmExtractor(CdtmDetectionResult result)
    {
        _fileName = result.FileName;
        _entries = result.Entries;
    }

    public static string SafeName(string name)
    {
        string flat = name.Replace('/', '_').Replace('\\', '_').Trim();
        return flat.Length == 0 ? "part" : flat;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var image = source.Data.ToArray();
        var parsed = CdtmParser.Open(image)
            ?? throw new InvalidDataException("CDTM image no longer parses cleanly");
        string baseDir = ArtifactBuilder.VersionedDir(source, Support.PathNames.Flatten(parsed.Item1));
        source.AddMetadata(new Metadata.BaseDirectoryPath(baseDir));
        foreach (var entry in parsed.Item2)
        {
            var bytes = new byte[entry.Size];
            Array.Copy(image, entry.Offset, bytes, 0, bytes.Length);
            var artifact = ArtifactBuilder.FlatIn(source, baseDir, SafeName(entry.Name) + ".bin", bytes);
            artifact.Flags |= DataSourceFlags.ProcessFurther;
            yield return artifact;
        }
    }
}
