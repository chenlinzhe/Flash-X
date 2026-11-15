namespace FolderScope.Core;

public sealed class DirectoryIndex
{
    public Dictionary<int, List<ScanNode>> Children { get; } = [];
    public Dictionary<int, int> DirectDirectories { get; } = [];
    public bool SortingCancelled { get; private set; }

    public static DirectoryIndex Create(ScanReport report, CancellationToken cancellationToken = default, bool sortChildren = true, CancellationToken sortCancellationToken = default, long? maximumManagedBytes = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumManagedBytes is < 1) throw new ArgumentOutOfRangeException(nameof(maximumManagedBytes));
        void CheckBudget()
        {
            if (maximumManagedBytes is long budget && GC.GetTotalMemory(false) > budget)
                throw new InvalidDataException("目录索引构建超过托管内存预算，加载已停止。");
        }
        CheckBudget();
        var visited = 0;
        var index = new DirectoryIndex();
        foreach (var node in report.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((++visited & 1023) == 0) CheckBudget();
            if (node.ParentId is not int parent) continue;
            if (!index.Children.TryGetValue(parent, out var children))
                index.Children.Add(parent, children = []);
            children.Add(node);
            if (node.IsDirectory) index.DirectDirectories[parent] = index.DirectDirectories.GetValueOrDefault(parent) + 1;
        }
        if (sortChildren) foreach (var children in index.Children.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckBudget();
            if (sortCancellationToken.IsCancellationRequested) { index.SortingCancelled = true; break; }
            // Only abort a load here: the partially sorted index is discarded.
            // Scan sorting cancellation keeps every entry and is checked between lists.
            var comparisons = 0;
            try
            {
                children.Sort((a, b) =>
                {
                    if ((++comparisons & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    var size = b.TotalBytes.CompareTo(a.TotalBytes);
                    return size != 0 ? size : a.Id.CompareTo(b.Id);
                });
            }
            catch (InvalidOperationException ex) when (ex.InnerException is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        CheckBudget();
        return index;
    }
}