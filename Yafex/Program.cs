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
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using log4net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Yafex.Fuse;
using Yafex.Support;

namespace Yafex
{
    public enum ProgramMode
    {
        Standalone,
        Fuse
    }

    public class ProgramOptions
    {
        public required string InputFile { get; set; }
        public string? DestDir { get; set; } = null;
        public ProgramMode ProgramMode { get; set; }
        public Dictionary<string, Dictionary<string, string>> FormatOptions { get; set; } = new();
        public string? KeyBundlePath { get; set; }
    }

    public class Program
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(Program));
        private readonly ProgramOptions _opts;

        Program(IEnumerable<string> args)
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
            _opts = ProcessArgs(args);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private YafexVfs? fuseVfs = null;

        private void FuseUsageError()
        {
            Console.Error.WriteLine("Usage: fuse [filename] [mountpoint]");
            Environment.Exit(1);
        }

        IHost BuildHost()
        {
            var hostBuilder = Host.CreateApplicationBuilder();
            FileFormatRepository.RegisterFileFormats(hostBuilder.Services);

            var secretsPath = _opts.KeyBundlePath;
            if (secretsPath == null || !File.Exists(secretsPath))
            {
                throw new InvalidOperationException($"Secrets file \"{secretsPath ?? ""}\" does not exist");
            }
            var keyFile = new KeyBundle(secretsPath);

            hostBuilder.Services.AddSingleton(keyFile);
            hostBuilder.Services.AddSingleton(_opts);
            hostBuilder.Services.AddSingleton<KeysRepository>();
            hostBuilder.Services.AddSingleton<FileFormatRepository>();
            hostBuilder.Services.AddSingleton<FormatFinder>();
            hostBuilder.Services.AddSingleton<Extractor>();

            var host = hostBuilder.Build();
            return host;
        }

        bool TryTake(IEnumerator<string> it, [MaybeNullWhen(false)] out string arg)
        {
            if (!it.MoveNext())
            {
                arg = null;
                return false;
            }

            arg = it.Current;
            return true;
        }

        private ProgramOptions ProcessArgs(IEnumerable<string> args)
        {
            ProgramMode programMode = ProgramMode.Standalone;
            var formatOptions = new Dictionary<string, Dictionary<string, string>>();
            string? inputFile = null;
            string? destDir = null;
            string? keysPath = Path.Combine(Directory.GetCurrentDirectory(), "secrets.json");

            var it = args.GetEnumerator();
            while (it.MoveNext())
            {
                var res = true;
                var arg = it.Current;
                switch (arg)
                {
                    case "--mount":
                        programMode = ProgramMode.Fuse;
                        break;
                    case "-k":
                        res = TryTake(it, out keysPath);
                        break;
                    case "-i":
                        res = TryTake(it, out inputFile);
                        break;
                    case "-d":
                        res = TryTake(it, out destDir);
                        break;
                    case "-o":
                        // -o fmt:opt=val
                        do
                        {
                            if((res=TryTake(it, out var fmtArg)) == false){
                                break;
                            }
                            var p = fmtArg!.Split(':', 2);
                            if (p.Length != 2) break;
                            var (format, prop, _) = p;

                            p = prop.Split('=', 2);
                            if (p.Length != 2) break;
                            var (key, val, _) = p;
                            
                            if (!formatOptions.TryGetValue(format, out var bucket))
                            {
                                bucket = new Dictionary<string, string>();
                            }
                            bucket[key] = val;
                            formatOptions[format] = bucket;
                        } while (false);
                        break;
                }
            }

            if(inputFile == null)
            {
                throw new ArgumentException("Input filename not specified");
            }
            if (!File.Exists(inputFile))
            {
                throw new ArgumentException("Input filename does not exist");
            }

            if(programMode == ProgramMode.Fuse && destDir == null)
            {
                throw new ArgumentException("Fuse mountpoint not specified");
            }


            var opts = new ProgramOptions
            {
                InputFile = inputFile,
                FormatOptions = formatOptions,
                ProgramMode = programMode,
                DestDir = destDir,
                KeyBundlePath = keysPath
            };
            return opts;
        }

        void Run()
        {
            Console.WriteLine("Firmex#");

            if(_opts.ProgramMode == ProgramMode.Fuse)
            {
                fuseVfs = new YafexVfs();
            }

            var host = BuildHost();

            host.Start();
            var extractor = host.Services.GetRequiredService<Extractor>();

            using (MFile input = new MFile(_opts.InputFile))
            {
                var artifacts = extractor.Extract(fuseVfs?.Root, input);
                var numArtifacts = 0;
                foreach (var artifact in artifacts)
                {
                    ++numArtifacts;
                    // $FIXME: centralized artifact printing
                }
                if (fuseVfs != null && _opts.DestDir != null && numArtifacts > 0)
                {
                    FuseInterop.Start(fuseVfs, _opts.DestDir);
                }
            }
        }

        public static void Main(string[] args)
        {
            var prg = new Program(args);
            prg.Run();
        }

        private static bool IsRunningInCygwin()
        {
            return GetModuleHandle("cygwin1") != IntPtr.Zero;
        }
    }
}
