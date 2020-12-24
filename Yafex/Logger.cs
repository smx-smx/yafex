using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace Smx.Yafex
{
    public class Logger
    {
        private static void ReloadConfig() {
            string filePath = Assembly.GetExecutingAssembly().Location + ".config";
            ExeConfigurationFileMap fileMap = new ExeConfigurationFileMap() {
                ExeConfigFilename = filePath
            };
            ConfigurationManager.OpenMappedExeConfiguration(fileMap, ConfigurationUserLevel.None);
            // will invoke the hooked GetEntryAssembly
            ConfigurationManager.RefreshSection("log4net");
        }

        public static void Setup() {
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

            hierarchy.Root.Level = Level.Info;
            hierarchy.Configured = true;
        }
    }
}