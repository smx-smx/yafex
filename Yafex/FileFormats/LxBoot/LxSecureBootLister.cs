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

namespace Yafex.FileFormats.LxBoot
{
    public class LxSecureBootLister : IFormatLister
    {
        private readonly LxSecureBootContext _ctx;

        public LxSecureBootLister(LxSecureBootContext ctx)
        {
            _ctx = ctx;
        }

        public void List(IDataSource source, DetectionResult result, TextWriter output)
        {
            output.WriteLine($"LxSecureBoot ({source.Name}, variant={_ctx.SpbcVariant}):");
            if (_ctx.SpbcVariant == SpbcVariant.Bare)
            {
                if (LxSecureBootDetector.TryReadBareSecondStage(source.Data, out uint size))
                {
                    output.WriteLine($"  bare LL second stage, size={size}");
                }
                return;
            }
            if (_ctx.SpbcVariant == SpbcVariant.Embedded)
            {
                foreach (var (off, size) in _ctx.EmbeddedImages)
                {
                    output.WriteLine($"  embedded LL image @0x{off:X} size={size}");
                }
                return;
            }
            for (int i = 0; i < _ctx.SpbcDescriptors.Count; i++)
            {
                var d = _ctx.SpbcDescriptors[i];
                output.WriteLine($"  desc[{i}] load=0x{d.LoadAddress:X8}");
            }
            if (_ctx.SpbcSecondStageOffset is uint second)
            {
                output.WriteLine($"  second stage @0x{second:X}");
            }
        }
    }
}
