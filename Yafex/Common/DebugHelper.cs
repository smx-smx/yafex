using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Smx.Yafex.Common
{
	public static class DebugHelper
	{
		public static void Launch() {
			if (!Debugger.IsAttached) {
				if (!Debugger.Launch()) {
					return;
				}
				while (!Debugger.IsAttached) {
					Thread.Sleep(500);
				}
				Debugger.Break();
			}
		}
	}
}
