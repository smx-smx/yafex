using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yafex.Metadata;

public class OutputFileName : IArtifactMetadata
{
    public string FileName { get; }

    public OutputFileName(string fileName)
    {
        FileName = fileName;
    }
}
