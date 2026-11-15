using System.IO;
using System.Text.Json;
using FolderScope.Core;

namespace FolderScope.Desktop;

internal sealed record ScanHistoryOverview(
    long SnapshotLength, DateTime SnapshotWriteUtc, string RootPath, DateTime FinishedUtc,
    long MinimumFileBytes, bool Complete, bool MeasuresAllocatedSpace, ScanNode Root,
    ScanHistoryOverview.OverviewEntry[] Entries, bool HasMore,
    Dictionary<int, ScanHistoryOverview.DrivePage>? DrivePages = null,
    Dictionary<int, ScanHistoryOverview.DrivePage>? FolderPages = null)
{
    private const int EntryLimit = 100;
    private const long MaximumOverviewBytes = 4 * 1024 * 1024;
    internal const string FileName = "overview-v1.json";

    internal sealed record OverviewEntry(ScanNode Node, int DirectDirectories);
    internal sealed record DrivePage(OverviewEntry[] Entries, bool HasMore);
    private const int DriveEntryLimit = 256;
    private const int FolderPagesPerDrive = 8;
    private const int FolderEntryLimit = 24;
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
    };

    internal static (long Length, DateTime WriteUtc) SnapshotStamp(string snapshot)
    {
        var info = new FileInfo(snapshot);
        return (info.Length, info.LastWriteTimeUtc);
    }

    internal static ScanHistoryOverview? TryRead(string snapshot)
    {
        try
        {
            var path = OverviewPath(snapshot);
            if (!File.Exists(path) || new FileInfo(path).Length > MaximumOverviewBytes) return null;
            using var stream = File.OpenRead(path);
            var result = JsonSerializer.Deserialize<ScanHistoryOverview>(stream);
            if (result is null || result.Root is not { Id: 0, IsDirectory: true }
                || string.IsNullOrWhiteSpace(result.RootPath)
                || result.Entries is null || result.Entries.Length > EntryLimit
                || result.Entries.Any(entry => entry?.Node is not { ParentId: 0 }))
                return null;
            if (result.DrivePages is { } pages && (pages.Count > EntryLimit || pages.Any(page =>
                !result.Entries.Any(entry => entry.Node.Id == page.Key && entry.Node.IsDirectory)
                || page.Value?.Entries is not { Length: <= DriveEntryLimit } entries
                || entries.Any(entry => entry?.Node?.ParentId != page.Key))))
                return null;
            if (result.FolderPages is { } folders)
            {
                if (result.DrivePages is null || folders.Count > EntryLimit * FolderPagesPerDrive) return null;
                var parents = result.DrivePages.Values.SelectMany(page => page.Entries)
                    .Where(entry => entry.Node.IsDirectory).Select(entry => entry.Node.Id).ToHashSet();
                if (folders.Any(page => !parents.Contains(page.Key)
                    || page.Value?.Entries is not { Length: <= FolderEntryLimit } entries
                    || entries.Any(entry => entry?.Node?.ParentId != page.Key)))
                    return null;
            }
            var stamp = SnapshotStamp(snapshot);
            return stamp == (result.SnapshotLength, result.SnapshotWriteUtc) ? result : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    internal static void TryWrite(string snapshot, (long Length, DateTime WriteUtc) stamp, ScanReport report, DirectoryIndex index)
    {
        string? pending = null;
        try
        {
            if (SnapshotStamp(snapshot) != stamp) return;
            var children = index.Children.GetValueOrDefault(0) ?? [];
            var entries = children.Take(EntryLimit)
                .Select(node => new OverviewEntry(node, index.DirectDirectories.GetValueOrDefault(node.Id))).ToArray();
            DrivePage Page(int id, int limit)
            {
                var nodes = index.Children.GetValueOrDefault(id) ?? [];
                return new DrivePage(nodes.Take(limit)
                    .Select(node => new OverviewEntry(node, index.DirectDirectories.GetValueOrDefault(node.Id))).ToArray(),
                    nodes.Count > limit);
            }
            var drivePages = report.RootPath == ComputerScanner.SelectionName
                ? entries.Where(entry => entry.Node.IsDirectory)
                    .ToDictionary(entry => entry.Node.Id, entry => Page(entry.Node.Id, DriveEntryLimit))
                : null;
            var folderPages = drivePages?.Values.SelectMany(page => page.Entries
                    .Where(entry => entry.Node.IsDirectory && entry.Node.TotalDirectories > 0)
                    .Take(FolderPagesPerDrive))
                .ToDictionary(entry => entry.Node.Id, entry => Page(entry.Node.Id, FolderEntryLimit));
            var overview = new ScanHistoryOverview(stamp.Length, stamp.WriteUtc, report.RootPath, report.FinishedUtc,
                report.MinimumFileBytes, report.Complete, report.MeasuresAllocatedSpace, report.Root, entries, children.Count > EntryLimit, drivePages, folderPages);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(overview, WriteOptions);
            if (bytes.Length > MaximumOverviewBytes) return;
            var path = OverviewPath(snapshot);
            pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(pending, bytes);
            if (SnapshotStamp(snapshot) == stamp) File.Move(pending, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // The preview is optional. The fully validated snapshot remains authoritative.
        }
        finally
        {
            if (pending is not null)
            {
                try { File.Delete(pending); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static string OverviewPath(string snapshot) => Path.Combine(Path.GetDirectoryName(snapshot)!, FileName);
}
