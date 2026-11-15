using System.IO;
using FolderScope.Core;

namespace FolderScope.Desktop;

internal static class ScanHistory
{
    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderScope", "scan-cache", "history");
    public static string[] Recent(string? rootDirectory = null) => !Directory.Exists(rootDirectory ?? Root) ? [] : Directory.GetDirectories(rootDirectory ?? Root, "scan-*")
        .Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0 && File.Exists(Path.Combine(d, "snapshot.json")))
        .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal).Take(3)
        .Select(d => Path.Combine(d, "snapshot.json")).ToArray();

    internal static bool IsStoredSnapshot(string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        return folder is not null
            && string.Equals(Path.GetDirectoryName(folder), Root, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(folder).StartsWith("scan-", StringComparison.Ordinal)
            && string.Equals(Path.GetFileName(path), "snapshot.json", StringComparison.OrdinalIgnoreCase);
    }

    public static void Save(ScanReport report, DirectoryIndex index, string? rootDirectory = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var originalDetailsPath = report.FileDetailsPath;
        var indexMoved = false;
        var rootDirectoryPath = rootDirectory ?? Root;
        Directory.CreateDirectory(rootDirectoryPath);
        var directory = Path.Combine(rootDirectoryPath, "scan-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (report.FileDetailsPath is string source && File.Exists(source))
        {
            var destination = Path.Combine(directory, "details.jsonl");
            // These are the fresh files from this scan; move them instead of duplicating large results.
            File.Move(source, destination);
            report.FileDetailsPath = destination;
            if (File.Exists(source + ".index.json"))
            {
                try { File.Move(source + ".index.json", destination + ".index.json"); indexMoved = true; }
                catch (IOException) { /* Browsing can rebuild/fall back without an index. */ }
                catch (UnauthorizedAccessException) { }
            }
        }
        var snapshot = Path.Combine(directory, "snapshot.json");
        try { ReportWriter.WriteHistorySnapshot(report, snapshot, cancellationToken); }
        catch
        {
            // A cancelled snapshot is not committed to history. Keep the current details browsable.
            if (originalDetailsPath is not null && report.FileDetailsPath != originalDetailsPath
                && report.FileDetailsPath is string movedDetails && File.Exists(movedDetails))
            {
                try
                {
                    File.Move(movedDetails, originalDetailsPath, overwrite: false);
                    report.FileDetailsPath = originalDetailsPath;
                    if (indexMoved) File.Move(movedDetails + ".index.json", originalDetailsPath + ".index.json", overwrite: false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    report.AddIssue(new ScanIssue(report.FileDetailsPath!, "file-detail-index",
                        "自动保存未完成，当前明细仍可从记录的路径浏览：" + ex.Message));
                }
            }
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            throw;
        }
        ScanHistoryOverview.TryWrite(snapshot, ScanHistoryOverview.SnapshotStamp(snapshot), report, index);
        // Only remove known files in this application's completed history folders.
        foreach (var expired in Directory.GetDirectories(rootDirectoryPath, "scan-*")
            .Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0 && File.Exists(Path.Combine(d, "snapshot.json")))
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal).Skip(3))
        {
            if (!Path.GetFullPath(expired).StartsWith(Path.GetFullPath(rootDirectoryPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var name in new[] { "snapshot.json", "details.jsonl", "details.jsonl.index.json", ScanHistoryOverview.FileName })
            {
                var file = Path.Combine(expired, name);
                if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) File.Delete(file);
            }
            if (!Directory.EnumerateFileSystemEntries(expired).Any()) Directory.Delete(expired);
        }
    }
}
