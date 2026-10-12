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

using System;
using System.IO;

namespace Yafex.FileFormats.Lzhs
{
    public class LzhsDetector : IFormatDetector
    {
        private static readonly ILog log = LogManager.GetLogger(nameof(LzhsDetector));

        public LzhsDetector()
        {
        }

        public DetectionResult Detect(IDataSource source)
        {
            var data = source.Data;

            LzhsHeader hdr;
            try
            {
                hdr = new LzhsHeader(data.Span);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentOutOfRangeException || ex is ArgumentException)
            {
                return new SimpleDetectionResult(0);
            }

            // An empty compressed stream carries no content; without this,
            // any header-shaped prefix with comp == 0 and a zero checksum
            // (e.g. ARM ELF e_ident) vacuously verifies. Mirrors the mount
            // matcher's comp > 0 requirement.
            if (hdr.CompressedSize == 0)
            {
                return new SimpleDetectionResult(0);
            }

            try
            {
                var decoder = new LzhsDecoder(data);

                foreach (var item in decoder.AsEnumerable()) ;
                if (decoder.VerifyChecksum())
                {
                    return new SimpleDetectionResult(50);
                }
                else
                {
                    return new SimpleDetectionResult(0);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentOutOfRangeException || ex is ArgumentException)
            {
                log.Debug("LZHS probe failed", ex);
                return new SimpleDetectionResult(0);
            }
        }
    }
}
