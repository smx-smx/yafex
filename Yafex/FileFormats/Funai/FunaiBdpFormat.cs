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
using System.IO.Compression;
using System.Text;

namespace Yafex.FileFormats.Funai;

/// <summary>Variant C — BDP Blu-ray index plus partitioned payloads. The whole
/// file is transformed with the shared block cipher; parsing then operates
/// on the transformed image.</summary>
public sealed class FunaiBdpIndexEntry
{
    public string Name = "";
    public uint Offset;
    public uint Size;
}

public record FunaiBdpDetectionResult(
    int Confidence,
    uint Key,
    List<FunaiBdpIndexEntry> Index) : DetectionResult(Confidence);

public static class FunaiBdpParser
{
    public static readonly byte[] IndexMagic = Encoding.ASCII.GetBytes("index_table");

    public const int IndexNameSize = 32;
    public const int BlobNameSize = 0x20;
    public const int IndexEntrySize = 44;

    public static bool ProbeMatches(byte[] probe16)
    {
        if (probe16.Length < 16) return false;
        for (int i = 0; i < 11; i++)
        {
            if (probe16[i] != IndexMagic[i]) return false;
        }
        for (int i = 11; i < 16; i++)
        {
            if (probe16[i] != 0) return false;
        }
        return true;
    }

    public static List<FunaiBdpIndexEntry> ParseIndex(byte[] image)
    {
        return ParseIndexForLength(image, image.Length);
    }

