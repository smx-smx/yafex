using log4net;
using Smx.Yafex.FileFormats.Epk;
using Smx.Yafex.Support;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Smx.Yafex.FileFormats.EpkV2
{
	public class Epk2Extractor : IFormatExtractor
	{
		private static ILog log = LogManager.GetLogger(nameof(EpkV2));

		public const string EPK2_MAGIC = "EPK2";

		private Config config;
		private Epk2Context ctx;

		public Epk2Extractor(Config config, DetectionResult result) {
			this.config = config;
			this.ctx = (Epk2Context)result.Context!;
		}

		private Pak2DetectionResult GetPak2Header(Span<byte> fileData, int offset) {
			var pak2 = fileData.Slice(offset, Marshal.SizeOf<PAK_V2_STRUCTURE>());
			var pakHeader = PAK_V2_STRUCTURE.GetHeader(pak2);
			var handler = new Pak2Handler(ctx);

			var pakResult = handler.Detect(pakHeader);
			if (!pakResult.Succeded()) {
				throw new Exception("Invalid PAK2 header, or decryption failed");
			}

			return (Pak2DetectionResult)pakResult.Context!;
		}

		private MemoryDataSourceBuffer NewPakBuffer(PAK_V2_HEADER pakHdr)
        {
			var flags = DataSourceType.Output;
			var processFurther = pakHdr.ImageType switch
            {
				"crc3" => false,
				"logo" => false,
				"mico" => false,
				_ => true
            };
            if (processFurther)
            {
				flags |= DataSourceType.ProcessFurther;
            }

			var buff = new MemoryDataSourceBuffer($"{pakHdr.ImageType}.pak", flags);
			return buff;
		}

		private (string, string, IDataSource) HandlePak(Span<byte> fileData, int offset, string baseDir, out int numberOfSegments) {
			string? pakName = null;

			MemoryDataSource outputFile;
			MemoryDataSourceBuffer outputBuffer = null;

			numberOfSegments = 0;

			while (true) { 
				var pak2 = GetPak2Header(fileData, offset);
				bool needsDecryption = pak2.WasDecrypted;

				var pakHdr = pak2.Header;
				uint curSeg = pakHdr.segmentIndex;
				if (curSeg == 0) {
					log.Info($"PAK '{pakHdr.ImageType}' contains {pakHdr.segmentCount} segment(s)");

					outputBuffer = NewPakBuffer(pakHdr);
					
					pakName = pakHdr.ImageType;
					numberOfSegments = (int)pakHdr.segmentCount;
				}

				if (outputBuffer == null) {
					throw new InvalidDataException($"Expected chunk index 0, got {curSeg}");
				}

				var pakData = fileData.Slice(offset + Marshal.SizeOf<PAK_V2_STRUCTURE>(), (int)pakHdr.segmentSize);
				if (needsDecryption)
				{
					pakData = ctx.Services.Decryptor!.Decrypt(pakData);
				}
				outputBuffer.Write(pakData);

				var build = pakHdr.devMode switch {
					PakBuildMode.DEBUG => "DEBUG",
					PakBuildMode.RELEASE => "RELEASE",
					PakBuildMode.TEST => "TEST",
					_ => $"UNKNOWN 0x{pakHdr.devMode:X}"
				};

				log.Info($"  segment #{curSeg + 1} (name='{pakHdr.ImageType}'," +
					$" version={pakHdr.SwVersion}," +
					$" platform='{pakHdr.ModelName}', offset='0x{offset:X}', size='{pakHdr.segmentSize} bytes'," +
					$" build={build})");

				if (curSeg + 1 == pakHdr.segmentCount) {
					outputFile = outputBuffer.ToDataSource();
					outputBuffer.Dispose();
					break;
				}

				offset += Marshal.SizeOf<PAK_V2_STRUCTURE>() + (int)pakHdr.segmentSize;
			}

			outputFile.Directory = baseDir;
			return (pakName!, outputFile.Name, outputFile);
		}

		public IEnumerable<IDataSource> Extract(IDataSource source) {
			var fileData = source.Data;

			var hdr = ctx.Header;

			log.Info("Firmware Info");
			log.Info("-------------");
			log.Info($"Firmware magic: {hdr.FileType}");
			log.Info($"Firmware type: {hdr.EpkMagic}");
			log.Info($"Firmware otaID: {hdr.OtaId}");
			log.Info($"Firmware version: {hdr.EpkVersion}");

			log.Info($"PAK count: {hdr.fileNum}");
			log.Info($"PAKs total size: {hdr.fileSize}");
			log.Info($"Header length: 0x{hdr.imageLocations[0].ImageOffset:X}");

			var fwVersion = $"{hdr.EpkVersion}-{hdr.OtaId}";

			var destDir = Path.Combine(config.DestDir, fwVersion);
			Directory.CreateDirectory(destDir);

			int numSignatures = 1; //header signature
			for(int curPak=0; curPak<hdr.fileNum; curPak++) {
				int pakLoc = (int)hdr.imageLocations[curPak].ImageOffset + (numSignatures * EPK_V2_STRUCTURE.SIGNATURE_SIZE);

				(string pakName,
				 string pakOutputPath,
				 IDataSource pak) = HandlePak(fileData.Span, pakLoc, destDir, out int numberOfSegments);
				numSignatures += numberOfSegments;

				log.Info($"#{curPak + 1}/{ctx.Header.fileNum} saved PAK ({pakName}) to file {pakOutputPath}");
				yield return pak;
			}
		}
	}
}
