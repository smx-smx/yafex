using Smx.Yafex.Support;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Smx.Yafex
{
	public class Artifact : IArtifact
	{
		private readonly string name;
		private readonly Stream data;

		public Artifact(string name, Stream data) {
			this.name = name;
			this.data = data;
		}

		public string Name => name;

		public void Finish() {
			data.Flush();
			if (data.CanSeek) {
				data.Seek(0, SeekOrigin.Begin);
			}
		}

		public void Dispose() {
			data.Dispose();
		}

		public Memory<byte> GetData() {
			byte[] buf = new byte[data.Length];
			data.Position = 0;
			data.Read(buf);
			return buf;
		}

		public void Write(ReadOnlySpan<byte> pakData) {
			data.Write(pakData);
		}
	}
}
