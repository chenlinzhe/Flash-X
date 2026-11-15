namespace FolderScope.Core;

public static class ScanCacheLocation
{
    public static bool IsWithin(string rootDirectory, string candidatePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return candidate.Equals(root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsOutsideScan(string scanPath, string cacheDirectory)
    {
        return !IsWithin(scanPath, cacheDirectory);
    }
}
