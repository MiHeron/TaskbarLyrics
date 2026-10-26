namespace TaskbarLyrics.App;

/// <summary>
/// Decides when QQ Music has gone away for long enough to stop TaskbarLyrics.
/// The policy is intentionally pure so the timing/threshold behavior can be tested
/// without timers or process enumeration.
/// </summary>
internal sealed class QqMusicPresencePolicy
{
    internal const int ExitMissThreshold = 3;

    private bool _hasSeenPlayer;
    private int _consecutiveMisses;

    public void Seed(bool running)
    {
        _hasSeenPlayer = running;
        _consecutiveMisses = 0;
    }

    public bool Evaluate(bool running)
    {
        if (running)
        {
            _hasSeenPlayer = true;
            _consecutiveMisses = 0;
            return false;
        }

        if (!_hasSeenPlayer)
        {
            return false;
        }

        _consecutiveMisses++;
        return _consecutiveMisses >= ExitMissThreshold;
    }
}
