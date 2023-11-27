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
using Yafex.Support;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Yafex.FileFormats.Lzhs
{
	public class LzhsDetector : IFormatDetector
	{
		private static readonly ILog log = LogManager.GetLogger(nameof(LzhsDetector));
		private Config config;

		public LzhsDetector(Config config) {
			this.config = config;
		}

		public DetectionResult Detect(IDataSource source) {
			var data = source.Data;

			LzhsHeader hdr;
			try {
				hdr = new LzhsHeader(data.Span);
			} catch (InvalidDataException) {
				return new DetectionResult(0, null);
			}

			var decoder = new LzhsDecoder(data);

			foreach (var item in decoder.AsEnumerable()) ;
			if (decoder.VerifyChecksum()) {
				return new DetectionResult(50, null);
			} else {
				return new DetectionResult(0, null);
			}
		}
	}
}
