using System;
using System.Collections.Generic;
using System.Text;

namespace Smx.Yafex
{
	[Flags]
    public enum DataSourceType {
		Input,
		Output,
		Temporary,
		ProcessFurther
	}


    public interface IDataSource
	{
		string? Name { get; set; }
		string? Directory { get; set; }

		Memory<byte> Data { get; }

		DataSourceType Flags { get; set; }
	}
}
