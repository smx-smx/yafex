using Yafex.Util;
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
