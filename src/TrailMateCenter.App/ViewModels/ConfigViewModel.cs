using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Globalization;
using TrailMateCenter.Models;
using TrailMateCenter.Protocol;
using TrailMateCenter.Services;

namespace TrailMateCenter.ViewModels;

public sealed partial class ConfigViewModel : ObservableObject
{
    private readonly HostLinkClient _client;
    private readonly ILogger _logger;

    public ConfigViewModel(HostLinkClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
        LoadConfigCommand = new AsyncRelayCommand(LoadConfigAsync);
        SaveConfigCommand = new AsyncRelayCommand(SaveConfigAsync);
    }

    public ObservableCollection<ConfigItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private bool _isAdvancedMode;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public IAsyncRelayCommand LoadConfigCommand { get; }
    public IAsyncRelayCommand SaveConfigCommand { get; }

    public void ApplyCapabilities(Capabilities? caps)
    {
        IsReadOnly = !(caps?.SupportsConfig ?? false);
        RefreshItemReadOnly();
    }

    private async Task LoadConfigAsync()
    {
        StatusMessage = "读取配置中...";
        try
        {
            var config = await _client.GetConfigAsync(CancellationToken.None);
            Items.Clear();
            foreach (var (key, value) in config.Items)
            {
                Items.Add(new ConfigItemViewModel(
                    key,
                    GetKeyName(key),
                    FormatValue(value),
                    IsReadOnly,
                    IsDangerousKey(key)));
            }
            RefreshItemReadOnly();
            StatusMessage = $"已加载 {Items.Count} 项";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取配置失败");
            StatusMessage = $"读取失败: {ex.Message}";
        }
    }

    private async Task SaveConfigAsync()
    {
        if (IsReadOnly)
        {
            StatusMessage = "设备配置为只读";
            return;
        }

        StatusMessage = "写入配置中...";
        try
        {
            var config = new DeviceConfig();
            foreach (var item in Items)
            {
                if (!TryParseValue(item.Value, out var bytes))
                {
                    StatusMessage = $"配置值无效: {item.KeyName}";
                    return;
                }
                config.Items[item.Key] = bytes;
            }

            await _client.SetConfigAsync(config, CancellationToken.None);
            StatusMessage = "写入完成";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "写入配置失败");
            StatusMessage = $"写入失败: {ex.Message}";
        }
    }

    partial void OnIsAdvancedModeChanged(bool value)
    {
        RefreshItemReadOnly();
    }

    private void RefreshItemReadOnly()
    {
        foreach (var item in Items)
        {
            var shouldReadOnly = IsReadOnly || (item.IsDangerous && !IsAdvancedMode);
            item.IsReadOnly = shouldReadOnly;
        }
    }

    private static bool IsDangerousKey(HostLinkConfigKey key)
    {
        return key is HostLinkConfigKey.MeshProtocol or HostLinkConfigKey.Region or HostLinkConfigKey.Channel or HostLinkConfigKey.DutyCycle;
    }

    private static string GetKeyName(HostLinkConfigKey key)
    {
        return key switch
        {
            HostLinkConfigKey.MeshProtocol => "MeshProtocol",
            HostLinkConfigKey.Region => "Region",
            HostLinkConfigKey.Channel => "Channel",
            HostLinkConfigKey.DutyCycle => "DutyCycle",
            HostLinkConfigKey.ChannelUtil => "ChannelUtil",
            HostLinkConfigKey.AprsEnable => "AprsEnable",
            HostLinkConfigKey.AprsIgateCallsign => "AprsIgateCallsign",
            HostLinkConfigKey.AprsIgateSsid => "AprsIgateSsid",
            HostLinkConfigKey.AprsToCall => "AprsToCall",
            HostLinkConfigKey.AprsPath => "AprsPath",
            HostLinkConfigKey.AprsTxMinIntervalSec => "AprsTxMinIntervalSec",
            HostLinkConfigKey.AprsDedupeWindowSec => "AprsDedupeWindowSec",
            HostLinkConfigKey.AprsSymbolTable => "AprsSymbolTable",
            HostLinkConfigKey.AprsSymbolCode => "AprsSymbolCode",
            HostLinkConfigKey.AprsPositionIntervalSec => "AprsPositionIntervalSec",
            HostLinkConfigKey.AprsNodeIdMap => "AprsNodeIdMap",
            _ => key.ToString(),
        };
    }

    private static string FormatValue(byte[] value)
    {
        if (value.Length == 1)
            return value[0].ToString(CultureInfo.InvariantCulture);
        return $"0x{Convert.ToHexString(value)}";
    }

    private static bool TryParseValue(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value))
            return false;
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hex = value[2..];
            if (hex.Length % 2 != 0)
                return false;
            try
            {
                bytes = Convert.FromHexString(hex);
                return true;
            }
            catch
            {
                return false;
            }
        }
        if (byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
        {
            bytes = new[] { b };
            return true;
        }
        return false;
    }
}
