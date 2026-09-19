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
using Yafex.Metadata;
using Yafex.Support;

namespace Yafex
{
    public enum ProgramMode
    {
        Standalone,
        Fuse,
        List,
        Probe,
        Help
    }

    public class ProgramOptions
    {
        public string? InputFile { get; set; }
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
            KeyBundle keyFile;
            if (secretsPath != null && File.Exists(secretsPath))
            {
                keyFile = new KeyBundle(secretsPath);
            }
            else
            {
                log.WarnFormat("Secrets file \"{0}\" not found, continuing without keys", secretsPath ?? "");
                keyFile = KeyBundle.Empty;
            }

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
            var keysExplicit = false;

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
                    case "--list":
                        programMode = ProgramMode.List;
                        break;
                    case "--probe":
                        programMode = ProgramMode.Probe;
                        break;
                    case "--help":
                    case "-h":
                    case "--list-formats":
                        programMode = ProgramMode.Help;
                        break;
                    case "-k":
                        res = TryTake(it, out keysPath);
                        keysExplicit = true;
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

            if(inputFile == null && programMode != ProgramMode.Help)
            {
                throw new ArgumentException("Input filename not specified");
            }
            if (inputFile != null && !File.Exists(inputFile))
            {
                throw new ArgumentException("Input filename does not exist");
            }

            if(programMode == ProgramMode.Fuse && destDir == null)
            {
                throw new ArgumentException("Fuse mountpoint not specified");
            }
            if (programMode == ProgramMode.List && inputFile == null)
            {
                throw new ArgumentException("Input filename not specified");
            }
            if (programMode == ProgramMode.Probe && inputFile == null)
            {
                throw new ArgumentException("Input filename not specified");
            }

            if (!keysExplicit)
            {
                keysPath = Environment.GetEnvironmentVariable("YAFEX_KEYS") ?? keysPath;
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

            var host = BuildHost();

            if (_opts.FormatOptions.TryGetValue("global", out var global)
                && global.TryGetValue("spool_mb", out var spoolMb)
                && long.TryParse(spoolMb, out var mb) && mb > 0)
            {
                ArtifactSpool.ThresholdBytes = mb << 20;
            }

            host.Start();
            var extractor = host.Services.GetRequiredService<Extractor>();
            var keysRepo = host.Services.GetRequiredService<KeysRepository>();

            if (_opts.ProgramMode == ProgramMode.Help)
            {
                PrintHelp(host.Services.GetRequiredService<FileFormatRepository>());
                return;
            }

            var owned = new List<IDisposable>();
            try
            {
                using (MFile input = new MFile(_opts.InputFile))
                {
                    if (_opts.ProgramMode == ProgramMode.Standalone && _opts.DestDir != null)
                    {
                        // appended last: wins the BaseDirectoryPath reverse-priority stack,
                        // so top-level outputs land under -d instead of next to the input
                        input.AddMetadata(new BaseDirectoryPath(_opts.DestDir));
                    }

                    if (_opts.ProgramMode == ProgramMode.Probe)
                    {
                        RunProbe(host.Services.GetRequiredService<FormatFinder>(), input);
                        return;
                    }
                    if (_opts.ProgramMode == ProgramMode.List)
                    {
                        RunList(host.Services.GetRequiredService<FormatFinder>(), input);
                        return;
                    }
                YafexVfs? fuseVfs = null;

                if (_opts.ProgramMode == ProgramMode.Fuse)
                {
                    if (EpkVfs.TryCreate(input, keysRepo, out var epkVfs))
                    {
                        // lazy EPK mount: paks are read/decrypted on demand
                        fuseVfs = epkVfs;
                    }
                    else
                    {
                        // generic materializing fallback for other formats
                        fuseVfs = new YafexVfs();
                    }
                }

                if (fuseVfs is EpkVfs)
                {
                    FuseInterop.Start(fuseVfs, _opts.DestDir);
                    return;
                }

                var artifacts = extractor.Extract(fuseVfs?.Root, input);
                var numArtifacts = 0;
                foreach (var artifact in artifacts)
                {
                    ++numArtifacts;
                    if (artifact is IDisposable disposable) owned.Add(disposable);
                    // $FIXME: centralized artifact printing
                }
                if (fuseVfs != null && _opts.DestDir != null && numArtifacts > 0)
                {
                    FuseInterop.Start(fuseVfs, _opts.DestDir);
                }
                } // using MFile
            } // try
            finally
            {
                // release streamed mappings; outputs themselves stay on disk
                foreach (var d in owned)
                {
                    try { d.Dispose(); }
                    catch (Exception ex) { log.Warn("artifact dispose failed", ex); }
                }
            }
        }

        private static void PrintHelp(FileFormatRepository repo)
        {
            Console.WriteLine("Usage: yafex -i <firmware> [-d <outdir>] [-k <keys.json>] [-o fmt:opt=val] [--mount|--list|--probe|--help]");
            Console.WriteLine("  --mount          FUSE-mount the firmware (needs -d <mountpoint>)");
            Console.WriteLine("  --list           list contents without extracting (payloads untouched)");
            Console.WriteLine("  --probe          test the file against all formats, show matches");
            Console.WriteLine("  -o fmt:opt=val   per-format option; -o global:spool_mb=<MB> streams big artifacts to disk (default 64)");
            Console.WriteLine("  keys: -k <path>, else $YAFEX_KEYS, else ./secrets.json (missing file = no keys, not fatal)");
            Console.WriteLine("Formats:");
            foreach (var fmt in repo.GetRegisteredFormats().OrderBy(f => f.ToString()))
            {
                Console.WriteLine($"  {Enum.GetName(fmt)?.ToLowerInvariant()}");
                if (repo.TryGetAddonForFormat(fmt, out var addon) && addon != null)
                {
                    foreach (var opt in addon.DescribeOptions())
                    {
                        Console.WriteLine($"    {opt.Key}: {opt.Value}");
                    }
                }
            }
        }

        private static void RunProbe(FormatFinder finder, IDataSource input)
        {
            var hits = finder.ProbeAll(input);
            if (hits.Count == 0)
            {
                Console.WriteLine("no format identified");
                return;
            }
            foreach (var (fmt, _, result) in hits)
            {
                Console.WriteLine($"{Enum.GetName(fmt)?.ToLowerInvariant()}: confidence={result.Confidence}");
            }
        }

        private static void RunList(FormatFinder finder, IDataSource input)
        {
            var (addon, result) = finder.DetectFormatAddon(input);
            if (addon == null || result == null || !result.Succeded())
            {
                Console.WriteLine("no format identified");
                return;
            }
            var lister = addon.CreateLister(result);
            if (lister == null)
            {
                Console.WriteLine($"listing not supported for {Enum.GetName(addon.FileFormat)?.ToLowerInvariant()}");
                return;
            }
            lister.List(input, result, Console.Out);
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
