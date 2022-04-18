using System;
using System.Collections.Generic;
using System.Text;

namespace Smx.Yafex
{
	public interface IFormatExtractor
	{
		IEnumerable<IDataSource> Extract(IDataSource source);
	}
}
