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
using System.Linq;

using Smx.SharpIO.Memory.Buffers;

namespace Yafex.FileFormats.Funai;

/// <summary>Variant A — UPG with a variable entry count. Headers and
/// bodies are interleaved: an 8-byte entry header, then payload plus a
/// 4-byte CRC and 64-byte hash trailer, repeated N times.</summary>
public sealed class FunaiUpgEntry
{
    public ushort Type;
    public uint EntrySize;
    public byte Unknown;
    public byte Encrypted;
    public long PayloadOffset;
    public int PayloadLength;
}

public record FunaiUpgDetectionResult(
    int Confidence,
    List<FunaiUpgEntry> Entries,
    uint? Key) : DetectionResult(Confidence);

public class FunaiUpgDetector : IFormatDetector
{
    public const string KeysId = "funai-upg-keys";

    private readonly KeysRepository _keys;
    private readonly IDictionary<string, string> _args;

    public FunaiUpgDetector(KeysRepository keys, IDictionary<string, string> args)
    {
        _keys = keys;
        _args = args;
    }

    private static bool HasMagic(ReadOnlySpan<byte> head)
    {
        return head.Length >= 8
            && head[0] == (byte)'U' && head[1] == (byte)'P' && head[2] == (byte)'G'
            && head[3] == 0 && head[4] == 0 && head[5] == 0;
    }

    internal static List<FunaiUpgEntry>? WalkEntries(byte[] image)
    {
        if (image.Length < 12) return null;
        if (image[0] != (byte)'U' || image[1] != (byte)'P' || image[2] != (byte)'G'
            || image[3] != 0 || image[4] != 0 || image[5] != 0) return null;
        ushort n = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(6, 2));
        if (n == 0) return null;
        // Total file size at 8 is informational, never validated.
        var entries = new List<FunaiUpgEntry>();
        long pos = 12;
        for (int i = 0; i < n; i++)
        {
            if (pos + 8 > image.Length) return null;
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan((int)pos, 2));
            uint es = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan((int)pos + 2, 4));
            byte unk = image[pos + 6];
            byte enc = image[pos + 7];
            if (enc != 0 && enc != 1) return null;
            if (es < 70) return null;
            long payloadLen = (long)es - 70;
            if (pos + 8 + payloadLen + 68 > image.Length) return null;
            entries.Add(new FunaiUpgEntry
            {
                Type = type,
                EntrySize = es,
                Unknown = unk,
                Encrypted = enc,
                PayloadOffset = pos + 8,
                PayloadLength = (int)payloadLen,
            });
            pos += 8 + payloadLen + 68;
        }
        if (pos != image.Length) return null;
        return entries;
    }

    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 12) return DetectionResult.Fail;
            var head = data.Slice(0, (int)Math.Min(8, data.Length)).ToArray();
            if (!HasMagic(head)) return DetectionResult.Fail;
            var image = data.ToArray();
            var entries = WalkEntries(image);
            if (entries == null) return DetectionResult.Fail;

            var probe = entries.FirstOrDefault(e => e.Type == 0 && e.Encrypted == 1);
            if (probe == null)
            {
                return new FunaiUpgDetectionResult(85, entries, null);
            }
            var candidates = FunaiKeys.ResolveCandidates(_args, _keys, KeysId);
            var payload = new byte[probe.PayloadLength];
            Array.Copy(image, probe.PayloadOffset, payload, 0, payload.Length);
            foreach (uint key in candidates)
            {
                var plain = FunaiCrypto.Decrypt(payload, key);
                if (FunaiCrypto.IsRestrictedSpan(plain))
                {
                    return new FunaiUpgDetectionResult(95, entries, key);
                }
            }
            return new FunaiUpgDetectionResult(85, entries, null);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class FunaiUpgExtractor : IFormatExtractor
{
    private readonly List<FunaiUpgEntry> _entries;
    private readonly uint? _key;

    public FunaiUpgExtractor(FunaiUpgDetectionResult result)
    {
        _entries = result.Entries;
        _key = result.Key;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var image = source.Data.ToArray();
        // Duplicate types overwrite in iteration order (last wins).
        var merged = new Dictionary<ushort, byte[]>();
        var order = new List<ushort>();
        foreach (var e in _entries)
        {
            var payload = new byte[e.PayloadLength];
            Array.Copy(image, e.PayloadOffset, payload, 0, payload.Length);
            if (e.Encrypted == 1 && _key.HasValue)
            {
                payload = FunaiCrypto.Decrypt(payload, _key.Value);
            }
            if (!merged.ContainsKey(e.Type)) order.Add(e.Type);
            merged[e.Type] = payload;
        }
        foreach (ushort type in order)
        {
            var artifact = ArtifactBuilder.Flat(source, $"{type}.bin", merged[type]);
            artifact.Flags |= DataSourceFlags.ProcessFurther;
            yield return artifact;
        }
    }
}
