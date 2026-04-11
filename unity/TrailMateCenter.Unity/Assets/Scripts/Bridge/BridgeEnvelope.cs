using Newtonsoft.Json.Linq;
namespace TrailMateCenter.Unity.Bridge
{
public sealed class BridgeEnvelope
{
    public string Type { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string TimestampUtc { get; set; } = string.Empty;
    public JObject Payload { get; set; } = new();

    public static BridgeEnvelope FromJson(string json)
    {
        var root = JObject.Parse(json);
        var payload = root["payload"] as JObject ?? root;
        return new BridgeEnvelope
        {
            Type = root.Value<string>("type") ?? string.Empty,
            CorrelationId = root.Value<string>("correlationId") ?? root.Value<string>("correlation_id") ?? string.Empty,
            RunId = root.Value<string>("runId") ?? root.Value<string>("run_id") ?? string.Empty,
            TimestampUtc = root.Value<string>("timestampUtc") ?? root.Value<string>("timestamp_utc") ?? string.Empty,
            Payload = payload,
        };
    }
}
}

