using FolderScope.Core;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Flash-X — Windows disk usage and file size analysis");
    Console.WriteLine("folderscope scan <directory> [--directories-only] [--file-details new.jsonl] [--allocated-space] [--measure-retained-memory] [--export report.json|report.csv|report.xml]...");
    Console.WriteLine("--min-file-mib N and --min-dir-mib N list matching logical sizes (inclusive, 1024 MiB = 1 GiB). Exports stay complete.");
    Console.WriteLine("Existing output files are never overwritten. Ctrl+C cancels and marks a partial report.");
    Console.WriteLine("Exit: 0 complete, 2 partial/errors/skipped links, 64 arguments, 74 I/O, 130 cancelled.");
    return 0;
}
if (args.Length < 2 || args[0] != "scan") return Usage("Expected scan <directory>.");
var outputs = new List<string>();
string? fileDetailsPath = null;
var retainFileNodes = true;
var measureAllocatedSpace = false;
var measureRetainedMemory = false;
long? minimumFile = null, minimumDirectory = null;
for (var i = 2; i < args.Length; i++)
{
    if (args[i] is "--min-file-mib" or "--min-dir-mib")
    {
        var option = args[i];
        if (++i >= args.Length || !long.TryParse(args[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var mib) || mib > long.MaxValue / 1048576)
            return Usage(option + " requires a nonnegative integer in MiB.");
        if (option == "--min-file-mib") minimumFile = mib * 1048576;
        else minimumDirectory = mib * 1048576;
        continue;
    }
    if (args[i] == "--measure-retained-memory") { measureRetainedMemory = true; continue; }
    if (args[i] == "--file-details" && i + 1 < args.Length) { fileDetailsPath = Path.GetFullPath(args[++i]); retainFileNodes = false; continue; }
    if (args[i] == "--allocated-space") { measureAllocatedSpace = true; continue; }
    if (args[i] == "--directories-only") { retainFileNodes = false; continue; }
    if (args[i] != "--export" || i + 1 >= args.Length) return Usage("Unknown or incomplete argument: " + args[i]);
    outputs.Add(args[++i]);
}
if (minimumFile.HasValue && !retainFileNodes && fileDetailsPath is null) return Usage("File threshold requires retained files or --file-details.");
using var cancelled = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancelled.Cancel(); };
try
{
    // Fail before a scan if a requested output would overwrite an existing file.
    var destinations = outputs.Select(Path.GetFullPath).ToArray();
    if (destinations.Distinct(StringComparer.OrdinalIgnoreCase).Count() != destinations.Length)
        return Usage("Duplicate export paths.");
    foreach (var output in destinations)
    {
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Output already exists: " + output);
        if (!Directory.Exists(Path.GetDirectoryName(output))) throw new DirectoryNotFoundException(Path.GetDirectoryName(output));
        if (Path.GetExtension(output).ToLowerInvariant() is not (".json" or ".csv" or ".xml"))
            return Usage("Unsupported export extension: " + output);
    }
    if (fileDetailsPath is not null)
    {
        var scanRoot = Path.GetFullPath(args[1]);
        if (!ScanCacheLocation.IsOutsideScan(scanRoot, fileDetailsPath))
            return Usage("File-detail output must be outside the scanned directory to avoid scanning its own growing output.");
        if (destinations.Contains(fileDetailsPath, StringComparer.OrdinalIgnoreCase))
            return Usage("File-detail output must differ from export destinations.");
    }
    using var fileStore = fileDetailsPath is null ? null : new FileDetailStore(fileDetailsPath);
    var scanWatch = System.Diagnostics.Stopwatch.StartNew();
    var report = new DirectoryScanner().Scan(args[1], cancelled.Token, retainFileNodes: retainFileNodes, measureAllocatedSpace: measureAllocatedSpace, fileDetails: fileStore);
    scanWatch.Stop();
    // Finish the authoritative detail file before publishing success or exporting its reference.
    fileStore?.Dispose();
    if (fileStore?.IndexWriteError is { } indexError)
        report.AddIssue(new ScanIssue(fileDetailsPath!, "file-detail-index", "Directory index could not be saved; details will be read sequentially: " + indexError));
    using var measuredProcess = System.Diagnostics.Process.GetCurrentProcess();
    measuredProcess.Refresh();
    Console.WriteLine($"Scan milliseconds: {scanWatch.Elapsed.TotalMilliseconds:F2}; managed bytes: {GC.GetTotalMemory(false)}; peak working set bytes: {measuredProcess.PeakWorkingSet64}");
    Console.WriteLine($"{report.RootPath}\nLogical bytes: {NodeSizeStatus.DescribeLogicalBytes(report.Root)}\nFiles: {report.Root.TotalFiles}\nSubdirectories: {report.Root.TotalDirectories}");
    Console.WriteLine($"Complete: {report.Complete}; Cancelled: {report.Cancelled}; Issues: {report.Issues.Count + report.SuppressedIssueCount}; Skipped links: {report.Nodes.Count(n => n.LinkSkipped) + report.UnlistedSkippedLinks}");
    Console.WriteLine($"Retains file details: {report.RetainsFileNodes}; retained nodes: {report.Nodes.Count}");
    if (measureRetainedMemory)
        Console.WriteLine($"Retained managed bytes after forced collection: {GC.GetTotalMemory(true)}; includes runtime and report, excludes desktop; diagnostic collection outside scan timing");
    if (measureAllocatedSpace) Console.WriteLine($"Allocated bytes (hard links counted once per scan): {report.Root.TotalAllocatedBytes?.ToString() ?? "unknown"}");
    if (minimumDirectory.HasValue || minimumFile.HasValue)
    {
        IEnumerable<ScanNode> candidates = report.Nodes;
        if (minimumFile.HasValue && fileDetailsPath is not null)
            candidates = candidates.Concat(FileDetailStore.ReadAllFiles(fileDetailsPath, report.FileDetailsStoreId, cancelled.Token));
        foreach (var node in candidates)
        {
            var threshold = node.IsDirectory ? minimumDirectory : minimumFile;
            var state = NodeSizeStatus.Get(node);
            if (threshold.HasValue && state is NodeSizeState.Known or NodeSizeState.Partial && node.TotalBytes >= threshold.Value)
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { kind = node.IsDirectory ? "directory" : "file", node.Path, node.TotalBytes, node.Incomplete, sizeState = state.ToString(), isLowerBound = state == NodeSizeState.Partial }));
        }
    }
    else foreach (var file in ReportQuery.SelectTop(report, ReportScope.Files, ReportOrder.Largest, 10))
        Console.WriteLine($"{NodeSizeStatus.DescribeLogicalBytes(file),14}  {file.Path}");
    foreach (var output in destinations) { ReportWriter.Write(report, output); Console.WriteLine("Exported: " + output); }
    foreach (var issue in report.Issues.Take(10)) Console.Error.WriteLine($"{issue.Operation}: {issue.Path}: {issue.Message}");
    return report.Cancelled ? 130 : report.Complete ? 0 : 2;
}
catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { return 130; }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
{ Console.Error.WriteLine(ex.Message); return 74; }
catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
{ return Usage(ex.Message); }
static int Usage(string message) { Console.Error.WriteLine(message); Console.Error.WriteLine("Use --help for syntax."); return 64; }
