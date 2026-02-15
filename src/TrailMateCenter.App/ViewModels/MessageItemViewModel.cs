using Avalonia;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using TrailMateCenter.Localization;
using TrailMateCenter.Models;

namespace TrailMateCenter.ViewModels;

public sealed partial class MessageItemViewModel : ObservableObject, ILocalizationAware
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
        var loc = LocalizationService.Instance;
        Timestamp = entry.Timestamp;
        DeviceTimestamp = entry.DeviceTimestamp;
        Direction = entry.Direction;
        From = entry.From;
        To = entry.To;
        Channel = entry.Channel;
        FromId = entry.FromId;
        ToId = entry.ToId;
        ChannelId = entry.ChannelId;
        IsBroadcast = IsBroadcastDestination(entry.ToId);
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
            ? loc.Format("Status.Message.DeviceTime", DeviceTimestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))
            : string.Empty;
        StatusText = BuildStatus();
        var outgoing = Direction == MessageDirection.Outgoing;
        BubbleBackground = outgoing ? "#17241E" : "#141A20";
        BubbleBorder = outgoing ? "#2E4A3A" : "#28323C";
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

        ChannelTag = loc.Format("Status.Message.ChannelTag", ChannelId);

        HasMessageId = MessageId.HasValue;
        MessageIdText = HasMessageId ? loc.Format("Status.Message.MessageIdTag", MessageId) : string.Empty;

        HasRssi = Rssi.HasValue;
        RssiText = HasRssi ? loc.Format("Status.Message.RssiTag", Rssi) : string.Empty;
        HasSnr = Snr.HasValue;
        SnrText = HasSnr ? loc.Format("Status.Message.SnrTag", Snr) : string.Empty;
        HasHop = Hop.HasValue;
        HopText = HasHop ? loc.Format("Status.Message.HopTag", Hop) : string.Empty;

        HasDirect = Direct.HasValue;
        DirectText = Direct.HasValue
            ? (Direct.Value ? loc.GetString("Status.Message.Direct") : loc.GetString("Status.Message.Relayed"))
            : string.Empty;
        DirectColor = Direct is true ? "#22C55E" : "#F97316";

        HasOrigin = Origin.HasValue && Origin.Value != RxOrigin.Unknown;
        OriginText = Origin switch
        {
            RxOrigin.Mesh => loc.GetString("Status.Message.OriginRf"),
            RxOrigin.External => loc.GetString("Status.Message.OriginIs"),
            _ => string.Empty,
        };
        OriginColor = Origin switch
        {
            RxOrigin.Mesh => "#60A5FA",
            RxOrigin.External => "#F59E0B",
            _ => "#93C5FD",
        };

        HasFromIs = FromIs.HasValue;
        FromIsText = FromIs.HasValue
            ? (FromIs.Value ? loc.GetString("Status.Message.FromIs") : loc.GetString("Status.Message.FromRf"))
            : string.Empty;
        FromIsColor = FromIs is true ? "#F59E0B" : "#10B981";
        HasRetry = Retry.HasValue;
        RetryText = HasRetry ? loc.Format("Status.Message.RetryTag", Retry) : string.Empty;
        HasAirtime = AirtimeMs.HasValue;
        AirtimeText = HasAirtime ? loc.Format("Status.Message.AirtimeTag", AirtimeMs) : string.Empty;
    }

    private static bool IsBroadcastDestination(uint? toId)
    {
        return !toId.HasValue || toId.Value == 0 || toId.Value == uint.MaxValue;
    }

    private string BuildTitle()
    {
        return Direction == MessageDirection.Outgoing
            ? LocalizationService.Instance.Format("Status.Message.MeTo", To)
            : From;
    }

    private string BuildStatus()
    {
        return Status switch
        {
            MessageDeliveryStatus.Pending => LocalizationService.Instance.GetString("Status.Message.Pending"),
            MessageDeliveryStatus.Acked => LocalizationService.Instance.GetString("Status.Message.Acked"),
            MessageDeliveryStatus.Succeeded => LocalizationService.Instance.GetString("Status.Message.Succeeded"),
            MessageDeliveryStatus.Timeout => LocalizationService.Instance.GetString("Status.Message.Timeout"),
            MessageDeliveryStatus.Failed => LocalizationService.Instance.GetString("Status.Message.Failed"),
            _ => Status.ToString(),
        };
    }

    private string BuildMetadata()
    {
        var loc = LocalizationService.Instance;
        var parts = new List<string>();
        if (MessageId.HasValue) parts.Add(loc.Format("Status.Message.Meta.Msg", MessageId));
        if (Rssi.HasValue) parts.Add(loc.Format("Status.Message.Meta.Rssi", Rssi));
        if (Snr.HasValue) parts.Add(loc.Format("Status.Message.Meta.Snr", Snr));
        if (Hop.HasValue) parts.Add(loc.Format("Status.Message.Meta.Hop", Hop));
        if (Retry.HasValue) parts.Add(loc.Format("Status.Message.Meta.Retry", Retry));
        if (AirtimeMs.HasValue) parts.Add(loc.Format("Status.Message.Meta.Airtime", AirtimeMs));
        return string.Join(loc.GetString("Common.Separator"), parts);
    }

    public void RefreshLocalization()
    {
        var loc = LocalizationService.Instance;
        Metadata = BuildMetadata();
        TitleText = BuildTitle();
        StatusText = BuildStatus();
        HasDeviceTime = DeviceTimestamp.HasValue;
        DeviceTimeText = HasDeviceTime
            ? loc.Format("Status.Message.DeviceTime", DeviceTimestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))
            : string.Empty;

        ChannelTag = loc.Format("Status.Message.ChannelTag", ChannelId);
        MessageIdText = HasMessageId ? loc.Format("Status.Message.MessageIdTag", MessageId) : string.Empty;
        RssiText = HasRssi ? loc.Format("Status.Message.RssiTag", Rssi) : string.Empty;
        SnrText = HasSnr ? loc.Format("Status.Message.SnrTag", Snr) : string.Empty;
        HopText = HasHop ? loc.Format("Status.Message.HopTag", Hop) : string.Empty;
        DirectText = HasDirect
            ? (Direct == true ? loc.GetString("Status.Message.Direct") : loc.GetString("Status.Message.Relayed"))
            : string.Empty;
        OriginText = Origin switch
        {
            RxOrigin.Mesh => loc.GetString("Status.Message.OriginRf"),
            RxOrigin.External => loc.GetString("Status.Message.OriginIs"),
            _ => string.Empty,
        };
        FromIsText = HasFromIs
            ? (FromIs == true ? loc.GetString("Status.Message.FromIs") : loc.GetString("Status.Message.FromRf"))
            : string.Empty;
        RetryText = HasRetry ? loc.Format("Status.Message.RetryTag", Retry) : string.Empty;
        AirtimeText = HasAirtime ? loc.Format("Status.Message.AirtimeTag", AirtimeMs) : string.Empty;
    }
}
