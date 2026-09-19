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
using System.Security.Cryptography;
using System.Text;

using log4net;

namespace Yafex.FileFormats.LgUp;

public class LgUpExtractor : IFormatExtractor
{
    private static readonly ILog log = LogManager.GetLogger(typeof(LgUpExtractor));

    private readonly LgUpContext _ctx;

    public LgUpExtractor(LgUpContext ctx)
    {
        _ctx = ctx;
    }

    public IEnumerable<IDataSource> Extract(IDataSource source)
    {
        var pkg = _ctx.Package;
        var file = source.Data.ToArray();
        var topDir = ArtifactBuilder.VersionedDir(source, $"lgup-v{pkg.Version}");

        if (pkg.Exec.Length > 0)
        {
            yield return Artifact(source, topDir, "lgua_exec.bin", pkg.Exec,
                $"LGUA embedded executable ({pkg.Exec.Length} bytes)");
        }

        for (var i = 0; i < pkg.Sections.Count; i++)
        {
            var sec = pkg.Sections[i];
            var secDir = Path.Combine(topDir, "partitions", Sanitize($"{i:D2}_{sec.Name}"));
            var cursor = pkg.DataBase + pkg.PatchOffsets[i];

            yield return Artifact(source, secDir, "section_header.txt",
                Encoding.UTF8.GetBytes(SectionDump(sec)), $"section {sec.Name} header");

            var u = 0;
            foreach (var unit in sec.Units)
            {
                unit.FileOffset = cursor;
                if (cursor + unit.PatchSize > file.Length)
                    throw new InvalidDataException($"unit {u} of section '{sec.Name}' overruns file");
                unit.Payload = file.AsSpan((int)cursor, (int)unit.PatchSize).ToArray();
                cursor += unit.PatchSize;

                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(unit.Payload), unit.Sha256))
                    log.WarnFormat("section '{0}' unit {1}: SHA256 mismatch, continuing", sec.Name, u);

                unit.Content = LgUpParser.StripPad(LgUpParser.AesCbcDecrypt(unit.Payload, _ctx.Key));
                unit.Output = unit.UncompressedSize != 0
                    ? LgUpDecompress.Decompress(pkg.CompressType, unit.Content)
                    : unit.Content;

                var ext = LgUpDecompress.ExtensionFor(unit.Output);
                var kind = LgUpFormat.UnitTypeName(unit.UpdateType);
                log.InfoFormat("section '{0}' unit {1}: {2} payload={3} output={4} bytes",
                    sec.Name, u, kind, unit.Payload.Length, unit.Output.Length);
                yield return Artifact(source, secDir, $"unit_{u:D4}.data{ext}", unit.Output,
                    $"section {sec.Name} unit {u} ({kind})", processFurther: true);
                u++;
            }

            if (sec.InstallType == LgUpFormat.InstallFull && sec.Units.Count > 0)
            {
                var img = sec.Units[0].Output;
                var md5 = Convert.ToHexString(MD5.HashData(img)).ToLowerInvariant();
                log.InfoFormat("full image '{0}': {1} bytes md5={2}", sec.Name, img.Length, md5);
                yield return Artifact(source, Path.Combine(topDir, "images"),
                    Sanitize(sec.Name) + ".img", img, $"full image {sec.Name}", processFurther: true);
            }
        }

        if (file.Length > pkg.TrailerOffset)
        {
            var trailer = file.AsSpan((int)pkg.TrailerOffset).ToArray();
            log.InfoFormat("trailer: {0} bytes beyond LGUP data end", trailer.Length);
            yield return Artifact(source, topDir, "trailer.bin", trailer, "trailer");
        }
    }

    private static IDataSource Artifact(
        IDataSource source, string dir, string name, byte[] content, string label, bool processFurther = false)
    {
        var flags = DataSourceFlags.Output;
        if (processFurther) flags |= DataSourceFlags.ProcessFurther;
        var ds = ArtifactBuilder.FromBytes(source, dir, name, content, flags);
        log.InfoFormat("saved {0} to {1}", label, Path.Combine(dir, name));
        return ds;
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        return sb.ToString();
    }

    private static string SectionDump(LgUpSection sec)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"install type : {LgUpFormat.InstallTypeName(sec.InstallType)} (0x{sec.InstallType:X8})");
        sb.AppendLine($"name         : {sec.Name}");
        if (sec.InstallType == LgUpFormat.InstallExt4)
        {
            sb.AppendLine($"source size  : {sec.SourceSize}");
            sb.AppendLine($"target size  : {sec.TargetSize}");
            sb.AppendLine($"key unit     : {sec.KeyUnit}");
            sb.AppendLine($"total units  : {sec.TotalUnits}");
        }
        else
        {
            sb.AppendLine($"name2        : {sec.Name2}");
            sb.AppendLine($"size         : {sec.FullSize}");
            sb.AppendLine($"flag         : {sec.FullFlag}");
            sb.AppendLine($"count        : {sec.FullCount}");
        }
        var u = 0;
        foreach (var unit in sec.Units)
        {
            sb.AppendLine($"-- unit {u++}: {LgUpFormat.UnitTypeName(unit.UpdateType)} (0x{unit.UpdateType:X8})");
            sb.AppendLine($"   patch size  : {unit.PatchSize}  compressed: {unit.UncompressedSize}");
            sb.AppendLine($"   sha256      : {Convert.ToHexString(unit.Sha256).ToLowerInvariant()}");
        }
        return sb.ToString();
    }
}
