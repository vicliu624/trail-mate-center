using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Runtime.Versioning;

namespace TrailMateCenter.Transport;

public sealed class SerialPortEnumerator : ISerialPortEnumerator
{
    public Task<IReadOnlyList<SerialPortInfo>> GetPortsAsync(CancellationToken cancellationToken)
    {
        var names = SerialPort.GetPortNames()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (OperatingSystem.IsMacOS())
        {
            names = NormalizeMacPortNames(names);
        }

        var ports = names
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new SerialPortInfo { PortName = name })
            .DistinctBy(p => p.PortName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (OperatingSystem.IsWindows())
        {
            TryHydrateWindowsDetails(ports);
        }

        return Task.FromResult<IReadOnlyList<SerialPortInfo>>(ports);
    }

    private static List<string> NormalizeMacPortNames(List<string> names)
    {
        var all = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var filtered = new List<string>(all.Count);

        foreach (var name in all.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (IsMacSystemBluetoothPort(name))
                continue;
            if (HasCuTwinOnMac(name, all))
                continue;
            filtered.Add(name);
        }

        return filtered;
    }

    private static bool HasCuTwinOnMac(string portName, HashSet<string> allPorts)
    {
        const string ttyPrefix = "/dev/tty.";
        if (!portName.StartsWith(ttyPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = portName[ttyPrefix.Length..];
        var cuName = $"/dev/cu.{suffix}";
        return allPorts.Contains(cuName);
    }

    private static bool IsMacSystemBluetoothPort(string portName)
    {
        if (string.IsNullOrWhiteSpace(portName))
            return false;

        var name = portName.ToLowerInvariant();
        if (name.Contains("bluetooth", StringComparison.Ordinal))
            return true;

        // Common macOS short bluetooth aliases.
        return name.EndsWith(".blth", StringComparison.Ordinal);
    }

    [SupportedOSPlatform("windows")]
    private static void TryHydrateWindowsDetails(List<SerialPortInfo> ports)
    {
        try
        {
            var lookup = ports.ToDictionary(p => p.PortName, StringComparer.OrdinalIgnoreCase);
            TryHydrateFromSerialPort(lookup);
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            foreach (var device in searcher.Get())
            {
                var name = device["Name"]?.ToString() ?? string.Empty;
                var pnp = device["PNPDeviceID"]?.ToString() ?? string.Empty;
                var portName = ExtractPortName(name);
                if (portName is null || !lookup.TryGetValue(portName, out var info))
                    continue;

                lookup[portName] = info with
                {
                    Description = string.IsNullOrWhiteSpace(info.Description) ? name : info.Description,
                    FriendlyName = string.IsNullOrWhiteSpace(info.FriendlyName) ? name : info.FriendlyName,
                    PnpDeviceId = string.IsNullOrWhiteSpace(info.PnpDeviceId) ? pnp : info.PnpDeviceId,
                    VendorId = ExtractVidPid(pnp, "VID"),
                    ProductId = ExtractVidPid(pnp, "PID"),
                };
            }

            ports.Clear();
            ports.AddRange(lookup.Values.OrderBy(p => p.PortName, StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            // Best-effort only.
        }
    }

    [SupportedOSPlatform("windows")]
    private static void TryHydrateFromSerialPort(Dictionary<string, SerialPortInfo> lookup)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT DeviceID, Name, Description, Manufacturer, PNPDeviceID FROM Win32_SerialPort");
            foreach (var device in searcher.Get())
            {
                var deviceId = device["DeviceID"]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(deviceId) || !lookup.TryGetValue(deviceId, out var info))
                    continue;

                var name = device["Name"]?.ToString();
                var description = device["Description"]?.ToString();
                var manufacturer = device["Manufacturer"]?.ToString();
                var pnp = device["PNPDeviceID"]?.ToString() ?? string.Empty;

                lookup[deviceId] = info with
                {
                    FriendlyName = name ?? info.FriendlyName,
                    Description = description ?? info.Description,
                    Manufacturer = manufacturer ?? info.Manufacturer,
                    PnpDeviceId = string.IsNullOrWhiteSpace(info.PnpDeviceId) ? pnp : info.PnpDeviceId,
                    VendorId = string.IsNullOrWhiteSpace(info.VendorId) ? ExtractVidPid(pnp, "VID") : info.VendorId,
                    ProductId = string.IsNullOrWhiteSpace(info.ProductId) ? ExtractVidPid(pnp, "PID") : info.ProductId,
                };
            }
        }
        catch
        {
            // Best-effort only.
        }
    }

    private static string? ExtractPortName(string input)
    {
        var match = Regex.Match(input, @"\((COM\d+)\)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractVidPid(string input, string key)
    {
        var match = Regex.Match(input, $"{key}_([0-9A-Fa-f]{{4}})");
        return match.Success ? match.Groups[1].Value : null;
    }
}
