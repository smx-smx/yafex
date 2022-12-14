using System;
using System.Collections.Generic;
using System.Text;

namespace Yafex
{
	public class FileFormatRepository
	{
		private readonly Dictionary<FileFormat, IFormatAddon> addons = new Dictionary<FileFormat, IFormatAddon>();

		public void RegisterFormat(FileFormat format, IFormatAddon addon) {
			this.addons[format] = addon;
		}

		public bool TryGetAddonForFormat(FileFormat format, out IFormatAddon? addon) {
			return addons.TryGetValue(format, out addon);
		}

		public IEnumerable<FileFormat> GetRegisteredFormats() {
			return addons.Keys;
		}

		public Dictionary<FileFormat, IFormatAddon> GetAddons() => addons;
	}
}
