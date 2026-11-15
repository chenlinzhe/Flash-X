namespace FolderScope.Core;

/// <summary>Scans available logical drives in sequence and combines them under one synthetic computer root.</summary>
public static class ComputerScanner
{
    public const string SelectionName = "整个电脑";

    public static ScanReport Scan(CancellationToken cancellationToken = default,
        IProgress<ScanProgress>? progress = null, int? maximumNodes = null,
        long maximumManagedBytes = 8L * 1024 * 1024 * 1024, bool retainFileNodes = true,
        bool measureAllocatedSpace = false, FileDetailStore? fileDetails = null,
        long minimumFileBytes = 0, string? excludedCacheDirectory = null, Action<ScanPreview>? preview = null)
    {
        var nodeLimit = maximumNodes ?? (fileDetails is null ? 10_000_000 : int.MaxValue);
        if (nodeLimit < 1) throw new ArgumentOutOfRangeException(nameof(maximumNodes));
        if (minimumFileBytes < 0) throw new ArgumentOutOfRangeException(nameof(minimumFileBytes));
        var started = DateTime.UtcNow;
        var report = new ScanReport
        {
            RootPath = SelectionName, StartedUtc = started, RetainsFileNodes = retainFileNodes,
            MinimumFileBytes = minimumFileBytes, FileDetailsPath = fileDetails?.FilePath,
            FileDetailsStoreId = fileDetails?.StoreId, MeasuresAllocatedSpace = measureAllocatedSpace,
            AllocationTotalsVersion = measureAllocatedSpace ? 1 : 0
        };
        report.Nodes.Add(new ScanNode
        {
            Id = 0, ParentId = null, Path = "shell:MyComputerFolder", Name = SelectionName,
            IsDirectory = true, UnlistedAllocatedBytes = measureAllocatedSpace ? 0 : null,
            OwnAllocatedBytes = measureAllocatedSpace ? 0 : null, TotalAllocatedBytes = measureAllocatedSpace ? 0 : null
        });

        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch (Exception ex) when (IsDriveError(ex))
        {
            MarkQueueIncomplete(report, ScanDiagnostics.ReasonFor(ex));
            report.AddIssue(ScanDiagnostics.Issue(SelectionName, "drive-enumeration", ex));
            report.FinishedUtc = DateTime.UtcNow;
            return report;
        }

        var scanner = new DirectoryScanner();
        for (var driveIndex = 0; driveIndex < drives.Length; driveIndex++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                report.Cancelled = true;
                MarkQueueIncomplete(report, ScanIncompleteReason.Cancelled);
                break;
            }
            if (report.Nodes.Count >= nodeLimit)
            {
                MarkQueueIncomplete(report, ScanIncompleteReason.NodeLimit);
                if (!report.Issues.Any(issue => issue.Operation == "node-limit"))
                    report.AddIssue(new ScanIssue(SelectionName, "node-limit",
                        $"Scan stopped at the configured {nodeLimit:N0} node safety limit; results are partial."));
                break;
            }
            var drive = drives[driveIndex];
            progress?.Report(new ScanProgress(0, $"[{driveIndex + 1}/{drives.Length}] 排队扫描 {drive.Name}"));
            if (cancellationToken.IsCancellationRequested)
            {
                report.Cancelled = true;
                MarkQueueIncomplete(report, ScanIncompleteReason.Cancelled);
                break;
            }

            try
            {
                if (!drive.IsReady)
                {
                    AddUnavailableDrive(report, drive.Name, new IOException("驱动器当前不可用或未就绪。"), measureAllocatedSpace);
                    continue;
                }

                var driveRoot = drive.RootDirectory.FullName;
                var excludedForDrive = excludedCacheDirectory is not null
                    && ScanCacheLocation.IsWithin(driveRoot, excludedCacheDirectory) ? excludedCacheDirectory : null;
                var offset = report.Nodes.Count;
                var remainingNodes = nodeLimit - offset;
                var childReport = scanner.Scan(driveRoot, cancellationToken,
                    new DriveProgress(progress, drive.Name, driveIndex + 1, drives.Length), remainingNodes,
                    maximumManagedBytes, retainFileNodes, measureAllocatedSpace, fileDetails,
                    minimumFileBytes, excludedForDrive, offset, preview);
                MergeDrive(report, childReport, drive.Name);
                if (childReport.Cancelled
                    || (childReport.Root.IncompleteReasons & ScanIncompleteReason.MemoryLimit) != 0) break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                report.Cancelled = true;
                MarkQueueIncomplete(report, ScanIncompleteReason.Cancelled);
                break;
            }
            catch (Exception ex) when (IsDriveError(ex))
            {
                AddUnavailableDrive(report, drive.Name, ex, measureAllocatedSpace);
            }
        }

