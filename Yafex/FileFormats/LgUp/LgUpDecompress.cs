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
using System.IO;
using System.IO.Compression;

namespace Yafex.FileFormats.LgUp;

/// <summary>Unit payload decompression per ECompressType. Ported from elix.</summary>
public static class LgUpDecompress
{
    public static byte[] Decompress(uint compressType, byte[] data)
    {
        switch (compressType)
        {
            case LgUpFormat.CompressNone:
                return data;
            case LgUpFormat.CompressZlib:
            {
                using var input = new MemoryStream(data);
                using var output = new MemoryStream();
                if (IsGzip(data))
                {
                    using var z = new GZipStream(input, CompressionMode.Decompress);
                    z.CopyTo(output);
                }
                else
                {
                    using var z = new ZLibStream(input, CompressionMode.Decompress);
                    z.CopyTo(output);
                }
                return output.ToArray();
            }
            case LgUpFormat.CompressZstd:
            {
                using var input = new MemoryStream(data);
                using var z = new ZstdSharp.DecompressionStream(input);
                using var output = new MemoryStream();
                z.CopyTo(output);
                return output.ToArray();
            }
            default:
                throw new InvalidDataException($"unsupported compress type {compressType}");
        }
    }

    public static bool IsGzip(byte[] d) => d.Length >= 2 && d[0] == 0x1F && d[1] == 0x8B;

    public static string ExtensionFor(byte[] d)
    {
        if (d.Length >= 8 && System.Text.Encoding.ASCII.GetString(d, 0, 8) == "BSDIFF40") return ".bsdiff";
        if (d.Length >= 8 && System.Text.Encoding.ASCII.GetString(d, 0, 8) == "IMGDIFF2") return ".imgdiff";
        if (IsGzip(d)) return ".gz";
        if (d.Length >= 2 && d[0] == 0x78) return ".zlib";
        return "";
    }
}
