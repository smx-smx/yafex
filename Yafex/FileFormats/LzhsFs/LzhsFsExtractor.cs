#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
using log4net;

using Yafex.FileFormats.Lzhs;
using Yafex.Support;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Smx.SharpIO.Memory.Buffers;
using Smx.SharpIO.Extensions;
using System.Threading.Tasks;

namespace Yafex.FileFormats.LzhsFs
{
    public class LzhsFsReader
    {
        public const long UNCOMPRESSED_HEADING_SIZE = 0x100000;

        private IDataSource source;
        private Span64<byte> span => source.Data.Span;

        public LzhsFsReader(IDataSource source)
        {
            this.source = source;
        }

        public byte[] GetUncompressedHeading()
        {
            return span.Slice(0, UNCOMPRESSED_HEADING_SIZE).ToArray();
        }

        private uint Pad(uint num, uint align = 16)
        {
            uint rem = num % align;
            uint pad = rem switch
            {
                0 => 0,
                _ => 16 - rem
            };
            return num + pad;
        }

        public long GetOutputSize()
        {
            var size = UNCOMPRESSED_HEADING_SIZE;
            foreach (var chunk in GetChunks())
            {
                size += chunk.SizeUncompressed;
            }
            return size;
        }

        public IEnumerable<LzhsChunk> GetChunks()
        {
            var data = source.Data;

            long inOffset = UNCOMPRESSED_HEADING_SIZE;
            long outOffset = UNCOMPRESSED_HEADING_SIZE;

            var lzhsHeaderSize = Marshal.SizeOf<LzhsHeader>();

            while (inOffset < data.Length)
            {
                var chunk = new LzhsChunk(data, inOffset, outOffset);
                inOffset += chunk.SegmentSize;
                outOffset += chunk.SizeUncompressed;
                yield return chunk;
            }
        }
    }

    public class LzhsFsWriter : IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(nameof(LzhsFsWriter));

        private readonly MFile mfOut;
        public LzhsFsWriter(string outputPath, long outputSize)
        {
            mfOut = new MFile(outputPath, readOnly: false);
            mfOut.SetLength(outputSize);
        }

        private void DumpFailedChunk(LzhsChunk chunk)
        {
            string dumpPath = Path.Combine(Path.GetTempPath(), $"lzhs_{chunk.SegmentIndex}.bin");
            log.Debug($"Failed chunk dumped: {dumpPath}");
            File.WriteAllBytes(dumpPath, chunk.SegmentData.ToArray());
        }

        public void WriteChunk(LzhsChunk chunk)
        {
            var ptrOut = mfOut.Data.Span.Slice(chunk.OutputOffset);

            if (chunk.IsUncompressed)
            {
                var raw = chunk.SegmentData.Span.Slice(LzhsHeader.SIZE, chunk.SegmentHeader.UncompressedSize);
                raw.CopyTo(ptrOut);
                return;
            }

            var decoder = chunk.NewDecoder();       
            int i = 0;
            foreach (var b in decoder.AsEnumerable())
            {
                ptrOut[i] = b;
                ++i;
            }

            if (!decoder.VerifyChecksum())
            {
                log.Error("-- CHECKSUM VERIFICATION FAILED --");
                //DumpFailedChunk(chunk);
            }
        }

        public void WriteData(byte[] data, long fileOffset)
        {
            var ptrOut = mfOut.Data.Slice(fileOffset);
            data.CopyTo(ptrOut);
        }

        public void Dispose()
        {
            mfOut.Dispose();
        }
    }

    public class LzhsFsExtractor : IFormatExtractor
    {
        private static readonly ILog log = LogManager.GetLogger(nameof(LzhsFsExtractor));

        private DetectionResult result;

        public LzhsFsExtractor(DetectionResult result)
        {
            this.result = result;
        }

        public IEnumerable<IDataSource> Extract(IDataSource source)
        {
            string fileName = Path.GetFileNameWithoutExtension(source.Name);
            string destPath = Path.Combine(source.RequireBaseDirectory(), $"{fileName}.unlzhs");

            var rdr = new LzhsFsReader(source);
            using (var writer = new LzhsFsWriter(destPath, rdr.GetOutputSize()))
            {
                writer.WriteData(rdr.GetUncompressedHeading(), 0);
                var chunks = rdr.GetChunks();
#if LZHS_DEBUG
                foreach(var chunk in chunks)
                {
                    log.Info($"[{chunk.InputOffset:X8}]: Extracting chunk {chunk.SegmentIndex} -> 0x{chunk.OutputOffset:X8} (0x{chunk.SegmentSize:X8}) {(chunk.IsUncompressed ? "[UNCOMPRESSED]" : "")}");
                    writer.WriteChunk(chunk);
                }
#else
                Parallel.ForEach(chunks, chunk =>
                {
                    log.Info($"[{chunk.InputOffset:X8}]: Extracting chunk {chunk.SegmentIndex} -> 0x{chunk.OutputOffset:X8} (0x{chunk.SegmentSize:X8}) {(chunk.IsUncompressed ? "[UNCOMPRESSED]" : "")}");
                    writer.WriteChunk(chunk);
                });
#endif
            }

            return Enumerable.Empty<IDataSource>();
        }
    }
}
