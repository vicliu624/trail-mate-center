using System.Text;
using System.Text.Json;
using TrailMateCenter.Models;
using TrailMateCenter.Storage;

namespace TrailMateCenter.Services;

public enum ExportFormat
{
    Jsonl,
    Csv,
}

public sealed class ExportService
{
    public async Task ExportMessagesAsync(
        SessionStore store,
        string filePath,
        ExportFormat format,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var messages = store.SnapshotMessages()
            .Where(m => (from is null || m.Timestamp >= from) && (to is null || m.Timestamp <= to))
            .OrderBy(m => m.Timestamp)
            .ToList();

        switch (format)
        {
            case ExportFormat.Jsonl:
                await WriteJsonlAsync(filePath, messages, cancellationToken);
                break;
            case ExportFormat.Csv:
                await WriteMessagesCsvAsync(filePath, messages, cancellationToken);
                break;
        }
    }

    public async Task ExportEventsAsync(
        SessionStore store,
        string filePath,
        ExportFormat format,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var eventsList = store.SnapshotEvents()
            .Where(e => (from is null || e.Timestamp >= from) && (to is null || e.Timestamp <= to))
            .OrderBy(e => e.Timestamp)
            .ToList();

        switch (format)
        {
            case ExportFormat.Jsonl:
                await WriteJsonlAsync(filePath, eventsList, cancellationToken);
                break;
            case ExportFormat.Csv:
                await WriteEventsCsvAsync(filePath, eventsList, cancellationToken);
                break;
        }
    }

    private static async Task WriteJsonlAsync<T>(string filePath, IEnumerable<T> items, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(filePath);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        foreach (var item in items)
        {
            var json = JsonSerializer.Serialize(item);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        }
    }

    private static async Task WriteMessagesCsvAsync(string filePath, IReadOnlyList<MessageEntry> messages, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Direction,From,To,Channel,Text,Status,Rssi,Snr,Hop,Retry,AirtimeMs,Seq,Error");
        foreach (var m in messages)
        {
            sb.AppendLine(string.Join(',',
                Escape(m.Timestamp.ToString("O")),
                Escape(m.Direction.ToString()),
                Escape(m.From),
                Escape(m.To),
                Escape(m.Channel),
                Escape(m.Text),
                Escape(m.Status.ToString()),
                Escape(m.Rssi?.ToString() ?? string.Empty),
                Escape(m.Snr?.ToString() ?? string.Empty),
                Escape(m.Hop?.ToString() ?? string.Empty),
                Escape(m.Retry?.ToString() ?? string.Empty),
                Escape(m.AirtimeMs?.ToString() ?? string.Empty),
                Escape(m.Seq.ToString()),
                Escape(m.ErrorMessage ?? string.Empty)));
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), cancellationToken);
    }

    private static async Task WriteEventsCsvAsync(string filePath, IReadOnlyList<HostLinkEvent> eventsList, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Type,Details");
        foreach (var ev in eventsList)
        {
            sb.AppendLine(string.Join(',',
                Escape(ev.Timestamp.ToString("O")),
                Escape(ev.GetType().Name),
                Escape(JsonSerializer.Serialize(ev))));
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), cancellationToken);
    }

    private static string Escape(string value)
    {
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
