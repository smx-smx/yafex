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
﻿using Yafex.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Yafex.Fuse;

namespace Yafex.FileFormats.Lzhs
{
	class LzhsAddon : IFormatAddon
	{
		public IFormatDetector CreateDetector(Config config) {
			return new LzhsDetector(config);
		}

		public IFormatExtractor CreateExtractor(Config config, DetectionResult result) {
			return new LzhsExtractor(config, result);
		}

        public IVfsNode CreateVfsNode(IDataSource ds)
        {
            throw new NotImplementedException();
        }
    }
}
