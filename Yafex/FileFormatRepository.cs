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
using System.Collections.Generic;

using log4net;

using Microsoft.Extensions.DependencyInjection;

using Yafex.FileFormats.EpkV1;
using Yafex.FileFormats.EpkV2;
using Yafex.FileFormats.EpkV3;
using Yafex.FileFormats.FreescaleNand;
using Yafex.FileFormats.LxBoot;
using Yafex.FileFormats.Lzhs;
using Yafex.FileFormats.MStarPkg;
using Yafex.FileFormats.Nfwb;
using Yafex.FileFormats.Partinfo;
using Yafex.FileFormats.Xex;

namespace Yafex
{
    public class FileFormatRepository
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(FileFormatRepository));

        public const string SERVICES_KEY = "Yafex.FileFormats";

        private readonly Dictionary<FileFormat, IFormatAddon> addons = new Dictionary<FileFormat, IFormatAddon>();

        public FileFormatRepository(IServiceProvider services)
        {
            var addons = services.GetKeyedServices<IFormatAddon>(SERVICES_KEY);
            foreach (var addon in addons)
            {
                log.DebugFormat("Registering format {0}", Enum.GetName(addon.FileFormat));
                RegisterFormat(addon);
            }
        }

        public void RegisterFormat(IFormatAddon addon)
        {
            RegisterFormat(addon.FileFormat, addon);
        }

        public void RegisterFormat(FileFormat format, IFormatAddon addon)
        {
            this.addons[format] = addon;
        }

        public bool TryGetAddonForFormat(FileFormat format, out IFormatAddon? addon)
        {
            return addons.TryGetValue(format, out addon);
        }

        public IEnumerable<FileFormat> GetRegisteredFormats()
        {
            return addons.Keys;
        }

        public Dictionary<FileFormat, IFormatAddon> GetAddons() => addons;

        public static void RegisterFileFormats(IServiceCollection services)
        {
            var key = SERVICES_KEY;
            services.AddKeyedSingleton<IFormatAddon, Epk1Addon>(key);
            services.AddKeyedSingleton<IFormatAddon, Epk2Addon>(key);
            services.AddKeyedSingleton<IFormatAddon, Epk2BetaAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, Epk3NewAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, PartinfoAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, LzhsAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, MStarPkgAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, FreescaleNandAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, XexAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, LxSecureBootAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, NfwbAddon>(key);
        }
    }
}
