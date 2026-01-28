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
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using log4net.Util;

using System;
using System.IO;
using System.Linq;

namespace Yafex
{
    public class Logger
    {
        private static void ReloadConfig()
        {
            var asmPath = typeof(Logger).Assembly.Location;
            var asmDir = Path.GetDirectoryName(asmPath);
            if(asmDir == null)
            {
                throw new InvalidOperationException("Failed to get assembly directory name");
            }

            // required when running under EzDotNet, as the appconfig won't be initialized
            SystemInfo.EntryAssemblyLocation = asmPath;
            var configPath = Path.Combine(asmDir, "log4net.config");

            XmlConfigurator.Configure(new FileInfo(configPath));
        }

        private static void ConfigureDefault(Hierarchy hierarchy)
        {
            PatternLayout patternLayout = new PatternLayout();
            //patternLayout.ConversionPattern = "%date [%thread] %-5level %logger - %message%newline";
            patternLayout.ConversionPattern = "%logger - %message%newline";
            patternLayout.ActivateOptions();

            ConsoleAppender console = new ConsoleAppender();
            console.Layout = patternLayout;
            console.ActivateOptions();
            hierarchy.Root.AddAppender(console);

            hierarchy.Root.Level = Level.Info;
            hierarchy.Configured = true;
        }

        public static void Setup(bool enableDebug)
        {
            ReloadConfig();

            Hierarchy hierarchy = (Hierarchy)LogManager.GetRepository();

            var appenders = hierarchy.GetAppenders();
            var existingConsole = appenders.FirstOrDefault(x => x is ConsoleAppender);

            if (existingConsole == null)
            {
                ConfigureDefault(hierarchy);
            }

            if (enableDebug)
            {
                hierarchy.Root.Level = Level.Finest;
            }
        }
    }
}
