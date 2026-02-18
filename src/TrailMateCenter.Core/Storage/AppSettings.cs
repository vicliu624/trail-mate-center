using TrailMateCenter.Models;

namespace TrailMateCenter.Storage;

public sealed record AppSettings
{
    public bool AutoReconnect { get; init; } = true;
    public bool AutoConnectOnDetect { get; init; }
    public int AckTimeoutMs { get; init; } = 1500;
    public int MaxRetries { get; init; } = 2;
    public string? LastPort { get; init; }
    public string? LastReplayFile { get; init; }
    public double ReplaySpeed { get; init; } = 1.0;
    public UiSettings Ui { get; init; } = new();
    public TacticalSettings Tactical { get; init; } = new();
    public AprsSettings Aprs { get; init; } = new();
    public MeshtasticMqttSettings Mqtt { get; init; } = MeshtasticMqttSettings.CreateDefault();
    public ContourSettings Contours { get; init; } = new();
}

public sealed record UiSettings
{
    public string Language { get; init; } = string.Empty;
    public string Theme { get; init; } = string.Empty;
    public bool OfflineMode { get; init; }
    public bool ShowMapLogs { get; init; }
    public bool ShowMapMqtt { get; init; } = true;
    public bool ShowMapGibs { get; init; }
    public string MapBaseLayer { get; init; } = "Osm";
}

public sealed record TacticalSettings
{
    public int OnlineThresholdSeconds { get; init; } = 30;
    public int WeakThresholdSeconds { get; init; } = 120;
    public byte CommandChannel { get; init; } = 0;
    public Dictionary<TacticalEventKind, TacticalSeverity> SeverityOverrides { get; init; } = new();
}

public sealed record AprsSettings
{
    public bool Enabled { get; init; }
    public string ServerHost { get; init; } = "rotate.aprs2.net";
    public int ServerPort { get; init; } = 14580;
    public string IgateCallsign { get; init; } = string.Empty;
    public byte IgateSsid { get; init; }
    public string Passcode { get; init; } = string.Empty;
    public string ToCall { get; init; } = "APRS";
    public string Path { get; init; } = string.Empty;
    public string Filter { get; init; } = string.Empty;
    public int TxMinIntervalSec { get; init; } = 30;
    public int DedupeWindowSec { get; init; } = 30;
    public int PositionIntervalSec { get; init; } = 60;
    public char SymbolTable { get; init; } = '/';
    public char SymbolCode { get; init; } = '>';
    public bool UseCompressed { get; init; } = true;
    public bool EmitStatus { get; init; } = true;
    public bool EmitTelemetry { get; init; } = true;
    public bool EmitWeather { get; init; } = true;
    public bool EmitMessages { get; init; } = true;
    public bool EmitWaypoints { get; init; } = true;
}

public sealed record MeshtasticMqttSettings
{
    public List<MeshtasticMqttSourceSettings> Sources { get; init; } = new();

    public static MeshtasticMqttSettings CreateDefault()
    {
        return new MeshtasticMqttSettings
        {
            Sources = new List<MeshtasticMqttSourceSettings>
            {
                MeshtasticMqttSourceSettings.CreateDefault(),
            },
        };
    }
}

public sealed record MeshtasticMqttSourceSettings
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; init; }
    public string Name { get; init; } = "Meshtastic CN";
    public string Host { get; init; } = "mqtt.mess.host";
    public int Port { get; init; } = 1883;
    public string Username { get; init; } = "meshdev";
    public string Password { get; init; } = "large4cats";
    public string Topic { get; init; } = "msh/CN/#";
    public bool UseTls { get; init; }
    public string ClientId { get; init; } = string.Empty;
    public bool CleanSession { get; init; }
    public int SubscribeQos { get; init; } = 1;

    public static MeshtasticMqttSourceSettings CreateDefault()
    {
        return new MeshtasticMqttSourceSettings();
    }
}

public sealed record ContourSettings
{
    public bool Enabled { get; init; } = true;
    public bool EnableUltraFine { get; init; }
    public EarthdataSettings Earthdata { get; init; } = new();
}

public sealed record EarthdataSettings
{
    public string Token { get; init; } = string.Empty;
}
