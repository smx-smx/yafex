#region License
/*
 * Copyright (c) 2026 Stefano Moioli
 * This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable for any damages arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following restrictions:
 *  1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */
#endregion
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using Yafex.Cygwin;
using Yafex.CygwinLauncher;

namespace Yafex.CygwinLauncher
{
    class Program
    {
        static bool TryTake(IEnumerator<string> it, out string arg)
        {
            if (!it.MoveNext())
            {
                arg = null;
                return false;
            }

            arg = it.Current;
            return true;
        }

        static void Usage()
        {
            Console.Write("");
        }


        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        static string ToCygwinPath(string str)
        {
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

        sealed class TeeTextWriter : TextWriter
        {
            private readonly TextWriter _a;
            private readonly TextWriter _b;

            public override Encoding Encoding => _a.Encoding;

            public TeeTextWriter(TextWriter a, TextWriter b)
            {
                _a = a;
                _b = b;
            }

            public override void Write(char value)
            {
                _a.Write(value);
                _b.Write(value);
            }

            public override void Write(char[] buffer, int index, int count)
            {
                _a.Write(buffer, index, count);
                _b.Write(buffer, index, count);
            }

            public override void Write(string value)
            {
                _a.Write(value);
                _b.Write(value);
            }

            public override void WriteLine()
            {
                _a.WriteLine();
                _b.WriteLine();
            }

            public override void WriteLine(string value)
            {
                _a.WriteLine(value);
                _b.WriteLine(value);
            }

            public override void Flush()
            {
                _a.Flush();
                _b.Flush();
            }
        }

        public static int Entry(IntPtr args, int sizeBytes)
        {
            //Debugger.Launch();
            string[] argv = ReadArgv(args, sizeBytes);

            string thisDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            AppDomain.CurrentDomain.SetData("APP_CONTEXT_BASE_DIRECTORY", thisDir);

            Action<MainDelegate> initializer;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                IsRunningInCygwin()
            )
            {
                initializer = (main) =>
                {
                    using (var stdin = new StreamReader(new CygwinInputStream(0)))
                    using (var stdout = new StreamWriter(new CygwinOutputStream(1)))
                    using (var stderr = new StreamWriter(new CygwinOutputStream(2)))
                    using (var fileStream = new FileStream(
                        Path.Combine(thisDir, "yafex-console.log"),
                        FileMode.Create, FileAccess.Write, FileShare.Read))
                    using (var fileLog = new StreamWriter(fileStream) { AutoFlush = true })
                    {
                        stdout.AutoFlush = true;
                        stderr.AutoFlush = true;

                        Console.SetIn(stdin);
                        Console.SetOut(new TeeTextWriter(stdout, fileLog));
                        Console.SetError(new TeeTextWriter(stderr, fileLog));

                        /**
                         * catch and print unhandled exceptions
                         * otherwise, when running in cygwin, unhandled exceptions will not be printed
                         ***/
                        try
                        {
                            main(argv);
                        }
                        catch (Exception ex)
                        {
                            var crashLog = Path.Combine(thisDir, "crash.log");
                            File.AppendAllText(crashLog,
                                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UNHANDLED EXCEPTION\n{ex}\n\n");
                            Console.Error.WriteLine("==== UNHANDLED EXCEPTION");
                            Console.Error.WriteLine(ex.ToString());
                            throw;
                        }
                    }
                };
            }
            else
            {
                initializer = (main) =>
                {
                    main(argv);
                };
            }


            initializer(Yafex.Program.Main);

#if false
            initializer((argv) =>
            {
                var task = Yafex.Program.MainAsync(argv);
                task.Wait();
            });
#endif
            return 0;
        }

        static void Launch(IEnumerator<string> args)
        {
            var thisPath = Assembly.GetExecutingAssembly().Location;
            var thisDir = Path.GetDirectoryName(thisPath);

            MethodInfo x = (MethodOf<Func<IntPtr, int, int>>)Program.Entry;
            var klass = x.DeclaringType;
            var targetAsm = klass.Assembly;
            var targetAsmName = targetAsm.GetName().Name;

            var basedir = "external";

            var pi = new ProcessStartInfo()
            {
                FileName = Path.Combine(basedir, "ezdotnet.exe"),
            };
            pi.ArgumentList.Add(Path.Combine(basedir, "cygcoreclrhost.dll"));
            pi.ArgumentList.Add($"{targetAsmName}.dll");
            pi.ArgumentList.Add(klass.FullName);
            pi.ArgumentList.Add(x.Name);
            while (args.MoveNext())
            {
                pi.ArgumentList.Add(args.Current);
            }
            Process.Start(pi);
        }

        static void Main(string[] args)
        {
            var it = ((IEnumerable<string>)args).GetEnumerator();
            Launch(it);
        }
    }
}
