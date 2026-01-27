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
using System.Text;

namespace Yafex.FileFormats.MStarPkg
{
    public class MStarPkgDetector : IFormatDetector
    {
        public const int MBOOT_SCRIPT_SIZE = 0x4000;

        public MStarPkgDetector()
        {
        }

        public DetectionResult Detect(IDataSource source)
        {
            if (source.Data.Length < MBOOT_SCRIPT_SIZE)
            {
                return new DetectionResult(0, null);
            }

            var script = Encoding.ASCII.GetString(
                source.Data.Span.Slice(0, MBOOT_SCRIPT_SIZE).ToArray()
            );

            if (script.Contains("setenv MstarUpgrade_complete"))
            {
                return new DetectionResult(100, null);
            }

            return new DetectionResult(0, null);
        }
    }
}
