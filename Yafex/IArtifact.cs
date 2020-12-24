using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Smx.Yafex
{
	public interface IArtifact : IDisposable
	{
		string Name { get; }
		void Write(ReadOnlySpan<byte> data);
		void Finish();
	}
}