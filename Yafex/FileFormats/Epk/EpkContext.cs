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
﻿using Yafex.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Yafex.FileFormats.Epk
{
	public abstract class EpkContext<T> where T : struct
	{
		/// <summary>
		/// 
		/// </summary>
		/// <remarks>
		/// EPK files can have plain header with crypted PAKs,
		/// so we will need to create the decryptor later
		/// </remarks>
		public readonly EpkServicesFactory ServiceFactory;
		public readonly EpkServices Services;
		public readonly T Header;

		public EpkContext(
			EpkServicesFactory servicesFactory,
			EpkServices services,
			T header
		) {
			this.ServiceFactory = servicesFactory;
			this.Services = services;
			this.Header = header;
		}

		public void EnsureDecryptor(ReadOnlySpan<byte> data, ValidatorDelegate validator) {
			if (this.Services.Decryptor == null) {
				this.Services.Decryptor = this.ServiceFactory.CreateEpkDecryptor(data, validator);
			}
		}

		public EpkDecryptionService? Decryptor => this.Services.Decryptor;
	}
}
