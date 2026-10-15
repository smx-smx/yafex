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

namespace Yafex.FileFormats.MstarEcb;

public record MstarEcbDetectionResult(int Confidence) : DetectionResult(Confidence);

/// <summary>Fully encrypted image: no shape to match, detection is a
/// trial decrypt of the head with the staging key.</summary>
public class MstarEcbDetector : IFormatDetector
{
    private readonly KeysRepository _keys;

    public MstarEcbDetector(KeysRepository keys)
    {
        _keys = keys;
    }

    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < MstarEcb.ProbeLength)
            {
                return DetectionResult.Fail;
            }
            if (!_keys.TryGetKey(MstarEcb.KeysId, out var entry))
            {
                return DetectionResult.Fail;
            }
            if (entry.key.Length != 16 && entry.key.Length != 24 && entry.key.Length != 32)
            {
                return DetectionResult.Fail;
            }
            var image = data.ToArray();
            if (!MstarEcb.IsValidKey(image, entry.key))
            {
                return DetectionResult.Fail;
            }
            return new MstarEcbDetectionResult(95);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class MstarEcbExtractor : IFormatExtractor
{
    private readonly KeysRepository _keys;

    public MstarEcbExtractor(KeysRepository keys)
    {
        _keys = keys;
    }

    public System.Collections.Generic.IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var image = source.Data.ToArray();
        if (!_keys.TryGetKey(MstarEcb.KeysId, out var entry)
            || !MstarEcb.IsValidKey(image, entry.key))
        {
            throw new InvalidDataException(
                $"MSTAR_ECB image needs the \"{MstarEcb.KeysId}\" key (staging secrets)");
        }
        var inner = MstarEcb.Decrypt(image, entry.key);
        var stem = System.IO.Path.GetFileNameWithoutExtension(source.Name ?? "mstar_ecb");
        var artifact = ArtifactBuilder.Flat(source, $"{stem}.decrypted.bin", inner);
        artifact.Flags |= DataSourceFlags.ProcessFurther;
        yield return artifact;
    }
}
