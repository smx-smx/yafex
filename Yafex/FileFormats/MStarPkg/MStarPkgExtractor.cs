using Yafex.Support;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace Yafex.FileFormats.MStarPkg
{
	class MStarPartition
	{
		public string Name;
		public uint Offset;
		public uint Size;

		public override string ToString() {
			return $"{Name} @0x{Offset:X} (0x{Size:X})";
		}
	}

	public class MStarPkgExtractor : IFormatExtractor
	{
		private Config config;

		public MStarPkgExtractor(Config config) {
			this.config = config;
		}

		private IEnumerable<MStarPartition> ParseMBootScript(string mbootScript) {
			string search = "# File Partition: ";
			var parts = mbootScript.Split(search);
			if (parts.Length < 2) {
				yield break;
			}

			for(int i=1; i<parts.Length; i++) {
				var lines = Regex.Split(parts[i], "\r?\n")
					.Where(x => !string.IsNullOrWhiteSpace(x))
					.ToList();

				if (lines.Count < 1) continue;
				var partName = lines[0];

				var partLoad = lines.Where(x => x.StartsWith("filepartload ")).FirstOrDefault();
				if (partLoad == null) continue;

				var args = partLoad.Split(' ');
				if (args.Length < 5) continue;

				var partOffset = args[3];
				if (partOffset == null) continue;

				var partSize = args[4];
				if (partSize == null) continue;

				yield return new MStarPartition() {
					Name = partName,
					Offset = Convert.ToUInt32(partOffset, 16),
					Size = Convert.ToUInt32(partSize, 16)
				};
			}
			yield break;
		}

		public IEnumerable<IDataSource> Extract(IDataSource source) {
			var mbootScript = Encoding.ASCII.GetString(
				source.Data.Slice(0, MStarPkgDetector.MBOOT_SCRIPT_SIZE).ToArray()
			);
			var partitions = ParseMBootScript(mbootScript);

			var baseDir = Path.Combine(config.DestDir, Path.GetFileNameWithoutExtension(source.Directory));
			Directory.CreateDirectory(baseDir);

			foreach(var part in partitions) {
				var destPath = Path.Combine(baseDir, $"{part.Name}.pak");

				var artifact = new MemoryDataSource(
					source
						.Data.Span
						.Slice((int)part.Offset, (int)part.Size)
						.ToArray()
				);
				yield return artifact;
			}
		}
	}
}