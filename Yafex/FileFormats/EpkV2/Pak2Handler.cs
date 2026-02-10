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
using Yafex.FileFormats.Epk;
using Yafex.Support;

using System;
using Smx.SharpIO.Memory.Buffers;

namespace Yafex.FileFormats.EpkV2
{
    class Pak2DetectionData
    {
        public bool WasDecrypted { get; set; }
        public PAK_V2_HEADER Header { get; set; }
    }

    record Pak2DetectionResult(int Confidence, Pak2DetectionData Context) : DetectionResult(Confidence) { }

    class Pak2Handler<THeader> : IFormatDetector where THeader : struct
    {
        private readonly EpkContext<THeader> ctx;

        public Pak2Handler(EpkContext<THeader> ctx)
        {
            this.ctx = ctx;
        }

        private static bool IsPlainHeader(PAK_V2_HEADER header)
        {
            return header.PakMagic == PAK_V2_HEADER.PAK_MAGIC;
        }

        private static bool CheckPak2Magic(ReadOnlySpan64<byte> data)
        {
            var hdr = data.ReadStruct<PAK_V2_HEADER>();
            return hdr.PakMagic == PAK_V2_HEADER.PAK_MAGIC;
        }

        private PAK_V2_HEADER DecryptIfNeeded(ReadOnlySpan64<byte> data, out bool wasDecrypted)
        {
            var hdr = data.ReadStruct<PAK_V2_HEADER>();
            if (IsPlainHeader(hdr))
            {
                wasDecrypted = false;
                return hdr;
            }

            var decryptor = ctx.GetOrCreateDecryptor(
                PAK_V2_HEADER.PAK_MAGIC,
                data, CheckPak2Magic
            );

            data = decryptor.Decrypt(data).Span;
            wasDecrypted = true;
            return data.ReadStruct<PAK_V2_HEADER>();
        }

        public Pak2DetectionResult Detect(ReadOnlySpan64<byte> data)
        {
            int confidence = 0;

            var pak2 = DecryptIfNeeded(data, out bool wasDecrypted);
            if (IsPlainHeader(pak2))
            {
                confidence += 100;
            }

            var result = new Pak2DetectionData()
            {
                Header = pak2,
                WasDecrypted = wasDecrypted
            };

            return new Pak2DetectionResult(confidence, result);
        }

        public DetectionResult Detect(IDataSource source) => Detect(source.Data.Span);
    }
}
