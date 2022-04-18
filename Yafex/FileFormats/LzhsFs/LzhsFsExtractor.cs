using log4net;
using Smx.Yafex.FileFormats.Lzhs;
using Smx.Yafex.Support;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Smx.Yafex.FileFormats.LzhsFs
{
	public class LzhsFsReader
	{
		public const int UNCOMPRESSED_HEADING_SIZE = 0x100000;

		private IDataSource source;
		private Span<byte> span => source.Data.Span;

		public LzhsFsReader(IDataSource source) {
			this.source = source;
		}

		public byte[] GetUncompressedHeading() {
			return span.Slice(0, UNCOMPRESSED_HEADING_SIZE).ToArray();
		}

		private uint Pad(uint num, uint align = 16) {
			uint rem = num % align;
			uint pad = rem switch {
				0 => 0,
				_ => 16 - rem
			};
			return num + pad;
		}

		public int GetOutputSize() {
			int size = UNCOMPRESSED_HEADING_SIZE;
			foreach(var chunk in GetChunks()) {
				size += (int)chunk.Header.uncompressedSize;
			}
			return size;
		}

		public IEnumerable<LzhsChunk> GetChunks() {
			var data = source.Data;

			int inOffset = UNCOMPRESSED_HEADING_SIZE;
			int outOffset = UNCOMPRESSED_HEADING_SIZE;

			var lzhsHeaderSize = Marshal.SizeOf<LzhsHeader>();

			while(inOffset < data.Length) {
				var outer = new LzhsHeader(span.Slice(inOffset, lzhsHeaderSize));
				// outer header contains segment number instead of checksum
				var chunkNo = outer.checksum;

				// compressed size excludes header size
				var chunkTotalSize = outer.compressedSize + lzhsHeaderSize;

				var lzhsStart = inOffset + lzhsHeaderSize;
				var lzhsSize = (int)Pad((uint)(chunkTotalSize - lzhsHeaderSize));

				var chunkBuf = data.Slice(lzhsStart, lzhsSize);

				var chunk = new LzhsChunk(chunkNo, lzhsSize, outOffset, chunkBuf);
				yield return chunk;

				inOffset = lzhsStart + lzhsSize;
				outOffset += (int)chunk.Header.uncompressedSize;
			}
		}
	}

	public class LzhsFsWriter : IDisposable {
		private static readonly ILog log = LogManager.GetLogger(nameof(LzhsFsWriter));

		private readonly MFile mfOut;
		public LzhsFsWriter(string outputPath, int outputSize) {
			mfOut = new MFile(outputPath, readOnly: false);
			mfOut.SetLength(outputSize);
		}

		private void DumpFailedChunk(LzhsChunk chunk) {
			string dumpPath = Path.Combine(Path.GetTempPath(), $"lzhs_{chunk.index}.bin");
			log.Debug($"Failed chunk dumped: {dumpPath}");
			File.WriteAllBytes(dumpPath, chunk.buf.ToArray());
		}

		public void WriteChunk(LzhsChunk chunk) {
			Trace.WriteLine($"{chunk.index}: {chunk.size}");
			var decoder = chunk.NewDecoder();

			var ptrOut = mfOut.Data.Span.Slice(chunk.outputOffset);
			int i = 0;
			foreach (var b in decoder.AsEnumerable()) {
				ptrOut[i] = b;
				++i;
			}

			if (!decoder.VerifyChecksum()) {
				log.Error("-- CHECKSUM VERIFICATION FAILED --");
				//DumpFailedChunk(chunk);
			}
		}

		public void WriteData(byte[] data, int fileOffset) {
			var ptrOut = mfOut.Data.Slice(fileOffset);
			data.CopyTo(ptrOut);
		}

		public void Dispose() {
			mfOut.Dispose();
		}
	}

	public class LzhsFsExtractor : IFormatExtractor
	{
		private static readonly ILog log = LogManager.GetLogger(nameof(LzhsFsExtractor));

		private Config config;
		private DetectionResult result;

		public LzhsFsExtractor(Config config, DetectionResult result) {
			this.config = config;
			this.result = result;
		}

		public IEnumerable<IDataSource> Extract(IDataSource source) {
			string fileName = Path.GetFileNameWithoutExtension(source.Name);
			string destPath = Path.Combine(config.DestDir, $"{fileName}.ext4");

			int activeThreads = 0;
			ManualResetEvent allFinished = new ManualResetEvent(false);

			var rdr = new LzhsFsReader(source);
			using (var writer = new LzhsFsWriter(destPath, rdr.GetOutputSize())) {
				writer.WriteData(rdr.GetUncompressedHeading(), 0);
				foreach(var chunk in rdr.GetChunks()) {
					Interlocked.Increment(ref activeThreads);
					ThreadPool.QueueUserWorkItem(arg => {
						log.Info($"Extracting chunk {chunk.index} -> 0x{chunk.outputOffset:X8} (0x{chunk.size:X8})");
						log.Info($"    0x{chunk.Header.compressedSize:X8} -> 0x{chunk.Header.uncompressedSize:X8}");
						writer.WriteChunk(chunk);
						if(Interlocked.Decrement(ref activeThreads) == 0) {
							allFinished.Set();
						}
					});					
				}

				allFinished.WaitOne();
			}

			return Enumerable.Empty<IDataSource>();
		}
	}
}