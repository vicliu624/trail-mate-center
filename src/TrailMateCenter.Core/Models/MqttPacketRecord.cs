namespace TrailMateCenter.Models;

public sealed record MqttPacketRecord(
    DateTimeOffset Timestamp,
    string SourceId,
    string SourceName,
    string Topic,
    int Qos,
    bool Retain,
    byte[] Payload,
    string PayloadSha256);
