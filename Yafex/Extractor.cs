using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Yafex.Fuse;
using Yafex.Metadata;
using Yafex.Support;

namespace Yafex
{
    public class Extractor
    {
        private readonly FormatFinder _finder;
        private readonly FileFormatRepository _repo;

        public Extractor(
            FileFormatRepository repo,
            FormatFinder finder
        )
        {
            _repo = repo;
            _finder = finder;
        }

        public IEnumerable<IDataSource> Extract(IVfsNode? root, IDataSource input)
        {
            var (bestAddon, bestResult) = _finder.DetectFormatAddon(input);
            if (bestAddon == null)
            {
                yield break;
            }

            var useVfs = root != null;
            if (useVfs)
            {
                var mountPoint = new YafexDirectory(input.Name, Helpers.OctalLiteral(0755));
                root.AddNode(mountPoint);
                root = mountPoint;
            }

            var extractor = bestAddon.CreateExtractor(bestResult);

            var artifacts = extractor.Extract(input);
            foreach (var artifact in artifacts)
            {
                // save intermediate 
                if (artifact.Flags.HasFlag(DataSourceFlags.Output)
                && !artifact.Flags.HasFlag(DataSourceFlags.Temporary
                ))
                {
                    yield return artifact;
                }

                if (useVfs)
                {
                    IVfsNode? node = null;
                    try
                    {
                        node = bestAddon.CreateVfsNode(artifact);
                    }
                    catch (Exception ex)
                    {
                        if (ex is NotImplementedException || ex is NotSupportedException) { } else throw;
                    }
                    if (node != null)
                    {
                        root.AddNode(node);
                    }
                }
                else
                // $TODO: flag to skip filesystem writing
                {
                    var filename = artifact.GetMetadata<OutputFileName>().FirstOrDefault();
                    if (filename != null)
                    {
                        var dirname = artifact.GetMetadata<OutputDirectoryName>().FirstOrDefault();
                        var path = artifact.GetMetadata<BaseDirectoryPath>().FirstOrDefault()?.DirectoryPath;
                        if (path != null)
                        {
                            if (dirname != null)
                            {
                                path = Path.Combine(path, dirname.DirectoryName);
                                Directory.CreateDirectory(path);
                            }
                            path = Path.Combine(path, filename.FileName);

                            using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                            {
                                fs.SetLength(0);
                                foreach(var chunk in artifact.Data.Span.GetChunks())
                                {
                                    fs.Write(chunk);
                                }
                            }
                        }
                    }
                }

                // handle matryoshka formats
                if (artifact.Flags.HasFlag(DataSourceFlags.ProcessFurther))
                {
                    var subArtifacts = Extract(root, artifact);
                    foreach (var sub in subArtifacts)
                    {
                        yield return sub;
                    }
                }
            }
        }
    }
}
