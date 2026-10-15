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
using Yafex.FileFormats.LzhsFs;
using Yafex.FileFormats.MediatekPkg;
using Yafex.FileFormats.MediatekPkgOld;
using Yafex.FileFormats.MStarPkg;
using Yafex.FileFormats.Nfwb;
using Yafex.FileFormats.Partinfo;
using Yafex.FileFormats.Xex;
using Yafex.FileFormats.SddlSec;
using Yafex.FileFormats.LgUp;
using Yafex.FileFormats.AllwinnerImg;
using Yafex.FileFormats.Msd11;
using Yafex.FileFormats.Lzo;
using Yafex.FileFormats.Cramfs;
using Yafex.FileFormats.Gzip;
using Yafex.FileFormats.Idb;
using Yafex.FileFormats.IntelHex;
using Yafex.FileFormats.Jffs2;
using Yafex.FileFormats.Lz4;
using Yafex.FileFormats.MstarSb;
using Yafex.FileFormats.MstarSecure;
using Yafex.FileFormats.MstarEcb;
using Yafex.FileFormats.MtkBoot;
using Yafex.FileFormats.Nfsb;
using Yafex.FileFormats.Sym;
using Yafex.FileFormats.Squashfs;
using Yafex.FileFormats.Squashfs3;
using Yafex.FileFormats.Uefi;
using Yafex.FileFormats.Ufs;
using Yafex.FileFormats.UImage;
using Yafex.FileFormats.AndroidBoot;
using Yafex.FileFormats.AmdPsp;
using Yafex.FileFormats.Squashfs2;
using Yafex.FileFormats.Tli;
using Yafex.FileFormats.Tar;
using Yafex.FileFormats.Ubi;
using Yafex.FileFormats.Ubifs;
using Yafex.FileFormats.BroadcomImg;
using Yafex.FileFormats.Spv;
using Yafex.FileFormats.AdbImg;
using Yafex.FileFormats.MtkBdp;
using Yafex.FileFormats.Funai;
using Yafex.FileFormats.Zipper;
using Yafex.FileFormats.Sparse;

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
            services.AddKeyedSingleton<IFormatAddon, MediatekPkgAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, MediatekPkgOldAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, LzhsFsAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, SddlSecAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, LgUpAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, AllwinnerImgAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, Msd11Addon>(key);
            services.AddKeyedSingleton<IFormatAddon, LgLzoAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, CramfsAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, Squashfs4Addon>(key);
            services.AddKeyedSingleton<IFormatAddon, Squashfs3Addon>(key);
            services.AddKeyedSingleton<IFormatAddon, NfsbAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, UImageAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, GzipAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, Lz4pAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, Jffs2Addon>(key);
            services.AddKeyedSingleton<IFormatAddon, IdbAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, SymAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, IntelHexAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, MtkBootAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, MstarSbAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, MstarSecureAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, MstarEcbAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, UefiAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, UfsAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, AndroidBootAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, AmdPspAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, Squashfs2Addon>(key);
            services.AddKeyedSingleton<IFormatAddon, TliAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, TarAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, UbiAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, UbifsAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, BroadcomImgAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, SpvAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, AdbImgAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, MtkBdpAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, FunaiUpgAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, FunaiPhlAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, FunaiBdpAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, FunaiMstarAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, ZipperAddon>(key);
            services.AddKeyedSingleton<IFormatAddon, SparseAddon>(key);
        }
    }
}