    public static List<FunaiBdpIndexEntry> ParseIndexForLength(byte[] head, long totalLength)
    {
        if (head.Length < 0x24) throw new InvalidDataException("BDP image too small for its index");
        if (!ProbeMatches(head.AsSpan(0, 16).ToArray()))
        {
            throw new InvalidDataException("BDP index magic missing");
        }
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(0x20, 4));
        if (count == 0 || count > 100000) throw new InvalidDataException($"BDP partition count {count} implausible");
        long need = 0x20 + 4 + (long)count * IndexEntrySize;
        if (need > head.Length) throw new InvalidDataException("BDP index runs past the image");
        var entries = new List<FunaiBdpIndexEntry>();
        long at = 0x20 + 4;
        for (long i = 0; i < count; i++)
        {
            string name = FunaiCrypto.DisplayString(head.AsSpan((int)at, IndexNameSize));
            uint off = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan((int)at + 32, 4));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan((int)at + 36, 4));
            // Checksum at +40 is read and ignored.
            if ((ulong)off + size > (ulong)totalLength)
            {
                throw new InvalidDataException($"BDP partition '{name}' runs past the image");
            }
            if (name.Length == 0) name = "part";
            entries.Add(new FunaiBdpIndexEntry { Name = name, Offset = off, Size = size });
            at += IndexEntrySize;
        }
        return entries;
    }

    public static byte[] SliceBlob(byte[] image, FunaiBdpIndexEntry entry)
    {
        var blob = new byte[entry.Size];
        Array.Copy(image, entry.Offset, blob, 0, blob.Length);
        return blob;
    }

    private static bool LooksLikeCmpr(byte[] blob)
    {
        if (blob.Length < BlobNameSize + 12) return false;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(BlobNameSize + 4, 4));
        uint dataSize = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(BlobNameSize + 8, 4));
        if (count > blob.Length || dataSize > blob.Length) return false;
        long overhead = (long)BlobNameSize + 12 + (long)count * 12;
        return (long)blob.Length - overhead == dataSize;
    }

    private static bool NameEndsWith(byte[] blob, string suffix)
    {
        string name = FunaiCrypto.DisplayString(blob.AsSpan(0, Math.Min(BlobNameSize, blob.Length)));
        return name.EndsWith(suffix, StringComparison.Ordinal);
    }

    public static byte[] ProcessBlob(byte[] blob)
    {
        if (blob.Length < BlobNameSize) throw new InvalidDataException("BDP partition blob too small for its name");
        if (LooksLikeCmpr(blob))
        {
            return ExpandCmpr(blob);
        }
        if (NameEndsWith(blob, "_image_rom"))
        {
            return ExpandImageRom(blob);
        }
        var plain = new byte[blob.Length - BlobNameSize];
        Array.Copy(blob, BlobNameSize, plain, 0, plain.Length);
        return plain;
    }

    private static byte[] ExpandCmpr(byte[] blob)
    {
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(BlobNameSize + 4, 4));
        uint dataSize = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(BlobNameSize + 8, 4));
        long descriptors = BlobNameSize + 12 + (long)count * 12;
        if (descriptors + dataSize != blob.Length)
        {
            throw new InvalidDataException("BDP cmpr sizes do not add up");
        }
        using var output = new MemoryStream((int)Math.Min(dataSize, 1 << 30));
        long dataAt = descriptors;
        for (long i = 0; i < count; i++)
        {
            long dOff = BlobNameSize + 12 + i * 12;
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan((int)dOff, 4));
            uint mode = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan((int)dOff + 4, 4));
            uint fill = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan((int)dOff + 8, 4));
            if (mode == 0)
            {
                if ((ulong)dataAt + size > (ulong)blob.Length)
                {
                    throw new InvalidDataException("BDP cmpr raw block runs past the blob");
                }
                output.Write(blob, (int)dataAt, (int)size);
                dataAt += size;
            }
            else if (mode == 1)
            {
                // Fill blocks emit without consuming stream bytes; data_size
                // covers stored (raw) bytes only, so the emitted image is
                // larger than data_size by the fill expansion.
                byte f = (byte)(fill & 0xFF);
                for (long k = 0; k < size; k++) output.WriteByte(f);
            }
            else
            {
                throw new InvalidDataException($"BDP cmpr unknown block mode {mode}");
            }
        }
        if (dataAt - descriptors != dataSize)
        {
            throw new InvalidDataException("BDP cmpr stored size mismatch");
        }
        return output.ToArray();
    }

    private static byte[] ExpandImageRom(byte[] blob)
    {
        const int header = BlobNameSize;
        if (blob.Length < header + 4 + 4 + 8 + 20 + 12 + 4 + 12)
        {
            throw new InvalidDataException("BDP image_rom header too small");
        }
        uint chunks = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(header, 4));
        if (chunks == 0 || chunks > 100000) throw new InvalidDataException($"BDP image_rom chunk count {chunks} implausible");
        uint dataBase = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(header + 4 + 4 + 8 + 20 + 12, 4));
        long table = header + 4 + 4 + 8 + 20 + 12 + 4 + 12;
        if (table + (long)chunks * 64 > blob.Length)
        {
            throw new InvalidDataException("BDP image_rom descriptor table runs past the blob");
        }
        using var output = new MemoryStream();
        for (long i = 0; i < chunks; i++)
        {
            long dOff = table + i * 64;
            uint chunkOff = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan((int)dOff + 20 + 20 + 8, 4));
            uint compSize = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan((int)dOff + 20 + 20 + 8 + 4, 4));
            long abs = (long)dataBase + chunkOff;
            if (abs < 0 || (ulong)abs + compSize > (ulong)blob.Length)
            {
                throw new InvalidDataException("BDP image_rom chunk runs past the blob");
            }
            var comp = new byte[compSize];
            Array.Copy(blob, abs, comp, 0, comp.Length);
            var inflated = InflateZlib(comp);
            output.Write(inflated, 0, inflated.Length);
        }
        return output.ToArray();
    }

    internal static byte[] InflateZlib(byte[] comp)
    {
        using var input = new MemoryStream(comp, false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Decrypts exactly [blobOffset, blobOffset+blobSize) as it
    /// appears in the full-file transform (file-aligned 8-byte blocks with
    /// a trailing passthrough at EOF). Decrypts each overlapped file block
    /// once.</summary>
    public static byte[] DecryptBlobRange(byte[] file, long blobOffset, int blobSize, long fileLength, uint key)
    {
        if (blobOffset < 0 || blobSize < 0 || blobOffset + blobSize > file.Length)
        {
            throw new InvalidDataException("BDP blob range out of file");
        }
        var output = new byte[blobSize];
        long fullBlocks = (fileLength / 8) * 8;
        long blobEnd = blobOffset + blobSize;
        long blockStart = (blobOffset / 8) * 8;
        while (blockStart < blobEnd)
        {
            if (blockStart >= fullBlocks)
            {
                // Trailing passthrough at EOF: copy raw bytes.
                long from = Math.Max(blockStart, blobOffset);
                long to = Math.Min(blobEnd, fileLength);
                for (long abs = from; abs < to; abs++)
                {
                    output[abs - blobOffset] = file[abs];
                }
                break;
            }
            uint a = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)blockStart, 4));
            uint b = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)blockStart + 4, 4));
            FunaiCrypto.DecryptBlock(a, b, key, out uint p0, out uint p1);
            for (int k = 0; k < 8; k++)
            {
                long abs = blockStart + k;
                if (abs < blobOffset || abs >= blobEnd) continue;
                uint word = k < 4 ? p0 : p1;
                output[abs - blobOffset] = (byte)((word >> (8 * (k % 4))) & 0xFF);
            }
            blockStart += 8;
        }
        return output;
    }
}

