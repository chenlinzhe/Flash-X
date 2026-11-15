namespace FolderScope.Core;

public sealed record NodeFilterOptions
{
    public string NameMasks { get; init; } = "";
    public ReportScope Scope { get; init; } = ReportScope.All;
    public long? MinimumBytes { get; init; }
    public long? MaximumBytes { get; init; }
    public DateTime? ModifiedFromUtc { get; init; }
    public DateTime? ModifiedToUtc { get; init; }
    public int RequiredAttributes { get; init; }
    public int ExcludedAttributes { get; init; }
    public bool ExcludeMatches { get; init; }
}

public static class NodeFilter
{
    public static Func<ScanNode, bool> Create(NodeFilterOptions options)
    {
        if (options.MinimumBytes < 0 || options.MaximumBytes < 0 || options.MinimumBytes > options.MaximumBytes || options.ModifiedFromUtc > options.ModifiedToUtc)
            throw new ArgumentException("Invalid filter bounds.");
        if (!Enum.IsDefined(options.Scope) || (options.RequiredAttributes & options.ExcludedAttributes) != 0)
            throw new ArgumentException("Invalid filter scope or conflicting attributes.");
        var names = NameFilter.Create(options.NameMasks);
        return node =>
        {
            // Scope restricts which node types the rule applies to; exclusion never expands scope.
            if (options.Scope == ReportScope.Files && node.IsDirectory || options.Scope == ReportScope.Directories && !node.IsDirectory) return false;
            if (options.MinimumBytes.HasValue || options.MaximumBytes.HasValue)
            {
                var state = NodeSizeStatus.Get(node);
                if (state is NodeSizeState.Unknown or NodeSizeState.SkippedLink) return false;
                if (state == NodeSizeState.Partial && options.MaximumBytes.HasValue) return false;
                if (state == NodeSizeState.Partial && node.TotalBytes < options.MinimumBytes) return false;
            }
            var matched = names(node) &&
                (!options.MinimumBytes.HasValue || node.TotalBytes >= options.MinimumBytes) &&
                (!options.MaximumBytes.HasValue || node.TotalBytes <= options.MaximumBytes) &&
                (!options.ModifiedFromUtc.HasValue || node.ModifiedUtc >= options.ModifiedFromUtc) &&
                (!options.ModifiedToUtc.HasValue || node.ModifiedUtc <= options.ModifiedToUtc) &&
                (node.Attributes & options.RequiredAttributes) == options.RequiredAttributes &&
                (node.Attributes & options.ExcludedAttributes) == 0;
            return options.ExcludeMatches ? !matched : matched;
        };
    }
}
