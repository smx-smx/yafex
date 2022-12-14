using log4net.Repository.Hierarchy;
using Yafex.Support;
using Yafex.Util;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Yafex.FileFormats.Epk
{

	public abstract class EpkDetector
	{
		protected EpkServicesFactory serviceFactory;

		public EpkDetector(Config config) {
			serviceFactory = new EpkServicesFactory(config);
		}

		protected static bool IsEpkVersionString(string verString) {
			var parts = verString.Split('.');
			return parts.Length >= 2 && parts.All(p => int.TryParse(p, out int _));
		}


		const string EPAK_MAGIC = "epak";

		protected static bool ValidateEpkHeader(ReadOnlySpan<byte> fileData) {
			byte[] magic = fileData.Slice(0, 4).ToArray();
			var str = Encoding.ASCII.GetString(magic);
			return str == EPAK_MAGIC;
		}
	}
}
