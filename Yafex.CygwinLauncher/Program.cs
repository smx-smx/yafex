using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Yafex.Cygwin;
using Yafex.CygwinLauncher;

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


        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        static string ToCygwinPath(string str) {
            var f = new FileInfo(str);
            var sb = new StringBuilder("/cygdrive/");
            sb.Append(f.FullName.Substring(0, 1).ToLowerInvariant());
            sb.Append(f.FullName.Substring(2).Replace('\\', '/'));
            return sb.ToString();
        }

        private delegate void MainDelegate(string[] args);

        private static string[] ReadArgv(IntPtr args, int sizeBytes)
        {
            int nargs = sizeBytes / IntPtr.Size;
            string[] argv = new string[nargs];

            for (int i = 0; i < nargs; i++, args += IntPtr.Size)
            {
                IntPtr charPtr = Marshal.ReadIntPtr(args);
                argv[i] = Marshal.PtrToStringAnsi(charPtr);
            }
            return argv;
        }

        private static bool IsRunningInCygwin()
        {
            return GetModuleHandle("cygwin1") != IntPtr.Zero;
        }

        public static int Entry(IntPtr args, int sizeBytes)
        {
            string[] argv = ReadArgv(args, sizeBytes);

            string thisDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            AppDomain.CurrentDomain.SetData("APP_CONTEXT_BASE_DIRECTORY", thisDir);

            Action<MainDelegate> initializer;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                IsRunningInCygwin()
            )
            {
                EzDotnetCompat.InstallHooks();
                initializer = (main) => {
                    using (var stdin = new StreamReader(new CygwinInputStream(0)))
                    using (var stdout = new StreamWriter(new CygwinOutputStream(1)))
                    using (var stderr = new StreamWriter(new CygwinOutputStream(2)))
                    {
                        stdout.AutoFlush = true;
                        stderr.AutoFlush = true;

                        Console.SetIn(stdin);
                        Console.SetOut(stdout);
                        Console.SetError(stderr);

                        main(argv);
                    }
                };
            }
            else
            {
                initializer = (main) => {
                    main(argv);
                };
            }


            initializer(Yafex.Program.Main);
            return 0;
        }

        static void Launch(IEnumerator<string> args) {
            var thisPath = Assembly.GetExecutingAssembly().Location;
            var thisDir = Path.GetDirectoryName(thisPath);

            MethodInfo x = (methodof<Func<IntPtr, int, int>>)Program.Entry;
            var klass = x.DeclaringType;
            var targetAsm = klass.Assembly;
            var targetAsmName = targetAsm.GetName().Name;

            var basedir = "external";

            var pi = new ProcessStartInfo() {
                FileName = Path.Combine(basedir, "ezdotnet.exe"),
            };
            pi.ArgumentList.Add(Path.Combine(basedir, "cygcoreclrhost.dll"));
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
