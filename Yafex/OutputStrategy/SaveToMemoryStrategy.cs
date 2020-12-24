using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Smx.Yafex.OutputStrategy
{
	public class SaveToMemoryStrategy : IArtifactFactory
	{
		public IArtifact Create(string identifier) {
			var stream = new MemoryStream();
			return new Artifact(identifier, stream);
		}
	}
}
