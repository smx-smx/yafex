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

namespace Yafex.FileFormats.Tar;

/// <summary>
/// Standalone ustar archives (including ones nested inside firmware
/// containers): detected by header magic plus a full checksum-validated
/// walk, listed and extracted per member. Confidence stays below
/// container formats so embedded tars never steal their detection.
/// </summary>
public static class TarFormat
{
    public static YafexTarReader? OpenAt(byte[] image, long start)
    {
        try
        {
            var stream = new MemoryStream(image, (int)start, image.Length - (int)start, false);
            var tar = new YafexTarReader(stream);
            foreach (var _ in tar.Entries)
            {
                return tar;
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    public static long FindStart(byte[] image)
    {
        return Scan(image, pos => OpenAt(image, pos) != null);
    }

    /// <summary>
    /// First offset holding a valid ustar header (magic + checksum), without
    /// walking the archive: for sniffing a head window that ends mid-archive.
    /// </summary>
    public static long FindHeader(byte[] head)
    {
        return Scan(head, _ => true);
    }

    private static long Scan(byte[] image, Func<long, bool> accept)
    {
        long limit = Math.Min(image.Length - 512, 65536);
        for (long pos = 0; pos < limit; pos++)
        {
            if (YafexTarReader.IsHeader(image, (int)pos) && accept(pos))
            {
                return pos;
            }
        }
        return -1;
    }
}

public record TarDetectionResult(int Confidence, byte[]? Image, long Offset) : DetectionResult(Confidence);

public class TarDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < 1024)
            {
                return DetectionResult.Fail;
            }
            var bytes = data.ToArray();
            long start = TarFormat.FindStart(bytes);
            if (start < 0)
            {
                return DetectionResult.Fail;
            }
            return new TarDetectionResult(85, bytes, start);
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class TarExtractor : IFormatExtractor
{
    private readonly byte[] _image;
    private readonly long _offset;

    public TarExtractor(TarDetectionResult result)
    {
        _image = result.Image!;
        _offset = result.Offset;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var tar = TarFormat.OpenAt(_image, _offset);
        if (tar == null)
        {
            yield break;
        }
        foreach (var e in tar.Entries)
        {
            string flat = Support.PathNames.Flatten(e.Name);
            if (flat.Length == 0 || e.Length > int.MaxValue)
            {
                continue;
            }
            var content = new byte[(int)e.Length];
            using (var s = tar.OpenFile(e.Name))
            {
                int done = 0;
                while (done < content.Length)
                {
                    int n = s.Read(content, done, content.Length - done);
                    if (n <= 0)
                    {
                        break;
                    }
                    done += n;
                }
                if (done != content.Length)
                {
                    continue;
                }
            }
            yield return ArtifactBuilder.Flat(source, flat, content);
        }
    }
}
