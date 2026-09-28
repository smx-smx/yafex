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
using System.Collections.Generic;
using System.IO;

using log4net;

using Yafex.Metadata;
using Yafex.Support;

namespace Yafex.FileFormats.AllwinnerImg;

public class AllwinnerImgExtractor : IFormatExtractor
{
    private readonly AllwinnerImgContext _ctx;    private static readonly ILog log = LogManager.GetLogger(nameof(AllwinnerImgExtractor));

    public AllwinnerImgExtractor(AllwinnerImgDetectionResult result)
    {
        _ctx = result.Context!;
    }

    private static string BaseName(string path)
    {
        var parts = path.Split(['\\', '/'], System.StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : parts[^1].Trim();
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {        var fileData = source.Data;
        var parts = AllwinnerImgReader.ReadTable(fileData, _ctx);

        log.Info("Firmware Info");
        log.Info("-------------");
        log.Info($"Format: LiveSuit IMAGEWTY v{_ctx.Version:X}");
        log.Info($"Parts: {parts.Count}");

        var imageName = (source as MFile)?.Name ?? "allwinner";
        var basedir = ArtifactBuilder.VersionedDir(source, imageName);
        source.AddMetadata(new BaseDirectoryPath(basedir));

        int i = 0;
        foreach (var part in parts)
        {
            i++;
            // subtypes are padded and not unique; the filename field holds
            // the real basename (e.g. sys_config.fex, Boot0.bin)
            var fileName = BaseName(part.FileName);
            if (string.IsNullOrEmpty(fileName)) fileName = part.SubType;
            var filePath = Path.Combine(basedir, fileName);
            log.Info($"#{i}/{parts.Count} saving Part (type='{part.MainType}'," +
                $" subtype='{part.SubType}'," +
                $" offset=0x{part.Offset:X}," +
                $" size='{part.OriginalLength}') to file {filePath}");

            var artifact = ArtifactBuilder.FlatIn(source, basedir, fileName,
                AllwinnerImgReader.DecryptPayload(fileData, part));
            artifact.Flags |= DataSourceFlags.ProcessFurther;
            yield return artifact;
        }
    }
}