        report.FinishedUtc = DateTime.UtcNow;
        return report;
    }

    private static void MarkQueueIncomplete(ScanReport report, ScanIncompleteReason reason)
    {
        report.Root.Incomplete = true;
        report.Root.IncompleteReasons |= reason;
        report.Root.TotalAllocatedBytes = null;
    }

    internal static void MergeDrive(ScanReport destination, ScanReport source, string driveName)
    {
        var offset = destination.Nodes.Count;
        foreach (var node in source.Nodes)
        {
            var id = checked(offset + node.Id);
            var parentId = node.Id == 0 ? 0 : checked(offset + node.ParentId!.Value);
            var name = node.Id == 0 ? driveName : node.Name;
            destination.Nodes.Add(CopyNode(node, id, parentId, name));
        }

        var target = destination.Root;
        var root = source.Root;
        target.TotalBytes = checked(target.TotalBytes + root.TotalBytes);
        target.TotalFiles = checked(target.TotalFiles + root.TotalFiles);
        target.TotalDirectories = checked(target.TotalDirectories + root.TotalDirectories + 1);
        target.Incomplete |= root.Incomplete;
        target.IncompleteReasons |= root.IncompleteReasons;
        if (destination.MeasuresAllocatedSpace)
            target.TotalAllocatedBytes = target.TotalAllocatedBytes is long current && root.TotalAllocatedBytes is long added
                ? checked(current + added) : null;

        destination.EstimateExcludedFiles = checked(destination.EstimateExcludedFiles + source.EstimateExcludedFiles);
        destination.UnlistedSkippedLinks = checked(destination.UnlistedSkippedLinks + source.UnlistedSkippedLinks);
        destination.DuplicateHardLinkPaths = checked(destination.DuplicateHardLinkPaths + source.DuplicateHardLinkPaths);
        destination.SuppressedIssueCount = checked(destination.SuppressedIssueCount + source.SuppressedIssueCount);
        foreach (var issue in source.Issues) destination.AddIssue(issue);
        destination.Cancelled |= source.Cancelled;
    }

    private static void AddUnavailableDrive(ScanReport report, string driveName, Exception error, bool measureAllocatedSpace)
    {
        var root = report.Root;
        var reason = ScanDiagnostics.ReasonFor(error);
        var id = report.Nodes.Count;
        report.Nodes.Add(new ScanNode
        {
            Id = id, ParentId = 0, Path = driveName, Name = driveName, IsDirectory = true,
            Incomplete = true, IncompleteReasons = reason,
            UnlistedAllocatedBytes = null,
            OwnAllocatedBytes = null,
            TotalAllocatedBytes = null
        });
        root.TotalDirectories = checked(root.TotalDirectories + 1);
        root.Incomplete = true;
        root.IncompleteReasons |= reason;
        if (measureAllocatedSpace) root.TotalAllocatedBytes = null;
        report.AddIssue(ScanDiagnostics.Issue(driveName, "drive-unavailable", error));
    }

    private static ScanNode CopyNode(ScanNode source, int id, int? parentId, string name) => new()
    {
        Id = id, ParentId = parentId, Path = source.Path, Name = name, IsDirectory = source.IsDirectory,
        UnlistedAllocatedBytes = source.UnlistedAllocatedBytes, FileAllocatedBytes = source.FileAllocatedBytes,
        HardLinkDuplicate = source.HardLinkDuplicate, OwnAllocatedBytes = source.OwnAllocatedBytes,
        TotalAllocatedBytes = source.TotalAllocatedBytes, OwnBytes = source.OwnBytes,
        UnlistedFileBytes = source.UnlistedFileBytes, UnlistedFileCount = source.UnlistedFileCount,
        TotalBytes = source.TotalBytes, TotalFiles = source.TotalFiles, TotalDirectories = source.TotalDirectories,
        Attributes = source.Attributes, ModifiedUtc = source.ModifiedUtc, CreatedUtc = source.CreatedUtc,
        AccessedUtc = source.AccessedUtc, LinkSkipped = source.LinkSkipped, Incomplete = source.Incomplete,
        IncompleteReasons = source.IncompleteReasons
    };

    private static bool IsDriveError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or System.Security.SecurityException or ArgumentException or System.ComponentModel.Win32Exception;

    private sealed class DriveProgress(IProgress<ScanProgress>? target, string drive, int index, int count) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => target?.Report(new ScanProgress(value.Entries, $"[{index}/{count}] {drive} · {value.Path}"));
    }
}
