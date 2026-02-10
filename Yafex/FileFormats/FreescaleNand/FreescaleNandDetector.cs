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
using Yafex.Support;

using System.Text;

namespace Yafex.FileFormats.FreescaleNand
{
    public class FreescaleNandDetector : IFormatDetector
    {
        public DetectionResult Detect(IDataSource source)
        {
            if (source.Data.Length > 0x20 &&
                source.Data.Span.Slice(0x10, 4)
                .ToArray()
                .AsString(Encoding.ASCII) == "FCB "
            )
            {
                return new SimpleDetectionResult(100);
            }
            return new SimpleDetectionResult(0);
        }
    }
}
