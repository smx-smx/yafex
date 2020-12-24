using log4net;
using Smx.Yafex.FileFormats.Epk;
using Smx.Yafex.Support;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Smx.Yafex.FileFormats.EpkV1
{
	internal class Epk1Extractor : ExtractorBase, IFormatExtractor
	{
		private Config config;
		private DetectionResult result;

		private readonly Epk1Type epkType;

		private static readonly ILog log = LogManager.GetLogger(nameof(Epk1Extractor));

		public Epk1Extractor(Config config, DetectionResult result) {
			this.config = config;
			this.epkType = (Epk1Type)result.Context!;
		}

		private void ExtractEpk1Be(ReadOnlySpan<byte> fileData) {
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

				var artifact = ArtifactOpen(filePath);
				artifact.Write(pakData);
				artifact.Finish();
			}
		}

		private void ExtractEpk1Old(ReadOnlySpan<byte> fileData) {
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
				var artifact = ArtifactOpen(filePath);
				artifact.Write(pakData);
				artifact.Finish();
			}
		}

		private void ExtractEpk1New(ReadOnlySpan<byte> fileData) {
			var hdr = fileData.ReadStruct<Epk1HeaderNew>();
			throw new NotImplementedException();
		}

		public IList<IArtifact> Extract(IDataSource source) {
			var fileData = source.Data.ToReadOnlySpan();
			switch (epkType) {
				case Epk1Type.BigEndian: ExtractEpk1Be(fileData); break;
				case Epk1Type.Old: ExtractEpk1Old(fileData); break;
				case Epk1Type.New: ExtractEpk1New(fileData); break;
			}

			return new List<IArtifact>();
		}
	}
}