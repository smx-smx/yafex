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
using Yafex.FileFormats.Epk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Smx.SharpIO;
using Yafex.Metadata;
using Yafex.Support;
using Smx.SharpIO.Memory.Buffers;

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

    public record Epk2DetectionResult<T>(int Confidence, EpkContext<T>? Context) : DetectionResult(Confidence)
        where T : struct
    { }

    public abstract class AbstractEpk2Extractor<THeader> : IFormatExtractor
        where THeader : struct, IEpkV2Header
    {
        private static readonly ILog logger = LogManager.GetLogger(nameof(EpkV2));

        private readonly EpkContext<THeader> _ctx;

        public AbstractEpk2Extractor(Epk2DetectionResult<THeader> result)
        {
            if (result.Context == null)
            {
                throw new ArgumentNullException(nameof(result.Context));
            }
            _ctx = result.Context;
        }

        protected abstract ReadOnlySpan64<T> GetPak2HeaderBytes<T>(ReadOnlySpan64<T> data) where T : unmanaged;
        protected abstract int PakStructureSize { get; }

        private (string, string, IDataSource) HandlePak(
            ReadOnlySpan64<byte> fileData,
            Epk2PakImage pak,
            string baseDir,
            DataSourceFlags flags
        )
        {
            var pakName = pak.Image.Name;
            var outputBuffer = NewPakBuffer(pak, flags);
            outputBuffer.AddMetadata(new OutputDirectoryName(baseDir));

            foreach (var seg in pak.Image.Segments)
            {
                var content = Epk2SegmentWalker.ReadSegmentContent(seg, fileData);
                outputBuffer.Write(content);
            }

            var outputFile = outputBuffer.ToDataSource();
            outputBuffer.Dispose();
            return (pakName, outputFile.Name, outputFile);
        }

        private MemoryDataSourceBuffer NewPakBuffer(Epk2PakImage pak, DataSourceFlags flags)
        {
            var processFurther = pak.Header.ImageType switch
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

            var buff = new MemoryDataSourceBuffer(pak.Image.Name, flags);
            buff.AddMetadata(new OutputFileName(pak.Image.Name));
            return buff;
        }

        public IEnumerable<IDataSource> Extract(IDataSource source)
        {
            return Extract(source, DataSourceFlags.Output);
        }

        protected abstract int SignatureSize { get; }

        public IEnumerable<IDataSource> Extract(IDataSource source, DataSourceFlags outputFlags)
        {
            var fileData = source.Data;

            var hdr = _ctx.Header;


            logger.Info("Firmware Info");
            logger.Info("-------------");
            logger.Info($"Firmware magic: {hdr.FileType}");
            logger.Info($"Firmware type: {hdr.EpkMagic}");
            logger.Info($"Firmware otaID: {hdr.OtaId}");
            logger.Info($"Firmware version: {hdr.EpkVersion}");

            logger.Info($"PAK count: {hdr.FileNum}");
            logger.Info($"PAKs total size: {hdr.FileSize}");
            logger.Info($"Header length: 0x{hdr.GetImageOffset(0):X}");

            var fwVersion = $"{hdr.EpkVersion}-{hdr.OtaId}";
            // absolute: Path.Combine drops the base when the second part is
            // rooted, matching the EpkV1 convention (single-level fw dir)
            var baseDir = Path.Combine(source.RequireBaseDirectory(), fwVersion);

            var images = Epk2SegmentWalker.BuildPakImages(
                fileData.AsReadonlySpan(),
                _ctx,
                hdr,
                signatureSize: SignatureSize,
                pakStructureSize: PakStructureSize,
                getHeaderBytes: d => GetPak2HeaderBytes(d)
            );

            int curPak = 0;
            foreach (var pak in images)
            {
                (string pakName,
                 string pakOutputPath,
                 IDataSource outputFile) = HandlePak(
                     fileData.Span,
                     pak,
                     baseDir: baseDir,
                     outputFlags
                );
                outputFile.SetChildOf(source);

                logger.Info($"#{curPak + 1}/{_ctx.Header.FileNum} saved PAK ({pakName}) to file {pakOutputPath}");
                ++curPak;
                yield return outputFile;
            }
        }
    }

    public class Epk2Extractor : AbstractEpk2Extractor<EPK_V2_HEADER>
    {
        public Epk2Extractor(Epk2DetectionResult<EPK_V2_HEADER> result) : base(result)
        {
        }

        protected override int SignatureSize => EPK_V2_STRUCTURE.SIGNATURE_SIZE;

        protected override int PakStructureSize => Marshal.SizeOf<PAK_V2_STRUCTURE>();

        protected override ReadOnlySpan64<T> GetPak2HeaderBytes<T>(ReadOnlySpan64<T> data)
        {
            return PAK_V2_STRUCTURE.GetHeader(data);
        }
    }

    public class Epk2BetaExtractor : AbstractEpk2Extractor<EPK_V2_BETA_HEADER>
    {
        public Epk2BetaExtractor(Epk2DetectionResult<EPK_V2_BETA_HEADER> result) : base(result)
        {
        }

        protected override int SignatureSize => 0;

        protected override int PakStructureSize => Marshal.SizeOf<PAK_V2_BETA_STRUCTURE>();

        protected override ReadOnlySpan64<T> GetPak2HeaderBytes<T>(ReadOnlySpan64<T> data)
        {
            return PAK_V2_BETA_STRUCTURE.GetHeader(data);
        }
    }
}
