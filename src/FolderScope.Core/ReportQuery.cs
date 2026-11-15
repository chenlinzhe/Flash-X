namespace FolderScope.Core;

public enum ReportScope { Files, Directories, All }
public enum ReportOrder { Largest, Oldest, Newest, LongestPath }

public static class ReportQuery
{
    public static IReadOnlyDictionary<int, int> DirectDirectoryCounts(ScanReport report)
    {
        var counts = new Dictionary<int, int>();
        foreach (var node in report.Nodes)
            if (node.IsDirectory && node.ParentId is int parent)
                counts[parent] = counts.GetValueOrDefault(parent) + 1;
        return counts;
    }
    public static IReadOnlyList<ScanNode> SelectTop(ScanReport report, ReportScope scope, ReportOrder order, int limit, Func<ScanNode, bool>? filter = null, CancellationToken cancellationToken = default)
        => SelectTop(report.Nodes.Skip(1), scope, order, limit, filter, cancellationToken);

    public static IReadOnlyList<ScanNode> SelectTop(IEnumerable<ScanNode> candidates, ReportScope scope, ReportOrder order, int limit, Func<ScanNode, bool>? filter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        int Compare(ScanNode a, ScanNode b)
        {
            var value = order switch
            {
                ReportOrder.Largest => Nullable.Compare(NodeSizeStatus.SortableBytes(b), NodeSizeStatus.SortableBytes(a)),
                ReportOrder.Oldest => Nullable.Compare(a.ModifiedUtc, b.ModifiedUtc),
                ReportOrder.Newest => Nullable.Compare(b.ModifiedUtc, a.ModifiedUtc),
                _ => b.PathLength.CompareTo(a.PathLength)
            };
            return value != 0 ? value : ScanNode.ComparePaths(a, b);
        }
        var heap = new PriorityQueue<ScanNode, ScanNode>(Comparer<ScanNode>.Create((a, b) => Compare(b, a)));
        foreach (var node in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.LinkSkipped || scope == ReportScope.Files && node.IsDirectory || scope == ReportScope.Directories && !node.IsDirectory) continue;
            if ((order is ReportOrder.Oldest or ReportOrder.Newest) && !node.ModifiedUtc.HasValue) continue;
            if (filter is not null && !filter(node)) continue;
            if (heap.Count < limit) heap.Enqueue(node, node);
            else if (Compare(node, heap.Peek()) < 0) { heap.Dequeue(); heap.Enqueue(node, node); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var result = heap.UnorderedItems.Select(item => item.Element).ToList();
        result.Sort(Compare);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
    public static IEnumerable<ScanNode> Select(ScanReport report, ReportScope scope, ReportOrder order)
    {
        var nodes = report.Nodes.Skip(1).Where(n => !n.LinkSkipped);
        nodes = scope switch
        {
            ReportScope.Files => nodes.Where(n => !n.IsDirectory),
            ReportScope.Directories => nodes.Where(n => n.IsDirectory),
            _ => nodes
        };
        return order switch
        {
            ReportOrder.Largest => nodes.OrderByDescending(NodeSizeStatus.SortableBytes).ThenBy(n => n.Path, StringComparer.OrdinalIgnoreCase),
            ReportOrder.Oldest => nodes.Where(n => n.ModifiedUtc.HasValue).OrderBy(n => n.ModifiedUtc).ThenBy(n => n.Path, StringComparer.OrdinalIgnoreCase),
            ReportOrder.Newest => nodes.Where(n => n.ModifiedUtc.HasValue).OrderByDescending(n => n.ModifiedUtc).ThenBy(n => n.Path, StringComparer.OrdinalIgnoreCase),
            _ => nodes.OrderByDescending(n => n.PathLength).ThenBy(n => n.Path, StringComparer.OrdinalIgnoreCase)
        };
    }
}
