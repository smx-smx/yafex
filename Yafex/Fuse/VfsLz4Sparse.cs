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

using Yafex.FileFormats.Lz4;
using Yafex.FileFormats.Sparse;

namespace Yafex.Fuse;

/// <summary>Mount expander for raw-LZ4-block-compressed Android sparse images
/// (MStar <c>lz4 -d …</c> + <c>sparse_write</c> payloads): decompresses the
/// block, then chains into sparse expansion. Raw LZ4 blocks carry no magic,
/// so matching is trial-decompression (bounded) and never throws.</summary>
public sealed class Lz4SparseExpander : IContentExpander
{
    public string Name => "lz4sparse";

    /// <summary>Maximum compressed input accepted for trial-decompression.</summary>
    public const long MaxInput = 256 << 20;

    /// <summary>Maximum decompressed output (grown geometrically to this cap).</summary>
    public const long MaxOutput = 2L << 30;

    public bool Matches(IVirtualFile file)
    {
        try
        {
            return TryDecompress(file, out _);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Trial-decompresses <paramref name="file"/> and validates the
    /// result as sparse. Never throws; false on any failure.</summary>
    public static bool TryDecompress(IVirtualFile file, out byte[] image)
    {
        image = [];
        try
        {
            if (file.Length < 16 || file.Length > MaxInput) return false;
            var src = file.ReadAvailable();
            var dst = DecompressBounded(src);
            if (dst == null || !SparseExpander.TryParse(dst, out _)) return false;
            image = dst;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Raw-LZ4-block decompression with a geometric output cap
    /// (<see cref="MaxOutput"/>). Returns null unless the whole input is
    /// consumed into a non-exact buffer.</summary>
    public static byte[]? DecompressBounded(byte[] src)
    {
        // Output size is unknown upfront: grow on overrun reports only;
        // any other failure means "not an LZ4 block".
        long cap = Math.Min(4L * src.Length + 16, MaxOutput);
        while (true)
        {
            var dst = new byte[(int)cap];
            try
            {
                int got = Lz4.DecompressBlock(src, 0, src.Length, dst, 0, dst.Length, out int consumed);
                if (consumed != src.Length) return null;
                if (got == dst.Length) return null;
                Array.Resize(ref dst, got);
                return dst;
            }
            catch (InvalidDataException ex) when (IsOverrun(ex) && cap < MaxOutput)
            {
                cap = Math.Min(cap * 2, MaxOutput);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private static bool IsOverrun(InvalidDataException ex)
        => ex.Message.Contains("overrun", StringComparison.Ordinal);

    public IVfsNode Expand(IVirtualFile file, ICollection<object> keepAlive)
    {
        return new LazyVfsDirectory(file.Name, Helpers.OctalLiteral(0755), () =>
        {
            if (!TryDecompress(file, out var image))
            {
                throw new InvalidDataException("not an LZ4-compressed sparse image");
            }
            var nodes = new List<IVfsNode>();
            var blob = new MemoryVirtualFile(file.Name + ".sparse", image);
            VfsBuilder.AddWithNested(nodes, blob, keepAlive);
            return nodes;
        });
    }
}
