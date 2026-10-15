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

        /// <summary>Window parsed for partitions: newer scripts run past
        /// the 16KB detection head (e.g. arcelik_new2's script ends at
        /// ~25KB); all observed script regions fit in 64KB.</summary>
        public const int SCRIPT_PARSE_SIZE = 0x10000;

        public MStarPkgDetector()
        {
        }

        public DetectionResult Detect(IDataSource source)
        {
            if (source.Data.Length < MBOOT_SCRIPT_SIZE)
            {
                return new SimpleDetectionResult(0);
            }

            var script = Encoding.ASCII.GetString(
                source.Data.Span.Slice(0, MBOOT_SCRIPT_SIZE).ToArray()
            );

            // u-boot-style upgrades end the script with setenv
            // MstarUpgrade_complete; mboot-style ones only share the
            // filepartload command (verified archive-wide: the marker hits
            // only mstar/*.bin in the head window).
            if (script.Contains("setenv MstarUpgrade_complete")
                || script.Contains("filepartload"))
            {
                return new SimpleDetectionResult(100);
            }

            return new SimpleDetectionResult(0);
        }
    }
}
