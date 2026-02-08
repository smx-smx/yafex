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
using Yafex.FileFormats.Lzhs;

using System;
using System.IO;
using System.Linq;

namespace Yafex.FileFormats.LzhsFs
{
    internal class LzhsFsDetector : IFormatDetector
    {
        public LzhsFsDetector()
        {
        }

        public DetectionResult Detect(IDataSource source)
        {
            if (source.Data.Length < LzhsFsReader.UNCOMPRESSED_HEADING_SIZE + 32)
            {
                return new DetectionResult(0, null);
            }

            try
            {
                LzhsHeader firstHdr = new LzhsHeader(source.Data.Span.Slice(LzhsFsReader.UNCOMPRESSED_HEADING_SIZE, 16));
                if (firstHdr.Checksum != 1) throw new InvalidDataException();

                var innerData = source.Data.Slice(LzhsFsReader.UNCOMPRESSED_HEADING_SIZE + 16);
                var dec = new LzhsDecoder(innerData);
                dec.AsEnumerable().ToArray();
                bool result = dec.VerifyChecksum();
                Console.WriteLine($"{firstHdr.Checksum}: " + (result ? "PASS" : "FAIL"));

                if (result) {
                    return new DetectionResult(80, null);
                }

                //Decompression failed, but we want to check in case the first segment is stored uncompressed
                if (dec.header.Checksum == 0x00 && dec.header.CompressedSize == dec.header.UncompressedSize)
                {
                    return new DetectionResult(50, null);
                }
                    
                return new DetectionResult(0, null);

            }
            catch (InvalidDataException)
            {
                return new DetectionResult(0, null);
            }
        }
    }
}
