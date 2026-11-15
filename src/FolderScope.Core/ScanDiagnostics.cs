using System.ComponentModel;

namespace FolderScope.Core;

[Flags]
public enum ScanIncompleteReason
{
    None = 0, AccessDenied = 1, ReadError = 2, LinkSkipped = 4,
    Cancelled = 8, NodeLimit = 16, MemoryLimit = 32, DetailStorage = 64
}

public static class ScanDiagnostics
{
    private static int? ErrorCode(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException or System.Security.SecurityException) return 5;
            if (current is Win32Exception native) return native.NativeErrorCode;
        }
        return (error.HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000)
            ? error.HResult & 0xFFFF : null;
    }
    public static ScanIncompleteReason ReasonFor(Exception error) =>
        ErrorCode(error) == 5 ? ScanIncompleteReason.AccessDenied : ScanIncompleteReason.ReadError;
    public static ScanIssue Issue(string path, string operation, Exception error) =>
        new(path, operation, error.Message, ErrorCode(error));
    public static string DescribeIssue(ScanIssue issue) => issue.Operation switch
    {
        "node-limit" => "达到节点上限",
        "memory-limit" => "达到内存上限",
        "file-detail-write" or "file-detail-flush" => "明细写入失败",
        "file-detail-index" => "明细索引失败",
        "scan-cache-excluded" => "程序扫描缓存目录已排除",
        "allocated-space" => issue.ErrorCode == 5 ? "占用空间：拒绝访问" : "占用空间读取失败",
        _ => issue.ErrorCode == 5 ? "拒绝访问" : "读取失败"
    };
    public static string DescribeNode(ScanNode node)
    {
        if (node.LinkSkipped) return "链接或重解析项已跳过";
        if (!node.Incomplete) return "";
        var reasons = new List<string>();
        var flags = node.IncompleteReasons;
        if (flags.HasFlag(ScanIncompleteReason.AccessDenied)) reasons.Add("含拒绝访问项");
        if (flags.HasFlag(ScanIncompleteReason.ReadError)) reasons.Add("含读取失败项");
        if (flags.HasFlag(ScanIncompleteReason.LinkSkipped)) reasons.Add("含跳过链接或重解析项");
        if (flags.HasFlag(ScanIncompleteReason.Cancelled)) reasons.Add("扫描已取消");
        if (flags.HasFlag(ScanIncompleteReason.NodeLimit)) reasons.Add("达到节点上限");
        if (flags.HasFlag(ScanIncompleteReason.MemoryLimit)) reasons.Add("达到内存上限");
        if (flags.HasFlag(ScanIncompleteReason.DetailStorage)) reasons.Add("明细写入失败");
        if (reasons.Count == 0) reasons.Add("部分未读取（旧结果无分类）");
        if (NodeSizeStatus.Get(node) == NodeSizeState.Unknown) reasons.Add("大小未知");
        return string.Join("；", reasons);
    }
    public static string Summarize(ScanReport report)
    {
        var parts = report.Issues.GroupBy(DescribeIssue).Select(g => $"{g.Key} {g.Count():N0}").ToList();
        var skipped = report.UnlistedSkippedLinks + report.Nodes.LongCount(n => n.LinkSkipped);
        if (skipped > 0) parts.Add($"跳过链接或重解析项 {skipped:N0}");
        if (report.Cancelled) parts.Add("已取消");
        if (report.SuppressedIssueCount > 0) parts.Add($"另省略 {report.SuppressedIssueCount:N0} 条问题");
        if (parts.Count == 0 && !report.Complete) parts.Add("部分未读取");
        return parts.Count == 0 ? "读取完整" : string.Join(" · ", parts);
    }
}
