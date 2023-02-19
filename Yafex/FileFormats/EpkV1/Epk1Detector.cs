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
﻿using Yafex.Support;
using System;
using System.Text;

namespace Yafex.FileFormats.EpkV1
{
	public enum Epk1Type
	{
		BigEndian,
		Old,
		New
	}

	public class Epk1Detector : IFormatDetector
	{
		private Config config;

		public Epk1Detector(Config config) {
			this.config = config;
		}

		private Epk1Type GetEpkType(Epk1Header hdr) {
			// pakCount is always at the same offset for all 3 structures
			if(hdr.pakCount >> 8 != 0) {
				return Epk1Type.BigEndian;
			}

			if(hdr.pakCount < 21) {
				return Epk1Type.Old;
			}

			return Epk1Type.New;
		}

		public DetectionResult Detect(IDataSource source) {
			var data = source.Data;
			var hdr = data.ReadStruct<Epk1Header>();
			
			int confidence = 0;
			if(hdr.EpakMagic == "epak") {
				confidence += 100;
			}

			var epkType = GetEpkType(hdr);
			return new DetectionResult(confidence, epkType);
		}
	}
}
