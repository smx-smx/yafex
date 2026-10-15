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
using System.IO;

using Smx.SharpIO.Memory.Buffers;

namespace Yafex.FileFormats.Funai;

/// <summary>Variant D — MStar wrapper with a 0x1000 info block (leading or
/// trailing) plus three raw payloads. Unencrypted; the SoC payload is
/// normally itself a secure MStar image handled by nested extraction.</summary>
public sealed class FunaiMstarInfo
{
    public string FileCode = "";
    public string BrandName = "";
    public string ModelName = "";
    public string SocVersion = "";
    public string FrcVersion = "";
    public long SocSize;
    public long Frc60Size;
    public long Frc120Size;
    public bool Leading;
}

public record FunaiMstarDetectionResult(
    int Confidence,
    FunaiMstarInfo Info) : DetectionResult(Confidence);

public static class FunaiMstarParser
{
    public const int InfoSize = 0x1000;

    private static readonly byte[] InfoMagic = [(byte)'#', (byte)'@', (byte)'I', (byte)'N', (byte)'F', (byte)'O'];

    public static bool HasInfoMagic(byte[] block)
    {
        if (block.Length < InfoMagic.Length) return false;
        for (int i = 0; i < InfoMagic.Length; i++)
        {
            if (block[i] != InfoMagic[i]) return false;
        }
        return true;
    }

    public static FunaiMstarInfo? ParseBlock(byte[] block, bool leading)
    {
        if (!HasInfoMagic(block)) return null;
        int nul = Array.IndexOf(block, (byte)0);
        int textLen = nul >= 0 ? nul : block.Length;
        string text;
        try
        {
            text = System.Text.Encoding.ASCII.GetString(block, 0, textLen);
        }
        catch (Exception)
        {
            return null;
        }
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Split(['\r', '\n']))
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (line == "#@INFO" || line == "#@END") continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        string[] required = ["FileCode", "BrandName", "ModelName", "SoC_Version", "FRC_Version", "SoC_Size", "FRC60_Size", "FRC120_Size"];
        foreach (var key in required)
        {
            if (!values.ContainsKey(key)) return null;
        }
        if (!long.TryParse(values["SoC_Size"], out long soc) || soc < 0) return null;
        if (!long.TryParse(values["FRC60_Size"], out long f60) || f60 < 0) return null;
        if (!long.TryParse(values["FRC120_Size"], out long f120) || f120 < 0) return null;
        return new FunaiMstarInfo
        {
            FileCode = values["FileCode"],
            BrandName = values["BrandName"],
            ModelName = values["ModelName"],
            SocVersion = values["SoC_Version"],
            FrcVersion = values["FRC_Version"],
            SocSize = soc,
            Frc60Size = f60,
            Frc120Size = f120,
            Leading = leading,
        };
    }

    public static FunaiMstarInfo? Open(byte[] image)
    {
        if (image.Length < InfoSize) return null;
        var head = new byte[InfoSize];
        Array.Copy(image, 0, head, 0, InfoSize);
        if (HasInfoMagic(head))
        {
            var info = ParseBlock(head, true);
            if (info != null && LayoutFits(image.Length, info, 0x1000)) return info;
        }
        var tail = new byte[InfoSize];
        Array.Copy(image, image.Length - InfoSize, tail, 0, InfoSize);
        if (HasInfoMagic(tail))
        {
            var info = ParseBlock(tail, false);
            if (info != null && LayoutFits(image.Length, info, 0)) return info;
        }
        return null;
    }

    public static bool LayoutFits(long totalLength, FunaiMstarInfo info, long dataOffset)
    {
        // The file holds the 0x1000 info block plus the three payloads;
        // only the info placement differs (leading: payloads at 0x1000,
        // trailing: payloads at 0).
        if (info.Leading && dataOffset != InfoSize) return false;
        if (!info.Leading && dataOffset != 0) return false;
        long want = info.SocSize + info.Frc60Size + info.Frc120Size + InfoSize;
        return want == totalLength;
    }

    public static long DataOffset(FunaiMstarInfo info) => info.Leading ? InfoSize : 0;
}

public class FunaiMstarDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < FunaiMstarParser.InfoSize) return DetectionResult.Fail;
            // Head window only for the leading test; tail for the trailing one.
            var head = data.Slice(0, FunaiMstarParser.InfoSize).ToArray();
            FunaiMstarInfo? info = null;
            if (FunaiMstarParser.HasInfoMagic(head))
            {
                info = FunaiMstarParser.ParseBlock(head, true);
                if (info != null && !FunaiMstarParser.LayoutFits(data.Length, info, FunaiMstarParser.InfoSize))
                {
                    info = null;
                }
            }
            if (info == null && data.Length >= FunaiMstarParser.InfoSize)
            {
                var tail = data.Slice(data.Length - FunaiMstarParser.InfoSize, FunaiMstarParser.InfoSize).ToArray();
                if (FunaiMstarParser.HasInfoMagic(tail))
                {
                    var candidate = FunaiMstarParser.ParseBlock(tail, false);
                    if (candidate != null && FunaiMstarParser.LayoutFits(data.Length, candidate, 0))
                    {
                        info = candidate;
                    }
                }
            }
            if (info == null) return DetectionResult.Fail;
            // Exact structural fit (magic plus all required keys, decimal
            // sizes and the length equation), not mere magic: outranks the
            // whole-image ECB/script heuristics that also match a trailing
            // wrapper's SoC bytes at offset 0. The SoC payload then recurses
            // into the inner image via ProcessFurther.
            return new FunaiMstarDetectionResult(96, info);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class FunaiMstarExtractor : IFormatExtractor
{
    private readonly FunaiMstarInfo _info;

    public FunaiMstarExtractor(FunaiMstarDetectionResult result)
    {
        _info = result.Info;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var data = source.Data;
        long dataOff = FunaiMstarParser.DataOffset(_info);
        string baseDir = ArtifactBuilder.VersionedDir(source, Support.PathNames.Flatten(_info.FileCode));
        source.AddMetadata(new Metadata.BaseDirectoryPath(baseDir));

        var payloads = new (string Name, long Size, bool Nested)[]
        {
            ("SoC", _info.SocSize, true),
            ("FRC60", _info.Frc60Size, false),
            ("FRC120", _info.Frc120Size, false),
        };
        long pos = dataOff;
        foreach (var (name, size, nested) in payloads)
        {
            if (size == 0) continue;
            if (pos + size > data.Length)
            {
                throw new InvalidDataException($"Funai MStar payload '{name}' runs past the image");
            }
            var bytes = data.Slice(pos, (int)size).ToArray();
            pos += size;
            IDataSource artifact;
            if (nested)
            {
                string socDir = System.IO.Path.Combine(baseDir, "SoC");
                artifact = ArtifactBuilder.FlatIn(source, socDir, $"{name}.bin", bytes);
                artifact.Flags |= DataSourceFlags.ProcessFurther;
            }
            else
            {
                artifact = ArtifactBuilder.FlatIn(source, baseDir, $"{name}.bin", bytes);
            }
            yield return artifact;
        }
    }
}
