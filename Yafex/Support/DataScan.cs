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

using Smx.SharpIO.Memory.Buffers;

namespace Yafex.Support;

/// <summary>
/// Signature scans over (possibly memory-mapped, multi-GB) data through
/// slices: vectorized <see cref="MemoryExtensions.IndexOf{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>
/// on span views of the source, never a copy of the whole input. Detectors
/// find candidates here and copy only a small <see cref="Window"/> to validate.
/// </summary>
public static class DataScan
{
    /// <summary>Span views per step; any size up to int.MaxValue works.</summary>
    private const int StepSize = 256 << 20;

    /// <summary>
    /// Offsets of <paramref name="needle"/> at or after <paramref name="start"/>,
    /// ascending; with <paramref name="alignment"/> &gt; 1 only offsets that
    /// are multiples of it.
    /// </summary>
    public static IEnumerable<long> FindAll(Memory64<byte> data, byte[] needle, long start = 0, int alignment = 1)
    {
        long pos = Math.Max(0, start);
        while (true)
        {
            long hit = IndexOf(data, needle, pos);
            if (hit < 0) yield break;
            if (alignment <= 1 || hit % alignment == 0)
            {
                yield return hit;
            }
            pos = hit + 1;
        }
    }

    /// <summary>First offset of <paramref name="needle"/> at or after <paramref name="start"/>, or -1.</summary>
    public static long IndexOf(Memory64<byte> data, byte[] needle, long start = 0)
    {
        if (needle.Length == 0) throw new ArgumentException("empty needle", nameof(needle));
        long length = data.Length;
        long pos = Math.Max(0, start);
        while (pos + needle.Length <= length)
        {
            int count = (int)Math.Min(StepSize, length - pos);
            var view = (ReadOnlySpan<byte>)data.Slice(pos, count).Span;
            int i = view.IndexOf(needle);
            if (i >= 0) return pos + i;
            if (pos + count >= length) break;
            // overlap so a needle straddling the step boundary is found
            pos += count - (needle.Length - 1);
        }
        return -1;
    }

    /// <summary>Copy of up to <paramref name="count"/> bytes at <paramref name="offset"/>, clamped to the data.</summary>
    public static byte[] Window(Memory64<byte> data, long offset, long count)
    {
        if (offset < 0 || offset >= data.Length || count <= 0) return Array.Empty<byte>();
        count = Math.Min(count, data.Length - offset);
        return data.Slice(offset, count).ToArray();
    }
}
