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
using System.IO;

namespace Yafex;

/// <summary>
/// A lazily-readable virtual file: length is known upfront, content is produced
/// on demand when <see cref="OpenRead"/> is called (decrypting/transforming only
/// the requested ranges). The passthrough pipeline primitive: chained virtual
/// files never touch disk or RAM beyond the read window.
/// </summary>
public interface IVirtualFile
{
    string Name { get; }
    long Length { get; }
    Stream OpenRead();
}

/// <summary>
/// Buffering helpers for parsers that need bytes rather than a stream.
/// </summary>
public static class VirtualFileExtensions
{
    /// <summary>
    /// Reads up to <paramref name="maxBytes"/> from the start. Lenient: a
    /// shorter file or short read returns what was available (probe windows).
    /// </summary>
    public static byte[] ReadHead(this IVirtualFile file, long maxBytes)
    {
        using var s = file.OpenRead();
        long count = Math.Min(s.Length, maxBytes);
        if (count > Array.MaxLength)
        {
            throw new InvalidDataException($"'{file.Name}' too large to buffer");
        }
        var buf = new byte[count];
        int got = s.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
        return got == buf.Length ? buf : buf[..got];
    }

    /// <summary>Whole content, lenient like <see cref="ReadHead"/>.</summary>
    public static byte[] ReadAvailable(this IVirtualFile file) => file.ReadHead(long.MaxValue);

    /// <summary>
    /// Whole content, strict: throws <see cref="InvalidDataException"/> when
    /// the file is too large to buffer or the stream ends early.
    /// </summary>
    public static byte[] ReadAllBytes(this IVirtualFile file)
    {
        using var s = file.OpenRead();
        if (s.Length > Array.MaxLength)
        {
            throw new InvalidDataException($"'{file.Name}' too large to buffer");
        }
        var buf = new byte[s.Length];
        if (s.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false) != buf.Length)
        {
            throw new InvalidDataException($"truncated '{file.Name}'");
        }
        return buf;
    }
}
