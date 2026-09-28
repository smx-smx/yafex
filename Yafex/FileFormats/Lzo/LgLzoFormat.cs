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
using System.IO;
using System.Linq;

namespace Yafex.FileFormats.Lzo;

public record LgLzoDetectionResult(int Confidence, LgLzoContext? Context) : DetectionResult(Confidence);

public sealed class LgLzoContext
{
    public LgLzo.StreamHeader Header = null!;
    public long TotalOut;
    public int BlockCount;
}

public class LgLzoDetector : IFormatDetector
{
    public DetectionResult Detect(IDataSource source)
    {
        try
        {
            var data = source.Data;
            if (data.Length < LgLzo.HeaderSize) return DetectionResult.Fail;
            var prefix = data.Slice(0, LgLzo.Magic.Length).ToArray();
            if (!LgLzo.HasMagic(prefix)) return DetectionResult.Fail;
            var bytes = data.ToArray();
            var (header, _) = LgLzo.ReadHeader(bytes);
            var (blocks, _, totalOut) = LgLzo.ReadBlockTable(bytes);
            return new LgLzoDetectionResult(90, new LgLzoContext
            {
                Header = header,
                TotalOut = totalOut,
                BlockCount = blocks.Count,
            });
        }
        catch (Exception)
        {
            return DetectionResult.Fail;
        }
    }
}

public class LgLzoLister : IFormatLister
{
    private readonly LgLzoContext _ctx;

    public LgLzoLister(LgLzoDetectionResult result)
    {
        _ctx = result.Context!;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        var h = _ctx.Header;
        output.WriteLine($"LgLzo (LG LZO stream, {source.Name}):");
        output.WriteLine($"  declared={h.DecompressedSize} blocks={_ctx.BlockCount}" +
            $" mapped={_ctx.TotalOut} blocksize={h.BlockSize} method={h.Method} level={h.Level}" +
            $" checksum={((h.Flags & 1) != 0 ? "adler32" : "none")}");
    }
}

public class LgLzoExtractor : IFormatExtractor
{
    private readonly LgLzoContext _ctx;

    public LgLzoExtractor(LgLzoDetectionResult result)
    {
        _ctx = result.Context!;
    }

    public System.Collections.Generic.IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var bytes = source.Data.ToArray();
        var plain = LgLzo.DecompressAll(bytes);
        if (plain.Length != _ctx.TotalOut)
            throw new InvalidDataException("LG-LZO block table does not match decoded output");
        var baseName = (source as Support.MFile)?.Name ?? source.Name ?? "lzo";
        var outName = baseName.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)
            ? baseName[..^4] + ".unlzo"
            : baseName + ".unlzo";
        // flat next to the parent: the parent dir is already versioned, and a
        // "lgap.pak/" subdir would collide with the pak file itself
        var artifact = ArtifactBuilder.Flat(source, outName, plain);
        artifact.Flags |= DataSourceFlags.ProcessFurther;
        yield return artifact;
    }
}
