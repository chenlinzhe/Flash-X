namespace FolderScope.Core;

public static class ChartSummary
{
    public static IReadOnlyList<ScanNode> Create(IEnumerable<ScanNode> nodes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var top = new List<ScanNode>(9);
        long total = 0;
        var count = 0;
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.TotalBytes <= 0) continue;
            total = checked(total + node.TotalBytes); count++;
            var index = top.FindIndex(n => n.TotalBytes < node.TotalBytes);
            if (index < 0) index = top.Count;
            if (index < 8) top.Insert(index, node);
            if (top.Count > 8) top.RemoveAt(8);
        }
        if (count > 8) top.Add(new ScanNode { Id = -1, Path = "", Name = $"其他（{count - 8}项）", TotalBytes = total - top.Sum(n => n.TotalBytes) });
        return top;
    }
}
