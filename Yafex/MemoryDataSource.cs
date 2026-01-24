#region License
/*
 * Copyright (c) 2023 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Yafex.Metadata;
using Yafex.Support;

namespace Yafex
{
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
            if(_metadata.TryGetValue(typeof(T), out var bucket))
            {
                return bucket.Cast<T>();
            }
            return Enumerable.Empty<T>();
        }
    }

    public class MemoryDataSource : BaseDataSource, IDataSource
    {
        private Memory<byte> data;

        public MemoryDataSource(Memory<byte> data) {
            this.data = data;
        }

        public Memory<byte> Data => data;

        public IEnumerable<byte> Bytes => data.ToEnumerable();

        private DataSourceFlags _flags;
        public DataSourceFlags Flags {
            get => _flags;
            set => _flags = value;
        }

        private string? _name;
        public string? Name
        {
            get => _name;
            set => _name = value;
        }

        private string? _path;
        public string? Directory
        {
            get => _path;
            set => _path = value;
        }
    }
}
