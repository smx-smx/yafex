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
                case PartinfoV1.PartmapInfo.MAGIC: return PartinfoType.PartinfoV1;
                case PartinfoV2.PartmapInfo.MAGIC: return PartinfoType.PartinfoV2;
                case PartinfoV3.PartmapInfo.MAGIC: return PartinfoType.PartinfoV3;
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

        private static bool IsPrintableAscii(byte b)
        {
            return b == 0 || (b >= 0x20 && b < 0x7F);
        }

        private static bool IsNameAt(IDataSource source, long offset, int maxLen)
        {
            if (source.Data.Length < offset + maxLen)
            {
                return false;
            }
            var slice = source.Data.Span.Slice(offset, maxLen);
            if (slice[0] == 0)
            {
                return false;
            }
            for (int i = 0; i < maxLen; i++)
            {
                if (!IsPrintableAscii(slice[i]))
                {
                    return false;
                }
            }
            return true;
        }

        // mtdi.pak device-map files share date-coded magics with partinfo tables,
        // but have their own shape: reserved u64, nmap/npartition, one device
        // record and 84-byte entries at a fixed offset.
        private bool LooksLikeMtdinfoFile(IDataSource source)
        {
            long len = source.Data.Length;
            if (len < MtdinfoFile.Layout.EntriesOffset + MtdinfoFile.Layout.EntryStride)
            {
                return false;
            }
            if ((len - MtdinfoFile.Layout.EntriesOffset) % MtdinfoFile.Layout.EntryStride != 0)
            {
                return false;
            }
            var reserved = source.Data.Span.Slice(4, 8);
            for (int i = 0; i < 8; i++)
            {
                if (reserved[i] != 0)
                {
                    return false;
                }
            }
            byte nmap = source.Data.Span.Slice(12, 1)[0];
            byte npart = source.Data.Span.Slice(13, 1)[0];
            long slots = (len - MtdinfoFile.Layout.EntriesOffset) / MtdinfoFile.Layout.EntryStride;
            if (nmap < 1 || npart < 1 || npart > slots)
            {
                return false;
            }
            return IsNameAt(source, 16, 32) && IsNameAt(source, MtdinfoFile.Layout.EntriesOffset, 32);
        }

        public DetectionResult Detect(IDataSource source)
        {
            if (source.Data.Length < 4)
            {
                return new PartinfoDetectionResult(0, null);
            }
            uint magic = source.Data.Slice(0, 4).Cast<uint>()[0];

            int confidence = 0;

            PartinfoType? partinfoType = ToPartinfoType(magic);
            PartinfoContext? ctx = null;

            switch (partinfoType)
            {
                case PartinfoType.MtdInfo:
                case PartinfoType.PartinfoV1:
                case PartinfoType.PartinfoV2:
                case PartinfoType.PartinfoV3:
                    confidence += 30;
                    ctx = new PartinfoContext()
                    {
                        PartinfoType = (PartinfoType)partinfoType
                    };
                    break;
                default:
                    if (LooksLikeMtdinfoFile(source))
                    {
                        confidence += 30;
                        ctx = new PartinfoContext()
                        {
                            PartinfoType = PartinfoType.MtdinfoFile
                        };
                    }
                    break;
            }
            return new PartinfoDetectionResult(confidence, ctx);
        }
    }
}
