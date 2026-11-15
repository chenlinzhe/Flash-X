namespace FolderScope.Core;

// A single producer publishes snapshots; the UI consumes the newest one without a queue.
public sealed class LatestScanProgress : IProgress<ScanProgress>
{
    private ScanProgress? _latest;
    public void Report(ScanProgress value) => Interlocked.Exchange(ref _latest, value);
    public ScanProgress? TakeLatest() => Interlocked.Exchange(ref _latest, null);
}