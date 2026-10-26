using System.Diagnostics;
using System.Windows.Threading;

using TaskbarLyrics.Core.Utilities;

using ThreadingTimer = System.Threading.Timer;

namespace TaskbarLyrics.App;

/// <summary>
/// Watches for QQ Music disappearing and asks the application to exit.
/// Runs on a background timer so process enumeration never blocks the UI dispatcher.
/// </summary>
internal sealed class QqMusicLifecycleMonitor : IDisposable
{
    private static readonly string[] PlayerProcessNames = ["QQMusic", "QQMusicApp"];
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly Dispatcher _dispatcher;
    private readonly Action _requestExit;
    private readonly QqMusicPresencePolicy _policy = new();
    private readonly ThreadingTimer _timer;
    private int _disposed;
    private int _tickBusy;
    private int _exitRequested;

    public QqMusicLifecycleMonitor(Dispatcher dispatcher, Action requestExit)
    {
        _dispatcher = dispatcher;
        _requestExit = requestExit;
        _policy.Seed(IsPlayerRunning());
        _timer = new ThreadingTimer(OnTick, null, PollInterval, PollInterval);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _timer.Dispose();
    }

    private void OnTick(object? state)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            Interlocked.CompareExchange(ref _tickBusy, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (!_policy.Evaluate(IsPlayerRunning()) ||
                Interlocked.Exchange(ref _exitRequested, 1) != 0)
            {
                return;
            }

            Dispose();
            _dispatcher.BeginInvoke(_requestExit);
        }
        catch (InvalidOperationException)
        {
            // The dispatcher or application is already shutting down.
        }
        catch (Exception exception)
        {
            Log.Warn($"[QQMusicLifecycle] Monitor tick failed: {exception.Message}");
        }
        finally
        {
            Volatile.Write(ref _tickBusy, 0);
        }
    }

    private static bool IsPlayerRunning()
    {
        foreach (var processName in PlayerProcessNames)
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
}
