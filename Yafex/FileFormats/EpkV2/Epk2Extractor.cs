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
using System.IO;
using System.Runtime.InteropServices;
using Org.BouncyCastle.Crypto.Engines;
using Smx.SharpIO;
using Org.BouncyCastle.Asn1.Pkcs;

namespace Yafex.FileFormats.EpkV2
{
    public class Epk2Stream : SpanStream
    {

        private readonly Epk2Context ctx;

        public Epk2Stream(
			Epk2Context ctx,
			Memory<byte> data,
			Endianness endianness = Endianness.LittleEndian
		) : base(data, endianness)
        {
			this.ctx = ctx;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var AES_BLOCK_SIZE = 16;
            // aes block size mask
            var ALIGN_MASK = AES_BLOCK_SIZE - 1;

            var alignedLength = (count + ALIGN_MASK) & ~ALIGN_MASK;

            var blockNum = offset / AES_BLOCK_SIZE;
            var blockOff = offset % AES_BLOCK_SIZE;

            var blocks = Memory.Slice(blockNum * AES_BLOCK_SIZE, alignedLength);
            var decryptedBlocks = ctx.Services.Decryptor!.Decrypt(blocks.Span);

            var data = decryptedBlocks.Slice(blockOff, count).ToArray();
			data.CopyTo(buffer, offset);
			return data.Length;
        }
    }

    public class Epk2Extractor : IFormatExtractor
	{
		private static readonly ILog logger = LogManager.GetLogger(nameof(EpkV2));

		private readonly Config config;
		private readonly Epk2Context ctx;

		public Epk2Extractor(Config config, DetectionResult result) {
			this.config = config;
			this.ctx = (Epk2Context)result.Context!;
		}

		private Pak2DetectionResult GetPak2Header(ReadOnlySpan<byte> fileData, int offset) {
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
			var flags = DataSourceFlags.Output;
			var processFurther = pakHdr.ImageType switch
            {
				"crc3" => false,
				"logo" => false,
				"mico" => false,
				_ => true
            };
            if (processFurther)
            {
				flags |= DataSourceFlags.ProcessFurther;
            }

			var buff = new MemoryDataSourceBuffer($"{pakHdr.ImageType}.pak", flags);
			return buff;
		}


		private (string, string, IDataSource) HandlePak(
			ReadOnlySpan<byte> fileData,
			int offset,
			/*string baseDir,*/
			out int numberOfSegments,
			DataSourceFlags flags
		) {
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
					logger.Info($"PAK '{pakHdr.ImageType}' contains {pakHdr.segmentCount} segment(s)");

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
					pakData = ctx.Services.Decryptor!.Decrypt(pakData).Span;
				}
				outputBuffer.Write(pakData);

				var build = pakHdr.devMode switch {
					PakBuildMode.DEBUG => "DEBUG",
					PakBuildMode.RELEASE => "RELEASE",
					PakBuildMode.TEST => "TEST",
					_ => $"UNKNOWN 0x{pakHdr.devMode:X}"
				};

				logger.Info($"  segment #{curSeg + 1} (name='{pakHdr.ImageType}'," +
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

			//outputFile.Directory = baseDir;
			return (pakName!, outputFile.Name, outputFile);
		}

		public IEnumerable<IDataSource> Extract(IDataSource source)
		{
			return Extract(source, DataSourceFlags.Output);
		}

		public IEnumerable<IDataSource> Extract(IDataSource source, DataSourceFlags outputFlags) {
			var fileData = source.Data;

			var hdr = ctx.Header;

			logger.Info("Firmware Info");
			logger.Info("-------------");
			logger.Info($"Firmware magic: {hdr.FileType}");
			logger.Info($"Firmware type: {hdr.EpkMagic}");
			logger.Info($"Firmware otaID: {hdr.OtaId}");
			logger.Info($"Firmware version: {hdr.EpkVersion}");

			logger.Info($"PAK count: {hdr.fileNum}");
			logger.Info($"PAKs total size: {hdr.fileSize}");
			logger.Info($"Header length: 0x{hdr.imageLocations[0].ImageOffset:X}");

			var fwVersion = $"{hdr.EpkVersion}-{hdr.OtaId}";

			//var destDir = Path.Combine(config.DestDir, fwVersion);
			//Directory.CreateDirectory(destDir);

			int numSignatures = 1; //header signature
			for(int curPak=0; curPak<hdr.fileNum; curPak++) {
				int pakLoc = (int)hdr.imageLocations[curPak].ImageOffset + (numSignatures * EPK_V2_STRUCTURE.SIGNATURE_SIZE);

				(string pakName,
				 string pakOutputPath,
				 IDataSource pak) = HandlePak(
					 fileData.Span,
					 pakLoc, /*destDir*/
					 out int numberOfSegments,
					 outputFlags
				);
				numSignatures += numberOfSegments;

				logger.Info($"#{curPak + 1}/{ctx.Header.fileNum} saved PAK ({pakName}) to file {pakOutputPath}");
				yield return pak;
			}
		}
	}
}
