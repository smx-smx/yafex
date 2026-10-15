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

namespace Yafex.FileFormats.Msd11;

public class Msd11Extractor : IFormatExtractor
{
    private readonly Msd11Context _ctx;
    private static readonly ILog log = LogManager.GetLogger(nameof(Msd11Extractor));

    public Msd11Extractor(Msd11DetectionResult result)
    {
        _ctx = result.Context!;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var fileData = source.Data.ToArray();

        log.Info("Firmware Info");
        log.Info("-------------");
        log.Info($"Format: Samsung MSDU11 ({_ctx.FirmwareName})");
        if (_ctx.Info != null)
        {
            log.Info($"Image: {_ctx.Info.Name} v{_ctx.Info.Major}.{_ctx.Info.Minor}");
        }
        log.Info($"Members: {_ctx.Members.Count}");

        var imageName = source.Name ?? "msd11";
        var basedir = ArtifactBuilder.VersionedDir(source, imageName);
        source.AddMetadata(new BaseDirectoryPath(basedir));

        int i = 0;
        foreach (var member in _ctx.Members)
        {
            i++;
            var filePath = Path.Combine(basedir, member.Name);
            log.Info($"#{i}/{_ctx.Members.Count} saving Member (id={member.Index}," +
                $" offset=0x{member.Offset:X}," +
                $" size='{member.Size}'," +
                $" encrypted='{member.Encrypted}') to file {filePath}");

            var artifact = ArtifactBuilder.FlatIn(source, basedir, member.Name,
                Msd11Reader.ReadMember(fileData, member, _ctx.Key));
            artifact.Flags |= DataSourceFlags.ProcessFurther;
            yield return artifact;
        }
    }
}
