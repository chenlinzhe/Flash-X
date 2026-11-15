using System.Text;
using System.Text.Json;
namespace FolderScope.Core;

public sealed record StoredFilePage(IReadOnlyList<ScanNode> Files, bool HasMore);
// Append-only scan storage. Caller owns the path; existing files are never overwritten.
public sealed class FileDetailStore : IDisposable
{
    private readonly StreamWriter _writer;
    private bool _disposed;
    public string? IndexWriteError { get; private set; }
    private readonly string _path;
    public string FilePath => Path.GetFullPath(_path);
    public Guid StoreId { get; } = Guid.NewGuid();
    private long _writtenBytes;
    private readonly Dictionary<int, List<DetailRange>> _ranges = [];
    public sealed record DetailRange(long Start, long End);
    public sealed record DetailIndex(long Length, long LastWriteTicks, Dictionary<int, List<DetailRange>> Ranges, int Version = 0, string? Digest = null);
    public FileDetailStore(string path)
    {
        _path = path;
        if (File.Exists(path + ".index.json")) throw new IOException("Detail index already exists.");
        _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { NewLine = "\n" };
        var header = JsonSerializer.Serialize(new { DetailSchema = "folderscope.details.v1", StoreId });
        _writer.WriteLine(header);
        _writtenBytes = Encoding.UTF8.GetByteCount(header) + 1;
    }
    public void Append(ScanNode file, int nodeIdOffset = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (file.IsDirectory || file.ParentId is null || nodeIdOffset < 0) throw new ArgumentException("Expected a file with a parent directory and a nonnegative ID offset.", nameof(file));
        var stored = nodeIdOffset == 0 ? file : new ScanNode
        {
            Id = checked(file.Id + nodeIdOffset), ParentId = checked(file.ParentId.Value + nodeIdOffset), Path = file.Path, Name = file.Name,
            IsDirectory = file.IsDirectory, UnlistedAllocatedBytes = file.UnlistedAllocatedBytes, FileAllocatedBytes = file.FileAllocatedBytes,
            HardLinkDuplicate = file.HardLinkDuplicate, OwnAllocatedBytes = file.OwnAllocatedBytes, TotalAllocatedBytes = file.TotalAllocatedBytes,
            OwnBytes = file.OwnBytes, UnlistedFileBytes = file.UnlistedFileBytes, UnlistedFileCount = file.UnlistedFileCount,
            TotalBytes = file.TotalBytes, TotalFiles = file.TotalFiles, TotalDirectories = file.TotalDirectories, Attributes = file.Attributes,
            ModifiedUtc = file.ModifiedUtc, CreatedUtc = file.CreatedUtc, AccessedUtc = file.AccessedUtc, LinkSkipped = file.LinkSkipped,
            Incomplete = file.Incomplete, IncompleteReasons = file.IncompleteReasons
        };
        var line = JsonSerializer.Serialize(stored);
        var end = checked(_writtenBytes + Encoding.UTF8.GetByteCount(line) + 1);
        _writer.WriteLine(line); // Publish index ranges only after the writer accepts the record.
        if (!_ranges.TryGetValue(stored.ParentId!.Value, out var ranges)) _ranges.Add(stored.ParentId.Value, ranges = []);
        if (ranges.Count > 0 && ranges[^1].End == _writtenBytes) ranges[^1] = ranges[^1] with { End = end };
        else ranges.Add(new(_writtenBytes, end));
        _writtenBytes = end;
    }
    public void Flush() { ObjectDisposedException.ThrowIf(_disposed, this); _writer.Flush(); }
    public void Dispose() => Finish(writeIndex: true);
    public void Finish(bool writeIndex)
    {
        if (_disposed) return;
        _disposed = true;
        _writer.Dispose();
        if (!writeIndex) return;
        try
        {
        using var indexStream = new FileStream(_path + ".index.json", FileMode.CreateNew, FileAccess.Write);
        var index = new DetailIndex(_writtenBytes, File.GetLastWriteTimeUtc(_path).Ticks, _ranges, 1);
        JsonSerializer.Serialize(indexStream, index with { Digest = ComputeDigest(index, CancellationToken.None) });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { IndexWriteError = ex.Message; }
    }
    // Accidental index corruption detection, not authentication or binding to a scan.
    private static string ComputeDigest(DetailIndex index, CancellationToken token)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var buffer = new byte[8];
        void Add(long value)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
            hash.AppendData(buffer);
        }
        Add(index.Version); Add(index.Length); Add(index.LastWriteTicks); Add(index.Ranges.Count);
        var parents = new int[index.Ranges.Count];
        var position = 0;
        foreach (var parent in index.Ranges.Keys)
        {
            token.ThrowIfCancellationRequested();
            parents[position++] = parent;
        }
        SortWithCancellation(parents, static (a, b) => a.CompareTo(b), token);
        foreach (var parent in parents)
        {
            token.ThrowIfCancellationRequested();
            var ranges = index.Ranges[parent];
            Add(parent); Add(ranges.Count);
            foreach (var range in ranges) { token.ThrowIfCancellationRequested(); Add(range.Start); Add(range.End); }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static void SortWithCancellation<T>(IList<T> values, Comparison<T> comparison, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var comparisons = 0;
        int Compare(T a, T b)
        {
            if ((++comparisons & 1023) == 0) token.ThrowIfCancellationRequested();
            return comparison(a, b);
        }
        try
        {
            if (values is T[] array) Array.Sort(array, Compare);
            else ((List<T>)values).Sort(Compare);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is OperationCanceledException && token.IsCancellationRequested)
        {
            // Framework sorting wraps comparer exceptions; cancellation must reach the caller.
            throw new OperationCanceledException(token);
        }
        token.ThrowIfCancellationRequested();
    }
    private static bool CoversAllRecords(DetailIndex index, long dataStart, CancellationToken token)
    {
        var ordered = new List<DetailRange>();
        foreach (var ranges in index.Ranges.Values)
        {
            token.ThrowIfCancellationRequested();
            if (ranges is null) return false;
            foreach (var range in ranges)
            {
                token.ThrowIfCancellationRequested();
                if (range is null || range.Start < 0 || range.End <= range.Start || range.End > index.Length) return false;
                ordered.Add(range);
            }
        }
        token.ThrowIfCancellationRequested();
        SortWithCancellation(ordered, static (a, b) => a.Start.CompareTo(b.Start), token);
        long end = dataStart;
        foreach (var range in ordered)
        {
            token.ThrowIfCancellationRequested();
            if (range.Start != end) return false;
            end = range.End;
        }
        return end == index.Length;
    }
    // Read and validate the header on the same handle used for all records, preventing replacement
    // between identity checking and reading. FileShare.Read denies writes/deletion while browsing.
    private static long ReadHeader(FileStream stream, Guid? expectedStoreId)
    {
        var prefix = new List<byte>();
        var terminated = false;
        for (var i = 0; i < 512; i++)
        {
            var value = stream.ReadByte();
            if (value < 0) break;
            if (value == 10) { terminated = true; break; }
            prefix.Add((byte)value);
        }
        if (terminated)
        {
            using var header = JsonDocument.Parse(Encoding.UTF8.GetString(prefix.ToArray()));
            if (header.RootElement.ValueKind == JsonValueKind.Object && header.RootElement.TryGetProperty("DetailSchema", out var schema))
            {
                if (schema.ValueKind != JsonValueKind.String || schema.GetString() != "folderscope.details.v1"
                    || !header.RootElement.TryGetProperty("StoreId", out var id)
                    || id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out var storeId) || storeId == Guid.Empty)
                    throw new InvalidDataException("文件明细标识损坏，不能关联本次结果。");
                if (expectedStoreId is Guid expected && expected != storeId)
                    throw new InvalidDataException("文件明细属于另一次扫描，已停止读取以避免显示错误结果。");
                return stream.Position;
            }
        }
        if (expectedStoreId is not null) throw new InvalidDataException("文件明细缺少本次扫描标识，不能验证关联。");
        stream.Position = 0; // Legacy detail files contain only records.
        return 0;
    }
    public static void ValidateAssociation(string path, Guid? expectedStoreId)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        ReadHeader(stream, expectedStoreId);
    }
    private static IEnumerable<string> ReadDirectoryLines(string path, int parentId, CancellationToken token, Guid? expectedStoreId)
    {
        token.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var dataStart = ReadHeader(stream, expectedStoreId);
        List<DetailRange>? ranges = null;
        var indexPath = path + ".index.json";
        try
        {
            if (File.Exists(indexPath) && new FileInfo(indexPath).Length <= 8 * 1024 * 1024)
            {
                token.ThrowIfCancellationRequested();
                using var indexStream = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var index = indexStream.Length <= 8 * 1024 * 1024 ? JsonSerializer.Deserialize<DetailIndex>(indexStream) : null;
                token.ThrowIfCancellationRequested();
                if (index is not null && index.Length == stream.Length && index.LastWriteTicks == File.GetLastWriteTimeUtc(path).Ticks
                    && index.Ranges is not null && index.Version == 1 && index.Digest is not null
                    && CoversAllRecords(index, dataStart, token) && string.Equals(index.Digest, ComputeDigest(index, token), StringComparison.Ordinal))
                    ranges = index.Ranges.GetValueOrDefault(parentId) ?? [];
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { ranges = null; }
        if (ranges is null)
        {
            stream.Position = dataStart;
            using var all = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
            while (all.ReadLine() is { } line) { token.ThrowIfCancellationRequested(); yield return line; }
            yield break;
        }
        foreach (var range in ranges)
        {
            token.ThrowIfCancellationRequested();
            stream.Seek(range.Start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
            var position = range.Start;
            while (position < range.End && reader.ReadLine() is { } line)
            {
                token.ThrowIfCancellationRequested();
                position += Encoding.UTF8.GetByteCount(line) + 1;
                yield return line;
            }
        }
    }
    public static IEnumerable<ScanNode> ReadAllFiles(string path, Guid? expectedStoreId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Position = ReadHeader(stream, expectedStoreId);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = JsonSerializer.Deserialize<ScanNode>(line) ?? throw new InvalidDataException("Empty file record.");
            ValidateFileRecord(file);
            yield return file;
        }
    }
    private static void ValidateFileRecord(ScanNode node)
    {
        // Older scans initialized a skipped link's own allocation to 0 while its total stayed unknown.
        var legacySkippedAllocation = node.LinkSkipped && node.OwnAllocatedBytes == 0 && node.TotalAllocatedBytes is null;
        if (node.IsDirectory || node.ParentId is null || node.ParentId < 0
            || string.IsNullOrWhiteSpace(node.Path) || string.IsNullOrEmpty(node.Name)
            || node.OwnBytes < 0 || node.TotalBytes != node.OwnBytes || node.TotalFiles < 0 || node.TotalFiles > 1
            || (!node.Incomplete && !node.LinkSkipped && node.TotalFiles != 1)
            || (node.LinkSkipped && (node.TotalBytes != 0 || node.TotalFiles != 0 || node.TotalAllocatedBytes is not null))
            || node.FileAllocatedBytes < 0 || node.OwnAllocatedBytes < 0 || node.TotalAllocatedBytes < 0
            || (!legacySkippedAllocation && node.TotalAllocatedBytes != node.OwnAllocatedBytes)
            || (node.HardLinkDuplicate && (node.OwnAllocatedBytes != 0 || node.FileAllocatedBytes is null)))
            throw new InvalidDataException("Invalid file record.");
    }
    public static StoredFilePage ReadPage(string path, int parentId, int offset = 0, int limit = 1000, CancellationToken cancellationToken = default, Guid? expectedStoreId = null)
    {
        if (offset < 0 || limit < 1 || limit > 10000) throw new ArgumentOutOfRangeException(nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();
        var files = new List<ScanNode>();
        var matched = 0L;
        foreach (var line in ReadDirectoryLines(path, parentId, cancellationToken, expectedStoreId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = JsonSerializer.Deserialize<ScanNode>(line) ?? throw new InvalidDataException("Empty file record.");
            ValidateFileRecord(node);
            if (node.ParentId != parentId) continue;
            if (matched++ < offset) continue;
            if (files.Count == limit) return new(files, true);
            files.Add(node);
        }
        return new(files, false);
    }
    public static StoredFilePage ReadLargest(string path, int parentId, int limit = 1000, CancellationToken cancellationToken = default, Guid? expectedStoreId = null)
    {
        if (limit < 1 || limit > 10000) throw new ArgumentOutOfRangeException(nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();
        int Compare(ScanNode a, ScanNode b)
        {
            var size = Nullable.Compare(NodeSizeStatus.SortableBytes(b), NodeSizeStatus.SortableBytes(a));
            return size != 0 ? size : ScanNode.ComparePaths(a, b);
        }
        var heap = new PriorityQueue<ScanNode, ScanNode>(Comparer<ScanNode>.Create((a, b) => Compare(b, a)));
        var matched = 0L;
        foreach (var line in ReadDirectoryLines(path, parentId, cancellationToken, expectedStoreId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = JsonSerializer.Deserialize<ScanNode>(line) ?? throw new InvalidDataException("Empty file record.");
            ValidateFileRecord(node);
            if (node.ParentId != parentId) continue;
            matched++;
            if (heap.Count < limit) heap.Enqueue(node, node);
            else if (Compare(node, heap.Peek()) < 0) { heap.Dequeue(); heap.Enqueue(node, node); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var files = heap.UnorderedItems.Select(item => item.Element).ToList();
        files.Sort(Compare);
        cancellationToken.ThrowIfCancellationRequested();
        return new(files, matched > limit);
    }}
