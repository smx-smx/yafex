using log4net;
using Yafex.Support;
using System.Collections.Generic;
using Smx.SharpIO;
using System.IO;
using Yafex.Metadata;

namespace Yafex.FileFormats.Nfwb
{
	internal class NfwbExtractor : IFormatExtractor
	{
		private DetectionResult result;
        private static readonly ILog log = LogManager.GetLogger(nameof(NfwbExtractor));

		public NfwbExtractor(DetectionResult result) {
			this.result = result;
		}

		public IEnumerable<IDataSource> Extract(IDataSource source) {
            var fileData = source.Data;
			var dataStream = new SpanStream(fileData);
            var hdr = dataStream.ReadStruct<Header>();

            log.Info("Firmware Info");
			log.Info("-------------");
            log.Info($"Firmware name: {hdr.FirmwareNameString}");
            log.Info($"Firmware version: {hdr.FirmwareVersion}");
            log.Info($"Data size: {hdr.dataSize}");
            log.Info($"Part count: {hdr.partCount}");

			var basedir = Path.Combine(source.RequireBaseDirectory(), $"{hdr.FirmwareNameString}-{hdr.FirmwareVersion}");
            source.AddMetadata(new BaseDirectoryPath(basedir));

  			for (int i = 0; i < hdr.partCount; i++)
  			{
    			var partEntry = dataStream.ReadStruct<PartEntry>();

				var partData = fileData.Slice(partEntry.offset, partEntry.size);

				var fileName = $"{partEntry.partitionID}.bin";
				var filePath = Path.Combine(basedir, fileName);

				log.Info($"#{i + 1}/{hdr.partCount} saving Part (id='{partEntry.partitionID}'," +
					$" offset=0x{partEntry.offset:X}," +
					$" size='{partEntry.size}') to file {filePath}");

                var artifact = new MemoryDataSource(partData.ToArray())
                {
                    Name = fileName
                };
				artifact.SetChildOf(source);
				artifact.AddMetadata(new OutputFileName(fileName));
				artifact.AddMetadata(new OutputDirectoryName(basedir));
				artifact.Flags |= DataSourceFlags.ProcessFurther;
				yield return artifact;
  			}
		}
	}
}