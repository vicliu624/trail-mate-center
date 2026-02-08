using TrailMateCenter.Models;
using TrailMateCenter.Protocol;

namespace TrailMateCenter.ViewModels;

public sealed class RawFrameViewModel
{
    public RawFrameViewModel(RawFrameRecord record)
    {
        Timestamp = record.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
        Direction = record.Direction == RawFrameDirection.Rx ? "RX" : "TX";
        DirectionColor = record.Direction == RawFrameDirection.Rx ? "#60A5FA" : "#34D399";
        Status = record.Status.ToString();
        StatusColor = record.Status == RawFrameStatus.Ok ? "#22C55E" : "#EF4444";
        Note = record.Note ?? string.Empty;

        var segments = HostLinkFrameInspector.ParseSegments(record.Frame);
        Segments = segments.Select(segment =>
            new RawFrameSegmentViewModel(
                segment.Label,
                ToHex(segment.Bytes),
                MapColor(segment.Kind)))
            .ToList();
    }

    public string Timestamp { get; }
    public string Direction { get; }
    public string DirectionColor { get; }
    public string Status { get; }
    public string StatusColor { get; }
    public string Note { get; }
    public IReadOnlyList<RawFrameSegmentViewModel> Segments { get; }

    private static string ToHex(byte[] bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;
        return string.Join(" ", bytes.Select(b => b.ToString("X2")));
    }

    private static string MapColor(RawFrameSegmentKind kind)
    {
        return kind switch
        {
            RawFrameSegmentKind.Sof => "#22D3EE",
            RawFrameSegmentKind.Version => "#93C5FD",
            RawFrameSegmentKind.Type => "#F472B6",
            RawFrameSegmentKind.Seq => "#FBBF24",
            RawFrameSegmentKind.Length => "#A78BFA",
            RawFrameSegmentKind.Payload => "#E5E7EB",
            RawFrameSegmentKind.Crc => "#F87171",
            RawFrameSegmentKind.Extra => "#9CA3AF",
            _ => "#9CA3AF",
        };
    }
}
