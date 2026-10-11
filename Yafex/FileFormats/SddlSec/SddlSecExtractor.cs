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
	public class SddlSecExtractor : IFormatExtractor
	{
        private readonly SddlSecContext _ctx;
        private static readonly ILog log = LogManager.GetLogger(nameof(SddlSecExtractor));
		private const string AES_KEY_ID = "sddl-sec-key";

		// Entry-key trial order (key values live in the caller's key
		// bundle, never here): new AES-128-CBC/PKCS7 first, then old
		// AES-128-CBC/NoPadding keys, then old 3DES-CBC/NoPadding keys.
		private static readonly (string Id, bool Old, bool TripleDes, PaddingMode Padding)[] TrialKeys =
		[
			(AES_KEY_ID, false, false, PaddingMode.PKCS7),
			("sddl-sec-old-aes-2009", true, false, PaddingMode.None),
			("sddl-sec-old-aes-jpn", true, false, PaddingMode.None),
			("sddl-sec-old-des-2010", true, true, PaddingMode.None),
			("sddl-sec-old-des-pre2009", true, true, PaddingMode.None),
		];

		private readonly KeysRepository _keys;
		private AesDecryptor? _newDecryptor;
		private Func<byte[], Memory64<byte>>? _entryDecrypt;
		private bool _trialDone;
		private bool _isOld;

		/// <summary>True once the entry-key trial selected an old-variant
		/// key (old entry framing + old module headers apply).</summary>
		public bool IsOldFormat => _trialDone && _isOld;

		public SddlSecExtractor(SddlSecContext ctx, KeysRepository keys) {
            _ctx = ctx;
			_keys = keys;
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

		private static byte[] DecryptBlocks(byte[] cipher, byte[] key, byte[] iv, bool tripleDes, PaddingMode padding)
		{
			using SymmetricAlgorithm algo = tripleDes ? TripleDES.Create() : Aes.Create();
			algo.Mode = CipherMode.CBC;
			algo.Padding = padding;
			algo.Key = key;
			algo.IV = iv;
			using var transform = algo.CreateDecryptor();
			return transform.TransformFinalBlock(cipher, 0, cipher.Length);
		}

		private static bool LooksLikeEntryHeader(byte[] plain)
		{
			if (plain.Length < 24) return false;
			for (int i = 0; i < 8; i++)
			{
				byte b = plain[i];
				if (b != 0 && (b < 0x20 || b > 0x7E)) return false;
			}
			var sizeText = Encoding.ASCII.GetString(plain, 12, 12).Trim(' ', '\0');
			if (sizeText.Length == 0 || sizeText.Any(c => c < '0' || c > '9')) return false;
			return uint.TryParse(sizeText, out uint size) && size >= 1 && size <= (1u << 30);
		}

		// Resolves the entry key once per extraction by trial-decrypting
		// the 32-byte entry header at the current stream position. The
		// stream position is preserved; later entries reuse the winner.
		private void EnsureTrial(SpanStream dataStream)
		{
			if (_trialDone) return;
			long pos = dataStream.Position;
			byte[] header;
			try
			{
				header = dataStream.ReadBytes(EntryHeader.PADDED_SIZE).ToArray();
			}
			finally
			{
				dataStream.Position = pos;
			}

			foreach (var (id, old, tripleDes, padding) in TrialKeys)
			{
				byte[] key, iv;
				if (!old)
				{
					if (!_keys.TryGetKey(id, out var entry)) continue;
					key = entry.key;
					iv = entry.iv;
					if (key.Length != 16 || iv.Length != 16) continue;
				}
				else
				{
					if (!_keys.TryGetRaw(id, out key, out iv)) continue;
					if (tripleDes ? (key.Length != 24 || iv.Length != 8)
						: (key.Length != 16 || iv.Length != 16)) continue;
				}

				byte[] plain;
				try
				{
					plain = DecryptBlocks(header, key, iv, tripleDes, padding);
				}
				catch (CryptographicException)
				{
					continue;
				}
				if (!LooksLikeEntryHeader(plain)) continue;

				if (!old)
				{
					var aes = Aes.Create();
					aes.Key = key;
					aes.IV = iv;
					aes.Mode = CipherMode.CBC;
					aes.Padding = PaddingMode.PKCS7;
					_newDecryptor = new AesDecryptor(aes);
					_entryDecrypt = bytes => _newDecryptor.Decrypt(bytes);
				}
				else
				{
					var k = (byte[])key.Clone();
					var v = (byte[])iv.Clone();
					var td = tripleDes;
					_entryDecrypt = bytes => new Memory64<byte>(DecryptBlocks(bytes, k, v, td, PaddingMode.None));
				}
				_isOld = old;
				_trialDone = true;
				log.Info($"[SDDL] entry key trial matched '{id}' (old-format={old})");
				return;
			}

			throw new InvalidDataException("No SDDL entry key trial matched the first entry header");
		}

		private static uint ParseAsciiCount(byte[] buf, ref int at, int width, string what)
		{
			if (at < 0 || (long)at + width > buf.Length)
				throw new InvalidDataException($"Old SDDL entry {what} count runs past the payload");
			var text = Encoding.ASCII.GetString(buf, at, width).Trim(' ', '\0');
			at += width;
			if (text.Length == 0 || text.Any(c => c < '0' || c > '9') || !uint.TryParse(text, out uint value))
				throw new InvalidDataException($"Old SDDL entry has a non-numeric {what} count");
			return value;
		}

		internal (EntryHeader Header, long DataOffset) ReadEntryHeader(SpanStream dataStream)
		{
			EnsureTrial(dataStream);
			var entryHeaderEncrypted = dataStream.ReadBytes(EntryHeader.PADDED_SIZE);
			var entryHeaderBytes = _entryDecrypt!(entryHeaderEncrypted);
			var entryHeader = entryHeaderBytes.Cast<byte, EntryHeader>().Span[0];
			return (entryHeader, dataStream.Position);
		}

		public (EntryHeader, Memory64<byte>) GetFile(SpanStream dataStream)
		{
			var (entryHeader, _) = ReadEntryHeader(dataStream);

			if (!_isOld)
			{
				var entryDataEncrypted = dataStream.ReadBytes(entryHeader.FileSize);
				var entryData = _entryDecrypt!(entryDataEncrypted);
				return (entryHeader, entryData);
			}

			// Old framing: a display-only ciphered run follows the header
			// (counted in FileSize, never decrypted), then one NoPadding
			// blob holding a second skip count, a 12-char data size and
			// the payload.
			var skipped = ParseStreamCount(dataStream);
			if ((ulong)skipped + 4 > entryHeader.FileSize)
				throw new InvalidDataException("Old SDDL entry display count exceeds file size");
			dataStream.ReadBytes(skipped);
			uint encSize = entryHeader.FileSize - 4u - skipped;
			byte[] inner;
			try
			{
				inner = _entryDecrypt!(dataStream.ReadBytes(encSize)).ToArray();
			}
			catch (CryptographicException ex)
			{
				throw new InvalidDataException("Old SDDL entry payload failed to decrypt", ex);
			}

			int at = 0;
			uint dataSkip = ParseAsciiCount(inner, ref at, 4, "data");
			// The M field is followed by M + 4 skipped bytes, then the
			// 12-char data size and the payload.
			if ((ulong)at + dataSkip + 4 + 12 > (ulong)inner.Length)
				throw new InvalidDataException("Old SDDL entry data skip runs past the payload");
			at += (int)dataSkip + 4;
			uint dataSize = ParseAsciiCount(inner, ref at, 12, "data size");
			if ((ulong)at + dataSize > (ulong)inner.Length)
				throw new InvalidDataException("Old SDDL entry payload runs past the decrypted blob");
			var payload = new byte[dataSize];
			Array.Copy(inner, at, payload, 0, (int)dataSize);
			return (entryHeader, new Memory64<byte>(payload));
		}

		private static uint ParseStreamCount(SpanStream stream)
		{
			var raw = stream.ReadBytes(4).ToArray();
			int at = 0;
			return ParseAsciiCount(raw, ref at, 4, "display");
		}

		// Assembles one module segment payload: header (new 48-byte magic
		// header or old 32+16-byte header), optional stream decipher and
		// zlib inflation, then the shared content-header/data cut. Used by
		// both the extractor and the lazy VFS so old modules behave the
		// same on both paths. Must follow GetFile (the key trial decides
		// which header layout applies).
		internal (uint DestOffset, byte[] Data, string Version, bool Compressed, bool HasSubFile, string? SubFile, uint Size) AssembleSegment(Memory64<byte> moduleData)
		{
			if (!_trialDone) throw new InvalidOperationException("Entry key trial has not run");
			if (!_isOld)
			{
				var moduleStream = new SpanStream(moduleData, Endianness.BigEndian);
				var moduleHeader = moduleStream.ReadStruct<ModuleHeader>();
				if (!moduleHeader.HeaderMagic.SequenceEqual(SddlSecHeader.SDDL_SEC_HEADER_MAGIC)){
					throw new InvalidDataException("Invalid module header magic");
				}

				var storedData = moduleStream.ReadBytes(moduleHeader.StoredDataSize);
				var decipheredData = moduleHeader.isCiphered? Decipher(storedData) : storedData;
				var finalData = moduleHeader.isCompressed ? DecompressZlib(decipheredData) : decipheredData;
				return FinishSegment(finalData, moduleHeader.VersionString, moduleHeader.isCompressed);
			}
			else
			{
				if (moduleData.Length < ModuleHeaderOld.COM_SIZE + Unsafe.SizeOf<ModuleHeaderOld>())
					throw new InvalidDataException("Old SDDL module segment too short for its header");
				var moduleStream = new SpanStream(moduleData, Endianness.BigEndian);
				moduleStream.Position = ModuleHeaderOld.COM_SIZE;
				var oldHeader = moduleStream.ReadStruct<ModuleHeaderOld>();

				var storedData = moduleStream.ReadBytes(oldHeader.CompressedSize);
				var decipheredData = oldHeader.isCiphered ? Decipher(storedData) : storedData;
				var finalData = oldHeader.isCompressed ? DecompressZlib(decipheredData) : decipheredData;
				return FinishSegment(finalData, $"old:{oldHeader.Id:X4}", oldHeader.isCompressed);
			}
		}

		private static (uint DestOffset, byte[] Data, string Version, bool Compressed, bool HasSubFile, string? SubFile, uint Size) FinishSegment(Memory64<byte> finalData, string version, bool compressed)
		{
			var contentReader = new SpanStream(finalData, Endianness.BigEndian);
			var contentHeader = contentReader.ReadStruct<ContentHeader>();

			//TODO: figure out how to handle this
			var subFileName = contentHeader.hasSubFile ? contentReader.ReadBytes(SUB_FILE_NAME_LENGHT).AsString(Encoding.ASCII) : null;

			//seek to data start
			contentReader.Position = contentHeader.SourceOffset;
			var outData = contentReader.ReadBytes(contentHeader.Size);

			return (contentHeader.DestOffset, outData, version, compressed, contentHeader.hasSubFile, subFileName, contentHeader.Size);
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
		private const string PEAKS_MODULE = "PEAKS";

		// Splits an assembled PEAKS module image into its fmaN partitions:
		// the "D50 " marker locates the boot-args block, whose root=/dev and
		// fmaN=size lines drive sequential cuts clamped at EOF. Each part
		// is emitted as a fmaN.bin sibling flagged for nested detection.
		// A ":c" flag marks a zlib-block container (§DecompressZlibBlocks);
		// a failed decompress warns and keeps the raw cut.
		private IEnumerable<IDataSource> SplitPeaks(List<(uint Dest, byte[] Data)> pieces, IDataSource source, string basedir)
		{
			long end = 0;
			foreach (var (dest, data) in pieces)
			{
				end = Math.Max(end, (long)dest + data.Length);
			}
			var image = new byte[end];
			foreach (var (dest, data) in pieces)
			{
				Array.Copy(data, 0, image, (long)dest, data.Length);
			}

			int search = (int)Math.Min(image.Length, 8L * 1024 * 1024);
			var marker = new byte[] { (byte)'D', (byte)'5', (byte)'0', (byte)' ' };
			int markerAt = -1;
			for (int i = 0; i + 4 <= search; i++)
			{
				if (image[i] == marker[0] && image[i + 1] == marker[1]
					&& image[i + 2] == marker[2] && image[i + 3] == marker[3])
				{
					markerAt = i;
					break;
				}
			}
			if (markerAt < 0) yield break;
			// Boot args: 0x200 ASCII bytes at the D50 marker + 16.
			if ((long)markerAt + 16 + 0x200 > image.Length) yield break;
			var args = Encoding.ASCII.GetString(image, markerAt + 16, 0x200);

			string? root = null;
			var parts = new List<(string Name, ulong Size, string? Flag)>();
			foreach (var rawLine in args.Split('\n'))
			{
				var line = rawLine.Trim(' ', '\r', '\0');
				if (line.StartsWith("root=/dev/", StringComparison.Ordinal))
				{
					root = line.Substring("root=/dev/".Length).Trim();
				}
				else if (line.StartsWith("fma", StringComparison.Ordinal))
				{
					var cut = line.Substring(3);
					int eq = cut.IndexOf('=');
					if (eq <= 0) continue;
					var num = cut.Substring(0, eq);
					var rest = cut.Substring(eq + 1);
					string? flag = null;
					int colon = rest.IndexOf(':');
					if (colon >= 0)
					{
						flag = rest.Substring(colon + 1);
						rest = rest.Substring(0, colon);
					}
					if (num.Any(c => c < '0' || c > '9')) continue;
					ulong mult = 1;
					if (rest.EndsWith('k') || rest.EndsWith('K'))
					{
						mult = 1024;
						rest = rest.Substring(0, rest.Length - 1);
					}
					else if (rest.EndsWith('M'))
					{
						mult = 1048576;
						rest = rest.Substring(0, rest.Length - 1);
					}
					if (rest.Length == 0 || rest.Any(c => c < '0' || c > '9')) continue;
					if (!ulong.TryParse(rest, out ulong numSize)) continue;
					parts.Add(($"fma{num}", numSize * mult, string.IsNullOrEmpty(flag) ? null : flag));
				}
			}

			if (root == null || parts.Count == 0) yield break;
			int rootIndex = parts.FindIndex(p => p.Name == root);
			if (rootIndex < 0) yield break;
			int start = Math.Max(0, rootIndex - 1);

			// Cuts start at the D50 marker: it heads the partition area
			// (any leading bytes are padding, not fma data).
			long pos = markerAt;
			for (int i = start; i < parts.Count; i++)
			{
				if (pos >= image.Length) break;
				long take = (long)Math.Min(parts[i].Size, (ulong)(image.Length - pos));
				var cutBytes = new byte[take];
				Array.Copy(image, pos, cutBytes, 0, take);
				pos += (long)parts[i].Size;

				byte[] emit = cutBytes;
				if (parts[i].Flag == "c")
				{
					try
					{
						emit = DecompressZlibBlocks(cutBytes);
					}
					catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException)
					{
						log.Warn($"[PEAKS] zlib-block decompress of {parts[i].Name} failed, keeping raw bytes: {ex.Message}");
					}
				}

				var part = ArtifactBuilder.FlatIn(source, basedir, $"{parts[i].Name}.bin", emit);
				part.Flags |= DataSourceFlags.Output | DataSourceFlags.ProcessFurther;
				log.Info($"[PEAKS] split {parts[i].Name}.bin ({emit.Length} bytes{(parts[i].Flag == "c" ? ", zlib-blocks" : "")})");
				yield return part;
			}
		}

		private static uint ReadU32(byte[] buf, int at, bool littleEndian)
		{
			if (littleEndian)
				return (uint)(buf[at] | (buf[at + 1] << 8) | (buf[at + 2] << 16) | (buf[at + 3] << 24));
			return (uint)((buf[at] << 24) | (buf[at + 1] << 16) | (buf[at + 2] << 8) | buf[at + 3]);
		}

		// zlib-block container: u32 uncompressed size + u32 first block
		// offset, then an offset table starting AT byte 4 whose first
		// entry repeats the first block offset. Each block inflates
		// independently (the last spans a fixed 16384 bytes) and the
		// results concatenate in order. Endianness follows the size
		// field: leading 00 00 means little-endian, else big-endian.
		private static byte[] DecompressZlibBlocks(byte[] blob)
		{
			if (blob.Length < 8)
				throw new InvalidDataException("zlib-block container too short for its header");
			bool littleEndian = blob[0] == 0 && blob[1] == 0;
			uint firstBlk = ReadU32(blob, 4, littleEndian);
			if (firstBlk < 8 || firstBlk > (uint)blob.Length || ((firstBlk - 4) % 4) != 0)
				throw new InvalidDataException("zlib-block container has a bad first-block offset");
			int count = (int)((firstBlk - 4) / 4);
			if (count == 0)
				throw new InvalidDataException("zlib-block container has an empty offset table");
			var locs = new uint[count];
			for (int i = 0; i < count; i++)
			{
				locs[i] = ReadU32(blob, 4 + 4 * i, littleEndian);
			}
			if (locs[0] != firstBlk)
				throw new InvalidDataException("zlib-block offset table does not start at the first block");
			for (int i = 0; i < count; i++)
			{
				if (locs[i] > (uint)blob.Length)
					throw new InvalidDataException("zlib-block offset runs past the container");
				if (i > 0 && locs[i] <= locs[i - 1])
					throw new InvalidDataException("zlib-block offsets are not ascending");
			}
			if ((ulong)locs[count - 1] + 16384 > (ulong)blob.Length)
				throw new InvalidDataException("zlib-block final block runs past the container");

			using var output = new MemoryStream();
			for (int i = 0; i < count - 1; i++)
			{
				var span = blob[(int)locs[i]..(int)locs[i + 1]];
				var inflated = DecompressZlib(new Memory64<byte>(span)).ToArray();
				output.Write(inflated, 0, inflated.Length);
			}
			var last = blob[(int)locs[count - 1]..(int)(locs[count - 1] + 16384)];
			var lastInflated = DecompressZlib(new Memory64<byte>(last)).ToArray();
			output.Write(lastInflated, 0, lastInflated.Length);
			return output.ToArray();
		}

		public IEnumerable<IDataSource> Extract(IDataSource source) {
			var data = source.Data;
			var st = new SpanStream(data, Endianness.BigEndian);

            log.Info("File Info");
            log.Info("-------------");
            log.Info($"Info file count: {Header.InfoEntriesCount}");
            log.Info($"Module file count: {Header.ModuleEntriesCount}");
            log.Info($"Total file count: {Header.TotalEntriesCount}");

			var basedir = ArtifactBuilder.VersionedDir(source, $"_SDDL.SEC");
            source.AddMetadata(new BaseDirectoryPath(basedir));

			st.Position = Unsafe.SizeOf<SddlSecHeader>();

			//get SDIT (always the first file)
			var (sditEntry, sditData) = GetFile(st);
			if (sditEntry.FileName != SDIT_FILE_NAME) {
				throw new InvalidDataException($"Expected {SDIT_FILE_NAME} as the first file, got {sditEntry.FileName}");
			}
			log.Info($"[SDIT] Name: {sditEntry.FileName}, Size: {sditEntry.FileSize}");
			if (_ctx.SaveSDIT) {
				yield return ArtifactBuilder.FlatIn(source, basedir, sditEntry.FileName, sditData);
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
					yield return ArtifactBuilder.FlatIn(source, basedir, fileEntry.FileName, fileData);
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

				var moduleBuf = ArtifactBuilder.BeginBuffer(
                    basedir, fileName, DataSourceFlags.Output | DataSourceFlags.ProcessFurther);

				List<(uint Dest, byte[] Data)>? peaksPieces =
					module.ModuleName == PEAKS_MODULE ? new List<(uint, byte[])>() : null;

				for (int iSegment = 0; iSegment < module.SegmentCount; iSegment++)
				{
					var (moduleEntry, moduleData) = GetFile(st);
					if (!moduleEntry.FileName.StartsWith(module.ModuleName)) {
						throw new InvalidDataException($"Module file {moduleEntry.FileName} does not start with the module name {module.ModuleName}");
					}

					//parse module seg data
					var (destOffset, outData, version, compressed, hasSubFile, subFileName, size) = AssembleSegment(moduleData);
					peaksPieces?.Add((destOffset, outData));

					log.Info($"	segment #{iSegment + 1}/{module.SegmentCount} (name='{moduleEntry.FileName}'," +
						$" size={moduleEntry.FileSize}," +
						$" version='{version}'" +
						$" compressed='{compressed}'" +
						$"{(hasSubFile ? $", target='{subFileName}'" : "")}" +
						$") --> 0x{size:X} @ 0x{destOffset:X}");

					moduleBuf.WriteAt((int)destOffset, outData);
				}

				var artifact = ArtifactBuilder.Complete(moduleBuf, source, basedir);
				yield return artifact;

				if (peaksPieces != null)
				{
					foreach (var part in SplitPeaks(peaksPieces, source, basedir))
					{
						yield return part;
					}
				}

				iModule++;
			}
  		}
	}
}