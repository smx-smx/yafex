using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Yafex.CygwinLauncher
{
    class ProgramOptions
	{
        public string CygwinPath;
		internal string RunnerPath;
		internal string RunnerImpl;
	}

	class Program
	{
        static bool TryTake(IEnumerator<string> it, out string? arg) {
            if (!it.MoveNext()) {
                arg = null;
                return false;
            }

            arg = it.Current;
            return true;
        }

        static void Usage() {
            Console.Write("");
        }

        static string ToCygwinPath(string str) {
            var f = new FileInfo(str);
            var sb = new StringBuilder("/cygdrive/");
            sb.Append(f.FullName.Substring(0, 1).ToLowerInvariant());
            sb.Append(f.FullName.Substring(2).Replace('\\', '/'));
            return sb.ToString();
        }

        static void Launch(IEnumerator<string> args) {
            var thisPath = Assembly.GetExecutingAssembly().Location;
            var thisDir = Path.GetDirectoryName(thisPath);

            MethodInfo x = (methodof<Func<IntPtr, int, int>>)Smx.Yafex.Program.Entry;
            var klass = x.DeclaringType;
            var targetAsm = klass.Assembly;
            var targetAsmName = targetAsm.GetName().Name;

            var pi = new ProcessStartInfo() {
                FileName = "ezdotnet.exe",
            };
            pi.ArgumentList.Add("cygcoreclrhost.dll");
            pi.ArgumentList.Add($"{targetAsmName}.dll");
            pi.ArgumentList.Add(klass.FullName);
            pi.ArgumentList.Add(x.Name);
			while (args.MoveNext()) {
                pi.ArgumentList.Add(args.Current);
			}
            Process.Start(pi);
		}

        static void Main(string[] args) {
            var it = ((IEnumerable<string>)args).GetEnumerator();
            Launch(it);
        }
	}
}
