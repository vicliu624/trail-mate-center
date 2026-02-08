using CommunityToolkit.Mvvm.ComponentModel;
using TrailMateCenter.Models;

namespace TrailMateCenter.ViewModels;

public sealed partial class ConversationItemViewModel : ObservableObject
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
        if (IsBroadcast)
        {
            return "广播";
        }

        if (!string.IsNullOrWhiteSpace(PeerLabel))
        {
            return $"与 {PeerLabel}";
        }

        if (PeerId.HasValue)
        {
            return $"与 0x{PeerId.Value:X8}";
        }

        return "私聊";
    }

    private string BuildSubtitle()
    {
        if (IsBroadcast)
        {
            return $"频道 {ChannelId}";
        }

        var idText = PeerId.HasValue ? $"0x{PeerId.Value:X8}" : "未知";
        return $"{idText} · 频道 {ChannelId}";
    }
}
