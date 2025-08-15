using System;
using System.IO;
using Jellyfin.Plugin.Newsletters.Configuration;

namespace Jellyfin.Plugin.Newsletters;

/// <summary>
/// Initializes a new instance of the <see cref="Logger"/> class.
/// </summary>
public class Logger
{
    private readonly PluginConfiguration config;
    private readonly string logDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="Logger"/> class.
    /// </summary>
    public Logger()
    {
        config = Plugin.Instance!.Configuration;
        logDirectory = config.LogDirectoryPath;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Debug"/> class.
    /// </summary>
    /// <param name="msg">The message to log as info.</param>
    public void Debug(object msg)
    {
        PluginConfiguration config = Plugin.Instance!.Configuration;
        if (config.DebugMode)
        {
            Info(msg);
        }
    }

    /// <summary>
    /// Inform info into the logs.
    /// </summary>
    /// <param name="msg">The message to log as info.</param>
    public void Info(object msg)
    {
        Inform(msg, "INFO");
    }

    /// <summary>
    /// Inform warn into the logs.
    /// </summary>
    /// <param name="msg">The message to log as info.</param>
    public void Warn(object msg)
    {
        Inform(msg, "WARN");
    }

    /// <summary>
    /// Inform error into the logs.
    /// </summary>
    /// <param name="msg">The message to log as info.</param>
    public void Error(object msg)
    {
        Inform(msg, "ERR");
    }

    /// <summary>
    /// Inform specific type of warning into the logs.
    /// </summary>
    /// <param name="msg">The message to infrom into the logs.</param>
    /// <param name="type">Type of warning ("ERR", "WARN", "INFO").</param>
    private void Inform(object msg, string type)
    {
        string logMsgPrefix = $"[NLP]: {GetDateTime()} - [{type}] ";
        Console.WriteLine($"{logMsgPrefix}{msg}");
        var logFile = GetCurrentLogFile();
        File.AppendAllText(logFile, $"{logMsgPrefix}{msg}\n");
    }

    private static string GetDateTime()
    {
        return DateTime.Now.ToString("[yyyy-MM-dd] :: [HH:mm:ss]", System.Globalization.CultureInfo.CurrentCulture);
    }

    private static string GetDate()
    {
        return DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.CurrentCulture);
    }

    private string GetCurrentLogFile()
    {
        return $"{logDirectory}/{GetDate()}_Newsletter.log";
    }
}