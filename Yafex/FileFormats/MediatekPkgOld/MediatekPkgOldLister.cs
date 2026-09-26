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
using System.Runtime.CompilerServices;

using Smx.SharpIO;

using Yafex.FileFormats.MediatekPkg;

namespace Yafex.FileFormats.MediatekPkgOld;

/// <summary>Header-only MTK PKG old listing: part entries are walked without
/// decrypting payloads.</summary>
public class MediatekPkgOldLister : IFormatLister
{
    private readonly MediatekPkgOldContext _ctx;

    public MediatekPkgOldLister(MediatekPkgOldDetectionResult result)
    {
        _ctx = result.Context!;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        var hdr = _ctx.Header;
        output.WriteLine($"MediatekPkgOld ({hdr.Version}-{hdr.ProductName}, header at 0x{_ctx.HeaderOffset:X}):");

        var st = new SpanStream(source.Data);
        st.Position = _ctx.HeaderOffset + MtkOldHeader.SIZE;

        var partEntrySize = Unsafe.SizeOf<PartEntry>();
        var count = 0;
        while (st.Position < st.Length)
        {
            if (st.Length - st.Position < partEntrySize) break;
            var part = st.ReadStruct<PartEntry>();
            if (part.Size > st.Length - st.Position) break;
            output.WriteLine($"  {part.PartName}.pak  size={part.Size}  flags={part.Flags}");
            st.Position += part.Size;
            if (++count > 100000) break;
        }
    }
}
