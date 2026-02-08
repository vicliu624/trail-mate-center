using CommunityToolkit.Mvvm.ComponentModel;
using TrailMateCenter.Models;

namespace TrailMateCenter.ViewModels;

public sealed partial class SeverityRuleViewModel : ObservableObject
{
    public SeverityRuleViewModel(TacticalEventKind kind, TacticalSeverity severity)
    {
        Kind = kind;
        Severity = severity;
        Options = Enum.GetValues<TacticalSeverity>();
    }

    public TacticalEventKind Kind { get; }
    public TacticalSeverity[] Options { get; }

    [ObservableProperty]
    private TacticalSeverity _severity;
}
