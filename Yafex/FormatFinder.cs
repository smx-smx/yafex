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

using System;
using System.Collections.Generic;
using System.Linq;

namespace Yafex
{
    public class FormatFinder
    {
        private readonly FileFormatRepository repo;
        private readonly ProgramOptions _opts;

        private static readonly ILog log = LogManager.GetLogger(typeof(FormatFinder));

        public FormatFinder(ProgramOptions opts, FileFormatRepository repo)
        {
            this.repo = repo;
            _opts = opts;
        }

        private DetectionResult Detect(FileFormat fmt, IFormatAddon addon, IDataSource source)
        {
            var formatName = (Enum.GetName(fmt) ?? string.Empty).ToLowerInvariant();
            if(!_opts.FormatOptions.TryGetValue(formatName, out var fmtOpts))
            {
                fmtOpts = new Dictionary<string, string>();
            }
            return addon.CreateDetector(fmtOpts).Detect(source);
        }

        /// <summary>
        /// Best match for the input, or (null, null) when nothing matched.
        /// Ties keep the first-registered format.
        /// </summary>
        public (IFormatAddon?, DetectionResult?) DetectFormatAddon(IDataSource source)
        {
            var hits = ProbeAll(source);
            if (hits.Count == 0)
            {
                return (null, null);
            }

            var best = hits[0];
            var tied = hits.Skip(1)
                .TakeWhile(h => h.Result.Confidence == best.Result.Confidence)
                .Select(h => Enum.GetName(h.Format) ?? h.Format.ToString())
                .ToList();
            if (tied.Count > 0)
            {
                log.DebugFormat("detection tie at {0} between {1} (winner) and {2}",
                    best.Result.Confidence, Enum.GetName(best.Format), string.Join(",", tied));
            }
            return (best.Addon, best.Result);
        }

        /// <summary>
        /// Tests the input against every registered format, returning all matches
        /// with confidence &gt; 0 ordered by descending confidence (stable: ties
        /// keep registration order). Detectors that throw are skipped (logged)
        /// instead of aborting the probe.
        /// </summary>
        public List<(FileFormat Format, IFormatAddon Addon, DetectionResult Result)> ProbeAll(IDataSource source)
        {
            var hits = new List<(FileFormat Format, IFormatAddon Addon, DetectionResult Result)>();
            foreach (var (fmt, addon) in repo.GetAddons())
            {
                DetectionResult? result;
                try
                {
                    result = Detect(fmt, addon, source);
                }
                catch (Exception ex)
                {
                    log.Debug($"Detector for {Enum.GetName(fmt)} failed", ex);
                    continue;
                }
                if (result == null || result.Confidence <= 0)
                {
                    continue;
                }
                hits.Add((fmt, addon, result));
            }
            return hits.OrderByDescending(h => h.Result.Confidence).ToList();
        }
    }
}
