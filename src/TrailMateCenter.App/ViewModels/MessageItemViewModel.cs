using Avalonia;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using TrailMateCenter.Models;

namespace TrailMateCenter.ViewModels;

public sealed partial class MessageItemViewModel : ObservableObject
{
    public MessageItemViewModel(MessageEntry entry)
    {
        UpdateFrom(entry);
    }

    [ObservableProperty]
    private DateTimeOffset _timestamp;

    [ObservableProperty]
    private DateTimeOffset? _deviceTimestamp;

    [ObservableProperty]
    private MessageDirection _direction;

    [ObservableProperty]
    private string _from = string.Empty;

    [ObservableProperty]
    private string _to = string.Empty;

    [ObservableProperty]
    private string _channel = string.Empty;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private uint? _fromId;

    [ObservableProperty]
    private uint? _toId;

    [ObservableProperty]
    private byte _channelId;

    [ObservableProperty]
    private bool _isBroadcast;

    [ObservableProperty]
    private MessageDeliveryStatus _status;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int? _rssi;

    [ObservableProperty]
    private int? _snr;

    [ObservableProperty]
    private int? _hop;

    [ObservableProperty]
    private bool? _direct;

    [ObservableProperty]
    private RxOrigin? _origin;

    [ObservableProperty]
    private bool? _fromIs;

    [ObservableProperty]
    private int? _retry;

    [ObservableProperty]
    private int? _airtimeMs;

    [ObservableProperty]
    private ushort _seq;

    [ObservableProperty]
    private uint? _messageId;

    [ObservableProperty]
    private string _metadata = string.Empty;

    [ObservableProperty]
    private string _titleText = string.Empty;

    [ObservableProperty]
    private string _timeText = string.Empty;

    [ObservableProperty]
    private string _deviceTimeText = string.Empty;

    [ObservableProperty]
    private bool _hasDeviceTime;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _titleColor = "#CFE3FF";

    [ObservableProperty]
    private string _textColor = "#E6EDF3";

    [ObservableProperty]
    private string _bubbleBackground = "#1A1E24";

    [ObservableProperty]
    private string _bubbleBorder = "#2E3238";

    [ObservableProperty]
    private HorizontalAlignment _bubbleAlignment = HorizontalAlignment.Left;

    [ObservableProperty]
    private Thickness _bubbleMargin = new(6, 6, 6, 6);

    [ObservableProperty]
    private string _statusColor = "#9AA3AE";

    [ObservableProperty]
    private string _channelTag = string.Empty;

    [ObservableProperty]
    private string _messageIdText = string.Empty;

    [ObservableProperty]
    private bool _hasMessageId;

    [ObservableProperty]
    private string _rssiText = string.Empty;

    [ObservableProperty]
    private bool _hasRssi;

    [ObservableProperty]
    private string _snrText = string.Empty;

    [ObservableProperty]
    private bool _hasSnr;

    [ObservableProperty]
    private string _hopText = string.Empty;

    [ObservableProperty]
    private bool _hasHop;

    [ObservableProperty]
    private string _directText = string.Empty;

    [ObservableProperty]
    private bool _hasDirect;

    [ObservableProperty]
    private string _directColor = "#22C55E";

    [ObservableProperty]
    private string _originText = string.Empty;

    [ObservableProperty]
    private bool _hasOrigin;

    [ObservableProperty]
    private string _originColor = "#93C5FD";

    [ObservableProperty]
    private string _fromIsText = string.Empty;

    [ObservableProperty]
    private bool _hasFromIs;

    [ObservableProperty]
    private string _fromIsColor = "#F59E0B";

    [ObservableProperty]
    private string _retryText = string.Empty;

    [ObservableProperty]
    private bool _hasRetry;

    [ObservableProperty]
    private string _airtimeText = string.Empty;

    [ObservableProperty]
    private bool _hasAirtime;

    partial void OnFromChanged(string value)
    {
        TitleText = BuildTitle();
    }

    partial void OnToChanged(string value)
    {
        TitleText = BuildTitle();
    }

