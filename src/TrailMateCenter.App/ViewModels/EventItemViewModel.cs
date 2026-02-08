using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using TrailMateCenter.Models;

namespace TrailMateCenter.ViewModels;

public sealed partial class EventItemViewModel : ObservableObject
{
    public EventItemViewModel(HostLinkEvent ev)
    {
        Timestamp = ev.Timestamp;
        Type = ev.GetType().Name;
        Detail = BuildDetail(ev);
        var meta = GetRxMeta(ev);
        if (meta is not null)
        {
            RxMetaText = BuildRxMetaText(meta);
            HasRxMeta = !string.IsNullOrWhiteSpace(RxMetaText);
            if (meta.FromIs == true || meta.Origin == RxOrigin.External)
            {
                HasFromIsTag = true;
                FromIsTagText = "From IS";
                FromIsTagColor = "#EF4444";
            }
        }
    }

    [ObservableProperty]
    private DateTimeOffset _timestamp;

    [ObservableProperty]
    private string _type = string.Empty;

    [ObservableProperty]
    private string _detail = string.Empty;

    [ObservableProperty]
    private string _rxMetaText = string.Empty;

    [ObservableProperty]
    private bool _hasRxMeta;

    [ObservableProperty]
    private bool _hasFromIsTag;

    [ObservableProperty]
    private string _fromIsTagText = string.Empty;

    [ObservableProperty]
    private string _fromIsTagColor = "#EF4444";

    public bool IsFromIs => HasFromIsTag;

    private static string BuildDetail(HostLinkEvent ev)
    {
        if (ev is AppDataEvent app)
        {
            return $"port={app.Portnum} from=0x{app.From:X8} to=0x{app.To:X8} ch={app.Channel} len={app.TotalLength} off={app.Offset} chunk={app.ChunkLength}";
        }
        if (ev is RxMessageEvent rx)
        {
            return $"from=0x{rx.From:X8} to=0x{rx.To:X8} ch={rx.Channel} msg={rx.MessageId} text={rx.Text}";
        }
        return ev.ToString() ?? string.Empty;
    }

    private static RxMetadata? GetRxMeta(HostLinkEvent ev)
    {
        return ev switch
        {
            AppDataEvent app => app.RxMeta,
            RxMessageEvent rx => rx.RxMeta,
            _ => null,
        };
    }

    private static string BuildRxMetaText(RxMetadata meta)
    {
        var parts = new List<string>();
        if (meta.TimestampUtc.HasValue) parts.Add($"UTC {meta.TimestampUtc:HH:mm:ss}");
        if (meta.Direct.HasValue) parts.Add(meta.Direct.Value ? "Direct" : "Relayed");
        if (meta.Origin != RxOrigin.Unknown) parts.Add(meta.Origin == RxOrigin.Mesh ? "Origin RF" : "Origin IS");
        if (meta.FromIs.HasValue) parts.Add(meta.FromIs.Value ? "From IS" : "From RF");
        if (meta.RssiDbm.HasValue) parts.Add($"RSSI {meta.RssiDbm}");
        if (meta.SnrDb.HasValue) parts.Add($"SNR {meta.SnrDb}");
        if (meta.HopCount.HasValue) parts.Add($"Hop {meta.HopCount}");
        if (meta.PacketId.HasValue) parts.Add($"Pkt {meta.PacketId}");
        if (meta.FreqHz.HasValue) parts.Add($"Freq {meta.FreqHz}");
        if (meta.Sf.HasValue) parts.Add($"SF {meta.Sf}");
        return parts.Count == 0 ? string.Empty : $"RX Meta: {string.Join(" | ", parts)}";
    }
}
