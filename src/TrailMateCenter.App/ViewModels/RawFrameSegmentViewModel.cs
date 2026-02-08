namespace TrailMateCenter.ViewModels;

public sealed class RawFrameSegmentViewModel
{
    public RawFrameSegmentViewModel(string label, string hex, string color)
    {
        Label = label;
        Hex = hex;
        Color = color;
    }

    public string Label { get; }
    public string Hex { get; }
    public string Color { get; }
    public string Display => $"{Label}: {Hex}";
}
