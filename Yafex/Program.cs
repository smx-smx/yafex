#region License
/*
 * Copyright (c) 2023 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
﻿using Yafex;
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
using log4net.Util;
using Yafex.Fuse;
using Yafex.FileFormats.LxBoot;
using System.Runtime.CompilerServices;
using Yafex.Metadata;

namespace Yafex
{
	public class Program
	{
		Program() {
			try {
                // needs https://github.com/apache/logging-log4net/pull/91
                //SystemInfo.EntryAssemblyLocation = Assembly.GetExecutingAssembly().Location;
                Logger.Setup();
			} catch(Exception) {
				Console.Error.WriteLine("Warning: log4net setup failed");
			}
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
		private static extern IntPtr GetModuleHandle(string lpModuleName);

		private Config config;
		private FormatFinder finder;

		private IEnumerable<IDataSource> Process(IVfsNode? root, IDataSource input)
        {
			var (bestAddon, bestResult) = finder.DetectFormatAddon(input);
			if (bestAddon == null)
            {
				yield break;
            }

			var useVfs = root != null;
			if (useVfs)
			{
				var mountPoint = new YafexDirectory(input.Name, Helpers.OctalLiteral(0755));
				root.AddNode(mountPoint);
				root = mountPoint;
			}

			var extractor = bestAddon.CreateExtractor(config, bestResult);

			var artifacts = extractor.Extract(input);
			foreach (var artifact in artifacts)
			{
                // save intermediate 
                if (artifact.Flags.HasFlag(DataSourceFlags.Output)
				&& !artifact.Flags.HasFlag(DataSourceFlags.Temporary
				)) {
					yield return artifact;
				}

				if (useVfs)
				{
					IVfsNode? node = null;
					try
					{
                        node = bestAddon.CreateVfsNode(artifact);
                    } catch(Exception ex) {
						if (ex is NotImplementedException || ex is NotSupportedException) { }
						else throw;
					}
					if(node != null)
					{
						root.AddNode(node);
					}
				} else
				// $TODO: flag to skip filesystem writing
				{
					var filename = artifact.GetMetadata<OutputFileName>().FirstOrDefault();
					if(filename != null)
					{
						var dirname = artifact.GetMetadata<OutputDirectoryName>().FirstOrDefault();

						var path = config.DestDir;
						if(dirname != null)
						{
							path = Path.Combine(path, dirname.DirectoryName);
							Directory.CreateDirectory(path);
						}
						path = Path.Combine(path, filename.FileName);

						using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
						{
							fs.SetLength(0);
							fs.Write(artifact.Data.Span);
						}	
					}
				}

				// handle matryoshka formats
				if (artifact.Flags.HasFlag(DataSourceFlags.ProcessFurther))
				{
					var subArtifacts = Process(root, artifact);
					foreach (var sub in subArtifacts)
					{
						yield return sub;
					}
				}
			}
		}

		private YafexVfs? fuseVfs = null;

		private void WriteOutputFile(IDataSource artifact)
		{
            if (artifact.Directory == null)
            {
                // if not overridden, use Config
                artifact.Directory = config.DestDir;
            }

            var path = Path.Combine(artifact.Directory, artifact.Name);

            // $TODO: use MFile in output mode?
            File.WriteAllBytes(path, artifact.Data.ToArray());
        }

		private void FuseUsageError()
		{
            Console.Error.WriteLine("Usage: fuse [filename] [mountpoint]");
            Environment.Exit(1);
        }

		void Run(string[] args) {
			Console.WriteLine("Firmex#");

			var it = args.GetEnumerator();

			string? filename = null;
            string? fuse_mountpoint = null;
            string? arg0 = null;
            if (it.MoveNext())
			{
				arg0 = it.Current.ToString();
			}

			if(arg0 == "fuse")
			{
                fuseVfs = new YafexVfs();
                if (!it.MoveNext())
				{
					FuseUsageError();
                }
				filename = it.Current.ToString();
				if (!it.MoveNext())
				{
					FuseUsageError();
                }
				fuse_mountpoint = it.Current.ToString();
            } else if(!string.IsNullOrEmpty(arg0))
			{
				filename = arg0;
			}

			Config config = new Config() {
				ConfigDir = Directory.GetCurrentDirectory(),
				DestDir = Path.GetDirectoryName(filename)
			};
			this.config = config;

			FileFormatRepository repo = new FileFormatRepository();
			repo.RegisterFormat(FileFormat.EpkV1, new Epk1Addon());
			repo.RegisterFormat(FileFormat.EpkV2, new Epk2Addon());
			repo.RegisterFormat(FileFormat.EpkV2Beta, new Epk2BetaAddon());
			repo.RegisterFormat(FileFormat.EpkV3b, new Epk3NewAddon());
			repo.RegisterFormat(FileFormat.Partinfo, new PartinfoAddon());
			repo.RegisterFormat(FileFormat.LZHS, new LzhsAddon());
			repo.RegisterFormat(FileFormat.LZHSFS, new LzhsFsAddon());
			repo.RegisterFormat(FileFormat.MStarPkg, new MStarPkgAddon());
			repo.RegisterFormat(FileFormat.FreescaleNand, new FreescaleNandAddon());
			repo.RegisterFormat(FileFormat.Xex, new XexAddon());
			repo.RegisterFormat(FileFormat.LxSecureBoot, new LxSecureBootAddon());


			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || IsRunningInCygwin()) {
				//repo.RegisterFormat(FileFormat.Squashfs, new SquashfsAddon());
			}

			FormatFinder finder = new FormatFinder(config, repo);
			this.finder = finder;

			using (MFile input = new MFile(filename))
            {
				var artifacts = Process(fuseVfs?.Root, input);
				Action<IDataSource> addDelegate = (fuseVfs != null)
					? (artifacts => { })
					: WriteOutputFile;

				int artifactsCount = 0;
				foreach(var artifact in artifacts)
				{
					addDelegate(artifact);
					artifactsCount++;
				}

                if (fuseVfs != null && fuse_mountpoint != null && artifactsCount > 0)
                {
                    FuseInterop.Start(fuseVfs, fuse_mountpoint);
                }
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
