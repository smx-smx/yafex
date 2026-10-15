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
using Yafex.Support;
using Smx.SharpIO.Memory.Buffers;
using System.Collections.Generic;
using System.IO;

using Yafex.FileSystems.Ubi;

namespace Yafex.FileFormats.Ubi;

/// <summary>
/// UBI (Unsorted Block Images) volumes on raw NAND dumps: detected by
/// eraseblock magic plus CRC-validated headers, listed per volume, and
/// extracted as one payload per volume (LEB data concatenated in order).
/// Confidence stays below container formats so embedded regions never
/// steal their detection.
/// </summary>
public static class UbiFormat
{
    public static UbiReader? OpenAt(byte[] image, long start)
    {
        try
        {
            return UbiReader.Open(image, start);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Sliced pre-check for detection: a "UBI#" erase-counter header whose
    /// CRC validates, copying only the 64-byte header per candidate.
    /// </summary>
    public static bool HasValidEc(Memory64<byte> data)
    {
        foreach (long hit in DataScan.FindAll(data, "UBI#"u8.ToArray()))
        {
            if (UbiConstants.ValidEc(DataScan.Window(data, hit, UbiConstants.HeaderSize), 0)) return true;
        }
        return false;
    }

    public static long FindStart(byte[] image)
    {
        for (long pos = 0; pos + 64 <= image.Length; pos++)
        {
            if (image[pos] == (byte)'U' && image[pos + 1] == (byte)'B'
                && image[pos + 2] == (byte)'I' && image[pos + 3] == (byte)'#'
                && UbiConstants.ValidEc(image, pos) && OpenAt(image, pos) != null)
            {
                return pos;
            }
        }
        return -1;
    }
}

public record UbiDetectionResult(int Confidence, byte[]? Image, long Offset) : DetectionResult(Confidence);

public class UbiDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 2 * 512)
            {
                return DetectionResult.Fail;
            }
            if (!UbiFormat.HasValidEc(data))
            {
                return DetectionResult.Fail;
            }
            var bytes = data.ToArray();
            long start = UbiFormat.FindStart(bytes);
            if (start < 0)
            {
                return DetectionResult.Fail;
            }
            return new UbiDetectionResult(start == 0 ? 90 : 85, bytes, start);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class UbiExtractor : IFormatExtractor
{
    private readonly byte[] _image;
    private readonly long _offset;

    public UbiExtractor(UbiDetectionResult result)
    {
        _image = result.Image!;
        _offset = result.Offset;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var ubi = UbiFormat.OpenAt(_image, _offset);
        if (ubi == null)
        {
            yield break;
        }
        foreach (var v in ubi.Volumes)
        {
            // the layout (volume-table) volume is metadata, not payload
            if (v.VolId >= 0x7F00)
            {
                continue;
            }
            string flat = Support.PathNames.Flatten(v.Name);
            if (flat.Length == 0)
            {
                continue;
            }
            yield return ArtifactBuilder.Flat(source, flat, v.Payload());
        }
    }
}
