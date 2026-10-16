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
/// blobs), plus an 8-byte trailer (magic + checksum) on web images.
/// Entry checksums and payload codecs are opaque to this layer; file
/// integrity comes from the verified trailer checksum.</summary>
public sealed class CdtmEntry
{
    public uint MtdNum;
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
    /// little-endian. The trailer checksum (TI Titan checksum over all
    /// bytes before the trailer) is verified; entry checksums are read
    /// and ignored (whole-file integrity already follows from it).</summary>
    public static (string, List<CdtmEntry>)? Open(byte[] image)
    {
        if (image.Length < 0xBC + 8 + TrailerSize) return null;
        if (!MagicOk(image, 0, Magic)) return null;
        // Flags: 0xFFFFFFFF for web images, 0x17FFFFFF high byte for
        // flash-direct images (documented web/flash distinction).
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(8, 4));
        bool web = flags == 0xFFFFFFFF;
        bool flashDirect = flags == 0x17FFFFFF;
        if (!web && !flashDirect) return null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(12, 4)) != 1) return null;
        uint headerLen = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(16, 4));
        if (!Printable(image.AsSpan(0x14, 4))) return null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(0x18, 4)) != 0x10203040) return null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(0x1C, 4)) != 0x0B010000) return null;
        uint fileSize = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(0x20, 4));
        uint miniLen = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(0x24, 4));
        long tableBase = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(0x28, 4));
        if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(0x2C, 4)) != 0xFFFFFFFF) return null;
        if (tableBase < 0x30 || tableBase + 12 > image.Length) return null;

        long fileNameOff = 0x40 + miniLen;
        if (fileNameOff < 0x40 || fileNameOff + 0x40 > image.Length) return null;
        string fileName = DisplayName(image, (int)fileNameOff, 0x40);
        if (fileName.Length == 0 || !Printable(image.AsSpan((int)fileNameOff, 0x40))) return null;

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)tableBase, 4));
        uint entrySize = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)tableBase + 4, 4));
        if (count == 0 || count > 64) return null;
        if (entrySize != EntrySize) return null;
        long recordsBase = tableBase + 12;
        if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)tableBase + 8, 4)) != recordsBase) return null;
        long tableEnd = recordsBase + count * entrySize;
        if (tableEnd > image.Length) return null;
        if (headerLen != tableEnd) return null;

        var entries = new List<CdtmEntry>();
        // Web images end with the 8-byte trailer; flash-direct images end
        // with payload. Bounds below are relative to the payload end.
        long payloadEnd = web ? image.Length - TrailerSize : image.Length;
        for (long i = 0; i < count; i++)
        {
            long baseOff = recordsBase + i * entrySize;
            uint mtd = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)baseOff + EntryIndexOffset, 4));
            var nameSpan = image.AsSpan((int)baseOff + EntryNameOffset, EntryNameSize);
            if (!Printable(nameSpan)) return null;
            string name = DisplayName(image, (int)baseOff + EntryNameOffset, EntryNameSize);
            if (name.Length == 0 || mtd == 0 || mtd > 999) return null;
            long off = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)baseOff, 4));
            long size = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)baseOff + 4, 4));
            long content = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)baseOff + 8, 4));
            if (size <= 0) return null;
            if (off < tableEnd) return null;
            if (off + size > payloadEnd) return null;
            if (content > size) return null;
            entries.Add(new CdtmEntry { MtdNum = mtd, Name = name, Offset = off, Size = size });
        }
        if (entries.Count == 0) return null;
        // Entries must not overlap, and the image ends with payload plus,
        // on web images, the 8-byte trailer (exact fit).
        var ordered = new List<CdtmEntry>(entries);
        ordered.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Offset < ordered[i - 1].Offset + ordered[i - 1].Size) return null;
        }
        long lastEnd = ordered[^1].Offset + ordered[^1].Size;
        if (web)
        {
            if (lastEnd != image.Length - TrailerSize) return null;
            if (fileSize != image.Length - TrailerSize) return null;
            if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(image.Length - 8, 4)) != TrailerMagic) return null;
            uint stored = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(image.Length - 4, 4));
            if (CdtmChecksum.Compute(image.AsSpan(0, image.Length - TrailerSize)) != stored) return null;
        }
        else
        {
            if (lastEnd != image.Length) return null;
            if (fileSize != image.Length) return null;
        }
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
            if (data.Length < 0xBC + 8 + CdtmParser.TrailerSize) return DetectionResult.Fail;
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
