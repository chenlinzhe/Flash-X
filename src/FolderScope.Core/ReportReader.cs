using System.Text.Json;

namespace FolderScope.Core;

public static class ReportReader
{
    private static readonly JsonSerializerOptions SnapshotJson = new() { DefaultBufferSize = 256 * 1024 };

    public static ScanReport Read(string source, int maximumNodes = 500000, CancellationToken cancellationToken = default, long maximumManagedBytes = 8L * 1024 * 1024 * 1024, long maximumSnapshotBytes = 128L * 1024 * 1024)
    {
        if (maximumNodes < 1) throw new ArgumentOutOfRangeException(nameof(maximumNodes));
        if (maximumManagedBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumManagedBytes));
        cancellationToken.ThrowIfCancellationRequested();
        CheckMemoryBudget(maximumManagedBytes);
        using var stream = new CancellableReadStream(source, cancellationToken, maximumManagedBytes);
        if (stream.Length > maximumSnapshotBytes) throw new InvalidDataException("快照超过当前内存预算允许的加载大小，原始结果仍保留。");
        SnapshotPreflight.Validate(stream, maximumNodes, cancellationToken);
        stream.Position = 0;
        var report = JsonSerializer.Deserialize<ScanReport>(stream, SnapshotJson) ?? throw new InvalidDataException("Empty scan report.");
        CheckMemoryBudget(maximumManagedBytes);
        if (report.Nodes is null || report.Issues is null || report.Nodes.Any(n => n is null) || report.Issues.Any(i => i is null))
            throw new InvalidDataException("Snapshot contains null collections or entries.");
        if (string.IsNullOrWhiteSpace(report.RootPath)) throw new InvalidDataException("Snapshot root path is missing.");
        if (report.FileDetailsStoreId == Guid.Empty || (report.FileDetailsStoreId is not null && string.IsNullOrWhiteSpace(report.FileDetailsPath))) throw new InvalidDataException("Invalid file detail association.");
        if (report.DuplicateHardLinkPaths < 0) throw new InvalidDataException("Invalid duplicate hard-link count.");
        if (report.UnlistedSkippedLinks < 0 || report.SuppressedIssueCount < 0) throw new InvalidDataException("Invalid suppressed error count.");
        if (report.Nodes.Count == 0 || report.Nodes[0].Id != 0 || report.Nodes[0].ParentId != null || !report.Nodes[0].IsDirectory)
            throw new InvalidDataException("Invalid root node.");
        for (var i = 0; i < report.Nodes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = report.Nodes[i];
            if (node.Id != i || string.IsNullOrWhiteSpace(node.Path) || string.IsNullOrEmpty(node.Name) || node.TotalBytes < 0 || node.OwnBytes < 0 || node.TotalFiles < 0 || node.TotalDirectories < 0 || node.UnlistedFileBytes < 0 || node.UnlistedFileCount < 0)
                throw new InvalidDataException("Invalid node data.");
            if ((report.RetainsFileNodes || !node.IsDirectory) && (node.UnlistedFileBytes != 0 || node.UnlistedFileCount != 0))
                throw new InvalidDataException("Unexpected unlisted file totals.");
            if (node.UnlistedAllocatedBytes < 0 || node.FileAllocatedBytes < 0 || node.OwnAllocatedBytes < 0 || node.TotalAllocatedBytes < 0)
                throw new InvalidDataException("Negative allocated-space value.");
            if (node.HardLinkDuplicate && (node.IsDirectory || node.OwnAllocatedBytes != 0 || node.FileAllocatedBytes is null))
                throw new InvalidDataException("Invalid duplicate hard-link allocation.");
            if (i > 0 && (node.ParentId is not int parent || parent < 0 || parent >= i || !report.Nodes[parent].IsDirectory))
                throw new InvalidDataException("Invalid or cyclic parent relationship.");
        }
        foreach (var node in report.Nodes)
            if ((node.Incomplete || node.LinkSkipped) && node.ParentId is int parent && !report.Nodes[parent].Incomplete)
                throw new InvalidDataException("Incomplete child has a falsely complete parent.");
        try
        {
            // Reuse one buffer instead of retaining three full-size aggregate arrays.
            cancellationToken.ThrowIfCancellationRequested();
            CheckMemoryBudget(maximumManagedBytes, 24L + 8L * report.Nodes.Count);
            var totals = new long[report.Nodes.Count];
            CheckMemoryBudget(maximumManagedBytes);
            for (var metric = 0; metric < 3; metric++)
            {
                for (var i = 0; i < totals.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var node = report.Nodes[i];
                    if (node.IsDirectory && node.OwnBytes != 0) throw new InvalidDataException("Directory has invalid own-byte count.");
                    totals[i] = metric switch
                    {
                        0 => checked(node.OwnBytes + node.UnlistedFileBytes),
                        1 => checked(node.UnlistedFileCount + (!node.IsDirectory && !node.LinkSkipped && !node.Incomplete ? 1L : 0L)),
                        _ => 0
                    };
                }
                for (var i = totals.Length - 1; i >= 0; i--)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var node = report.Nodes[i];
                    var expected = metric switch { 0 => node.TotalBytes, 1 => node.TotalFiles, _ => node.TotalDirectories };
                    if (expected != totals[i]) throw new InvalidDataException("Inconsistent scan totals.");
                    if (node.ParentId is int parent)
                        totals[parent] = checked(totals[parent] + totals[i] + (metric == 2 && node.IsDirectory ? 1 : 0));
                }
            }
        }
        catch (OverflowException ex) { throw new InvalidDataException("Scan totals overflow.", ex); }
        if (report.MeasuresAllocatedSpace && (report.RetainsFileNodes || report.AllocationTotalsVersion == 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckMemoryBudget(maximumManagedBytes, 24L + 16L * report.Nodes.Count);
            var allocatedTotals = report.Nodes.Select(n => n.Incomplete || n.LinkSkipped ? (long?)null : n.IsDirectory && !report.RetainsFileNodes ? n.UnlistedAllocatedBytes : n.OwnAllocatedBytes).ToArray();
            CheckMemoryBudget(maximumManagedBytes);
            try
            {
                for (var i = report.Nodes.Count - 1; i >= 0; i--)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var node = report.Nodes[i];
                    if (node.TotalAllocatedBytes != allocatedTotals[i]) throw new InvalidDataException("Inconsistent allocated-space totals.");
                    if (node.ParentId is int parent)
                        allocatedTotals[parent] = allocatedTotals[parent].HasValue && allocatedTotals[i].HasValue
                            ? checked(allocatedTotals[parent]!.Value + allocatedTotals[i]!.Value) : null;
                }
            }
            catch (OverflowException ex) { throw new InvalidDataException("Allocated-space totals overflow.", ex); }
        }
        // Compact only canonical file paths; preserve unusual legacy paths verbatim.
        foreach (var node in report.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.IsDirectory || node.ParentId is not int parent || string.IsNullOrEmpty(node.Name)) continue;
            var directoryPath = report.Nodes[parent].Path;
            var path = node.Path;
            var separatorLength = Path.EndsInDirectorySeparator(directoryPath) ? 0 : 1;
            if (path.Length == directoryPath.Length + separatorLength + node.Name.Length
                && path.AsSpan(0, directoryPath.Length).SequenceEqual(directoryPath.AsSpan())
                && (separatorLength == 0 || path[directoryPath.Length] == Path.DirectorySeparatorChar)
                && path.AsSpan(directoryPath.Length + separatorLength).SequenceEqual(node.Name.AsSpan()))
                node.ShareDirectoryPath(directoryPath);
        }
        cancellationToken.ThrowIfCancellationRequested();
        CheckMemoryBudget(maximumManagedBytes);
        return report;
    }
    private static void CheckMemoryBudget(long maximumManagedBytes, long upcomingBytes = 0)
    {
        if (upcomingBytes > maximumManagedBytes || GC.GetTotalMemory(false) > maximumManagedBytes - upcomingBytes)
            throw new InvalidDataException("加载已停止：托管内存超过加载预算。原扫描结果保留，请关闭不需要的结果后重试。");
    }

    // Cancellation is checked between reads; it cannot interrupt a blocked filesystem call.
    private sealed class CancellableReadStream(string path, CancellationToken token, long maximumManagedBytes)
        : FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            token.ThrowIfCancellationRequested();
            CheckMemoryBudget(maximumManagedBytes);
            return base.Read(buffer, offset, count);
        }
        public override int Read(Span<byte> buffer)
        {
            token.ThrowIfCancellationRequested();
            CheckMemoryBudget(maximumManagedBytes);
            return base.Read(buffer);
        }
    }
}
