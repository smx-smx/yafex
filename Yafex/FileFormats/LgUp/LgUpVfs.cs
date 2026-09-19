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
using System.Text;

using Yafex.Fuse;

namespace Yafex.FileFormats.LgUp;

/// <summary>
/// FUSE VFS over an LGUP package: units as lazy virtual files (decrypted +
/// decompressed on first read, memoized per unit) with squashfs subdirectories
/// where applicable. Mirrors the extractor's naming so VFS paths are predictable.
/// Views opted in: decrypted, raw payload, per-unit metadata, expanded dirs.
/// </summary>
public class LgUpVfs : YafexVfs
{
    private readonly List<object> _keepAlive = new();

    public LgUpVfs(LgUpContext ctx, IDataSource source)
    {
        var pkg = ctx.Package;
        var rootDir = new YafexDirectory(source.Name ?? "lgup", Helpers.OctalLiteral(0755));
        Root.AddNode(rootDir);

        for (var i = 0; i < pkg.Sections.Count; i++)
        {
            var sec = pkg.Sections[i];
            var secDir = new YafexDirectory(
                Sanitize($"{i:D2}_{sec.Name}"), Helpers.OctalLiteral(0755));
            rootDir.AddNode(secDir);

            var cursor = pkg.DataBase + pkg.PatchOffsets[i];
            var u = 0;
            foreach (var unit in sec.Units)
            {
                // NOTE: no content extension here — sniffing it would decrypt every
                // unit at mount time and defeat the lazy reads.
                var file = new LgUpUnitFile(
                    $"unit_{u:D4}.data", source, cursor, unit, ctx.Key, pkg.CompressType);
                var captured = (Sec: sec, Unit: unit, Index: u, Offset: cursor, Compress: pkg.CompressType);
                VfsBuilder.AddImage(
                    secDir, file,
                    VfsImageViews.All,
                    _keepAlive,
                    raw: new RangeVirtualFile(file.Name, source, cursor, unit.PatchSize),
                    describe: () => DescribeUnit(captured.Sec, captured.Unit, captured.Index, captured.Offset, captured.Compress));
                cursor += unit.PatchSize;
                u++;
            }
        }
    }

    private static string DescribeUnit(LgUpSection sec, LgUpUnit unit, int index, long offset, uint compressType)
    {
        return $"section      : {sec.Name} ({LgUpFormat.InstallTypeName(sec.InstallType)})\n" +
            $"unit         : {index} ({LgUpFormat.UnitTypeName(unit.UpdateType)})\n" +
            $"file offset  : 0x{offset:X}\n" +
            $"payload size : {unit.PatchSize}\n" +
            $"compressed   : {(unit.UncompressedSize != 0 ? LgUpFormat.CompressTypeName(compressType) : "no")}\n" +
            $"sha256       : {Convert.ToHexString(unit.Sha256).ToLowerInvariant()}";
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        return sb.ToString();
    }
}
