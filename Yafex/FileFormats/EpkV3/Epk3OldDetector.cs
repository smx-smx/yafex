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

namespace Yafex.FileFormats.EpkV3
{
	public class Epk3OldDetector : EpkDetector, IFormatDetector
	{
		private static readonly ILog log = LogManager.GetLogger(nameof(Epk3OldDetector));

		private Config config;

		public Epk3OldDetector(Config config) : base(config) {
			this.config = config;
		}

		private static bool IsPlainHeader(EPK_V3_HEADER hdr) {
			return hdr.EpkMagic == EPK_V3_HEADER.EPK3_MAGIC;
		}

		private bool IsPlainHeaderData(ReadOnlySpan<byte> data) {
			EPK_V3_HEADER hdr = data.ReadStruct<EPK_V3_HEADER>();
			return IsPlainHeader(hdr);
		}

		private Epk3Variant DetectEpk3Type(ReadOnlySpan<byte> data) {
			var head_old = data.ReadStruct<EPK_V3_HEAD_STRUCTURE>();

			if (IsEpkVersionString(head_old.platformVersion)
			 && IsEpkVersionString(head_old.sdkVersion)
			 ) {
				return Epk3Variant.OLD;
			}

			var head_new = data.ReadStruct<EPK_V3_NEW_HEAD_STRUCTURE>();
			if(IsEpkVersionString(head_new.platformVersion)
			&& IsEpkVersionString(head_new.sdkVersion)
			){
				return Epk3Variant.NEW;
			}

			return Epk3Variant.UNKNOWN;
		}

		private Epk3Context<T> CreateContext<T>(T header) where T : struct {
			return new Epk3Context<T>(
				serviceFactory,
				new EpkServices(),
				header
			);
		}

		public DetectionResult Detect(IDataSource source) {
			var data = source.Data.Span;

			int confidence = 0;

			var type = DetectEpk3Type(data);
			if(type == Epk3Variant.UNKNOWN) {
				return new DetectionResult(0, null);
			}

			confidence += 50;

			throw new NotImplementedException();
		}
	}
}
