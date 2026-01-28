#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
using log4net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Yafex.Fuse;
using Yafex.Support;

namespace Yafex
{
    public class Program
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(Program));

        Program()
        {
            try
            {
                IEnumerable<string> trueStrings = ["1", "true"];
                var isDebug = trueStrings.Contains((Environment.GetEnvironmentVariable("YAFEX_DEBUG") ?? "0").ToLowerInvariant());
                Logger.Setup(enableDebug: isDebug);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Warning: log4net setup failed");
                Console.Error.WriteLine(ex);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private YafexVfs? fuseVfs = null;

        private void FuseUsageError()
        {
            Console.Error.WriteLine("Usage: fuse [filename] [mountpoint]");
            Environment.Exit(1);
        }

        private Config BuildConfig(string fileName)
        {
            Config config = new Config()
            {
                ConfigDir = Directory.GetCurrentDirectory(),
                DestDir = Path.GetDirectoryName(fileName)
            };
            return config;
        }

        IHost BuildHost(Config config)
        {
            var hostBuilder = Host.CreateApplicationBuilder();
            FileFormatRepository.RegisterFileFormats(hostBuilder.Services);

            var secretsPath = Path.Combine(config.ConfigDir, "secrets.json");
            if (!File.Exists(secretsPath))
            {
                throw new InvalidOperationException($"Secrets file \"{secretsPath}\" does not exist");
            }
            var keyFile = new KeyBundle(secretsPath);

            hostBuilder.Services.AddSingleton(config);
            hostBuilder.Services.AddSingleton(keyFile);
            hostBuilder.Services.AddSingleton<KeysRepository>();
            hostBuilder.Services.AddSingleton<FileFormatRepository>();
            hostBuilder.Services.AddSingleton<FormatFinder>();
            hostBuilder.Services.AddSingleton<Extractor>();

            var host = hostBuilder.Build();
            return host;
        }

        void Run(string[] args)
        {
            Console.WriteLine("Firmex#");

            var it = args.GetEnumerator();

            string? filename = null;
            string? fuse_mountpoint = null;
            string? arg0 = null;
            if (it.MoveNext())
            {
                arg0 = it.Current.ToString();
            }

            if (arg0 == "fuse")
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
            }
            else if (!string.IsNullOrEmpty(arg0))
            {
                filename = arg0;
            }

            var cfg = BuildConfig(filename);
            var host = BuildHost(cfg);

            host.Start();
            var extractor = host.Services.GetRequiredService<Extractor>();

            using (MFile input = new MFile(filename))
            {
                var artifacts = extractor.Extract(fuseVfs?.Root, input);
                var numArtifacts = 0;
                foreach (var artifact in artifacts)
                {
                    ++numArtifacts;
                    // $FIXME: centralized artifact printing
                }
                if (fuseVfs != null && fuse_mountpoint != null && numArtifacts > 0)
                {
                    FuseInterop.Start(fuseVfs, fuse_mountpoint);
                }
            }
        }

        public static void Main(string[] args)
        {
            var prg = new Program();
            prg.Run(args);
        }

        private static bool IsRunningInCygwin()
        {
            return GetModuleHandle("cygwin1") != IntPtr.Zero;
        }
    }
}
