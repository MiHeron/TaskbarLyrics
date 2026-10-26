using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace TaskbarLyrics.Watchdog;

internal static class Program
{
    private const string MutexName = @"Local\TaskbarLyrics.Watchdog";
    private static readonly string[] PlayerProcessNames = ["QQMusic", "QQMusicApp"];
    private static readonly string[] ApplicationProcessNames = ["TaskbarLyrics", "TaskbarLyrics.App"];
    private static readonly string[] ApplicationFileNames = ["TaskbarLyrics.exe", "TaskbarLyrics.App.exe"];
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TaskbarLyrics",
        "settings.json");
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaskbarLyrics",
        "watchdog.log");

    private static int Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            return 0;
        }

        var playerRunning = IsAnyProcessRunning(PlayerProcessNames);
        if (playerRunning && IsFeatureEnabled())
        {
            TryStartApplication();
        }

        while (true)
        {
            Thread.Sleep(PollInterval);
            var running = IsAnyProcessRunning(PlayerProcessNames);
            if (running && !playerRunning && IsFeatureEnabled())
            {
                TryStartApplication();
            }

            playerRunning = running;
        }
    }

    private static void TryStartApplication()
    {
        if (IsAnyProcessRunning(ApplicationProcessNames))
        {
            return;
        }

        var applicationPath = FindApplicationPath();
        if (applicationPath is null)
        {
            Log("TaskbarLyrics.exe was not found next to the watchdog.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(applicationPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(applicationPath) ?? AppContext.BaseDirectory
            });
            Log($"Started {applicationPath}");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            Log($"Failed to start {applicationPath}: {exception.Message}");
        }
    }

    private static string? FindApplicationPath()
    {
        foreach (var fileName in ApplicationFileNames)
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsFeatureEnabled()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            return document.RootElement.TryGetProperty("RunWithQQMusic", out var value) &&
                value.ValueKind == JsonValueKind.True;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Log($"Failed to read settings: {exception.Message}");
            return false;
        }
    }

    private static bool IsAnyProcessRunning(string[] processNames)
    {
        foreach (var processName in processNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            try
            {
                if (processes.Length > 0)
                {
                    return true;
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        return false;
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Logging must never stop the watchdog.
        }
    }
}
