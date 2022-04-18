using System;
using System.Collections.Generic;
using System.Text;

namespace Smx.Yafex
{
	[Flags]
    public enum DataSourceType {
		Input = 1 << 0,
		Output = 1 << 1,
		Temporary = 1 << 2,
		ProcessFurther = 1 << 3
	}


    public interface IDataSource
	{
		string? Name { get; set; }
		string? Directory { get; set; }

		Memory<byte> Data { get; }

		DataSourceType Flags { get; set; }
	}
}
