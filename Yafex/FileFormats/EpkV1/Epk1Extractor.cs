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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Yafex.FileFormats.EpkV1
{
	internal class Epk1Extractor : IFormatExtractor
	{
		private Config config;
		private DetectionResult result;

		private readonly Epk1Type epkType;

		private static readonly ILog log = LogManager.GetLogger(nameof(Epk1Extractor));

		public Epk1Extractor(Config config, DetectionResult result) {
			this.config = config;
			this.epkType = (Epk1Type)result.Context!;
		}

		private IEnumerable<IDataSource> ExtractEpk1Be(Memory<byte> fileData) {
			var hdr = fileData.ReadStruct<Epk1BeHeader>();

			Func<int, PakRec> GetPakRec = (int i) => {
				var rec = hdr.pakRecs[i];
				return new PakRec() {
					offset = rec.offset.BigEndianToHost(),
					size = rec.size.BigEndianToHost()
				};
			};

			Func<PakHeader, PakHeader> AdjustPakHeader = (PakHeader hdr) => {
				hdr.imageSize = hdr.imageSize.BigEndianToHost();
				hdr.swDate = hdr.swDate.BigEndianToHost();
				hdr.swVersion = hdr.swVersion.BigEndianToHost();
				hdr.devMode = (PakBuildMode)((uint)hdr.devMode).BigEndianToHost();
				return hdr;
			};

			string fwVerString;
			{
				var firstPak = AdjustPakHeader(
					fileData.ReadStruct<PakHeader>((int)GetPakRec(0).offset)
				);
				fwVerString = $"{hdr.EpakVersion}-{firstPak.Platform}";
			}

			var basedir = Path.Combine(config.DestDir, fwVerString);
			Directory.CreateDirectory(basedir);

			for (int i=0; i<hdr.PakCount; i++) {
				var rec = GetPakRec(i);
				if(rec.offset == 0) {
					continue;
				}

				var pakHdr = fileData.ReadStruct<PakHeader>((int)rec.offset);
				pakHdr = AdjustPakHeader(pakHdr);

				var pakData = fileData.Slice((int)rec.offset + Marshal.SizeOf<PakHeader>(), (int)pakHdr.imageSize);

				var fileName = $"{pakHdr.PakName}.pak";
				var filePath = Path.Combine(basedir, fileName);

				log.Info($"#{i + 1}/{hdr.PakCount} saving PAK (name='{pakHdr.PakName}'," +
					$" platform='{pakHdr.Platform}'," +
					$" offset=0x{rec.offset:X}," +
					$" size='{rec.size}') to file {filePath}");

				yield return new MemoryDataSource(pakData.ToArray());
			}
		}

		private IEnumerable<IDataSource> ExtractEpk1Old(Memory<byte> fileData) {
			var hdr = fileData.ReadStruct<Epk1Header>();

			var basedir = Path.Combine(config.DestDir, $"{hdr.EpakVersion}-{hdr.OtaID}");
			Directory.CreateDirectory(basedir);

			for (int i=0; i<hdr.pakCount; i++) {
				var rec = hdr.pakRecs[i];

				var pakHdr = fileData.ReadStruct<PakHeader>((int)rec.offset);

				var fileName = $"{pakHdr.PakName}.pak";
				var filePath = Path.Combine(basedir, fileName);

				log.Info($"#{i+1}/{hdr.pakCount} saving PAK (name='{pakHdr.PakName}'," +
					$" platform='{pakHdr.Platform}'," +
					$" offset=0x{rec.offset:X}," +
					$" size='{rec.size}') to file {filePath}");

				var pakData = fileData.Slice(
					(int)(rec.offset + Marshal.SizeOf<PakHeader>()),
					(int)(pakHdr.imageSize)
				);

				yield return new MemoryDataSource(pakData.ToArray());
			}
		}

		private IEnumerable<IDataSource> ExtractEpk1New(Memory<byte> fileData) {
			var hdr = fileData.ReadStruct<Epk1HeaderNew>();
			throw new NotImplementedException();
		}

		public IEnumerable<IDataSource> Extract(IDataSource source) {
			var fileData = source.Data;

			var artifacts = epkType switch {
				Epk1Type.BigEndian => ExtractEpk1Be(fileData),
				Epk1Type.Old => ExtractEpk1Old(fileData),
				Epk1Type.New => ExtractEpk1New(fileData)
			};
			return artifacts;
		}
	}
}
