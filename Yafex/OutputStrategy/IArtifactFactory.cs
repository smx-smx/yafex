using System;
using System.Collections.Generic;
using System.Text;

namespace Smx.Yafex.OutputStrategy
{
	public interface IArtifactFactory
	{
		public IArtifact Create(string identifier);
	}
}
