using Smx.Yafex.Support;
using System;
using System.Collections.Generic;
using System.Text;

namespace Smx.Yafex
{
	public interface IFormatFactory<T>
	{
		T CreateExtractor(MFile mf);
	}
}
