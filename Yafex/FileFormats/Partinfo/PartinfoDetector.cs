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
using log4net;

using Yafex.Support;

using System.Linq;

namespace Yafex.FileFormats.Partinfo
{
    public record PartinfoDetectionResult(int Confidence, PartinfoContext? Context) : DetectionResult(Confidence);

    public class PartinfoDetector : IFormatDetector
    {
        private static ILog log = LogManager.GetLogger(nameof(PartinfoDetector));

        private PartinfoType? ToPartinfoType(uint magic)
        {
            switch (magic)
            {
                case PartinfoV2.PartmapInfo.MAGIC: return PartinfoType.PartinfoV2;
                case PartinfoV1.PartmapInfo.MAGIC: return PartinfoType.PartinfoV1;
                case MtdInfo.PartmapInfo.MAGIC: return PartinfoType.MtdInfo;
                default:
                    uint d = (magic >> 0) & 0xFF;
                    uint m = (magic >> 8) & 0xFF;
                    uint y = (magic >> 16) & 0xFFFF;
                    if (y >= 2008 && m <= 12 && d <= 31)
                    {
                        // this might be an unsupported partinfo format, but it's too generic to assume so
                        // just log
                        log.Warn($"Potential unknown partinfo magic 0x{magic:X}");
                        return PartinfoType.Unknown;
                    }
                    return null;
            }
        }

        public DetectionResult Detect(IDataSource source)
        {
            uint magic = source.Data.Slice(0, 4).Cast<uint>()[0];

            int confidence = 0;

            PartinfoType? partinfoType = ToPartinfoType(magic);
            PartinfoContext? ctx = null;

            switch (partinfoType)
            {
                case PartinfoType.MtdInfo:
                case PartinfoType.PartinfoV1:
                case PartinfoType.PartinfoV2:
                    confidence += 30;
                    ctx = new PartinfoContext()
                    {
                        PartinfoType = (PartinfoType)partinfoType
                    };
                    break;
            }
            return new PartinfoDetectionResult(confidence, ctx);
        }
    }
}
