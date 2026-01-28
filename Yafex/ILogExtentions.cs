using System;
using System.Reflection;
using log4net;
using log4net.Core;

namespace Yafex;
public static class ILogExtentions
{
    private static void Log(ILog log, Type? type, Level level, Exception? exception, string message)
    {
        if(type == null)
        {
            throw new InvalidOperationException("declaring type cannot be null");
        }

        log.Logger.Log(type, level, message, exception);
    }

    public static void Trace(this ILog log, string message, Exception? exception)
    {
        Log(log, MethodBase.GetCurrentMethod()?.DeclaringType, Level.Trace, exception, message);
    }

    public static void Verbose(this ILog log, string message, Exception? exception)
    {
        Log(log, MethodBase.GetCurrentMethod()?.DeclaringType, Level.Verbose, exception, message);
    }

    public static void Fine(this ILog log, string message, Exception? exception)
    {
        Log(log, MethodBase.GetCurrentMethod()?.DeclaringType, Level.Fine, exception, message);
    }

    public static void Finer(this ILog log, string message, Exception? exception)
    {
        Log(log, MethodBase.GetCurrentMethod()?.DeclaringType, Level.Finer, exception, message);
    }

    public static void Finest(this ILog log, string message, Exception? exception)
    {
        Log(log, MethodBase.GetCurrentMethod()?.DeclaringType, Level.Finest, exception, message);
    }

    public static void Severe(this ILog log, string message, Exception? exception)
    {
        Log(log, MethodBase.GetCurrentMethod()?.DeclaringType, Level.Severe, exception, message);
    }

    public static void Notice(this ILog log, string message, Exception? exception)
    {
        Log(log, MethodBase.GetCurrentMethod()?.DeclaringType, Level.Notice, exception, message);
    }

    public static void Alert(this ILog log, string message, Exception? exception)
    {
        Log(log, MethodBase.GetCurrentMethod()?.DeclaringType, Level.Alert, exception, message);
    }

    public static void Critical(this ILog log, string message, Exception? exception)
    {
        Log(log, MethodBase.GetCurrentMethod()?.DeclaringType, Level.Critical, exception, message);
    }

    public static void Emergency(this ILog log, string message, Exception? exception)
    {
        Log(log, MethodBase.GetCurrentMethod()?.DeclaringType, Level.Emergency, exception, message);
    }

    public static void Fatal(this ILog log, string message, Exception? exception)
    {
        Log(log, MethodBase.GetCurrentMethod()?.DeclaringType, Level.Fatal, exception, message);
    }

    public static void VerboseFormat(this ILog log, string message, params object?[] args) => Verbose(log, string.Format(message, args));
    public static void TraceFormat(this ILog log, string message, params object?[] args) => Trace(log, string.Format(message, args));
    public static void FineFormat(this ILog log, string message, params object?[] args) => Fine(log, string.Format(message, args));
    public static void FinerFormat(this ILog log, string message, params object?[] args) => Finer(log, string.Format(message, args));
    public static void FinestFormat(this ILog log, string message, params object?[] args) => Finest(log, string.Format(message, args));
    public static void SevereFormat(this ILog log, string message, params object?[] args) => Severe(log, string.Format(message, args));
    public static void NoticeFormat(this ILog log, string message, params object?[] args) => Notice(log, string.Format(message, args));
    public static void AlertFormat(this ILog log, string message, params object?[] args) => Alert(log, string.Format(message, args));
    public static void CriticalFormat(this ILog log, string message, params object?[] args) => Critical(log, string.Format(message, args));
    public static void EmergencyFormat(this ILog log, string message, params object?[] args) => Emergency(log, string.Format(message, args));
    public static void FatalFormat(this ILog log, string message, params object?[] args) => Fatal(log, string.Format(message, args));

    public static void Verbose(this ILog log, string message) => log.Verbose(message, null);
    public static void Trace(this ILog log, string message) => log.Trace(message, null);
    public static void Fine(this ILog log, string message) => log.Fine(message, null);
    public static void Finer(this ILog log, string message) => log.Finer(message, null);
    public static void Finest(this ILog log, string message) => log.Finest(message, null);
    public static void Severe(this ILog log, string message) => log.Severe(message, null);
    public static void Notice(this ILog log, string message) => log.Notice(message, null);
    public static void Alert(this ILog log, string message) => log.Alert(message, null);
    public static void Critical(this ILog log, string message) => log.Critical(message, null);
    public static void Emergency(this ILog log, string message) => log.Emergency(message, null);
    public static void Fatal(this ILog log, string message) => log.Fatal(message, null);
}