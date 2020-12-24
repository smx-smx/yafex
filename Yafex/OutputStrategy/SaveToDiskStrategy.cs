using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Smx.Yafex.OutputStrategy
{
	public class SaveToDiskStrategy : IArtifactFactory
	{
		public IArtifact Create(string path) {
			var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
			return new Artifact(path, stream);
		}
	}
}
