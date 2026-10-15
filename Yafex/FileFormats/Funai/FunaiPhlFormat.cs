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

namespace Yafex.FileFormats.Funai;

/// <summary>Variant B — UPG-PHL with a fixed 8-slot header. Bodies are
/// concatenated in slot order with no per-entry trailer; slots with type
/// 0xFFFF and size 0 are skipped.</summary>
public sealed class FunaiPhlSlot
{
    public ushort BodyType;
    public uint BodySize;
    public long BodyOffset;
}

public record FunaiPhlDetectionResult(
    int Confidence,
    List<FunaiPhlSlot> Slots,
    uint? Key) : DetectionResult(Confidence);

public class FunaiPhlDetector : IFormatDetector
{
    public const string KeysId = "funai-upg-keys";

    private readonly KeysRepository _keys;
    private readonly IDictionary<string, string> _args;

    public FunaiPhlDetector(KeysRepository keys, IDictionary<string, string> args)
    {
        _keys = keys;
        _args = args;
    }

    internal static List<FunaiPhlSlot>? WalkSlots(byte[] image)
    {
        if (image.Length < 64) return null;
        if (image[0] != (byte)'U' || image[1] != (byte)'P' || image[2] != (byte)'G'
            || image[3] != 0 || image[4] != 0 || image[5] != 0
            || image[6] != 0 || image[7] != 0) return null;
        var slots = new List<FunaiPhlSlot>();
        long pos = 64;
        for (int i = 0; i < 8; i++)
        {
            int off = 6 + i * 6;
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(off, 2));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(off + 2, 4));
            if (type == 0xFFFF && size == 0) continue;
            if (size > (ulong)image.Length) return null;
            if (pos + (long)size > image.Length) return null;
            slots.Add(new FunaiPhlSlot { BodyType = type, BodySize = size, BodyOffset = pos });
            pos += size;
        }
        if (pos != image.Length) return null;
        if (slots.Count == 0) return null;
        // Observed files always carry slot 0 as descriptor type 0.
        if (slots[0].BodyType != 0) return null;
        return slots;
    }

    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 64) return DetectionResult.Fail;
            var image = data.ToArray();
            var slots = WalkSlots(image);
            if (slots == null) return DetectionResult.Fail;

            var descriptor = slots[0];
            var candidates = FunaiKeys.ResolveCandidates(_args, _keys, KeysId);
            var probe = new byte[(int)Math.Min(16, descriptor.BodySize)];
            Array.Copy(image, descriptor.BodyOffset, probe, 0, probe.Length);
            foreach (uint key in candidates)
            {
                var plain = FunaiCrypto.Decrypt(probe, key);
                int test = (int)Math.Min(16, plain.Length);
                if (FunaiCrypto.IsRestrictedSpan(plain.AsSpan(0, test)))
                {
                    return new FunaiPhlDetectionResult(95, slots, key);
                }
            }
            return new FunaiPhlDetectionResult(85, slots, null);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class FunaiPhlExtractor : IFormatExtractor
{
    private readonly List<FunaiPhlSlot> _slots;
    private readonly uint? _key;

    public FunaiPhlExtractor(FunaiPhlDetectionResult result)
    {
        _slots = result.Slots;
        _key = result.Key;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var image = source.Data.ToArray();
        var merged = new Dictionary<ushort, byte[]>();
        var order = new List<ushort>();
        foreach (var s in _slots)
        {
            var body = new byte[s.BodySize];
            Array.Copy(image, s.BodyOffset, body, 0, body.Length);
            if (_key.HasValue)
            {
                body = FunaiCrypto.Decrypt(body, _key.Value);
            }
            if (!merged.ContainsKey(s.BodyType)) order.Add(s.BodyType);
            merged[s.BodyType] = body;
        }
        foreach (ushort type in order)
        {
            var artifact = ArtifactBuilder.Flat(source, $"{type}.bin", merged[type]);
            artifact.Flags |= DataSourceFlags.ProcessFurther;
            yield return artifact;
        }
    }
}
