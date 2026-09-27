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
using Smx.SharpIO.Memory.Buffers;

namespace Yafex.FileFormats.MediatekPkg;

/// <summary>Header-only MediatekPkg listing: part entries are walked without
/// decrypting or reading payloads.</summary>
public class MediatekPkgLister : IFormatLister
{
    private readonly MediatekPkgContext _ctx;

    public MediatekPkgLister(MediatekPkgDetectionResult result)
    {
        _ctx = result.Context!;
    }

    public void List(IDataSource source, DetectionResult result, TextWriter output)
    {
        var hdr = _ctx.Header.Span[0];
        output.WriteLine($"MediatekPkg ({hdr.Version}-{hdr.ProductName}, variant={_ctx.Variant}):");

        var st = new SpanStream(source.Data);
        st.Position = Unsafe.SizeOf<PkgHeader>();
        if (_ctx.Variant == MtkPkgVariant.Standard)
        {
            st.Position += PkgHeader.STANDARD_DIGEST_SIZE;
        }
        if (_ctx.Variant == MtkPkgVariant.New)
        {
            st.Position += PkgHeader.NEW_DIGEST_SIZE;
        }
        if (_ctx.Flags.HasFlag(MtkPkgQuirks.Philips))
        {
            st.Position += MediatekPkgDetector.PHILIPS_EXTRA_HEADER_SIZE;
        }

        // NB: every part record is followed by a DataHeader-sized block
        // (mirrors the extractor stride); this holds for all variants.
        var strideHeaderSize = Unsafe.SizeOf<DataHeader>();
        var partEntrySize = Unsafe.SizeOf<PartEntry>();
        var count = 0;
        while (st.Position < st.Length)
        {
            if (_ctx.Flags.HasFlag(MtkPkgQuirks.Philips)
                && st.Position == st.Length - MediatekPkgDetector.PHILIPS_FOOTER_SIGNATURE_SIZE)
            {
                break;
            }
            if (st.Length - st.Position < partEntrySize)
            {
                break;
            }
            var part = st.ReadStruct<PartEntry>();
            // corrupt sizes must not slingshot the walk past the end
            if ((long)part.Size + strideHeaderSize > st.Length - st.Position)
            {
                break;
            }
            output.WriteLine($"  {part.PartName}.pak  size={part.Size}  flags={part.Flags}");
            st.Position += part.Size + strideHeaderSize;
            if (++count > 100000)
            {
                break;
            }
        }
    }
}
