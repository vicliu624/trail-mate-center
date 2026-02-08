using CommunityToolkit.Mvvm.ComponentModel;
using TrailMateCenter.Localization;
using TrailMateCenter.Models;

namespace TrailMateCenter.ViewModels;

public sealed partial class ConversationItemViewModel : ObservableObject, ILocalizationAware
{
    public ConversationItemViewModel(string key, bool isBroadcast, uint? peerId, byte channelId)
    {
        Key = key;
        IsBroadcast = isBroadcast;
        PeerId = peerId;
        ChannelId = channelId;
        PeerLabel = peerId.HasValue ? $"0x{peerId.Value:X8}" : string.Empty;
    }

    public string Key { get; }
    public bool IsBroadcast { get; }
    public uint? PeerId { get; }
    public byte ChannelId { get; }
    public string PeerLabel { get; private set; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _preview = string.Empty;

    [ObservableProperty]
    private string _timeText = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private DateTimeOffset _lastTimestamp;

    [ObservableProperty]
    private int _unreadCount;

    public bool HasUnread => UnreadCount > 0;

    public void UpdateFrom(MessageEntry entry, string? peerLabel = null)
    {
        if (!string.IsNullOrWhiteSpace(peerLabel))
            PeerLabel = peerLabel;
        LastTimestamp = entry.Timestamp;
        Preview = entry.Text;
        TimeText = entry.Timestamp.ToLocalTime().ToString("HH:mm");
        Title = BuildTitle();
        Subtitle = BuildSubtitle();
    }

    public void UpdatePeerLabel(string? peerLabel)
    {
        if (!string.IsNullOrWhiteSpace(peerLabel))
            PeerLabel = peerLabel;
        Title = BuildTitle();
        Subtitle = BuildSubtitle();
    }

    partial void OnUnreadCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnread));
    }

    private string BuildTitle()
    {
        var loc = LocalizationService.Instance;
        if (IsBroadcast)
        {
            return loc.GetString("Chat.Broadcast");
        }

        if (!string.IsNullOrWhiteSpace(PeerLabel))
        {
            return loc.Format("Chat.WithPeer", PeerLabel);
        }

        if (PeerId.HasValue)
        {
            return loc.Format("Chat.WithPeer", $"0x{PeerId.Value:X8}");
        }

        return loc.GetString("Chat.Private");
    }

    private string BuildSubtitle()
    {
        var loc = LocalizationService.Instance;
        if (IsBroadcast)
        {
            return loc.Format("Common.ChannelFormat", ChannelId);
        }

        var idText = PeerId.HasValue ? $"0x{PeerId.Value:X8}" : loc.GetString("Common.Unknown");
        return loc.Format("Chat.ConversationSubtitle", idText, ChannelId);
    }

    public void RefreshLocalization()
    {
        Title = BuildTitle();
        Subtitle = BuildSubtitle();
    }
}
