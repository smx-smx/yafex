using Smx.SharpIO;

using System;
using System.Collections.Generic;

using Yafex.Fuse;

namespace Yafex.FileFormats.Nfwb
{
    public class NfwbAddon : IFormatAddon
    {
        public FileFormat FileFormat => FileFormat.Nfwb;

        public IFormatDetector CreateDetector(IDictionary<string, string> args)
        {
            return new NfwbDetector();
        }

        public IFormatExtractor CreateExtractor(DetectionResult result)
        {
            return new NfwbExtractor(result);
        }

        public YafexVfs? CreateVfs(IDataSource input, DetectionResult result)
        {
            try
            {
                var data = input.Data;
                var stream = new Smx.SharpIO.SpanStream(data);
                var hdr = stream.ReadStruct<Header>();
                VfsMount.TryRoot(input, hdr.FirmwareNameString, out var vfs, out var root);
                var keepAlive = new System.Collections.Generic.List<object>();
                for (long i = 0; i < hdr.partCount && i < 100000; i++)
                {
                    var entry = stream.ReadStruct<PartEntry>();
                    VfsMount.AddRange(root, input, $"{entry.partitionID}.bin",
                        entry.offset, entry.size,
                        $"partition: {entry.partitionID}\nfirmware: {hdr.FirmwareNameString} {hdr.FirmwareVersion}",
                        keepAlive);
                }
                return vfs;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}