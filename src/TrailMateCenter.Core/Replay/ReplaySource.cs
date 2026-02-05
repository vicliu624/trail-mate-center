using System.Text.Json;

namespace TrailMateCenter.Replay;

public sealed class ReplaySource
{
    public async IAsyncEnumerable<ReplayRecord> ReadAsync(string filePath, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            ReplayRecord? record = null;
            try
            {
                record = JsonSerializer.Deserialize<ReplayRecord>(line);
            }
            catch
            {
                // ignore invalid lines
            }

            if (record is not null)
                yield return record;
        }
    }
}
