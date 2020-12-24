using System;
using System.Collections.Generic;
using System.Text;

namespace Smx.Yafex
{
	public interface IFormatExtractor
	{
		IList<IArtifact> Extract(IDataSource source);
	}
}
