using System.Text.RegularExpressions;

namespace FolderScope.Core;

public static class NameFilter
{
    public static Func<ScanNode, bool> Create(string masks, bool exclude = false)
    {
        var patterns = masks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (patterns.Length == 0) return _ => true;
        if (patterns.Length > 64 || patterns.Any(p => p.Length > 256)) throw new ArgumentException("Filter is too long.");
        var expressions = patterns.Select(p => new Regex("\\A" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100))).ToArray();
        return node => expressions.Any(r => r.IsMatch(node.Name)) != exclude;
    }
}
