#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Yafex.Metadata;
using Yafex.Support;

namespace Yafex
{
    public static class DataSourceExtensions
    {
        public static string RequireBaseDirectory(this IDataSource ds)
        {
            var path = ds.GetMetadata<BaseDirectoryPath>().FirstOrDefault()?.DirectoryPath;
            ArgumentNullException.ThrowIfNull(path);
            return path;
        }

        public static string? GetDestinationPath(this IDataSource ds)
        {
            var baseDir = ds.GetMetadata<BaseDirectoryPath>().FirstOrDefault()?.DirectoryPath;
            var destDir = ds.GetMetadata<OutputDirectoryName>().FirstOrDefault()?.DirectoryName;
            var destFile = ds.GetMetadata<OutputFileName>().FirstOrDefault()?.FileName;

            if (baseDir == null || destFile == null) return null;

            var path = baseDir;
            if (destDir != null)
            {
                path = Path.Combine(path, destDir);
            }
            path = Path.Combine(path, destFile);
            return path;
        }

        public static void SetChildOf(this IDataSource child, IDataSource parent)
        {
            var childBaseDir = child.GetMetadata<BaseDirectoryPath>().FirstOrDefault();
            if (childBaseDir != null)
            {
                // child already specifies an alternative source directory, ignore
                return;
            }


            // BaseDirectoryPath acts as a reverse priority queue.
            // the last added Path is the preferred one
            // example for nested files:
            // - BaseDirectoryPath[0]: directory of the source file
            // - BaseDirectoryPath[1]: preferred subdirectory name
            var sourceDir = parent.GetMetadata<BaseDirectoryPath>().LastOrDefault();
            if (sourceDir == null)
            {
                // missing source directory, cannot inherit
                return;
            }

            // get eventual subpath
            var childDirName = child.GetMetadata<OutputDirectoryName>().FirstOrDefault();

            var subPath = sourceDir.DirectoryPath;
            if (childDirName != null)
            {
                subPath = Path.Combine(subPath, childDirName.DirectoryName);
            }
            child.AddMetadata(new BaseDirectoryPath(subPath));
        }
    }

    public abstract class BaseDataSource
    {

        private readonly IDictionary<Type, IList<IArtifactMetadata>> _metadata = new Dictionary<Type, IList<IArtifactMetadata>>();
        public IDictionary<Type, IList<IArtifactMetadata>> Metadata => _metadata;

        public void AddMetadata<T>(T metadata) where T : IArtifactMetadata
        {
            if (!_metadata.TryGetValue(typeof(T), out var bucket))
            {
                bucket = new List<IArtifactMetadata>();
            }
            bucket.Add(metadata);
            _metadata[typeof(T)] = bucket;
        }

        public IEnumerable<T> GetMetadata<T>() where T : IArtifactMetadata
        {
            if (_metadata.TryGetValue(typeof(T), out var bucket))
            {
                return bucket.Cast<T>();
            }
            return Enumerable.Empty<T>();
        }
    }
}
