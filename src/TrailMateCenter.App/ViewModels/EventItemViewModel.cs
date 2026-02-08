using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using TrailMateCenter.Localization;
using TrailMateCenter.Models;

namespace TrailMateCenter.ViewModels;

public sealed partial class EventItemViewModel : ObservableObject, ILocalizationAware
{
    private readonly HostLinkEvent _event;
    private readonly RxMetadata? _meta;

    public EventItemViewModel(HostLinkEvent ev)
    {
        _event = ev;
        var loc = LocalizationService.Instance;
        Timestamp = ev.Timestamp;
        Type = ev.GetType().Name;
        Detail = BuildDetail(ev);
        _meta = GetRxMeta(ev);
        if (_meta is not null)
        {
            RxMetaText = BuildRxMetaText(_meta);
            HasRxMeta = !string.IsNullOrWhiteSpace(RxMetaText);
            if (_meta.FromIs == true || _meta.Origin == RxOrigin.External)
            {
                HasFromIsTag = true;
                FromIsTagText = loc.GetString("Event.FromIsTag");
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
        var loc = LocalizationService.Instance;
        var parts = new List<string>();
        if (meta.TimestampUtc.HasValue) parts.Add(loc.Format("Event.Meta.Utc", meta.TimestampUtc.Value.ToString("HH:mm:ss")));
        if (meta.Direct.HasValue)
            parts.Add(meta.Direct.Value ? loc.GetString("Event.Meta.Direct") : loc.GetString("Event.Meta.Relayed"));
        if (meta.Origin != RxOrigin.Unknown)
            parts.Add(meta.Origin == RxOrigin.Mesh ? loc.GetString("Event.Meta.OriginRf") : loc.GetString("Event.Meta.OriginIs"));
        if (meta.FromIs.HasValue)
            parts.Add(meta.FromIs.Value ? loc.GetString("Event.Meta.FromIs") : loc.GetString("Event.Meta.FromRf"));
        if (meta.RssiDbm.HasValue) parts.Add(loc.Format("Event.Meta.Rssi", meta.RssiDbm));
        if (meta.SnrDb.HasValue) parts.Add(loc.Format("Event.Meta.Snr", meta.SnrDb));
        if (meta.HopCount.HasValue) parts.Add(loc.Format("Event.Meta.Hop", meta.HopCount));
        if (meta.PacketId.HasValue) parts.Add(loc.Format("Event.Meta.Packet", meta.PacketId));
        if (meta.FreqHz.HasValue) parts.Add(loc.Format("Event.Meta.Freq", meta.FreqHz));
        if (meta.Sf.HasValue) parts.Add(loc.Format("Event.Meta.Sf", meta.Sf));
        return parts.Count == 0 ? string.Empty : loc.Format("Event.RxMeta", string.Join(loc.GetString("Common.Separator"), parts));
    }

    public void RefreshLocalization()
    {
        var loc = LocalizationService.Instance;
        Detail = BuildDetail(_event);

        if (_meta is not null)
        {
            RxMetaText = BuildRxMetaText(_meta);
            HasRxMeta = !string.IsNullOrWhiteSpace(RxMetaText);
            if (_meta.FromIs == true || _meta.Origin == RxOrigin.External)
            {
                HasFromIsTag = true;
                FromIsTagText = loc.GetString("Event.FromIsTag");
            }
            else
            {
                HasFromIsTag = false;
                FromIsTagText = string.Empty;
            }
        }
        else
        {
            RxMetaText = string.Empty;
            HasRxMeta = false;
            HasFromIsTag = false;
            FromIsTagText = string.Empty;
        }
    }
}
