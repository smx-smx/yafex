using Yafex.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yafex.FileFormats.Xex
{
    public class XexAddon : IFormatAddon
    {
        public IFormatDetector CreateDetector(Config config)
        {
            return new XexDetector();
        }

        public IFormatExtractor CreateExtractor(Config config, DetectionResult result)
        {
            return new XexExtractor();
        }
    }
}
