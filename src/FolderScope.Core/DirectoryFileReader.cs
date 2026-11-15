namespace FolderScope.Core;

public sealed record DirectoryFilePage(IReadOnlyList<ScanNode> Files, bool Truncated, int SkippedLinks = 0);
public static class DirectoryFileReader
{
    public static DirectoryFilePage Read(string directory, int limit = 1000, CancellationToken cancellationToken = default, long minimumFileBytes = 0)
    {
        if (minimumFileBytes < 0) throw new ArgumentOutOfRangeException(nameof(minimumFileBytes));
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();
        var info = new DirectoryInfo(directory);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Refusing to enumerate a linked directory.");
        var files = new List<ScanNode>();
        var skippedLinks = 0;
        foreach (var file in info.EnumerateFiles("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0) { skippedLinks++; continue; }

            var size = file.Length;
            if (minimumFileBytes > 0 && size <= minimumFileBytes) continue;
            if (files.Count == limit) return new(files, true, skippedLinks);
            files.Add(new ScanNode { Id = files.Count, Path = file.FullName, Name = file.Name, OwnBytes = size, TotalBytes = size, TotalFiles = 1,
                Attributes = (int)file.Attributes, ModifiedUtc = file.LastWriteTimeUtc, CreatedUtc = file.CreationTimeUtc, AccessedUtc = file.LastAccessTimeUtc });
        }
        return new(files, false, skippedLinks);
    }
}
