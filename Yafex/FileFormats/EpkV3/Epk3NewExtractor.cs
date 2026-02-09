using log4net;

using Smx.SharpIO.Extensions;
using Smx.SharpIO.Memory.Buffers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using Yafex.Support;

namespace Yafex.FileFormats.EpkV3
{
    public class Epk3NewExtractor : IFormatExtractor
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(Epk3NewExtractor));

        private readonly Epk3NewContext ctx;

        public Epk3NewExtractor(DetectionResult result)
        {
            this.ctx = (Epk3NewContext)result.Context!;

        }

        private IEnumerable<PAK_V3_HEADER> GetPackageInfoPackages(Memory64<byte> packageInfo, int packageInfoCount)
        {
            var listOffset = Marshal.SizeOf<PAK_V3_NEW_LISTHEADER>();
            var sizeOfPak = Marshal.SizeOf<PAK_V3_HEADER>();
            for (int i = 0; i < packageInfoCount; i++)
            {
                var pak = packageInfo.ReadStruct<PAK_V3_HEADER>(listOffset);
                yield return pak;
                listOffset += sizeOfPak;
            }
        }

        private (PAK_V3_NEW_LISTHEADER, IDictionary<string, List<PAK_V3_HEADER>>) GetPackageInfo(EPK_V3_NEW_STRUCTURE epk, ReadOnlySpan64<byte> data)
        {
            var offset = Marshal.OffsetOf<EPK_V3_NEW_STRUCTURE>(nameof(epk.packageInfo)).ToInt32();

            var packageInfoBytes = data.Slice(offset, ctx.Header.packageInfoSize);
            var decryptor = ctx.GetDecryptor(EPK_V3_NEW_HEADER.EPK3_MAGIC);
            var decrypted = decryptor.Decrypt(packageInfoBytes);

            var listHeader = decrypted.ReadStruct<PAK_V3_NEW_LISTHEADER>();
            var packages = GetPackageInfoPackages(decrypted, (int)listHeader.packageInfoCount)
                .GroupBy(p => p.packageName.AsString(Encoding.ASCII))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(p => p).ToList()
                );


            return (listHeader, packages);
        }

        private MemoryDataSourceBuffer NewPakBuffer(PAK_V3_HEADER pakHdr)
        {
            var flags = DataSourceFlags.Output;
            var processFurther = true; // $TODO
            if (processFurther)
            {
                flags |= DataSourceFlags.ProcessFurther;
            }

            var buff = new MemoryDataSourceBuffer($"{pakHdr.packageName}.pak", flags);
            return buff;
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
            var (packageInfo, packages) = GetPackageInfo(epk, span);
            if (packageInfo.pakInfoMagic != hdr.pakInfoMagic)
            {
                var msg = string.Format("{0} mismatch! (expected:{1:X}, actual: {2:X})",
                        nameof(hdr.pakInfoMagic),
                        hdr.pakInfoMagic,
                        packageInfo.pakInfoMagic);
                logger.Error(msg);
                throw new InvalidDataException(msg);
            }

            var dataOffset = (0
                + Marshal.OffsetOf<EPK_V3_NEW_STRUCTURE>(nameof(epk.packageInfo)).ToInt32()
                + EPK_V3_NEW_STRUCTURE.SIGNATURE_SIZE
                + hdr.packageInfoSize
            );
            var dataPtr = span.Slice(dataOffset);

            var artifacts = new List<IDataSource>();

            var decryptor = ctx.GetDecryptor(EPK_V3_NEW_HEADER.EPK3_MAGIC);

            var i = 0;
            foreach (var pak in packages)
            {
                var buff = new MemoryDataSourceBuffer($"{pak.Key}.pak", DataSourceFlags.Output);

                var segmentIndex = pak.Value.First().segmentInfo.segmentIndex;
                foreach (var chunk in pak.Value)
                {
                    if (segmentIndex++ >= chunk.segmentInfo.segmentCount) break;

                    dataOffset += EPK_V3_NEW_STRUCTURE.SIGNATURE_SIZE;

                    if (chunk.packageName.AsString(Encoding.ASCII) == "intmicom")
                    {
                        chunk.ToString(); // $DEBUG
                    }

                    logger.DebugFormat("segment #{0} (name='{1}', version='{2}', offset='0x{3}', size='{4} bytes')",
                        chunk.segmentInfo.segmentIndex + 1,
                        chunk.packageName.AsString(Encoding.ASCII),
                        $"{chunk.packageVersion[3]}.{chunk.packageVersion[2]}.{chunk.packageVersion[1]}.{chunk.packageVersion[0]}",
                        dataOffset,
                        chunk.segmentInfo.segmentSize
                    );

                    var dataSize = sizeof(uint) + chunk.segmentInfo.segmentSize;
                    var chunkData = dataPtr.Slice(EPK_V3_NEW_STRUCTURE.SIGNATURE_SIZE, dataSize);

                    var decrypted = decryptor.Decrypt(chunkData);

                    var decryptedSegmentIndex = decrypted.Cast<uint>().Slice(0, 1)[0];
                    if (decryptedSegmentIndex != i)
                    {
                        logger.WarnFormat("Warning: Decrypted segment doesn't match expected index! (index: {0}, expected: {1})",
                            decryptedSegmentIndex, i);
                    }

                    buff.Write(decrypted.Slice(4).ToArray());

                    dataPtr = dataPtr.Slice(EPK_V3_NEW_STRUCTURE.SIGNATURE_SIZE + chunkData.Length);
                    dataOffset += chunkData.Length;

                    ++i;
                }

                artifacts.Add(buff.ToDataSource());
            }

            return artifacts;
        }
    }
}