using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace Smx.Yafex.Support
{
	public class MFile : IDataSource, IDisposable
	{
		private readonly string filePath;
		private readonly FileStream fs;

		private MemoryMappedFile mf;
		private MemoryMappedSpan<byte> span;

		public Memory<byte> Data => span.Memory;

		private string _name;
        public string? Name {
			get => _name;
			set => throw new NotSupportedException();
		}
        public string? Directory {
			get => Path.GetDirectoryName(filePath);
			set => throw new NotSupportedException();
		}

		private DataSourceType _flags;
        public DataSourceType Flags {
			get => _flags;
			set => throw new NotImplementedException();
		}

        public Span<T> GetData<T>(int offset=0) where T : unmanaged {
			return span.GetSpan()
					   .Slice(offset)
					   .Cast<T>();
		}

		private readonly bool isReadOnly;

		public int GetLength() {
			return (int)fs.Length;
		}

		private void CloseMapping() {
			if (this.span != null) {
				this.span.Dispose();
			}
			this.span = null;
			if(this.mf != null) {
				this.mf.Dispose();
			}
			this.mf = null;
		}

		public void SetLength(int length) {
			if (isReadOnly) {
				throw new InvalidOperationException("Cannot reallocate a R/O MFile");
			}

			CloseMapping();
			this.fs.SetLength(length);
			CreateMapping(length);
		}

		private void CreateMapping(int length) {
			CloseMapping();

			if (isReadOnly) {
				this.mf = MemoryMappedFile.CreateFromFile(this.fs, null, 0,
					MemoryMappedFileAccess.Read, HandleInheritability.Inheritable, true);
			} else {
				this.mf = MemoryMappedFile.CreateFromFile(this.fs, null, 0,
					MemoryMappedFileAccess.ReadWrite, HandleInheritability.Inheritable, true);
			}

			if (this.fs.Length >= int.MaxValue) {
				throw new NotSupportedException("Files bigger than 4GB are currently not supported");
			}

			this.span = new MemoryMappedSpan<byte>(this.mf, length, readOnly: this.isReadOnly);
		}

		public MFile(string filePath, bool readOnly = true) {
			this.filePath = filePath;
			this.isReadOnly = readOnly;

			if (isReadOnly) {
				this.fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			} else {
				this.fs = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
			}

			if(this.fs.Length > 0) {
				CreateMapping((int)fs.Length);
			}

			_name = System.IO.Path.GetFileNameWithoutExtension(filePath);
			_flags = (readOnly) ? DataSourceType.Input : DataSourceType.Input | DataSourceType.Output;
		}

		public void Dispose() {
			span.Dispose();
			mf.Dispose();
			fs.Close();
		}
	}
}
