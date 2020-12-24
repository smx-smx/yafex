using Smx.Yafex.FileFormats.Epk;
using Smx.Yafex.Util;
using System;

namespace Smx.Yafex.FileFormats.EpkV2
{
	public class Epk2Context : EpkContext<EPK_V2_HEADER>
	{
		public Epk2Context(EpkServicesFactory servicesFactory,
			EpkServices services,
			EPK_V2_HEADER header
		) : base(servicesFactory, services, header) {
		}
	}
}
