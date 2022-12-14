using Yafex;
using Yafex.FileFormats;
using Yafex.FileFormats.EpkV1;
using Yafex.FileFormats.EpkV2;
using Yafex.FileFormats.EpkV3;
using Yafex.FileFormats.FreescaleNand;
using Yafex.FileFormats.Lzhs;
using Yafex.FileFormats.LzhsFs;
using Yafex.FileFormats.MStarPkg;
using Yafex.FileFormats.Partinfo;
using Yafex.FileFormats.Squashfs;
using Yafex.FileFormats.Xex;
using Yafex.Support;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Yafex
{
	public class Program
	{
		Program() {
			try {
				Logger.Setup();
			} catch(Exception) {
				Console.Error.WriteLine("Warning: log4net setup failed");
			}
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
		private static extern IntPtr GetModuleHandle(string lpModuleName);

		private Config config;
		private FormatFinder finder;

		private void Process(IDataSource input)
        {
			var extractor = finder.CreateExtractor(input);
			if (extractor == null)
            {
				return;
            }

			var artifacts = extractor.Extract(input);
			foreach (var artifact in artifacts)
			{
                // save intermediate 
                if (artifact.Flags.HasFlag(DataSourceType.Output)
				&& !artifact.Flags.HasFlag(DataSourceType.Temporary
				)) {
					if (artifact.Directory == null) {
						// if not overridden, use Config
						artifact.Directory = config.DestDir;
					}

					var path = Path.Combine(artifact.Directory, artifact.Name);
					
					// $TODO: use MFile in output mode?
					File.WriteAllBytes(path, artifact.Data.ToArray());
				}

				// handle matryoshka formats
				if (artifact.Flags.HasFlag(DataSourceType.ProcessFurther))
				{
					Process(artifact);
				}
			}
		}

		void Run(string[] args) {
			//DebugHelper.Launch();
			Console.WriteLine("Firmex#");

			var inputFile = args[0];

			Config config = new Config() {
				ConfigDir = Directory.GetCurrentDirectory(),
				DestDir = Path.GetDirectoryName(inputFile)
			};
			this.config = config;

			FileFormatRepository repo = new FileFormatRepository();
			repo.RegisterFormat(FileFormat.EpkV1, new Epk1Addon());
			repo.RegisterFormat(FileFormat.EpkV2, new Epk2Addon());
			repo.RegisterFormat(FileFormat.EpkV3b, new Epk3NewAddon());
			repo.RegisterFormat(FileFormat.Partinfo, new PartinfoAddon());
			repo.RegisterFormat(FileFormat.LZHS, new LzhsAddon());
			repo.RegisterFormat(FileFormat.LZHSFS, new LzhsFsAddon());
			repo.RegisterFormat(FileFormat.MStarPkg, new MStarPkgAddon());
			repo.RegisterFormat(FileFormat.FreescaleNand, new FreescaleNandAddon());
			repo.RegisterFormat(FileFormat.Xex, new XexAddon());


			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IsRunningInCygwin()) {
				repo.RegisterFormat(FileFormat.Squashfs, new SquashfsAddon());
			}

			FormatFinder finder = new FormatFinder(config, repo);
			this.finder = finder;


			using (MFile input = new MFile(inputFile))
            {
				Process(input);
            }
		}

		public static void Main(string[] args) {
			new Program().Run(args);
		}

		private static bool IsRunningInCygwin() {
			return GetModuleHandle("cygwin1") != IntPtr.Zero;
		}
	}
}
