using System;
using System.Collections.Generic;

using Yafex.Fuse;

namespace Yafex.FileFormats.SddlSec
{
    public class SddlSecAddon : IFormatAddon<SddlSecDetectionResult>
    {
        public FileFormat FileFormat => FileFormat.SddlSec;

        private readonly KeysRepository _keys;

        public SddlSecAddon(KeysRepository keys)
        {
            _keys = keys;
        }

        public IFormatDetector CreateDetector(IDictionary<string, string> args)
        {
            return new SddlSecDetector(args);
        }

        public IFormatExtractor CreateExtractor(SddlSecDetectionResult result)
        {
            return new SddlSecExtractor(result.Context, _keys);
        }

        public YafexVfs? CreateVfs(IDataSource source, DetectionResult result)
        {
            if (source is not Support.MFile input) return null;
            if (result is not SddlSecDetectionResult typed) return null;
            return SddlVfs.Create(input, _keys, typed);
        }
    }
}