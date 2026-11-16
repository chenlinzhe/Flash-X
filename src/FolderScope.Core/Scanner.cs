namespace FolderScope.Core;

public sealed class ScanNode
{
    public int Id { get; init; }
    public int? ParentId { get; init; }
    private string _path = "";
    private bool _sharedDirectoryPath;
    public required string Path
    {
        get => _sharedDirectoryPath ? System.IO.Path.Combine(_path, Name) : _path;
        init => _path = value;
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public int PathLength => !_sharedDirectoryPath ? _path.Length : _path.Length + (System.IO.Path.EndsInDirectorySeparator(_path) ? 0 : 1) + Name.Length;
    internal static int ComparePaths(ScanNode a, ScanNode b)
    {
        if (a._sharedDirectoryPath && b._sharedDirectoryPath && ReferenceEquals(a._path, b._path))
            return StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        if (a._sharedDirectoryPath && b._sharedDirectoryPath && a._path.Length == b._path.Length
            && System.IO.Path.EndsInDirectorySeparator(a._path) == System.IO.Path.EndsInDirectorySeparator(b._path))
        {
            var directoryOrder = StringComparer.OrdinalIgnoreCase.Compare(a._path, b._path);
            return directoryOrder != 0 ? directoryOrder : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        }
        return StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path);
    }
    internal void ShareDirectoryPath(string directoryPath) { _path = directoryPath; _sharedDirectoryPath = true; }
    public required string Name { get; init; }
    public bool IsDirectory { get; init; }
    public long? UnlistedAllocatedBytes { get; set; }
    public long? FileAllocatedBytes { get; set; }
    public bool HardLinkDuplicate { get; set; }
    public long? OwnAllocatedBytes { get; set; }
    public long? TotalAllocatedBytes { get; set; }
    public long OwnBytes { get; set; }
    public long UnlistedFileBytes { get; set; }
    public long UnlistedFileCount { get; set; }
    public long TotalBytes { get; set; }
    public long TotalFiles { get; set; }
    public long TotalDirectories { get; set; }
    public int Attributes { get; set; }
    public DateTime? ModifiedUtc { get; set; }
    public DateTime? CreatedUtc { get; set; }
    public DateTime? AccessedUtc { get; set; }
    public bool LinkSkipped { get; set; }
    public bool Incomplete { get; set; }
    public ScanIncompleteReason IncompleteReasons { get; set; }
}

public sealed record ScanIssue(string Path, string Operation, string Message, int? ErrorCode = null);
public sealed record ScanProgress(int Entries, string Path);
public sealed record ScanPreview(string Path, string? ParentPath, string Name, long DirectBytes, bool Enumerated, bool Incomplete = false);
public sealed class ScanReport
{
    public string Schema { get; } = "folderscope.scan.v1";
    public required string RootPath { get; init; }
    public DateTime StartedUtc { get; init; }
    public DateTime FinishedUtc { get; set; }
    public string ByteSemantics => MinimumFileBytes == 0 ? "Logical file lengths counted per path; see MeasuresAllocatedSpace and AllocationSemantics for separate allocated totals" : $"Quick estimate sums logical lengths for files strictly larger than {MinimumFileBytes} bytes per path";
    private readonly HashSet<FileIdentity> _allocatedIdentities = [];
    public long DuplicateHardLinkPaths { get; set; }
    internal void ReleaseAllocationIdentities() { _allocatedIdentities.Clear(); _allocatedIdentities.TrimExcess(); }
    internal long CountAllocatedSpace(FileSpaceInfo info)
    {
        if (info.LinkCount <= 1) return info.AllocatedBytes;
        if (info.Identity is not FileIdentity identity) throw new IOException("Hard-linked file identity unavailable; unique allocated total is unknown.");
        if (_allocatedIdentities.Add(identity)) return info.AllocatedBytes;
        DuplicateHardLinkPaths++;
        return 0;
    }
    public bool MeasuresAllocatedSpace { get; init; }
    public int AllocationTotalsVersion { get; init; }
    public string AllocationSemantics => MeasuresAllocatedSpace ? "Allocation metadata per data file; hard links counted once per scan and attributed to first encountered path; duplicate file paths retain FileAllocatedBytes; null means unknown" : "Allocated space not measured";
    public bool Cancelled { get; set; }
    // Missing values in legacy snapshots mean true, so compact snapshots must write false explicitly.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public bool RetainsFileNodes { get; init; } = true;
    public string? FileDetailsPath { get; set; }
    public Guid? FileDetailsStoreId { get; init; }
    public long MinimumFileBytes { get; init; }
    public long EstimateExcludedFiles { get; set; }
    public long UnlistedSkippedLinks { get; set; }
    public List<ScanNode> Nodes { get; init; } = [];
    public List<ScanIssue> Issues { get; init; } = [];
    public long SuppressedIssueCount { get; set; }
    public void AddIssue(ScanIssue issue)
    {
        if (Issues.Count < 10000) Issues.Add(issue);
        else
        {
            SuppressedIssueCount++;
            if (issue.Operation is "node-limit" or "memory-limit") Issues[^1] = issue;
        }
    }
    public bool Complete => !Cancelled && UnlistedSkippedLinks == 0 && Issues.Count == 0 && SuppressedIssueCount == 0 && Nodes.All(n => !n.LinkSkipped && !n.Incomplete);
    public ScanNode Root => Nodes[0];
}

