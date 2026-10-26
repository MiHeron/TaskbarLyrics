using System.ComponentModel;
using System.Diagnostics;
using System.IO;

using Microsoft.Win32;

using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

/// <summary>
/// Manages the optional QQ Music watchdog: logon registration, one-off launch and cleanup.
/// </summary>
internal static class WatchdogStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TaskbarLyricsWatchdog";
    private const string WatchdogFileName = "TaskbarLyrics.Watchdog.exe";
    private const string WatchdogProcessName = "TaskbarLyrics.Watchdog";

    public static string WatchdogPath => Path.Combine(AppContext.BaseDirectory, WatchdogFileName);

    public static bool IsWatchdogPresent() => File.Exists(WatchdogPath);

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string value &&
            value.Contains(WatchdogFileName, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key is null)
        {
            return;
        }

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        if (!IsWatchdogPresent())
        {
            Log.Warn($"[Watchdog] Executable not found: {WatchdogPath}");
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        key.SetValue(ValueName, $"\"{WatchdogPath}\"");
    }

    public static void Start()
    {
        if (!IsWatchdogPresent() || IsRunning())
        {
            return;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(WatchdogPath)
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            Log.Warn($"[Watchdog] Failed to start: {exception.Message}");
        }
    }

    public static void StopRunning()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(WatchdogProcessName);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        foreach (var process in processes)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                Log.Warn($"[Watchdog] Failed to stop process {process.Id}: {exception.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static bool IsRunning()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(WatchdogProcessName);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
