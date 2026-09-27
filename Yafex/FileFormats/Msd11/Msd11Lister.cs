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

namespace Yafex.FileFormats.Msd11;

/// <summary>Table-only MSDU11 listing: sections come from the header, names
/// from the detection-time TOC. No member payload is touched.</summary>
public class Msd11Lister : IFormatLister
{
    private readonly Msd11Context _ctx;

    public Msd11Lister(Msd11DetectionResult result)
    {
        _ctx = result.Context!;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        output.WriteLine($"Msd11 (Samsung MSDU11, '{_ctx.FirmwareName}', {_ctx.Members.Count} members):");
        if (_ctx.Info != null)
        {
            output.WriteLine($"image: {_ctx.Info.Name} v{_ctx.Info.Major}.{_ctx.Info.Minor}");
        }
        int i = 0;
        foreach (var member in _ctx.Members)
        {
            i++;
            output.WriteLine($"#{i} id={member.Index}" +
                $" offset=0x{member.Offset:X} stored={member.Size} encrypted={member.Encrypted}" +
                $" name='{member.Name}'");
        }
    }
}
