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
using System.Runtime.CompilerServices;

using Smx.SharpIO;
using Smx.SharpIO.Memory.Buffers;

using Yafex.Support;

namespace Yafex.FileFormats.AllwinnerImg;

public record AllwinnerImgDetectionResult(int Confidence, AllwinnerImgContext? Context) : DetectionResult(Confidence);

public class AllwinnerImgContext
{
    public uint Version { get; set; }
    public uint NumFiles { get; set; }
    public uint ImageSize { get; set; }
}

/// <summary>Classic LiveSuit image detector: RC6-decrypts the first 1024
/// bytes with the header key and validates the IMAGEWTY magic, the header
/// version (0x100/0x300) and a sane file count. Unencrypted (A31+)
/// IMAGEWTY images are accepted by magic directly.</summary>
public class AllwinnerImgDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        var data = source.Data;
        if (data.Length < AwHeader.EncryptedSize) return new AllwinnerImgDetectionResult(0, null);

        var head = data.Slice(0, AwHeader.EncryptedSize).ToArray();
        if (IsHeader(head, out var ctx)) return new AllwinnerImgDetectionResult(100, ctx);

        var plain = AwCrypto.Decrypt(head, AwCrypto.HeaderKey());
        if (IsHeader(plain, out ctx)) return new AllwinnerImgDetectionResult(100, ctx);

        return new AllwinnerImgDetectionResult(0, null);
    }

    private static bool IsHeader(byte[] head, out AllwinnerImgContext? ctx)
    {
        ctx = null;
        if (head.Length < Unsafe.SizeOf<AwHeader>()) return false;
        var span = new ReadOnlySpan64<byte>(head);
        var hdr = span.ReadStruct<AwHeader>();
        if (hdr.MagicString != AwHeader.Magic) return false;
        if (hdr.HeaderVersion != AwHeader.Version1 && hdr.HeaderVersion != AwHeader.Version3) return false;
        // v3 shifts the id block by one word; num_files sits at the same
        // relative slot in both layouts
        long idOff = hdr.HeaderVersion == AwHeader.Version3 ? 36 : 32;
        uint numFiles = span.ReadStruct<AwIdBlockV1>(idOff).NumFiles;
        if (numFiles == 0 || numFiles > 100000) return false;
        if (hdr.ImageSize > (16L << 30)) return false;
        ctx = new AllwinnerImgContext
        {
            Version = hdr.HeaderVersion,
            NumFiles = numFiles,
            ImageSize = hdr.ImageSize,
        };
        return true;
    }
}
