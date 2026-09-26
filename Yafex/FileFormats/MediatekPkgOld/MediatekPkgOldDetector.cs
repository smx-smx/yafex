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
using System.Text;

using Smx.SharpIO.Memory.Buffers;

using Yafex.Support;

namespace Yafex.FileFormats.MediatekPkgOld;

public record MediatekPkgOldDetectionResult(int Confidence, MediatekPkgOldContext? Context) : DetectionResult(Confidence);

public class MediatekPkgOldContext
{
    public long HeaderOffset { get; set; }
    public MtkOldHeader Header { get; set; }
}

/// <summary>MTK PKG old variant (Feistel header crypto, 4-byte keys).
/// Header may sit at 0, 64 (Sony/Philips extra) or 128 (Philips extra).</summary>
public class MediatekPkgOldDetector : IFormatDetector
{
    private const string HEADER_KEY_ID = "mtkpkg-old-header-key";

    private static readonly long[] HeaderOffsets = [0, 64, 128];

    private readonly KeysRepository _keys;
    private readonly MediatekPkgOldContext _ctx = new();

    public MediatekPkgOldDetector(KeysRepository keys)
    {
        _keys = keys;
    }

    private static bool IsDecryptedHeader(ReadOnlySpan64<byte> bytes)
    {
        if (bytes.Length < MtkOldHeader.SIZE) return false;
        var magic = bytes.Slice(4, 8).ToArray();
        return Encoding.ASCII.GetString(magic) == MtkOldHeader.MTK_MAGIC;
    }

    public DetectionResult Detect(IDataSource source)
    {
        var data = source.Data;

        if (!_keys.TryGetRaw(HEADER_KEY_ID, out var key, out var iv)
            || key.Length != 4 || iv.Length != 4)
        {
            return new MediatekPkgOldDetectionResult(0, null);
        }

        foreach (var off in HeaderOffsets)
        {
            if (data.Length < off + MtkOldHeader.SIZE) continue;
            var cipher = data.Slice(off, MtkOldHeader.SIZE).ToArray();
            byte[] plain;
            try
            {
                plain = MtkOldCrypto.Decrypt(cipher, key, iv);
            }
            catch (ArgumentException)
            {
                continue;
            }
            if (!IsDecryptedHeader(new ReadOnlySpan64<byte>(plain))) continue;

            _ctx.HeaderOffset = off;
            _ctx.Header = new ReadOnlySpan64<byte>(plain).ReadStruct<MtkOldHeader>();
            return new MediatekPkgOldDetectionResult(100, _ctx);
        }

        return new MediatekPkgOldDetectionResult(0, null);
    }
}
