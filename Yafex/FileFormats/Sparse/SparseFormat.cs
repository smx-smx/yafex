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

namespace Yafex.FileFormats.Sparse;

/// <summary>Android sparse image (simg): a 28-byte header plus a chunk walk
/// (raw / fill / don't-care / crc32) expanding to total_blks * blk_sz bytes.
/// Integrity fields (image checksum, crc32 chunks) are read and ignored.</summary>
public sealed class SparseChunk
{
    public ushort Type;
    /// <summary>Byte offset of this chunk's output in the raw image.</summary>
    public long OutOffset;
    /// <summary>Output length in bytes (chunk_sz * blk_sz).</summary>
    public long OutLength;
    /// <summary>Payload offset in the sparse file (raw bytes / fill word).</summary>
    public long FileOffset;
    public uint Fill;
}

public sealed class SparseLayout
{
    public uint BlockSize;
    public long TotalBlocks;
    public long OutputLength;
    public List<SparseChunk> Chunks = new();
}

public static class SparseParser
{
    public const uint Magic = 0xED26FF3A;

    public const ushort Raw = 0xCAC1;
    public const ushort Fill = 0xCAC2;
    public const ushort DontCare = 0xCAC3;
    public const ushort Crc32 = 0xCAC4;

    public const int HeaderSize = 28;
    public const int ChunkHeaderSize = 12;

    private static bool IsPow2(uint v) => v != 0 && (v & (v - 1)) == 0;

    /// <summary>Validates the header and walks every chunk. Chunks must
    /// account for exactly total_blks with no gaps/overlaps, and the stream
    /// must end exactly at EOF.</summary>
    public static SparseLayout? Open(byte[] image)
    {
        if (image.Length < HeaderSize) return null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(0, 4)) != Magic) return null;
        ushort major = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(4, 2));
        ushort minor = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(6, 2));
        if (major != 1 || minor != 0) return null;
        if (BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(8, 2)) != HeaderSize) return null;
        if (BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(10, 2)) != ChunkHeaderSize) return null;
        uint blkSz = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(12, 4));
        if (!IsPow2(blkSz) || blkSz < 512 || blkSz > 65536) return null;
        uint totalBlks = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(16, 4));
        uint totalChunks = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(20, 4));
        if (totalChunks == 0 || totalChunks > 100_000_000) return null;
        // Image checksum at +24 is read and ignored.

        var layout = new SparseLayout
        {
            BlockSize = blkSz,
            TotalBlocks = totalBlks,
            OutputLength = (long)totalBlks * blkSz,
        };
        long at = HeaderSize;
        long outPos = 0;
        for (long i = 0; i < totalChunks; i++)
        {
            if (at + ChunkHeaderSize > image.Length) return null;
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan((int)at, 2));
            uint chunkSz = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)at + 4, 4));
            uint totalSz = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)at + 8, 4));
            long outLen = (long)chunkSz * blkSz;
            switch (type)
            {
                case Raw:
                    if (totalSz != ChunkHeaderSize + outLen) return null;
                    break;
                case Fill:
                case Crc32:
                    if (totalSz != ChunkHeaderSize + 4) return null;
                    break;
                case DontCare:
                    if (totalSz != ChunkHeaderSize) return null;
                    break;
                default:
                    return null;
            }
            if (at + totalSz > image.Length) return null;
            uint fill = (type == Fill || type == Crc32)
                ? BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)at + 12, 4))
                : 0;
            layout.Chunks.Add(new SparseChunk
            {
                Type = type,
                OutOffset = outPos,
                OutLength = outLen,
                FileOffset = type == Raw ? at + ChunkHeaderSize
                    : type == Fill || type == Crc32 ? at + ChunkHeaderSize : at,
                Fill = fill,
            });
            at += totalSz;
            outPos += type == Crc32 ? 0 : outLen;
        }
        if (outPos != layout.OutputLength) return null;
        if (at != image.Length) return null;
        return layout;
    }

}

public record SparseDetectionResult(
    int Confidence,
    SparseLayout Layout) : DetectionResult(Confidence);

public class SparseDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < SparseParser.HeaderSize) return DetectionResult.Fail;
            var image = data.ToArray();
            var layout = SparseParser.Open(image);
            if (layout == null) return DetectionResult.Fail;
            return new SparseDetectionResult(90, layout);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class SparseExtractor : IFormatExtractor
{
    private readonly SparseLayout _layout;

    public SparseExtractor(SparseDetectionResult result)
    {
        _layout = result.Layout;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var image = source.Data.ToArray();
        var layout = SparseParser.Open(image)
            ?? throw new InvalidDataException("Sparse image no longer parses cleanly");
        var stem = System.IO.Path.GetFileNameWithoutExtension(source.Name ?? "sparse");
        var baseDir = ArtifactBuilder.VersionedDir(source, stem);
        source.AddMetadata(new Metadata.BaseDirectoryPath(baseDir));
        var fileName = $"{stem}.raw.bin";
        var buff = ArtifactBuilder.BeginBuffer(
            baseDir, fileName, DataSourceFlags.ProcessFurther, SpoolHint.Auto);
        try
        {
            // Stream chunk by chunk so multi-GB outputs never sit fully in RAM.
            var zero = new byte[1 << 20];
            var word = new byte[4];
            foreach (var c in layout.Chunks)
            {
                switch (c.Type)
                {
                    case SparseParser.Raw:
                    {
                        long done = 0;
                        while (done < c.OutLength)
                        {
                            int take = (int)Math.Min(1 << 20, c.OutLength - done);
                            buff.Write(image.AsSpan((int)(c.FileOffset + done), take).ToArray());
                            done += take;
                        }
                        break;
                    }
                    case SparseParser.Fill:
                        BinaryPrimitives.WriteUInt32LittleEndian(word, c.Fill);
                        long filled = 0;
                        while (filled < c.OutLength)
                        {
                            int take = (int)Math.Min(zero.Length, c.OutLength - filled);
                            for (int i = 0; i < take; i += 4)
                            {
                                int w = Math.Min(4, take - i);
                                Array.Copy(word, 0, zero, i, w);
                            }
                            buff.Write(zero.AsSpan(0, take).ToArray());
                            filled += take;
                        }
                        break;
                    case SparseParser.DontCare:
                    {
                        Array.Clear(zero, 0, zero.Length);
                        long skipped = 0;
                        while (skipped < c.OutLength)
                        {
                            int take = (int)Math.Min(zero.Length, c.OutLength - skipped);
                            buff.Write(zero.AsSpan(0, take).ToArray());
                            skipped += take;
                        }
                        break;
                    }
                    case SparseParser.Crc32:
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException || ex is ArgumentException)
        {
            throw new InvalidDataException("Sparse image truncated mid-chunk", ex);
        }
        yield return ArtifactBuilder.Complete(buff, source, baseDir);
    }
}
