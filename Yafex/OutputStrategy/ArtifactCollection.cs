using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Smx.Yafex.OutputStrategy
{
	public class ArtifactCollection : IArtifactFactory, IDisposable
	{
		public readonly IList<IArtifact> Artifacts = new List<IArtifact>();
		private IArtifactFactory innerStrategy;

		public ArtifactCollection(IArtifactFactory innerStrategy) {
			this.innerStrategy = innerStrategy;
		}

		public void SetStrategy(IArtifactFactory strategy) {
			innerStrategy = strategy;
		}

		public IArtifact Create(string identifier) {
			IArtifact artifact = innerStrategy.Create(identifier);
			Artifacts.Add(artifact);
			return artifact;
		}

		public void Dispose() {
			foreach (var artifact in Artifacts) {
				artifact.Dispose();
			}
		}
	}
}
