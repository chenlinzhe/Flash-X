namespace FolderScope.Core;

// The caller supplies a monotonic elapsed time, so wall-clock changes cannot trigger termination.
public sealed class UnresponsiveDeadline(TimeSpan timeout)
{
    private TimeSpan? _firstUnresponsive;
    public bool Observe(bool responding, TimeSpan elapsed)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (responding) { _firstUnresponsive = null; return false; }
        _firstUnresponsive ??= elapsed;
        return elapsed - _firstUnresponsive.Value >= timeout;
    }
}