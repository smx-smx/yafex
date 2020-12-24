using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Text;

namespace Smx.Yafex.Support
{
	public static class IServiceContainerExtensions
	{
		public static void AddService<T>(this IServiceContainer @this, object instance) {
			@this.AddService(typeof(T), instance);
		}

		public static T GetService<T>(this IServiceContainer @this) {
			return (T)@this.GetService(typeof(T));
		}
	}
}
