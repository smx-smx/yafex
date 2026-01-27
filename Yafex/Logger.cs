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
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;

using System.Configuration;
using System.Reflection;

namespace Yafex
{
    public class Logger
    {
        private static void ReloadConfig()
        {
            string filePath = Assembly.GetExecutingAssembly().Location + ".config";
            ExeConfigurationFileMap fileMap = new ExeConfigurationFileMap()
            {
                ExeConfigFilename = filePath
            };
            ConfigurationManager.OpenMappedExeConfiguration(fileMap, ConfigurationUserLevel.None);
            // will invoke the hooked GetEntryAssembly
            ConfigurationManager.RefreshSection("log4net");
        }

        public static void Setup()
        {
            // required when running under EzDotNet, as the appconfig won't be initialized
            ReloadConfig();

            Hierarchy hierarchy = (Hierarchy)LogManager.GetRepository();

            PatternLayout patternLayout = new PatternLayout();
            //patternLayout.ConversionPattern = "%date [%thread] %-5level %logger - %message%newline";
            patternLayout.ConversionPattern = "%logger - %message%newline";
            patternLayout.ActivateOptions();

            ConsoleAppender console = new ConsoleAppender();
            console.Layout = patternLayout;
            console.ActivateOptions();
            hierarchy.Root.AddAppender(console);

            hierarchy.Root.Level = Level.Finest;
            hierarchy.Configured = true;
        }
    }
}
