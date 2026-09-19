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

using Smx.SharpIO.Extensions;
using Smx.SharpIO.Memory.Buffers;

using Yafex.FileFormats.Epk;
using Yafex.Support;

namespace Yafex.FileFormats.EpkV2
{
    /// <summary>
    /// Header-only EPKv2 listing via <see cref="Epk2SegmentWalker"/>: PAK headers
    /// are decrypted, payloads are never read.
    /// </summary>
    public class Epk2Lister<THeader> : IFormatLister
        where THeader : struct, IEpkV2Header
    {
        private readonly EpkContext<THeader> _ctx;

        public Epk2Lister(Epk2DetectionResult<THeader> result)
        {
            _ctx = result.Context!;
        }

        public void List(IDataSource source, DetectionResult result, TextWriter output)
        {
            var hdr = _ctx.Header;
            output.WriteLine($"EPKv2 ({hdr.EpkMagic} {hdr.EpkVersion}-{hdr.OtaId}, {hdr.FileNum} files):");
            var images = Epk2SegmentWalker.BuildPakImages(
                source.Data.AsReadonlySpan(), _ctx, hdr);
            foreach (var pak in images)
            {
                output.WriteLine($"  {pak.Image.Name}  segments={pak.Image.Segments.Count}  size={pak.Image.Length}");
            }
        }
    }
}
