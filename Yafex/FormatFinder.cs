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
using System.Linq;

namespace Yafex
{
    ref struct FinderArg
    {
        public FileFormat fmt;
        public Span<byte> data;
    }

    public class FormatFinder
    {
        private readonly FileFormatRepository repo;

        private delegate int GetConfidenceDelegate(FinderArg arg);

        private static readonly ILog log = LogManager.GetLogger(typeof(FormatFinder));

        public FormatFinder(FileFormatRepository repo)
        {
            this.repo = repo;
        }

        private DetectionResult Detect(FileFormat fmt, IDataSource source)
        {
            repo.TryGetAddonForFormat(fmt, out var addon);
            return addon!.CreateDetector().Detect(source);
        }

        public (IFormatAddon?, DetectionResult?) DetectFormatAddon(IDataSource source)
        {
            var formats = repo.GetRegisteredFormats();
            if (formats.Count() < 1)
                return (null, null);


            int bestConfidence = 0;
            IFormatAddon? bestAddon = null;
            DetectionResult bestResult = null!;

            foreach (var fmt in formats)
            {
                repo.TryGetAddonForFormat(fmt, out var addon);
                DetectionResult? result = null;
                try
                {
                    result = Detect(fmt, source);

                    if (result.Confidence > bestConfidence)
                    {
                        bestConfidence = result.Confidence;
                        bestAddon = addon;
                        bestResult = result;
                    }
                }
                catch (DllNotFoundException ex)
                {
                    log.Error("Failed to load native addon", ex);
                }
            }

            return (bestAddon, bestResult);
        }
    }
}
