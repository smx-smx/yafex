using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Yafex.CygwinLauncher
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <see>https://stackoverflow.com/a/3472250/11782802</see>
    public class methodof<T>
    {
        private MethodInfo method;

        public methodof(T func) {
            Delegate del = (Delegate)(object)func;
            this.method = del.Method;
        }

        public static implicit operator methodof<T>(T methodof) {
            return new methodof<T>(methodof);
        }

        public static implicit operator MethodInfo(methodof<T> methodof) {
            return methodof.method;
        }
    }
}
