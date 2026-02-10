using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.IO.Compression;

using Smx.SharpIO;
using Smx.SharpIO.Extensions;
using Smx.SharpIO.Memory.Buffers;

using Yafex.Metadata;
using Yafex.Support;

using log4net;

namespace Yafex.FileFormats.SddlSec
{
	internal class SddlSecExtractor : IFormatExtractor
	{
        private readonly SddlSecContext _ctx;
        private static readonly ILog log = LogManager.GetLogger(nameof(SddlSecExtractor));
		private const string AES_KEY_ID = "sddl-sec-key";
		private readonly Aes _aesKey;
		private readonly AesDecryptor Decryptor;

		public SddlSecExtractor(SddlSecContext ctx, KeysRepository keys) {
			var aesKey = keys.GetKey(AES_KEY_ID);
			_aesKey = aesKey.GetAes(PaddingMode.PKCS7);
			Decryptor = new AesDecryptor(_aesKey);
            _ctx = ctx;
		}

        private SddlSecHeader Header => _ctx.Header;

		public static Memory64<byte> Decipher(Memory64<byte> data)
		{
			var src = data.Span;
			var outBuf = new NativeMemoryManager64<byte>(data.Length);
			var dst = outBuf.Memory.Span;

			uint v3 = 0x388;
			byte j = 0;
			int srcPos = 0;
			int remaining = (int)data.Length;

			while (remaining != 0)
			{
				int chunk = remaining;
				if (chunk > 0x7f) chunk = 0x80;
				if (chunk != 0)
				{
					for (int k = 0; k < chunk; k++)
					{
						byte sb = src[srcPos + k];
						j = (byte)(j + 1);
						byte key = (byte)((v3 & 0xff00) >> 8);
						uint v11 = (uint)sb + 38400u;
						v3 = unchecked(v3 + v11 + 163u);
						if (j == 0)
						{
							v3 = 0x388;
						}
						dst[srcPos + k] = (byte)(sb ^ key);
					}
					srcPos += chunk;
				}
				remaining -= chunk;
			}

			return outBuf.Memory;
		}
		
		public static Memory64<byte> DecompressZlib(Memory64<byte> zlibData)
		{
			var tmp = zlibData.Span.ToArray();
			using (var input = new MemoryStream(tmp))
			using (var zlib = new ZLibStream(input, CompressionMode.Decompress))
			using (var output = new MemoryStream())
			{
				zlib.CopyTo(output);
				var outArr = output.ToArray();
				var result = new NativeMemoryManager64<byte>(outArr.Length);
				outArr.AsSpan().CopyTo((Span<byte>)result.Memory.Span);
				return result.Memory;
			}
		}

		public (EntryHeader, Memory64<byte>) GetFile(SpanStream dataStream)
		{
			var entryHeaderEncrypted = dataStream.ReadBytes(EntryHeader.PADDED_SIZE);
			var entryHeaderBytes = Decryptor.Decrypt(entryHeaderEncrypted);
			var entryHeader = entryHeaderBytes.Cast<byte, EntryHeader>().Span[0];

			var entryDataEncrypted = dataStream.ReadBytes(entryHeader.FileSize);
			var entryData = Decryptor.Decrypt(entryDataEncrypted);

			return (entryHeader, entryData);
		}

		public List<SditModuleEntry> ParseSditToModules(Memory64<byte> sditData)
		{
			var dataStream = new SpanStream(sditData, Endianness.BigEndian);
			var sditHeader = dataStream.ReadStruct<SditHeader>();

			if (!sditHeader.HeaderMagic.SequenceEqual(SddlSecHeader.SDDL_SEC_HEADER_MAGIC)){
				throw new InvalidDataException("Invalid SDIT header magic");
			}

			log.Debug($"[SDIT] Group count: {sditHeader.GroupCount}");
			var moduleList = new List<SditModuleEntry> {};

			//traverse all groups to make sure we get all the modules that are in the file
			for (int group_i = 0; group_i < sditHeader.GroupCount; group_i++)
			{
				var groupHeader = dataStream.ReadStruct<SditGroupHeader>();
				log.Debug($"[SDIT] Group {groupHeader.GroupID} - Entry count {groupHeader.EntryCount}");

				for (int entry_i = 0; entry_i < groupHeader.EntryCount; entry_i++)
				{
					var moduleEntry = dataStream.ReadStruct<SditModuleEntry>();
					log.Debug($"[SDIT] - Entry: {moduleEntry.ModuleName}, Segcount: {moduleEntry.SegmentCount}, Version {moduleEntry.VersionString}");

					//only add unique modules
					if (!moduleList.Any(e => e.ModuleName == moduleEntry.ModuleName))
					{
						moduleList.Add(moduleEntry);
						log.Info($"[SDIT] Module - {moduleEntry.ModuleName}, Version {moduleEntry.VersionString}, Segment count: {moduleEntry.SegmentCount}");
					}	
				}
			}			

			return moduleList;
		}

		public static string SDIT_FILE_NAME = "SDIT.FDI";
		public static string INFO_FILE_EXTENSION = ".TXT";
		public static int SUB_FILE_NAME_LENGHT = 0x100;

