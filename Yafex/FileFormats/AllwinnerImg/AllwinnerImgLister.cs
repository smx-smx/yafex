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

namespace Yafex.FileFormats.AllwinnerImg;

/// <summary>Header/table-only LiveSuit listing: entries are walked without
/// decrypting or reading payloads.</summary>
public class AllwinnerImgLister : IFormatLister
{
    private readonly AllwinnerImgContext _ctx;

    public AllwinnerImgLister(AllwinnerImgDetectionResult result)
    {
        _ctx = result.Context!;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        var parts = AllwinnerImgReader.ReadTable(source.Data, _ctx);
        output.WriteLine($"AllwinnerImg (LiveSuit IMAGEWTY v{_ctx.Version:X}, {_ctx.NumFiles} entries):");
        int i = 0;
        foreach (var part in parts)
        {
            i++;
            output.WriteLine($"#{i} {part.MainType}/{part.SubType}" +
                $" offset=0x{part.Offset:X} stored={part.StoredLength} original={part.OriginalLength}" +
                $" file='{part.FileName}'");
        }
    }
}
