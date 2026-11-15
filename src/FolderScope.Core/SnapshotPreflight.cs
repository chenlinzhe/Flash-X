using System.Text.Json;

namespace FolderScope.Core;

// Bounded token buffer: validates envelope/counts without retaining a JSON DOM.
internal static class SnapshotPreflight
{
    public static void Validate(Stream stream, int maximumNodes, CancellationToken token)
    {
        var buffer = new byte[65536];
        // Utf8JsonReader expects the BOM to have been removed by the stream caller.
        var buffered = 0;
        while (buffered < 3)
        {
            token.ThrowIfCancellationRequested();
            var prefixRead = stream.Read(buffer, buffered, 3 - buffered);
            if (prefixRead == 0) break;
            buffered += prefixRead;
        }
        if (buffered == 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            buffered = 0;
        var state = new JsonReaderState();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? property = null;
        string? array = null;
        var nodes = 0;
        var issues = 0;
        var started = false;
        var ended = false;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, buffered, buffer.Length - buffered);
            var final = read == 0;
            buffered += read;
            var reader = new Utf8JsonReader(buffer.AsSpan(0, buffered), final, state);
            var ticks = 0;
            while (reader.Read())
            {
                if ((++ticks & 1023) == 0) token.ThrowIfCancellationRequested();
                if (!started)
                {
                    if (reader.TokenType != JsonTokenType.StartObject)
                        throw new InvalidDataException("Snapshot root must be an object.");
                    started = true;
                    continue;
                }
                if (reader.CurrentDepth == 0 && reader.TokenType == JsonTokenType.EndObject) ended = true;
                if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.PropertyName)
                {
                    property = reader.GetString();
                    if (property is "Schema" or "Nodes" or "Issues")
                        if (!seen.Add(property)) throw new InvalidDataException("Duplicate snapshot envelope property.");
                    continue;
                }
                if (property is not null && reader.CurrentDepth == 1)
                {
                    if (property == "Schema" && (reader.TokenType != JsonTokenType.String || reader.GetString() != "folderscope.scan.v1"))
                        throw new InvalidDataException("Unsupported scan schema; original FSP is not supported.");
                    if (property is "Nodes" or "Issues")
                    {
                        if (reader.TokenType != JsonTokenType.StartArray) throw new InvalidDataException("Snapshot collection must be an array.");
                        array = property;
                    }
                    property = null;
                }
                if (array is not null && reader.CurrentDepth == 2 && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.StartObject) throw new InvalidDataException("Snapshot collection entry must be an object.");
                    if (array == "Nodes" && ++nodes > maximumNodes) throw new InvalidDataException("Snapshot node limit exceeded.");
                    if (array == "Issues" && ++issues > 10000) throw new InvalidDataException("Snapshot error detail limit exceeded.");
                    // Validate tokens in a complete entry without running the envelope logic for each field.
                    // TrySkip leaves the reader in place when the entry spans buffers.
                    reader.TrySkip();
                }
                if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.EndArray) array = null;
            }
            var consumed = checked((int)reader.BytesConsumed);
            state = reader.CurrentState;
            buffer.AsSpan(consumed, buffered - consumed).CopyTo(buffer);
            buffered -= consumed;
            if (final)
            {
                if (!started || !ended || !seen.Contains("Schema") || !seen.Contains("Nodes"))
                    throw new InvalidDataException("Snapshot envelope is incomplete.");
                return;
            }
            if (buffered == buffer.Length)
            {
                if (buffer.Length == 1024 * 1024) throw new InvalidDataException("Snapshot JSON token exceeds 1 MiB.");
                Array.Resize(ref buffer, buffer.Length * 2);
            }
        }
    }
}
