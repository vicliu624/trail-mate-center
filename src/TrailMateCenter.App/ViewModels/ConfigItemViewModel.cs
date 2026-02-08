using CommunityToolkit.Mvvm.ComponentModel;
using TrailMateCenter.Protocol;

namespace TrailMateCenter.ViewModels;

public sealed partial class ConfigItemViewModel : ObservableObject
{
    public ConfigItemViewModel(HostLinkConfigKey key, string name, string value, bool isReadOnly, bool isDangerous)
    {
        Key = key;
        KeyName = name;
        Value = value;
        IsReadOnly = isReadOnly;
        IsDangerous = isDangerous;
    }

    [ObservableProperty]
    private HostLinkConfigKey _key;

    [ObservableProperty]
    private string _keyName = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private bool _isDangerous;
}
