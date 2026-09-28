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
using System.Text;

namespace Yafex.FileFormats.Nfsb;

/// <summary>
/// LG NFSB wrapper: a 0x1000-byte header (magic + checksum-algorithm marker)
/// prepended to the real payload (usually squashfs). Stripping the header
/// exposes the inner image.
/// </summary>
public static class Nfsb
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("NFSB");
    public const int HeaderSize = 0x1000;

    private static readonly (int Offset, string Algo)[] Markers = new[]
    {
        (0x0E, "md5"), (0x1A, "md5"),
        (0x0E, "sha256"), (0x1A, "sha256"),
    };

    public static bool HasMagic(byte[] prefix)
    {
        if (prefix.Length < Magic.Length)
        {
            return false;
        }
        for (int i = 0; i < Magic.Length; i++)
        {
            if (prefix[i] != Magic[i])
            {
                return false;
            }
        }
        foreach (var (offset, algo) in Markers)
        {
            if (prefix.Length >= offset + algo.Length)
            {
                bool ok = true;
                for (int i = 0; i < algo.Length; i++)
                {
                    if (prefix[offset + i] != (byte)algo[i])
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                {
                    return true;
                }
            }
        }
        return false;
    }

    public static byte[] Strip(byte[] image)
    {
        if (!HasMagic(image))
        {
            throw new InvalidDataException("not an NFSB image");
        }
        if (image.Length <= HeaderSize)
        {
            throw new InvalidDataException("truncated NFSB image");
        }
        var inner = new byte[image.Length - HeaderSize];
        Buffer.BlockCopy(image, HeaderSize, inner, 0, inner.Length);
        return inner;
    }
}
