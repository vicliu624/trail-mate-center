namespace TrailMateCenter.ViewModels;

public sealed class ChannelOptionViewModel
{
    public ChannelOptionViewModel(byte id, string? label = null)
    {
        Id = id;
        Label = string.IsNullOrWhiteSpace(label) ? $"频道 {id}" : label;
    }

    public byte Id { get; }
    public string Label { get; }

    public override string ToString() => Label;
}
