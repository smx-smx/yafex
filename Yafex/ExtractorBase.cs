using Smx.Yafex.OutputStrategy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Smx.Yafex
{
	public abstract class ExtractorBase : IDisposable
	{
		private ArtifactCollection artifactCollector = new ArtifactCollection(new SaveToDiskStrategy());

		public void SetOutputStrategy(IArtifactFactory strategy) {
			artifactCollector.SetStrategy(strategy);
		}

		public IArtifact ArtifactOpen(string path) {
			return artifactCollector.Create(path);
		}

		public IList<IArtifact> GetArtifacts() {
			return artifactCollector.Artifacts;
		}

		public void Dispose() {
			 artifactCollector.Dispose();
		}
	}
}
