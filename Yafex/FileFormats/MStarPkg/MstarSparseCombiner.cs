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

using Smx.SharpIO.Extensions;
using Smx.SharpIO.Memory.Buffers;

using Yafex.FileFormats.Sparse;
using Yafex.Support;

using Lz4Codec = Yafex.FileFormats.Lz4.Lz4;

namespace Yafex.FileFormats.MStarPkg;

/// <summary>Replays <see cref="MStarFlashPlan"/> groups the way the device
/// would: each segment (LZ4-decompressed when the script says so) is a
/// sparse image, applied in order with last-write-wins; don't-care chunks
/// leave previous content. Every step is validated (exact sizes, exact
/// chunk walks, shared geometry); any inconsistency aborts the group.</summary>
public static class MstarSparseCombiner
{
    private const long Window = 1 << 20;

    /// <summary>Decodes one flash segment to its sparse image bytes.</summary>
    public static byte[] DecodeSegment(IDataSource image, FlashSegment seg)
    {
        if (seg.FileOffset < 0 || seg.StoredSize <= 0
            || seg.FileOffset + seg.StoredSize > image.Data.Length)
        {
            throw new InvalidDataException("flash segment outside the image");
        }
        if (seg.StoredSize > int.MaxValue || seg.DecompressedSize > int.MaxValue)
        {
            throw new InvalidDataException("flash segment oversized");
        }
        var window = image.Data.Slice(seg.FileOffset, (int)seg.StoredSize).ToArray();
        if (!seg.Compressed)
        {
            return window;
        }
        var plain = new byte[(int)seg.DecompressedSize];
        int got;
        try
        {
            got = Lz4Codec.DecompressBlock(window, 0, window.Length, plain, 0, plain.Length);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("flash segment LZ4 decode failed", ex);
        }
        // The device stops once the stated output size is reached; any
        // trailing stored slack is never touched.
        if (got != plain.Length)
        {
            throw new InvalidDataException(
                $"flash segment decoded {got} bytes, expected {plain.Length}");
        }
        return plain;
    }

    /// <summary>Replays one flash group into <paramref name="sink"/>:
    /// decodes every segment (LZ4 when the script says so) and overlays the
    /// sparse images in order. Returns the raw image length.</summary>
    public static long Combine(IDataSource image, FlashGroup group, IArtifactBuffer sink)
    {
        if (group.Segments.Count == 0)
        {
            throw new InvalidDataException("nothing to combine");
        }
        var parts = new List<byte[]>();
        foreach (var seg in group.Segments)
        {
            parts.Add(DecodeSegment(image, seg));
        }
        return Overlay(parts, sink);
    }

    /// <summary>Overlays ordered sparse images into <paramref name="sink"/>
    /// and returns the raw image length.</summary>
    public static long Overlay(IList<byte[]> sparseImagesInOrder, IArtifactBuffer sink)
    {
        if (sparseImagesInOrder.Count == 0)
        {
            throw new InvalidDataException("nothing to combine");
        }
        var layouts = new List<SparseLayout>();
        foreach (var img in sparseImagesInOrder)
        {
            layouts.Add(SparseParser.Open(img)
                ?? throw new InvalidDataException("flash part is not a sparse image"));
        }
        uint blkSz = layouts[0].BlockSize;
        long totalBlks = layouts[0].TotalBlocks;
        foreach (var layout in layouts)
        {
            if (layout.BlockSize != blkSz || layout.TotalBlocks != totalBlks)
            {
                throw new InvalidDataException("flash parts describe different images");
            }
        }
        long total = totalBlks * blkSz;
        if (total <= 0)
        {
            throw new InvalidDataException("flash image has no content");
        }
        long writtenEnd = 0;
        var piece = new byte[Window];
        for (int s = 0; s < sparseImagesInOrder.Count; s++)
        {
            var img = sparseImagesInOrder[s];
            foreach (var c in layouts[s].Chunks)
            {
                if (c.Type == SparseParser.DontCare || c.Type == SparseParser.Crc32) continue;
                if (c.Type == SparseParser.Raw)
                {
                    long done = 0;
                    while (done < c.OutLength)
                    {
                        int take = (int)Math.Min(Window, c.OutLength - done);
                        Array.Copy(img, c.FileOffset + done, piece, 0, take);
                        sink.WriteAt(c.OutOffset + done, new Memory64<byte>(piece).Slice(0, take).AsReadonlySpan());
                        done += take;
                    }
                }
                else if (c.Type == SparseParser.Fill)
                {
                    var word = new byte[4];
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(word, c.Fill);
                    long done = 0;
                    while (done < c.OutLength)
                    {
                        int take = (int)Math.Min(Window, c.OutLength - done);
                        for (int i = 0; i < take; i++)
                        {
                            piece[i] = word[(c.OutOffset + done + i) % 4];
                        }
                        sink.WriteAt(c.OutOffset + done, new Memory64<byte>(piece).Slice(0, take).AsReadonlySpan());
                        done += take;
                    }
                }
                writtenEnd = Math.Max(writtenEnd, c.OutOffset + c.OutLength);
            }
        }
        if (writtenEnd < total)
        {
            // Trailing unwritten region (don't-care tail): extend with zeros.
            sink.WriteAt(total - 1, new Memory64<byte>(new byte[] { 0 }).AsReadonlySpan());
        }
        return total;
    }
}
