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
using System.Collections.Generic;
using Yafex.FileFormats.EpkV1;
using Yafex.FileFormats.EpkV2;
using Yafex.FileFormats.EpkV3;
using Yafex.FileFormats.FreescaleNand;
using Yafex.FileFormats.LxBoot;
using Yafex.FileFormats.Lzhs;
using Yafex.FileFormats.MStarPkg;
using Yafex.FileFormats.Partinfo;
using Yafex.FileFormats.Xex;

namespace Yafex
{
	public class FileFormatRepository
	{
		private readonly Dictionary<FileFormat, IFormatAddon> addons = new Dictionary<FileFormat, IFormatAddon>();

		public FileFormatRepository(
			Epk1Addon epk1,
			Epk2Addon epk2,
			Epk2BetaAddon epk2_beta,
			Epk3NewAddon epk3_new,
			PartinfoAddon partinfo,
			LzhsAddon lzhs,
			MStarPkgAddon mstar_pkg,
            FreescaleNandAddon freescale_nand,
			XexAddon xex,
			LxSecureBootAddon lx_secureBoot
		)
		{
			foreach(var fmt in (IEnumerable<IFormatAddon>)[
				epk1, epk2, epk2_beta,
				epk3_new, partinfo, lzhs,
				mstar_pkg, freescale_nand,
				xex, lx_secureBoot
			])
			{
				RegisterFormat(fmt);
			}
        }

		public void RegisterFormat(IFormatAddon addon)
		{
			RegisterFormat(addon.FileFormat, addon);
		}

		public void RegisterFormat(FileFormat format, IFormatAddon addon) {
			this.addons[format] = addon;
		}

		public bool TryGetAddonForFormat(FileFormat format, out IFormatAddon? addon) {
			return addons.TryGetValue(format, out addon);
		}

		public IEnumerable<FileFormat> GetRegisteredFormats() {
			return addons.Keys;
		}

		public Dictionary<FileFormat, IFormatAddon> GetAddons() => addons;
	}
}
