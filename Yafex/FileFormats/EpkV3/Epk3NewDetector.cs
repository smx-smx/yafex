#region License
/*
 * Copyright (c) 2023 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
﻿using log4net;
using Yafex.FileFormats.Epk;
using Yafex.Support;
using System;
using System.Text;

namespace Yafex.FileFormats.EpkV3
{
	public enum Epk3Variant
	{
		UNKNOWN,
		OLD,
		NEW
	}

	public class Epk3NewDetector : EpkDetector, IFormatDetector
	{
		private static readonly ILog log = LogManager.GetLogger(nameof(Epk3NewDetector));

		public Epk3NewDetector(KeysRepository keysRepo) : base(keysRepo) {
		}

		private static bool IsPlainHeader(EPK_V3_NEW_HEADER hdr) {
			return hdr.EpkMagic == EPK_V3_NEW_HEADER.EPK3_MAGIC;
		}

		private bool IsPlainHeaderData(ReadOnlySpan<byte> data) {
			EPK_V3_NEW_HEADER hdr = data.ReadStruct<EPK_V3_NEW_HEADER>();
			return IsPlainHeader(hdr);
		}

		private bool ValidateEpk3Header(ReadOnlySpan<byte> data) {
			var bytes = data.Slice(0, 4).ToArray();
			return Encoding.ASCII.GetString(bytes) == EPK_V3_NEW_HEADER.EPK3_MAGIC;
		}

		private Epk3NewContext CreateContext(EPK_V3_NEW_HEADER? header)
		{
			return new Epk3NewContext(
				serviceFactory,
				new EpkServices(),
				header.HasValue ? header.Value : default);
		}

		public DetectionResult Detect(IDataSource source) {
			var data = source.Data.Span;

			int confidence = 0;

			var epk3 = data.ReadStruct<EPK_V3_NEW_STRUCTURE>();
			if (IsEpkVersionString(epk3.head.platformVersion)) confidence += 40;
			if (IsEpkVersionString(epk3.head.sdkVersion)) confidence += 40;

			Epk3NewContext? ctx = null;

			EPK_V3_NEW_HEADER header = epk3.head.epkHeader;
			if (!IsPlainHeader(header)) {
				var headBytes = EPK_V3_NEW_STRUCTURE.GetHead(data.AsReadonlySpan());
				var hdrBytes = EPK_V3_NEW_HEAD_STRUCTURE.GetHeader(headBytes);
				var decryptor = serviceFactory.CreateEpkDecryptor(hdrBytes, ValidateEpk3Header);
				if(decryptor != null) {
					var decrypted = decryptor.Decrypt(hdrBytes).ReadStruct<EPK_V3_NEW_HEADER>();
					confidence = 100;
					ctx = CreateContext(decrypted);
					ctx.AddDecryptor(EPK_V3_NEW_HEADER.EPK3_MAGIC, decryptor);
				} else {
					if(confidence > 40) {
						log.Info("This could be a valid EPK2, but there's no matching AES key");
					}
					confidence = 0;
				}
			}
			return new DetectionResult(confidence, ctx);
		}
	}
}
