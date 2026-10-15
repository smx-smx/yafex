using log4net;

using Smx.SharpIO.Extensions;
using Smx.SharpIO.Memory.Buffers;

using System;
using System.Collections.Generic;
using System.IO;

using Yafex.Support;

namespace Yafex.FileFormats.EpkV3
{
    public record Epk3NewDetectionResult(int Confidence, Epk3NewContext? Context) : DetectionResult(Confidence);

    public class Epk3NewExtractor : IFormatExtractor
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(Epk3NewExtractor));

        private readonly Epk3NewContext ctx;

        public Epk3NewExtractor(Epk3NewDetectionResult result)
        {
            // IsUsable guarantees a non-null context (FormatAddon rejects
            // unusable results before construction).
            this.ctx = result.Context!;
        }

        public IEnumerable<IDataSource> Extract(IDataSource source)
        {
            var fileData = source.Data;
            var span = fileData.AsReadonlySpan();

            var hdr = ctx.Header;

            logger.Info("Firmware Info");
            logger.Info("-------------");
            logger.Info($"Firmware magic: {hdr.EpkMagic}");
            logger.Info($"Firmware otaID: {hdr.OtaId}");
            logger.Info($"Firmware version: {hdr.EpkVersion}");

            var epk = fileData.ReadStruct<EPK_V3_NEW_STRUCTURE>();
            var image = new Epk3NewImage(ctx, span, epk);
            if (ctx.Variant != Epk3Variant.OLD && image.PakInfoMagic != hdr.pakInfoMagic)
            {
                var msg = string.Format("{0} mismatch! (expected:{1:X}, actual: {2:X})",
                        nameof(hdr.pakInfoMagic),
                        hdr.pakInfoMagic,
                        image.PakInfoMagic);
                logger.Error(msg);
                throw new InvalidDataException(msg);
            }

            var artifacts = new List<IDataSource>();

            var decryptor = ctx.GetDecryptor(EPK_V3_NEW_HEADER.EPK3_MAGIC);

            var fwVersion = $"{hdr.EpkVersion}-{hdr.OtaId}";
            var baseDir = ArtifactBuilder.VersionedDir(source, fwVersion);

            var curPak = 0;
            foreach (var pak in image.Paks)
            {
                var buff = ArtifactBuilder.BeginBuffer(baseDir, pak.Image.Name, DataSourceFlags.Output);

                foreach (var seg in pak.Image.Segments)
                {
                    if (seg.WasDecrypted && seg.Decryptor != null)
                    {
                        var encrypted = span.Slice(seg.PhysicalDataOffset, seg.EncryptedSize);
                        var decrypted = seg.Decryptor.Decrypt(encrypted);
                        buff.Write(decrypted.Slice(seg.ContentOffset, seg.LogicalSize).ToArray());
                    }
                    else
                    {
                        buff.Write(span.Slice(seg.PhysicalDataOffset + seg.ContentOffset, seg.LogicalSize).ToArray());
                    }
                }

                var outputFile = ArtifactBuilder.Complete(buff, source, baseDir);

                logger.Info($"#{curPak + 1}/{image.Paks.Count} saved PAK ({pak.Name}) to file {outputFile.Name}");
                ++curPak;
                artifacts.Add(outputFile);
            }

            return artifacts;
        }
    }
}