using CommunityToolkit.Mvvm.ComponentModel;
using TrailMateCenter.Storage;

namespace TrailMateCenter.ViewModels;

public sealed partial class MqttSourceViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _name = "Meshtastic CN";

    [ObservableProperty]
    private string _host = "mqtt.mess.host";

    [ObservableProperty]
    private int _port = 1883;

    [ObservableProperty]
    private string _username = "meshdev";

    [ObservableProperty]
    private string _password = "large4cats";

    [ObservableProperty]
    private string _topic = "msh/CN/#";

    [ObservableProperty]
    private bool _useTls;

    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private bool _cleanSession;

    [ObservableProperty]
    private int _subscribeQos = 1;

    public MeshtasticMqttSourceSettings ToSettings()
    {
        var normalizedId = string.IsNullOrWhiteSpace(Id)
            ? Guid.NewGuid().ToString("N")
            : Id.Trim();
        var defaultPort = UseTls ? 8883 : 1883;

        return new MeshtasticMqttSourceSettings
        {
            Id = normalizedId,
            Enabled = Enabled,
            Name = string.IsNullOrWhiteSpace(Name) ? "Meshtastic MQTT" : Name.Trim(),
            Host = Host?.Trim() ?? string.Empty,
            Port = Port is > 0 and <= 65535 ? Port : defaultPort,
            Username = Username?.Trim() ?? string.Empty,
            Password = Password ?? string.Empty,
            Topic = Topic?.Trim() ?? string.Empty,
            UseTls = UseTls,
            ClientId = ClientId?.Trim() ?? string.Empty,
            CleanSession = CleanSession,
            SubscribeQos = Math.Clamp(SubscribeQos, 0, 2),
        };
    }

    public static MqttSourceViewModel CreateDefault()
    {
        return FromSettings(MeshtasticMqttSourceSettings.CreateDefault());
    }

    public static MqttSourceViewModel FromSettings(MeshtasticMqttSourceSettings settings)
    {
        return new MqttSourceViewModel
        {
            Id = string.IsNullOrWhiteSpace(settings.Id) ? Guid.NewGuid().ToString("N") : settings.Id.Trim(),
            Enabled = settings.Enabled,
            Name = settings.Name ?? string.Empty,
            Host = settings.Host ?? string.Empty,
            Port = settings.Port,
            Username = settings.Username ?? string.Empty,
            Password = settings.Password ?? string.Empty,
            Topic = settings.Topic ?? string.Empty,
            UseTls = settings.UseTls,
            ClientId = settings.ClientId ?? string.Empty,
            CleanSession = settings.CleanSession,
            SubscribeQos = Math.Clamp(settings.SubscribeQos, 0, 2),
        };
    }
}