public class FunaiBdpDetector : IFormatDetector
{
    public const string KeysId = "funai-bdp-keys";

    private readonly KeysRepository _keys;
    private readonly IDictionary<string, string> _args;

    public FunaiBdpDetector(KeysRepository keys, IDictionary<string, string> args)
    {
        _keys = keys;
        _args = args;
    }

    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 16) return DetectionResult.Fail;
            var probe = data.Slice(0, 16).ToArray();
            var candidates = FunaiKeys.ResolveCandidates(_args, _keys, KeysId);
            foreach (uint key in candidates)
            {
                var plain = FunaiCrypto.Decrypt(probe, key);
                if (FunaiBdpParser.ProbeMatches(plain))
                {
                    // Parse the index from a head window only (full-file
                    // transform happens at extraction).
                    var image = data.ToArray();
                    long want = 0x28;
                    if (image.Length >= 0x28)
                    {
                        // Count sits in the second half of a file block;
                        // decrypt a block-aligned head so it is covered.
                        var headProbe = FunaiCrypto.Decrypt(image.AsSpan(0, 0x28).ToArray(), key);
                        uint count = BinaryPrimitives.ReadUInt32LittleEndian(headProbe.AsSpan(0x20, 4));
                        if (count == 0 || count > 100000) continue;
                        want = 0x20 + 4 + (long)count * FunaiBdpParser.IndexEntrySize;
                    }
                    long take = Math.Min(image.Length, ((want + 7) / 8) * 8);
                    var window = new byte[take];
                    Array.Copy(image, window, take);
                    var transformed = FunaiCrypto.Decrypt(window, key);
                    // ParseIndex validates offsets against the full length.
                    var padded = new byte[image.Length];
                    Array.Copy(transformed, padded, transformed.Length);
                    var index = FunaiBdpParser.ParseIndexForLength(padded, image.Length);
                    return new FunaiBdpDetectionResult(95, key, index);
                }
            }
            return DetectionResult.Fail;
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException)
        {
            return DetectionResult.Fail;
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class FunaiBdpExtractor : IFormatExtractor
{
    private readonly uint _key;
    private readonly List<FunaiBdpIndexEntry> _index;

    public FunaiBdpExtractor(FunaiBdpDetectionResult result)
    {
        _key = result.Key;
        _index = result.Index;
    }

    public static string SafeName(string name)
    {
        string flat = name.Replace('/', '_').Replace('\\', '_').Trim();
        return flat.Length == 0 ? "part" : flat;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var file = source.Data.ToArray();
        var image = FunaiCrypto.Decrypt(file, _key);
        var index = FunaiBdpParser.ParseIndex(image);
        foreach (var entry in index)
        {
            var blob = FunaiBdpParser.SliceBlob(image, entry);
            byte[] output;
            try
            {
                output = FunaiBdpParser.ProcessBlob(blob);
            }
            catch (Exception ex) when (ex is InvalidDataException)
            {
                throw new InvalidDataException($"BDP partition '{entry.Name}' failed to process: {ex.Message}", ex);
            }
            var artifact = ArtifactBuilder.Flat(source, SafeName(entry.Name) + ".bin", output);
            artifact.Flags |= DataSourceFlags.ProcessFurther;
            yield return artifact;
        }
    }
}
