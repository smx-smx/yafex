using FirmexSharp.Cygwin;
using Smx.Yafex;
using Smx.Yafex.FileFormats;
using Smx.Yafex.FileFormats.EpkV1;
using Smx.Yafex.FileFormats.EpkV2;
using Smx.Yafex.FileFormats.EpkV3;
using Smx.Yafex.FileFormats.Lzhs;
using Smx.Yafex.FileFormats.LzhsFs;
using Smx.Yafex.FileFormats.MStarPkg;
using Smx.Yafex.FileFormats.Partinfo;
using Smx.Yafex.FileFormats.Squashfs;
using Smx.Yafex.Support;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Smx.Yafex
{
	class Program
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

		void Run(string[] args) {
			Console.WriteLine("Firmex#");

			var inputFile = args[0];

			Config config = new Config() {
				ConfigDir = Environment.GetEnvironmentVariable("CONFIG_DIR"),
				DestDir = Path.GetDirectoryName(inputFile)
			};

			FileFormatRepository repo = new FileFormatRepository();
			repo.RegisterFormat(FileFormat.EpkV1, new Epk1Addon());
			repo.RegisterFormat(FileFormat.EpkV2, new Epk2Addon());
			repo.RegisterFormat(FileFormat.EpkV3b, new Epk3NewAddon());
			repo.RegisterFormat(FileFormat.Partinfo, new PartinfoAddon());
			repo.RegisterFormat(FileFormat.LZHS, new LzhsAddon());
			repo.RegisterFormat(FileFormat.LZHSFS, new LzhsFsAddon());
			repo.RegisterFormat(FileFormat.MStarPkg, new MStarPkgAddon());

			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IsRunningInCygwin()) {
				repo.RegisterFormat(FileFormat.Squashfs, new SquashfsAddon());
			}

			FormatFinder finder = new FormatFinder(config, repo);

			using (MFile input = new MFile(inputFile)) {
				var extractor = finder.CreateExtractor(input);
				if (extractor != null) {
					extractor.Extract(input);
				}
			}
		}

		static void Main(string[] args) {
			new Program().Run(args);
		}

		private delegate void MainDelegate(string[] args);

		private static string[] ReadArgv(IntPtr args, int sizeBytes) {
			int nargs = sizeBytes / IntPtr.Size;
			string[] argv = new string[nargs];

			for (int i = 0; i < nargs; i++, args += IntPtr.Size) {
				IntPtr charPtr = Marshal.ReadIntPtr(args);
				argv[i] = Marshal.PtrToStringAnsi(charPtr);
			}
			return argv;
		}

		private static bool IsRunningInCygwin() {
			return GetModuleHandle("cygwin1") != IntPtr.Zero;
		}

		private static int Entry(IntPtr args, int sizeBytes) {
			string[] argv = ReadArgv(args, sizeBytes);

			string thisDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			AppDomain.CurrentDomain.SetData("APP_CONTEXT_BASE_DIRECTORY", thisDir);

			Action<MainDelegate> initializer;

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
				IsRunningInCygwin()
			) {
				EzDotnetCompat.InstallHooks();
				initializer = (main) => {
					using (var stdin = new StreamReader(new CygwinInputStream(0)))
					using (var stdout = new StreamWriter(new CygwinOutputStream(1)))
					using (var stderr = new StreamWriter(new CygwinOutputStream(2))) {
						stdout.AutoFlush = true;
						stderr.AutoFlush = true;

						Console.SetIn(stdin);
						Console.SetOut(stdout);
						Console.SetError(stderr);

						main(argv);
					}
				};
			} else {
				initializer = (main) => {
					main(argv);
				};
			}


			initializer(Main);
			return 0;
		}
	}
}
