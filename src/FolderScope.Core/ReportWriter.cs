using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

namespace FolderScope.Core;

public static class ReportWriter
{
    private static readonly JsonSerializerOptions ExportJson = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions HistoryJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

    public static void Write(ScanReport report, string destination, CancellationToken cancellationToken = default) =>
        Write(report, destination, ExportJson, cancellationToken);

    public static void WriteHistorySnapshot(ScanReport report, string destination, CancellationToken cancellationToken = default) =>
        Write(report, destination, HistoryJson, cancellationToken);

    private static void Write(ScanReport report, string destination, JsonSerializerOptions json, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = System.IO.Path.GetFullPath(destination);
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".json" or ".csv" or ".xml"))
            throw new ArgumentException("Use .json, .csv or .xml.");
        var parent = System.IO.Path.GetDirectoryName(path)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException($"Output already exists: {path}");
        var temporary = System.IO.Path.Combine(parent, ".folderscope-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new CancellableOutputStream(temporary, cancellationToken))
            {
                if (extension == ".json") JsonSerializer.Serialize(stream, report, json);
                else if (extension == ".csv") WriteCsv(report, stream);
                else WriteXml(report, stream);
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary); // Only this call's newly created temporary file.
        }
    }

    internal sealed class CancellableOutputStream(string path, CancellationToken token)
        : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            token.ThrowIfCancellationRequested();
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            token.ThrowIfCancellationRequested();
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            token.ThrowIfCancellationRequested();
            base.WriteByte(value);
        }
    }

    private static void WriteCsv(ScanReport report, Stream stream)
    {
        using var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true);
        var directCounts = ReportQuery.DirectDirectoryCounts(report);
        var scanComplete = report.Complete.ToString();
        writer.WriteLine("Id,ParentId,Path,Kind,OwnBytes,TotalBytes,TotalFiles,TotalDirectories,ModifiedUtc,LinkSkipped,Incomplete,ScanCancelled,Name,CreatedUtc,AccessedUtc,Attributes,DirectDirectories,PathLength,NameLength,ScanComplete,SuppressedIssueCount,RetainsFileNodes,UnlistedFileBytes,UnlistedFileCount,SizeState,KnownLogicalBytes,IsLowerBound");
        foreach (var n in report.Nodes)
        {
            var cells = new[] { n.Id.ToString(CultureInfo.InvariantCulture), n.ParentId?.ToString(CultureInfo.InvariantCulture) ?? "",
                n.Path, n.IsDirectory ? "Directory" : "File", n.OwnBytes.ToString(CultureInfo.InvariantCulture),
                n.TotalBytes.ToString(CultureInfo.InvariantCulture), n.TotalFiles.ToString(CultureInfo.InvariantCulture),
                n.TotalDirectories.ToString(CultureInfo.InvariantCulture), n.ModifiedUtc?.ToString("O") ?? "",
                n.LinkSkipped.ToString(), n.Incomplete.ToString(), report.Cancelled.ToString(), n.Name,
                n.CreatedUtc?.ToString("O") ?? "", n.AccessedUtc?.ToString("O") ?? "",
                n.Attributes.ToString(CultureInfo.InvariantCulture), directCounts.GetValueOrDefault(n.Id).ToString(CultureInfo.InvariantCulture),
                n.Path.Length.ToString(CultureInfo.InvariantCulture), n.Name.Length.ToString(CultureInfo.InvariantCulture),
                scanComplete, report.SuppressedIssueCount.ToString(CultureInfo.InvariantCulture),
                report.RetainsFileNodes.ToString(), n.UnlistedFileBytes.ToString(CultureInfo.InvariantCulture), n.UnlistedFileCount.ToString(CultureInfo.InvariantCulture),
                NodeSizeStatus.Get(n).ToString(), NodeSizeStatus.SortableBytes(n)?.ToString(CultureInfo.InvariantCulture) ?? "",
                (NodeSizeStatus.Get(n) == NodeSizeState.Partial).ToString() };
            writer.WriteLine(string.Join(',', cells.Select(CsvCell)));
        }
    }
    private static string CsvCell(string value)
    {
        // Do not let a file name become an Excel formula. JSON/XML preserve exact names.
        if (value.Length > 0 && "=+-@\t\r\n".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private static void WriteXml(ScanReport report, Stream stream)
    {
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, CloseOutput = false });
        writer.WriteStartElement("scan");
        writer.WriteAttributeString("schema", report.Schema);
        writer.WriteAttributeString("retainsFileNodes", XmlConvert.ToString(report.RetainsFileNodes));
        writer.WriteAttributeString("unlistedSkippedLinks", XmlConvert.ToString(report.UnlistedSkippedLinks));
        writer.WriteAttributeString("root", report.RootPath);
        writer.WriteAttributeString("startedUtc", XmlConvert.ToString(report.StartedUtc, XmlDateTimeSerializationMode.Utc));
        writer.WriteAttributeString("finishedUtc", XmlConvert.ToString(report.FinishedUtc, XmlDateTimeSerializationMode.Utc));
        writer.WriteAttributeString("complete", XmlConvert.ToString(report.Complete));
        writer.WriteAttributeString("cancelled", XmlConvert.ToString(report.Cancelled));
        writer.WriteAttributeString("suppressedIssueCount", XmlConvert.ToString(report.SuppressedIssueCount));
        writer.WriteAttributeString("byteSemantics", report.ByteSemantics);
        foreach (var n in report.Nodes)
        {
            writer.WriteStartElement("node");
            writer.WriteAttributeString("id", XmlConvert.ToString(n.Id));
            if (n.ParentId.HasValue) writer.WriteAttributeString("parentId", XmlConvert.ToString(n.ParentId.Value));
            writer.WriteAttributeString("path", n.Path);
            writer.WriteAttributeString("name", n.Name);
            writer.WriteAttributeString("directory", XmlConvert.ToString(n.IsDirectory));
            writer.WriteAttributeString("ownBytes", XmlConvert.ToString(n.OwnBytes));
            writer.WriteAttributeString("unlistedFileBytes", XmlConvert.ToString(n.UnlistedFileBytes));
            writer.WriteAttributeString("unlistedFileCount", XmlConvert.ToString(n.UnlistedFileCount));
            writer.WriteAttributeString("bytes", XmlConvert.ToString(n.TotalBytes));
            writer.WriteAttributeString("sizeState", NodeSizeStatus.Get(n).ToString());
            if (NodeSizeStatus.SortableBytes(n) is long knownBytes)
                writer.WriteAttributeString("knownLogicalBytes", XmlConvert.ToString(knownBytes));
            writer.WriteAttributeString("isLowerBound", XmlConvert.ToString(NodeSizeStatus.Get(n) == NodeSizeState.Partial));
            writer.WriteAttributeString("files", XmlConvert.ToString(n.TotalFiles));
            writer.WriteAttributeString("directories", XmlConvert.ToString(n.TotalDirectories));
            writer.WriteAttributeString("linkSkipped", XmlConvert.ToString(n.LinkSkipped));
            writer.WriteAttributeString("incomplete", XmlConvert.ToString(n.Incomplete));
            writer.WriteAttributeString("attributes", XmlConvert.ToString(n.Attributes));
            if (n.ModifiedUtc.HasValue)
                writer.WriteAttributeString("modifiedUtc", XmlConvert.ToString(n.ModifiedUtc.Value, XmlDateTimeSerializationMode.Utc));
            if (n.CreatedUtc.HasValue)
                writer.WriteAttributeString("createdUtc", XmlConvert.ToString(n.CreatedUtc.Value, XmlDateTimeSerializationMode.Utc));
            if (n.AccessedUtc.HasValue)
                writer.WriteAttributeString("accessedUtc", XmlConvert.ToString(n.AccessedUtc.Value, XmlDateTimeSerializationMode.Utc));
            writer.WriteEndElement();
        }
        foreach (var error in report.Issues)
        {
            writer.WriteStartElement("issue"); writer.WriteAttributeString("path", error.Path);
            writer.WriteAttributeString("operation", error.Operation); writer.WriteString(error.Message); writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }
}
