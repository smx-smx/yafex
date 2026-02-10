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
using System;

using log4net;

using Smx.SharpIO.Memory.Buffers;

using Yafex.FileFormats.Epk;
using Yafex.FileFormats.EpkV1;
using Yafex.FileFormats.EpkV3;
using Yafex.Support;

namespace Yafex.FileFormats.EpkV2
{
    public class Epk2Detector : EpkDetector, IFormatDetector
    {
        private static readonly ILog log = LogManager.GetLogger(nameof(Epk2Detector));

        public Epk2Detector(KeysRepository keysRepo) : base(keysRepo)
        {
        }

        private static bool IsPlainHeader(EPK_V2_HEADER hdr)
        {
            return hdr.FileType == EPAK_MAGIC
                && hdr.EpkMagic == EPK_V2_HEADER.EPK2_MAGIC;
        }

        protected bool IsPlainHeaderData(ReadOnlySpan64<byte> data)
        {
            EPK_V2_HEADER hdr = data.ReadStruct<EPK_V2_HEADER>();
            return IsPlainHeader(hdr);
        }

        private Epk2Context CreateContext(EPK_V2_HEADER? header)
        {
            var services = new EpkServices();
            return new Epk2Context(serviceFactory, services, header.HasValue ? header.Value : default);
        }

        public DetectionResult Detect(IDataSource source)
        {
            var data = source.Data.Span;

            int confidence = 0;
            Epk2Context? ctx = null;

            // Beta EPK2 has no signatures, and immediately starts with the header
            bool isBetaEpk = IsPlainHeaderData(data);

            EPK_V2_STRUCTURE epk = data.ReadStruct<EPK_V2_STRUCTURE>();

            if (IsEpkVersionString(epk.platformVersion)) confidence += 40;
            if (IsEpkVersionString(epk.sdkVersion)) confidence += 40;

            var header = epk.epkHeader;
            if (!IsPlainHeader(header))
            {
                var hdrBytes = EPK_V2_STRUCTURE.GetHeader(data.AsReadonlySpan());
                var decryptor = serviceFactory.CreateEpkDecryptor(hdrBytes, ValidateEpkHeader);
                if (decryptor != null)
                {
                    header = decryptor.Decrypt(hdrBytes).ReadStruct<EPK_V2_HEADER>();
                    var cryptedCtx = CreateContext(header);
                    cryptedCtx.AddDecryptor(EPK_V2_HEADER.EPK2_MAGIC, decryptor);
                    ctx = cryptedCtx;
                }
                else
                {
                    if (confidence > 40)
                    {
                        log.Info("This could be a valid EPK2, but there's no matching AES key");
                    }
                    confidence = 0;
                }
            }

            if (IsPlainHeader(header))
            {
                confidence += 100;
                ctx = CreateContext(header);
                return new Epk2DetectionResult<EPK_V2_HEADER>(confidence, ctx);
            }


            return new Epk2DetectionResult<EPK_V2_HEADER>(confidence, ctx);
        }
    }
}
