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

using Smx.SharpIO.Memory.Buffers;

using Yafex.Support;

namespace Yafex.FileFormats.EpkV3
{
    /// <summary>Header-only EPKv3-new listing: package info is decrypted, payloads are never read.</summary>
    public class Epk3NewLister : IFormatLister
    {
        private readonly Epk3NewContext _ctx;

        public Epk3NewLister(Epk3NewDetectionResult result)
        {
            _ctx = result.Context!;
        }

        public void List(IDataSource source, DetectionResult result, TextWriter output)
        {
            var hdr = _ctx.Header;
            var fileData = source.Data;
            var epk = fileData.ReadStruct<EPK_V3_NEW_STRUCTURE>();
            var image = new Epk3NewImage(_ctx, fileData.AsReadonlySpan(), epk);
            output.WriteLine($"EPKv3 ({hdr.EpkVersion}-{hdr.OtaId}, {image.Paks.Count} packages):");
            foreach (var pak in image.Paks)
            {
                output.WriteLine($"  {pak.Image.Name}  segments={pak.Image.Segments.Count}  size={pak.Image.Length}");
            }
        }
    }
}