    public void UpdateFrom(MessageEntry entry)
    {
        Timestamp = entry.Timestamp;
        DeviceTimestamp = entry.DeviceTimestamp;
        Direction = entry.Direction;
        From = entry.From;
        To = entry.To;
        Channel = entry.Channel;
        FromId = entry.FromId;
        ToId = entry.ToId;
        ChannelId = entry.ChannelId;
        IsBroadcast = entry.ToId is null || entry.ToId == 0;
        Text = entry.Text;
        Status = entry.Status;
        ErrorMessage = entry.ErrorMessage;
        Rssi = entry.Rssi;
        Snr = entry.Snr;
        Hop = entry.Hop;
        Direct = entry.Direct;
        Origin = entry.Origin;
        FromIs = entry.FromIs;
        Retry = entry.Retry;
        AirtimeMs = entry.AirtimeMs;
        Seq = entry.Seq;
        MessageId = entry.MessageId;
        Metadata = BuildMetadata();
        TitleText = BuildTitle();
        TimeText = Timestamp.ToLocalTime().ToString("HH:mm:ss");
        HasDeviceTime = DeviceTimestamp.HasValue;
        DeviceTimeText = HasDeviceTime
            ? $"设备 {DeviceTimestamp.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            : string.Empty;
        StatusText = BuildStatus();
        var outgoing = Direction == MessageDirection.Outgoing;
        BubbleBackground = outgoing ? "#1C2A2F" : "#1A1E24";
        BubbleBorder = outgoing ? "#2B4D3B" : "#2E3238";
        BubbleAlignment = outgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        BubbleMargin = outgoing ? new Thickness(60, 6, 6, 6) : new Thickness(6, 6, 60, 6);
        TitleColor = Direction switch
        {
            MessageDirection.Outgoing => "#A7F3D0",
            MessageDirection.Incoming => "#93C5FD",
            MessageDirection.System => "#FDE68A",
            _ => "#CFE3FF",
        };
        StatusColor = Status switch
        {
            MessageDeliveryStatus.Succeeded => "#22C55E",
            MessageDeliveryStatus.Acked => "#10B981",
            MessageDeliveryStatus.Pending => "#F59E0B",
            MessageDeliveryStatus.Timeout => "#EF4444",
            MessageDeliveryStatus.Failed => "#EF4444",
            _ => "#9AA3AE",
        };

        ChannelTag = $"CH {ChannelId}";

        HasMessageId = MessageId.HasValue;
        MessageIdText = HasMessageId ? $"MSG {MessageId}" : string.Empty;

        HasRssi = Rssi.HasValue;
        RssiText = HasRssi ? $"RSSI {Rssi}" : string.Empty;
        HasSnr = Snr.HasValue;
        SnrText = HasSnr ? $"SNR {Snr}" : string.Empty;
        HasHop = Hop.HasValue;
        HopText = HasHop ? $"HOP {Hop}" : string.Empty;

        HasDirect = Direct.HasValue;
        DirectText = Direct.HasValue ? (Direct.Value ? "Direct" : "Relayed") : string.Empty;
        DirectColor = Direct is true ? "#22C55E" : "#F97316";

        HasOrigin = Origin.HasValue && Origin.Value != RxOrigin.Unknown;
        OriginText = Origin switch
        {
            RxOrigin.Mesh => "Origin RF",
            RxOrigin.External => "Origin IS",
            _ => string.Empty,
        };
        OriginColor = Origin switch
        {
            RxOrigin.Mesh => "#60A5FA",
            RxOrigin.External => "#F59E0B",
            _ => "#93C5FD",
        };

        HasFromIs = FromIs.HasValue;
        FromIsText = FromIs.HasValue ? (FromIs.Value ? "From IS" : "From RF") : string.Empty;
        FromIsColor = FromIs is true ? "#F59E0B" : "#10B981";
        HasRetry = Retry.HasValue;
        RetryText = HasRetry ? $"RETRY {Retry}" : string.Empty;
        HasAirtime = AirtimeMs.HasValue;
        AirtimeText = HasAirtime ? $"AIR {AirtimeMs}ms" : string.Empty;
    }

    private string BuildTitle()
    {
        return Direction == MessageDirection.Outgoing ? $"我 → {To}" : From;
    }

    private string BuildStatus()
    {
        return Status switch
        {
            MessageDeliveryStatus.Pending => "发送中",
            MessageDeliveryStatus.Acked => "已接收",
            MessageDeliveryStatus.Succeeded => "成功",
            MessageDeliveryStatus.Timeout => "超时",
            MessageDeliveryStatus.Failed => "失败",
            _ => Status.ToString(),
        };
    }

    private string BuildMetadata()
    {
        var parts = new List<string>();
        if (MessageId.HasValue) parts.Add($"Msg {MessageId}");
        if (Rssi.HasValue) parts.Add($"RSSI {Rssi}");
        if (Snr.HasValue) parts.Add($"SNR {Snr}");
        if (Hop.HasValue) parts.Add($"Hop {Hop}");
        if (Retry.HasValue) parts.Add($"Retry {Retry}");
        if (AirtimeMs.HasValue) parts.Add($"Airtime {AirtimeMs}ms");
        return string.Join(" | ", parts);
    }
}
