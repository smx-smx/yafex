using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Yafex.FileFormats.Epk;

namespace Yafex.FileFormats.EpkV3
{
    public class Epk3NewContext : EpkContext<EPK_V3_NEW_HEADER>
    {
        public Epk3NewContext(EpkServicesFactory servicesFactory,
            EpkServices services,
            EPK_V3_NEW_HEADER header
        ) : base(servicesFactory, services, header)
        {
        }
    }
}
