using System.Collections.Generic;

using Yafex.Fuse;

namespace Yafex.FileFormats.LgUp;

public class LgUpAddon : IFormatAddon<LgUpDetectionResult>
{
    public FileFormat FileFormat => throw new System.NotImplementedException();

    public IFormatDetector CreateDetector(IDictionary<string, string> args)
    {
        return new LgUpDetector();
    }

    public IFormatExtractor CreateExtractor(LgUpDetectionResult result)
    {
        return new LgUpExtractor();
    }

    public IVfsNode CreateVfsNode(IDataSource ds)
    {
        throw new System.NotImplementedException();
    }
}