		public IEnumerable<IDataSource> Extract(IDataSource source) {
			var data = source.Data;
			var st = new SpanStream(data, Endianness.BigEndian);

            log.Info("File Info");
            log.Info("-------------");
            log.Info($"Info file count: {Header.InfoEntriesCount}");
            log.Info($"Module file count: {Header.ModuleEntriesCount}");
            log.Info($"Total file count: {Header.TotalEntriesCount}");

			var basedir = Path.Combine(source.RequireBaseDirectory(), $"_SDDL.SEC");
            source.AddMetadata(new BaseDirectoryPath(basedir));

			st.Position = Unsafe.SizeOf<SddlSecHeader>();

			//get SDIT (always the first file)
			var (sditEntry, sditData) = GetFile(st);
			if (sditEntry.FileName != SDIT_FILE_NAME) {
				throw new InvalidDataException($"Expected {SDIT_FILE_NAME} as the first file, got {sditEntry.FileName}");
			}
			log.Info($"[SDIT] Name: {sditEntry.FileName}, Size: {sditEntry.FileSize}");
			if (_ctx.SaveSDIT) {
				var artifact = new MemoryDataSource(sditData) {
                	Name = sditEntry.FileName
            	};
            	artifact.SetChildOf(source);
            	artifact.AddMetadata(new OutputFileName(sditEntry.FileName));
            	artifact.AddMetadata(new OutputDirectoryName(basedir));
            	yield return artifact;
			}

			//.. parse SDIT to get module counts ..
			var moduleList = ParseSditToModules(sditData);
			
			//get Info files (.TXT)
			for (int i = 0; i < Header.InfoEntriesCount; i++)
			{
				var (fileEntry, fileData) = GetFile(st);
				if (!fileEntry.FileName.EndsWith(INFO_FILE_EXTENSION)) {
					throw new InvalidDataException($"Info file {fileEntry.FileName} does not have the expected extension {INFO_FILE_EXTENSION}");
				}
				log.Info($"[INFO] #{i + 1}/{Header.InfoEntriesCount} - Name: {fileEntry.FileName}, Size: {fileEntry.FileSize}");
				if (_ctx.SaveInfo) {
					var artifact = new MemoryDataSource(fileData) {
                		Name = fileEntry.FileName
            		};
            		artifact.SetChildOf(source);
            		artifact.AddMetadata(new OutputFileName(fileEntry.FileName));
            		artifact.AddMetadata(new OutputDirectoryName(basedir));
            		yield return artifact;
				}

				//print content of info file
				var infoString = fileData.AsString(Encoding.UTF8);
				foreach(var line in infoString.Split("\n")) {
					log.Info($"{line}");
				}
			}

			//here process the modules that were parsed from SDIT (all following files are modules in the order they appear in SDIT)
			int iModule = 0;
			foreach (var module in moduleList)
			{
				var fileName = $"{module.ModuleName}.bin";
				var filePath = Path.Combine(basedir, fileName);

				log.Info($"#{iModule + 1}/{moduleList.Count} saving Module (name='{module.ModuleName}'," +
					$" segment_count={module.SegmentCount}" +
					$") to file {filePath}");

				var moduleBuf = new MemoryDataSourceBuffer(fileName, DataSourceFlags.Output);

				for (int iSegment = 0; iSegment < module.SegmentCount; iSegment++)
				{
					var (moduleEntry, moduleData) = GetFile(st);
					if (!moduleEntry.FileName.StartsWith(module.ModuleName)) {
						throw new InvalidDataException($"Module file {moduleEntry.FileName} does not start with the module name {module.ModuleName}");
					}

					//parse module seg data
					var moduleStream = new SpanStream(moduleData, Endianness.BigEndian);
					var moduleHeader = moduleStream.ReadStruct<ModuleHeader>();
					if (!moduleHeader.HeaderMagic.SequenceEqual(SddlSecHeader.SDDL_SEC_HEADER_MAGIC)){
						throw new InvalidDataException("Invalid module header magic");
					}

					var storedData = moduleStream.ReadBytes(moduleHeader.StoredDataSize);
					var decipheredData = moduleHeader.isCiphered? Decipher(storedData) : storedData;
					var finalData = moduleHeader.isCompressed ? DecompressZlib(decipheredData) : decipheredData;

					var contentReader = new SpanStream(finalData, Endianness.BigEndian);
					var contentHeader = contentReader.ReadStruct<ContentHeader>();

					//TODO: figure out how to handle this
					var subFileName = contentHeader.hasSubFile ? contentReader.ReadBytes(SUB_FILE_NAME_LENGHT).AsString(Encoding.ASCII) : null;

					//seek to data start
					contentReader.Position = contentHeader.SourceOffset;
					var outData = contentReader.ReadBytes(contentHeader.Size);
					
					log.Info($"	segment #{iSegment + 1}/{module.SegmentCount} (name='{moduleEntry.FileName}'," +
						$" size={moduleEntry.FileSize}," +
						$" version='{moduleHeader.VersionString}'" +
						$" compressed='{moduleHeader.isCompressed}'" +
						$"{(contentHeader.hasSubFile ? $", target='{subFileName}'" : "")}" +
						$") --> 0x{contentHeader.Size:X} @ 0x{contentHeader.DestOffset:X}");

					moduleBuf.WriteAt((int)contentHeader.DestOffset, outData);
				}

				var artifact = moduleBuf.ToDataSource();
				artifact.SetChildOf(source);
				artifact.AddMetadata(new OutputFileName(fileName));
				artifact.AddMetadata(new OutputDirectoryName(basedir));
				artifact.Flags |= DataSourceFlags.ProcessFurther;
				yield return artifact;

				iModule++;
			}
  		}
	}
}