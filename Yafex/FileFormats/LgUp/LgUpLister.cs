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
using System.IO;

namespace Yafex.FileFormats.LgUp;

/// <summary>Header-only LGUP listing: sections/units from the decrypted header, payloads untouched.</summary>
public class LgUpLister : IFormatLister
{
    private readonly LgUpContext _ctx;

    public LgUpLister(LgUpDetectionResult result)
    {
        _ctx = result.Context!;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        var pkg = _ctx.Package;
        output.WriteLine($"LGUP v{pkg.Version} ({pkg.OuterMagic}, {LgUpFormat.CompressTypeName(pkg.CompressType)}, {pkg.Sections.Count} sections):");
        for (var i = 0; i < pkg.Sections.Count; i++)
        {
            var sec = pkg.Sections[i];
            output.WriteLine($"  [{i}] {sec.Name} {LgUpFormat.InstallTypeName(sec.InstallType)} units={sec.Units.Count}");
            var u = 0;
            foreach (var unit in sec.Units)
            {
                output.WriteLine($"    unit {u++}: {LgUpFormat.UnitTypeName(unit.UpdateType)} payload={unit.PatchSize} output={(unit.UncompressedSize != 0 ? "compressed" : "raw")}");
            }
        }
    }
}
