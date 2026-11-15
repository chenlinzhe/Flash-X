using System.IO;
using System.Text.Json;

namespace FolderScope.Desktop;

internal sealed record ScanHistoryEntry(string Path, string Target, DateTime FinishedUtc)
{
    public string Label => FinishedUtc.ToLocalTime().ToString("MM-dd HH:mm") + " · " + Target;
    public override string ToString() => Label;

    internal static ScanHistoryEntry Read(string snapshot)
    {
        var overview = ScanHistoryOverview.TryRead(snapshot);
        if (overview is not null) return new(snapshot, overview.RootPath, overview.FinishedUtc);
        var target = "扫描记录";
        var time = File.GetLastWriteTimeUtc(snapshot);
        // Only inspect the envelope prefix. Loading the full snapshot belongs to LoadReport.
        using var stream = File.OpenRead(snapshot);
        var bytes = new byte[16384];
        var count = stream.Read(bytes);
        var reader = new Utf8JsonReader(bytes.AsSpan(0, count), isFinalBlock: false, state: default);
        string? property = null;
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1)
                {
                    property = reader.GetString();
                    if (property == "Nodes") break;
                }
                else if (reader.TokenType == JsonTokenType.String && reader.CurrentDepth == 1)
                {
                    if (property == "RootPath") target = reader.GetString() ?? target;
                    if (property == "FinishedUtc" && reader.TryGetDateTime(out var date)) time = date;
                }
            }
        }
        catch (JsonException) { }
        return new(snapshot, target, time);
    }
}
