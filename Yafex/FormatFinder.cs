using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Yafex.Support;

namespace Yafex
{
	ref struct FinderArg
	{
		public FileFormat fmt;
		public Span<byte> data;
	}

	public class FormatFinder
	{
		private readonly Config config;
		private readonly FileFormatRepository repo;

		private delegate int GetConfidenceDelegate(FinderArg arg);

		public FormatFinder(Config config, FileFormatRepository repo) {
			this.config = config;
			this.repo = repo;
		}

		private DetectionResult Detect(FileFormat fmt, IDataSource source) {
			repo.TryGetAddonForFormat(fmt, out var addon);
			return addon!.CreateDetector(config).Detect(source);
		}


		public IFormatExtractor? CreateExtractor(IDataSource source) {
			var formats = repo.GetRegisteredFormats();
			if (formats.Count() < 1)
				return null;


			int bestConfidence = 0;
			IFormatAddon? bestAddon = null;
			DetectionResult bestResult = null!;

			foreach(var fmt in formats) {
				repo.TryGetAddonForFormat(fmt, out var addon);
				var result = Detect(fmt, source);

				if(result.Confidence > bestConfidence) {
					bestConfidence = result.Confidence;
					bestAddon = addon;
					bestResult = result;
				}
			}

			if(bestAddon == null) {
				return null;
			}

			return bestAddon.CreateExtractor(config, bestResult);
		}
	}
}
