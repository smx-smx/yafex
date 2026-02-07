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
using System.IO.MemoryMappedFiles;

using Smx.SharpIO.Memory.Buffers;

using Yafex.Metadata;

namespace Yafex.Support
{
    public class MFile : BaseDataSource, IDataSource, IDisposable
    {
        private readonly string filePath;
        private readonly FileStream fs;

        private MemoryMappedFile mf;
        private MemoryMappedSpan<byte> span;

        public Memory64<byte> Data => span.Memory;

        public IEnumerable<byte> Bytes => Data.ToEnumerable();

        private string _name;
        public string? Name
        {
            get => _name;
            set => throw new NotSupportedException();
        }
        public string? Directory
        {
            get => Path.GetDirectoryName(filePath);
            set => throw new NotSupportedException();
        }

        private DataSourceFlags _flags;
        public DataSourceFlags Flags
        {
            get => _flags;
            set => throw new NotImplementedException();
        }

        public Span<T> GetData<T>(int offset = 0) where T : unmanaged
        {
            return span.GetSpan()
                       .Slice(offset)
                       .Cast<T>();
        }

        private readonly bool isReadOnly;

        public int GetLength()
        {
            return (int)fs.Length;
        }

        private void CloseMapping()
        {
            if (this.span != null)
            {
                this.span.Dispose();
            }
            this.span = null;
            if (this.mf != null)
            {
                this.mf.Dispose();
            }
            this.mf = null;
        }

        public void SetLength(int length)
        {
            if (isReadOnly)
            {
                throw new InvalidOperationException("Cannot reallocate a R/O MFile");
            }

            CloseMapping();
            this.fs.SetLength(length);
            CreateMapping(length);
        }

        private void CreateMapping(int length)
        {
            CloseMapping();

            if (isReadOnly)
            {
                this.mf = MemoryMappedFile.CreateFromFile(this.fs, null, 0,
                    MemoryMappedFileAccess.Read, HandleInheritability.Inheritable, true);
            }
            else
            {
                this.mf = MemoryMappedFile.CreateFromFile(this.fs, null, 0,
                    MemoryMappedFileAccess.ReadWrite, HandleInheritability.Inheritable, true);
            }

            if (this.fs.Length >= int.MaxValue)
            {
                throw new NotSupportedException("Files bigger than 4GB are currently not supported");
            }

            this.span = new MemoryMappedSpan<byte>(this.mf, length, readOnly: this.isReadOnly);
        }

        public MFile(string filePath, bool readOnly = true)
        {
            this.filePath = filePath;
            this.isReadOnly = readOnly;

            if (isReadOnly)
            {
                this.fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            else
            {
                this.fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            }

            if (this.fs.Length > 0)
            {
                CreateMapping((int)fs.Length);
            }

            _name = Path.GetFileNameWithoutExtension(filePath);
            _flags = (readOnly) ? DataSourceFlags.Input : DataSourceFlags.Input | DataSourceFlags.Output;

            var dirName = Path.GetDirectoryName(filePath);
            if (dirName != null)
            {
                AddMetadata(new BaseDirectoryPath(dirName));
            }
        }

        public void Dispose()
        {
            span.Dispose();
            mf.Dispose();
            fs.Close();
        }
    }
}
