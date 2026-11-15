namespace FolderScope.Core;

public enum NodeSizeState { Known, Partial, Unknown, SkippedLink }
public static class NodeSizeStatus
{
    public static string DescribeLogicalBytes(ScanNode node) => Get(node) switch
    {
        NodeSizeState.Unknown => "unknown",
        NodeSizeState.SkippedLink => "unknown (skipped)",
        NodeSizeState.Partial => ">= " + node.TotalBytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " (partial)",
        _ => node.TotalBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
    public static long? SortableBytes(ScanNode node) => Get(node) is NodeSizeState.Unknown or NodeSizeState.SkippedLink ? null : node.TotalBytes;
    public static NodeSizeState Get(ScanNode node) => node.LinkSkipped ? NodeSizeState.SkippedLink : node.Incomplete ? (node.TotalBytes == 0 ? NodeSizeState.Unknown : NodeSizeState.Partial) : NodeSizeState.Known;
}
