#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Yafex.FileFormats.MstarSecure;

public record MstarSecureDetectionResult(int Confidence, MstarSecure.Chunk? Chunk) : DetectionResult(Confidence);

public class MstarSecureDetector : IFormatDetector
{
    private readonly KeysRepository _keys;

    public MstarSecureDetector(KeysRepository keys)
    {
        _keys = keys;
    }

    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < MstarSecure.TableOffset + MstarSecure.TableSize)
            {
                return DetectionResult.Fail;
            }
            // Head window only: the table sits at a fixed offset.
            int headLen = (int)Math.Min(data.Length, MstarSecure.TableOffset + MstarSecure.TableSize);
            var head = data.Slice(0, headLen).ToArray();
            var chunk = MstarSecure.ReadChunk(head, data.Length);
            if (chunk == null)
            {
                return DetectionResult.Fail;
            }
            if (TryValidate(data, chunk))
            {
                return new MstarSecureDetectionResult(95, chunk);
            }
            return new MstarSecureDetectionResult(85, chunk);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }

    private bool TryValidate(Smx.SharpIO.Memory.Buffers.Memory64<byte> data, MstarSecure.Chunk chunk)
    {
        if (!_keys.TryGetKeyCollection(MstarSecure.KeysId, out var keys))
        {
            return false;
        }
        return MstarSecure.FindValidKey(data.ToArray(), chunk, keys) != null;
    }
}

public class MstarSecureExtractor : IFormatExtractor
{
    private readonly KeysRepository _keys;
    private readonly MstarSecure.Chunk _chunk;

    public MstarSecureExtractor(KeysRepository keys, MstarSecure.Chunk chunk)
    {
        _keys = keys;
        _chunk = chunk;
    }

    public System.Collections.Generic.IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var image = source.Data.ToArray();
        var chunk = MstarSecure.ReadChunk(image, image.Length) ?? _chunk;
        var key = FindValidKey(image, chunk);
        if (key == null)
        {
            throw new InvalidDataException(
                $"MSTAR_SECURE image needs a working \"{MstarSecure.KeysId}\" key (staging secrets)");
        }
        var inner = MstarSecure.DecryptImage(image, chunk, key);
        var stem = System.IO.Path.GetFileNameWithoutExtension(source.Name ?? "mstar_secure");
        var artifact = ArtifactBuilder.Flat(source, $"{stem}.decrypted.bin", inner);
        artifact.Flags |= DataSourceFlags.ProcessFurther;
        yield return artifact;
    }

    private byte[]? FindValidKey(byte[] image, MstarSecure.Chunk chunk)
    {
        if (!_keys.TryGetKeyCollection(MstarSecure.KeysId, out var keys))
        {
            return null;
        }
        return MstarSecure.FindValidKey(image, chunk, keys);
    }
}