public sealed class DirectoryScanner
{
    private readonly Func<string, FileSpaceInfo> _readSpace;
    public DirectoryScanner(Func<string, FileSpaceInfo>? readSpace = null) => _readSpace = readSpace ?? FileSpaceReader.Read;
    private readonly Func<DirectoryInfo, EnumerationOptions, IEnumerable<FileSystemInfo>> _enumerate =
        static (directory, options) => directory.EnumerateFileSystemInfos("*", options);
    internal DirectoryScanner(Func<DirectoryInfo, EnumerationOptions, IEnumerable<FileSystemInfo>> enumerate) : this() => _enumerate = enumerate;
    public ScanReport Scan(string rootPath, CancellationToken cancellationToken = default,
        IProgress<ScanProgress>? progress = null, int? maximumNodes = null, long maximumManagedBytes = 8L * 1024 * 1024 * 1024, bool retainFileNodes = true, bool measureAllocatedSpace = false, FileDetailStore? fileDetails = null, long minimumFileBytes = 0, string? excludedDirectory = null, int fileDetailNodeIdOffset = 0, Action<ScanPreview>? preview = null)
    {
        if (fileDetails is not null && retainFileNodes) throw new ArgumentException("Disk details require directory-only retention.");
        if (minimumFileBytes < 0) throw new ArgumentOutOfRangeException(nameof(minimumFileBytes));
        if (fileDetailNodeIdOffset < 0) throw new ArgumentOutOfRangeException(nameof(fileDetailNodeIdOffset));
        var nodeLimit = maximumNodes ?? (fileDetails is null ? 10_000_000 : int.MaxValue);
        if (nodeLimit < 1) throw new ArgumentOutOfRangeException(nameof(maximumNodes));
        if (maximumManagedBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumManagedBytes));
        var fullPath = System.IO.Path.GetFullPath(rootPath);
        if (fileDetails is not null && !ScanCacheLocation.IsOutsideScan(fullPath, fileDetails.FilePath)
            && (excludedDirectory is null || !ScanCacheLocation.IsWithin(excludedDirectory, fileDetails.FilePath)))
            throw new ArgumentException("File-detail output must be outside the scanned directory.", nameof(fileDetails));
        var excludedFullPath = excludedDirectory is null ? null : Path.GetFullPath(excludedDirectory);
        if (excludedFullPath is not null && !ScanCacheLocation.IsWithin(fullPath, excludedFullPath))
            throw new ArgumentException("Excluded directory must be inside the scanned directory.", nameof(excludedDirectory));
        var root = new DirectoryInfo(fullPath);
        if (!root.Exists) throw new DirectoryNotFoundException(fullPath);
        var report = new ScanReport { RootPath = fullPath, StartedUtc = DateTime.UtcNow, RetainsFileNodes = retainFileNodes, MinimumFileBytes = minimumFileBytes, FileDetailsPath = fileDetails?.FilePath, FileDetailsStoreId = fileDetails?.StoreId, MeasuresAllocatedSpace = measureAllocatedSpace, AllocationTotalsVersion = measureAllocatedSpace ? 1 : 0 };
        var rootNode = NewNode(root, null, report);
        report.Nodes.Add(rootNode);
        var rootBlocked = rootNode.LinkSkipped || rootNode.Incomplete;
        preview?.Invoke(new ScanPreview(fullPath, null, rootNode.Name.Length == 0 ? fullPath : rootNode.Name, 0,
            Enumerated: rootBlocked, Incomplete: rootBlocked));
        var pending = new Stack<(DirectoryInfo Folder, int Id)>();
        if (!rootNode.LinkSkipped && !rootNode.Incomplete) pending.Push((root, 0));
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false, IgnoreInaccessible = false,
            AttributesToSkip = 0, ReturnSpecialDirectories = false
        };
        int? currentDirectoryId = null;
        void MarkUnfinished(ScanIncompleteReason reason)
        {
            MarkIncomplete(report.Root, reason);
            if (currentDirectoryId.HasValue) MarkIncomplete(report.Nodes[currentDirectoryId.Value], reason);
            foreach (var (_, id) in pending) MarkIncomplete(report.Nodes[id], reason);
        }
        var visitedEntries = 1;
        var lastProgressTick = Environment.TickCount64;
        try
        {
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (folder, id) = pending.Pop();
                currentDirectoryId = id;
                long retainedDirectBytes = 0;
                try
                {
                    foreach (var item in _enumerate(folder, options))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        visitedEntries++;
                        var progressTick = Environment.TickCount64;
                        if (visitedEntries % 100 == 0 || progressTick - lastProgressTick >= 80)
                        {
                            lastProgressTick = progressTick;
                            progress?.Report(new ScanProgress(visitedEntries, item.FullName));
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                        if (excludedFullPath is not null && item is DirectoryInfo && IsSamePath(item.FullName, excludedFullPath))
                        {
                            MarkIncomplete(report.Nodes[id], ScanIncompleteReason.ReadError);
                            report.AddIssue(new ScanIssue(excludedFullPath, "scan-cache-excluded", "FolderScope 扫描缓存目录已排除，避免把本次扫描输出再次计入扫描结果。"));
                            continue;
                        }
                        if (minimumFileBytes > 0 && item is FileInfo thresholdFile)
                        {
                            var belowEstimateThreshold = false;
                            try
                            {
                                var attributes = thresholdFile.Attributes;
                                belowEstimateThreshold = (attributes & FileAttributes.ReparsePoint) == 0 && thresholdFile.Length <= minimumFileBytes;
                            }
                            catch (Exception ex) when (IsFileSystemError(ex))
                            {
                                // Keep inaccessible/unknown entries so NewNode can record the scan issue.
                            }
                            if (belowEstimateThreshold) { report.EstimateExcludedFiles++; continue; }
                        }
                        if ((retainFileNodes || item is DirectoryInfo) && report.Nodes.Count >= nodeLimit) throw new ScanLimitException();
                        if ((visitedEntries == 2 || visitedEntries % 256 == 0) && GC.GetTotalMemory(false) >= maximumManagedBytes) throw new ScanMemoryLimitException();
                        if (!retainFileNodes && item is FileInfo compactFile)
                        {
                            if (fileDetails is null) AddUnlistedFile(compactFile, report.Nodes[id], report);
                            else
                            {
                                var detail = NewNode(compactFile, id, report);
                                try { fileDetails.Append(detail, fileDetailNodeIdOffset); }
                                catch (IOException ex) { throw new DetailWriteException("File-detail storage failed; scan stopped.", ex); }
                                var parentNode = report.Nodes[id];
                                parentNode.IncompleteReasons |= detail.IncompleteReasons;
                                if (detail.LinkSkipped) { report.UnlistedSkippedLinks++; parentNode.Incomplete = true; }
                                else
                                {
                                    parentNode.UnlistedFileBytes = checked(parentNode.UnlistedFileBytes + detail.TotalBytes);
                                    parentNode.UnlistedFileCount += detail.TotalFiles;
                                    parentNode.TotalBytes = checked(parentNode.TotalBytes + detail.TotalBytes);
                                    parentNode.TotalFiles += detail.TotalFiles;
                                    parentNode.Incomplete |= detail.Incomplete;
                                }
                                if (report.MeasuresAllocatedSpace) parentNode.TotalAllocatedBytes = parentNode.TotalAllocatedBytes.HasValue && detail.TotalAllocatedBytes.HasValue
                                    ? checked(parentNode.TotalAllocatedBytes.Value + detail.TotalAllocatedBytes.Value) : null;
                                parentNode.UnlistedAllocatedBytes = parentNode.TotalAllocatedBytes;
                            }
                            continue;
                        }
                        var node = NewNode(item, id, report);
                        report.Nodes.Add(node);
                        if (!node.IsDirectory) retainedDirectBytes = checked(retainedDirectBytes + node.TotalBytes);
                        else if (preview is not null)
                        {
                            var blocked = node.LinkSkipped || node.Incomplete;
                            preview(new ScanPreview(node.Path, folder.FullName, node.Name, 0,
                                Enumerated: blocked, Incomplete: blocked));
                        }
                        if (item is DirectoryInfo child && !node.LinkSkipped && !node.Incomplete)
                            pending.Push((child, node.Id));
                    }
                }
                catch (Exception ex) when (IsFileSystemError(ex))
                {
                    MarkIncomplete(report.Nodes[id], ScanDiagnostics.ReasonFor(ex));
                    report.AddIssue(ScanDiagnostics.Issue(folder.FullName, "enumerate", ex));
                }
                cancellationToken.ThrowIfCancellationRequested();
                preview?.Invoke(new ScanPreview(folder.FullName, report.Nodes[id].ParentId is int parentId ? report.Nodes[parentId].Path : null,
                    report.Nodes[id].Name, checked(retainedDirectBytes + report.Nodes[id].UnlistedFileBytes), true, report.Nodes[id].Incomplete));
                currentDirectoryId = null;
            }
        }
        catch (DetailWriteException ex)
        {
            MarkUnfinished(ScanIncompleteReason.DetailStorage);
            report.AddIssue(new ScanIssue(fullPath, "file-detail-write", ex.Message + " " + ex.InnerException?.Message));
        }
        catch (ScanMemoryLimitException)
        {
            MarkUnfinished(ScanIncompleteReason.MemoryLimit);
            report.AddIssue(new ScanIssue(fullPath, "memory-limit", $"Managed heap reached the configured {maximumManagedBytes} byte budget; results are partial."));
        }
        catch (ScanLimitException)
        {
            MarkUnfinished(ScanIncompleteReason.NodeLimit);
            report.AddIssue(new ScanIssue(fullPath, "node-limit", $"Scan stopped at the configured {nodeLimit:N0} node safety limit; results are partial."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            report.Cancelled = true;
            MarkUnfinished(ScanIncompleteReason.Cancelled);
        }
        if (report.MeasuresAllocatedSpace)
            foreach (var node in report.Nodes) if (node.Incomplete || node.LinkSkipped) node.TotalAllocatedBytes = null;
        // Child IDs always follow their parents. Reverse aggregation avoids recursive stack overflow.
        for (var i = report.Nodes.Count - 1; i > 0; i--)
        {
            var node = report.Nodes[i];
            var parent = report.Nodes[node.ParentId!.Value];
            parent.TotalBytes = checked(parent.TotalBytes + node.TotalBytes);
            if (report.MeasuresAllocatedSpace) parent.TotalAllocatedBytes = parent.TotalAllocatedBytes.HasValue && node.TotalAllocatedBytes.HasValue ? checked(parent.TotalAllocatedBytes.Value + node.TotalAllocatedBytes.Value) : null;
            parent.TotalFiles = checked(parent.TotalFiles + node.TotalFiles);
            parent.TotalDirectories = checked(parent.TotalDirectories + node.TotalDirectories + (node.IsDirectory ? 1 : 0));
            parent.Incomplete |= node.Incomplete || node.LinkSkipped;
            parent.IncompleteReasons |= node.IncompleteReasons;
        }
        try { fileDetails?.Flush(); }
        catch (IOException ex)
        {
            MarkIncomplete(report.Root, ScanIncompleteReason.DetailStorage);
            if (report.MeasuresAllocatedSpace) report.Root.TotalAllocatedBytes = null;
            report.AddIssue(new ScanIssue(fullPath, "file-detail-flush", "File-detail flush failed; saved records may be incomplete. " + ex.Message));
        }
        report.ReleaseAllocationIdentities();
        report.FinishedUtc = DateTime.UtcNow;
        progress?.Report(new ScanProgress(visitedEntries, fullPath));
        return report;
    }

    private void AddUnlistedFile(FileInfo file, ScanNode parent, ScanReport report)
    {
        try
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            { report.UnlistedSkippedLinks++; MarkIncomplete(parent, ScanIncompleteReason.LinkSkipped); if (report.MeasuresAllocatedSpace) parent.TotalAllocatedBytes = null; return; }
            var length = file.Length;
            parent.UnlistedFileBytes = checked(parent.UnlistedFileBytes + length);
            parent.UnlistedFileCount++;
            parent.TotalBytes = checked(parent.TotalBytes + length);
            parent.TotalFiles++;
            if (report.MeasuresAllocatedSpace)
            {
                try
                {
                    var allocated = report.CountAllocatedSpace(_readSpace(file.FullName));
                    if (parent.TotalAllocatedBytes.HasValue) parent.TotalAllocatedBytes = checked(parent.TotalAllocatedBytes.Value + allocated);
                    parent.UnlistedAllocatedBytes = parent.TotalAllocatedBytes;
                }
                catch (Exception ex) when (IsFileSystemError(ex) || ex is InvalidDataException)
                {
                    parent.UnlistedAllocatedBytes = parent.TotalAllocatedBytes = null;
                    report.AddIssue(ScanDiagnostics.Issue(file.FullName, "allocated-space", ex));
                }
            }
        }
        catch (Exception ex) when (IsFileSystemError(ex))
        { MarkIncomplete(parent, ScanDiagnostics.ReasonFor(ex)); if (report.MeasuresAllocatedSpace) parent.TotalAllocatedBytes = null; report.AddIssue(ScanDiagnostics.Issue(file.FullName, "metadata", ex)); }
    }
    private ScanNode NewNode(FileSystemInfo info, int? parent, ScanReport report)
    {
        var node = new ScanNode
        {
            Id = report.Nodes.Count, ParentId = parent, Path = parent.HasValue && info is FileInfo ? report.Nodes[parent.Value].Path : info.FullName, Name = info.Name,
            UnlistedAllocatedBytes = report.MeasuresAllocatedSpace ? 0 : null,
            OwnAllocatedBytes = report.MeasuresAllocatedSpace ? 0 : null, TotalAllocatedBytes = report.MeasuresAllocatedSpace ? 0 : null,
            IsDirectory = info is DirectoryInfo
        };
        if (parent.HasValue && info is FileInfo) node.ShareDirectoryPath(report.Nodes[parent.Value].Path);
        try
        {
            var attributes = info.Attributes;
            node.Attributes = (int)attributes;
            node.ModifiedUtc = info.LastWriteTimeUtc;
            node.CreatedUtc = info.CreationTimeUtc;
            node.AccessedUtc = info.LastAccessTimeUtc;
            // Both directory and file reparse points are excluded, so scans cannot loop or hydrate link targets.
            node.LinkSkipped = (attributes & FileAttributes.ReparsePoint) != 0;
            if (node.LinkSkipped) { node.IncompleteReasons = ScanIncompleteReason.LinkSkipped; if (report.MeasuresAllocatedSpace) node.OwnAllocatedBytes = node.TotalAllocatedBytes = null; return node; }
            if (info is FileInfo file)
            {
                node.OwnBytes = node.TotalBytes = file.Length;
                node.TotalFiles = 1;
                if (report.MeasuresAllocatedSpace)
                {
                    try
                    {
                    var space = _readSpace(file.FullName);
                    node.FileAllocatedBytes = space.AllocatedBytes;
                    var previousDuplicates = report.DuplicateHardLinkPaths;
                    node.OwnAllocatedBytes = node.TotalAllocatedBytes = report.CountAllocatedSpace(space);
                    node.HardLinkDuplicate = report.DuplicateHardLinkPaths != previousDuplicates;
                    }
                    catch (Exception ex) when (IsFileSystemError(ex) || ex is InvalidDataException)
                    {
                        node.OwnAllocatedBytes = node.TotalAllocatedBytes = null;
                        report.AddIssue(ScanDiagnostics.Issue(info.FullName, "allocated-space", ex));
                    }
                }
            }
        }
        catch (Exception ex) when (IsFileSystemError(ex))
        {
            MarkIncomplete(node, ScanDiagnostics.ReasonFor(ex));
            if (report.MeasuresAllocatedSpace) node.OwnAllocatedBytes = node.TotalAllocatedBytes = null;
            report.AddIssue(ScanDiagnostics.Issue(info.FullName, "metadata", ex));
        }
        return node;
    }
    private static void MarkIncomplete(ScanNode node, ScanIncompleteReason reason)
    {
        node.Incomplete = true;
        node.IncompleteReasons |= reason;
    }
    private static bool IsSamePath(string left, string right) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    private sealed class DetailWriteException(string message, Exception inner) : Exception(message, inner) { }
    private sealed class ScanLimitException : Exception { }
    private sealed class ScanMemoryLimitException : Exception { }
    private static bool IsFileSystemError(Exception ex) => ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}
