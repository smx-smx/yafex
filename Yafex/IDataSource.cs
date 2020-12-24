using System;
using System.Collections.Generic;
using System.Text;

namespace Smx.Yafex
{
	public interface IDataSource
	{
		string Path { get; }
		//ReadOnlySpan<byte> Data { get; }
		Memory<byte> Data { get; }
	}
}
