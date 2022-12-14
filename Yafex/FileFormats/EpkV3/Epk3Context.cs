using Yafex.FileFormats.Epk;

namespace Yafex.FileFormats.EpkV3
{
	public class Epk3Context<T> : EpkContext<T> where T : struct
	{
		public Epk3Context(
			EpkServicesFactory servicesFactory, EpkServices services, T header
		) : base(servicesFactory, services, header){}

	}
}