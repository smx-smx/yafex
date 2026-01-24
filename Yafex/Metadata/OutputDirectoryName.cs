using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yafex.Metadata
{
    public class OutputDirectoryName : IArtifactMetadata
    {
        public string DirectoryName { get; }

        public OutputDirectoryName(string directoryName)
        {
            DirectoryName = directoryName;
        }
    }
}
