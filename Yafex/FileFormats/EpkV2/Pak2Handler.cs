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
﻿using Yafex.FileFormats.Epk;
using Yafex.Support;
using System;
using System.Runtime.InteropServices;

namespace Yafex.FileFormats.EpkV2
{
	class Pak2DetectionResult
	{
		public bool WasDecrypted { get; set; }
		public PAK_V2_HEADER Header { get; set; }
	}

	class Pak2Handler : IFormatDetector
	{
		private readonly Epk2Context ctx;

		public Pak2Handler(Epk2Context ctx) {
			this.ctx = ctx;
		}

		private static bool IsPlainHeader(PAK_V2_HEADER header) {
			return header.PakMagic == PAK_V2_HEADER.PAK_MAGIC;
		}

		private static bool CheckPak2Magic(ReadOnlySpan<byte> data) {
			var hdr = data.ReadStruct<PAK_V2_HEADER>();
			return hdr.PakMagic == PAK_V2_HEADER.PAK_MAGIC;
		}

		private PAK_V2_HEADER DecryptIfNeeded(ReadOnlySpan<byte> data, out bool wasDecrypted) {
			var hdr = data.ReadStruct<PAK_V2_HEADER>();
			if (IsPlainHeader(hdr)) {
				wasDecrypted = false;
				return hdr;
			}

			this.ctx.EnsureDecryptor(data, CheckPak2Magic);
			data = ctx.Services.Decryptor!.Decrypt(data);
			wasDecrypted = true;
			return data.ReadStruct<PAK_V2_HEADER>();
		}

		public DetectionResult Detect(ReadOnlySpan<byte> data) {
			int confidence = 0;

			var pak2 = DecryptIfNeeded(data, out bool wasDecrypted);
			if (IsPlainHeader(pak2)) {
				confidence += 100;
			}

			var result = new Pak2DetectionResult() {
				Header = pak2,
				WasDecrypted = wasDecrypted
			};

			return new DetectionResult(confidence, result);
		}

		public DetectionResult Detect(IDataSource source) => Detect(source.Data.ToReadOnlySpan());
	}
}
