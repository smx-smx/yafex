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
using System.Runtime.CompilerServices;

using Smx.SharpIO.Memory.Buffers;

using Yafex.Support;

namespace Yafex.FileFormats.AllwinnerImg;

public sealed record AwPart(string MainType, string SubType, string FileName, long Offset, long StoredLength, long OriginalLength);

/// <summary>Shared LiveSuit table walk: RC6-decrypts the file-entry table
/// with the entries key and parses v1/v3 entries. Payloads stay untouched.</summary>
public static class AllwinnerImgReader
{
    public static List<AwPart> ReadTable(Memory64<byte> data, AllwinnerImgContext ctx)
    {
        var parts = new List<AwPart>();
        long tableLen = (long)ctx.NumFiles * AwHeader.EntrySize;
        if (data.Length < AwHeader.EncryptedSize + tableLen) return parts;
        var table = AwCrypto.Decrypt(
            data.Slice(AwHeader.EncryptedSize, (int)tableLen).ToArray(),
            AwCrypto.EntriesKey());
        bool v3 = ctx.Version == AwHeader.Version3;
        for (long i = 0; i < ctx.NumFiles; i++)
        {
            int base_ = (int)(i * AwHeader.EntrySize);
            var entry = new ReadOnlySpan64<byte>(table).ReadStruct<AwEntry>(base_);
            string main = entry.MainType;
            string sub = entry.SubType;
            if (string.IsNullOrEmpty(sub)) continue;
            long stored, orig, off;
            string name;
            if (v3)
            {
                var e = new ReadOnlySpan64<byte>(table).ReadStruct<AwEntryV3>(base_ + 32);
                stored = e.StoredLength; orig = e.OriginalLength; off = e.Offset; name = e.FileName;
            }
            else
            {
                var e = new ReadOnlySpan64<byte>(table).ReadStruct<AwEntryV1>(base_ + 32);
                stored = e.StoredLength; orig = e.OriginalLength; off = e.Offset; name = e.FileName;
            }
            if (stored <= 0 || orig <= 0 || off < 0 || off + stored > data.Length) continue;
            parts.Add(new AwPart(main, sub, name, off, stored, orig));
        }
        return parts;
    }

    /// <summary>RC6-decrypts a stored payload region with the payload key
    /// and truncates to the original length (stored is 16-padded).</summary>
    public static byte[] DecryptPayload(Memory64<byte> data, AwPart part)
    {
        var cipher = data.Slice(part.Offset, (int)part.StoredLength).ToArray();
        var plain = AwCrypto.Decrypt(cipher, AwCrypto.PayloadKey());
        if (plain.Length > part.OriginalLength)
        {
            var trunc = new byte[part.OriginalLength];
            Array.Copy(plain, trunc, trunc.Length);
            return trunc;
        }
        return plain;
    }
}
