using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace TrailMateCenter.Views.Controls;

public sealed class UnityViewportHost : NativeControlHost
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipSiblings = 0x04000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int SsBlackRect = 0x0004;
    private const int GwlStyle = -16;
    private const int SwShow = 5;
    private static readonly IntPtr HwndMessage = new(-3);

    private DispatcherTimer? _attachTimer;
    private IntPtr _hostHwnd;
    private IntPtr _unityHwnd;
    private IntPtr _unityOriginalStyle;
    private bool _unityStyleCaptured;

    public static readonly StyledProperty<string> UnityWindowTitleProperty =
        AvaloniaProperty.Register<UnityViewportHost, string>(nameof(UnityWindowTitle), string.Empty);

    public static readonly StyledProperty<string> UnityWindowClassProperty =
        AvaloniaProperty.Register<UnityViewportHost, string>(nameof(UnityWindowClass), string.Empty);

    public static readonly StyledProperty<string> AllowedProcessNameProperty =
        AvaloniaProperty.Register<UnityViewportHost, string>(nameof(AllowedProcessName), string.Empty);

    public static readonly StyledProperty<int> AttachPollIntervalMsProperty =
        AvaloniaProperty.Register<UnityViewportHost, int>(nameof(AttachPollIntervalMs), 500);

    public string UnityWindowTitle
    {
        get => GetValue(UnityWindowTitleProperty);
        set => SetValue(UnityWindowTitleProperty, value);
    }

    public string UnityWindowClass
    {
        get => GetValue(UnityWindowClassProperty);
        set => SetValue(UnityWindowClassProperty, value);
    }

    public string AllowedProcessName
    {
        get => GetValue(AllowedProcessNameProperty);
        set => SetValue(AllowedProcessNameProperty, value);
    }

    public int AttachPollIntervalMs
    {
        get => GetValue(AttachPollIntervalMsProperty);
        set => SetValue(AttachPollIntervalMsProperty, value);
    }

    public bool IsUnityWindowAttached => _unityHwnd != IntPtr.Zero && IsWindow(_unityHwnd);

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
        {
            return base.CreateNativeControlCore(parent);
        }

        _hostHwnd = CreateWindowEx(
            0,
            "STATIC",
            string.Empty,
            WsChild | WsVisible | WsClipChildren | WsClipSiblings | SsBlackRect,
            0,
            0,
            100,
            100,
            parent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_hostHwnd == IntPtr.Zero)
        {
            return base.CreateNativeControlCore(parent);
        }

        StartAttachPolling();
        return new PlatformHandle(_hostHwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        StopAttachPolling();
        DetachUnityWindow();

        if (OperatingSystem.IsWindows() && _hostHwnd != IntPtr.Zero)
        {
            DestroyWindow(_hostHwnd);
            _hostHwnd = IntPtr.Zero;
        }

        base.DestroyNativeControlCore(control);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        ResizeUnityWindow(finalSize);
        return base.ArrangeOverride(finalSize);
    }

    private void StartAttachPolling()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var intervalMs = Math.Max(250, AttachPollIntervalMs);
        _attachTimer ??= new DispatcherTimer();
        _attachTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        _attachTimer.Tick -= OnAttachPollTick;
        _attachTimer.Tick += OnAttachPollTick;
        _attachTimer.Start();
    }

    private void StopAttachPolling()
    {
        if (_attachTimer is null)
            return;

        _attachTimer.Tick -= OnAttachPollTick;
        _attachTimer.Stop();
    }

    private void OnAttachPollTick(object? sender, EventArgs e)
    {
        if (!OperatingSystem.IsWindows() || _hostHwnd == IntPtr.Zero)
            return;

        if (_unityHwnd != IntPtr.Zero && !IsWindow(_unityHwnd))
        {
            _unityHwnd = IntPtr.Zero;
        }

        if (_unityHwnd == IntPtr.Zero)
        {
            TryAttachUnityWindow();
        }
        else
        {
            ResizeUnityWindow(Bounds.Size);
        }
    }

    private void TryAttachUnityWindow()
    {
        var unityHwnd = ResolveUnityWindowHandle();
        if (unityHwnd == IntPtr.Zero || unityHwnd == _hostHwnd)
            return;
        if (!IsAllowedUnityWindow(unityHwnd))
            return;

        var stylePtr = GetWindowLongPtr(unityHwnd, GwlStyle);
        if (!_unityStyleCaptured)
        {
            _unityOriginalStyle = stylePtr;
            _unityStyleCaptured = true;
        }

        var style = stylePtr.ToInt64();
        style |= WsChild;
        style &= ~WsPopup;
        SetWindowLongPtr(unityHwnd, GwlStyle, new IntPtr(style));
        SetParent(unityHwnd, _hostHwnd);
        ShowWindow(unityHwnd, SwShow);
        _unityHwnd = unityHwnd;

        ResizeUnityWindow(Bounds.Size);
    }

    private IntPtr ResolveUnityWindowHandle()
    {
        var hwndRaw = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_HWND");
        if (TryParseHandle(hwndRaw, out var explicitHwnd) && IsWindow(explicitHwnd))
        {
            return explicitHwnd;
        }

        var className = ResolveValue(UnityWindowClass, "TRAILMATE_PROPAGATION_UNITY_WINDOW_CLASS");
        var title = ResolveValue(UnityWindowTitle, "TRAILMATE_PROPAGATION_UNITY_WINDOW_TITLE");
        if (string.IsNullOrWhiteSpace(className) && string.IsNullOrWhiteSpace(title))
        {
            return IntPtr.Zero;
        }

        var classArg = string.IsNullOrWhiteSpace(className) ? null : className;
        var titleArg = string.IsNullOrWhiteSpace(title) ? null : title;
        var hwnd = FindWindow(classArg, titleArg);
        if (hwnd != IntPtr.Zero && hwnd != HwndMessage)
            return hwnd;

        return IntPtr.Zero;
    }

    private bool IsAllowedUnityWindow(IntPtr hwnd)
    {
        if (!IsStrictAttachEnabled())
            return true;

        var pid = GetWindowProcessId(hwnd);
        if (pid <= 0)
            return false;

        var allowedPidRaw = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_ALLOWED_PID");
        if (int.TryParse(allowedPidRaw, out var allowedPid))
            return pid == allowedPid;

        var allowedProcess = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_ALLOWED_PROCESS");
        if (string.IsNullOrWhiteSpace(allowedProcess))
        {
            allowedProcess = AllowedProcessName;
        }
        var allowedExe = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_EXECUTABLE");

        try
        {
            using var process = Process.GetProcessById(pid);
            var processName = process.ProcessName;
            var processPath = process.MainModule?.FileName ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(allowedProcess))
            {
                var target = NormalizeProcessName(allowedProcess);
                return string.Equals(processName, target, StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrWhiteSpace(allowedExe))
            {
                var target = NormalizePath(allowedExe);
                var actual = NormalizePath(processPath);
                return !string.IsNullOrWhiteSpace(actual) &&
                       string.Equals(actual, target, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool IsStrictAttachEnabled()
    {
        var value = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_ATTACH_STRICT");
        if (string.IsNullOrWhiteSpace(value))
            return true;
        if (bool.TryParse(value, out var parsed))
            return parsed;

        return value.Trim() switch
        {
            "1" => true,
            "yes" => true,
            "on" => true,
            "0" => false,
            "no" => false,
            "off" => false,
            _ => true,
        };
    }

    private static int GetWindowProcessId(IntPtr hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        return unchecked((int)pid);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }

    private static string NormalizeProcessName(string process)
    {
        if (string.IsNullOrWhiteSpace(process))
            return string.Empty;

        var trimmed = process.Trim();
        if (trimmed.Contains('\\') || trimmed.Contains('/') || trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(trimmed);

        return trimmed;
    }

    private static string ResolveValue(string propertyValue, string environmentVariable)
    {
        var env = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        return string.IsNullOrWhiteSpace(propertyValue) ? string.Empty : propertyValue.Trim();
    }

    private static bool TryParseHandle(string? value, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var raw = value.Trim();
        NumberStyles style = NumberStyles.Integer;
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[2..];
            style = NumberStyles.AllowHexSpecifier;
        }

        if (!long.TryParse(raw, style, CultureInfo.InvariantCulture, out var parsed))
            return false;

        handle = new IntPtr(parsed);
        return handle != IntPtr.Zero;
    }

    private void ResizeUnityWindow(Size size)
    {
        if (!OperatingSystem.IsWindows() || _unityHwnd == IntPtr.Zero || !IsWindow(_unityHwnd))
            return;

        var width = Math.Max(1, (int)Math.Round(size.Width));
        var height = Math.Max(1, (int)Math.Round(size.Height));
        MoveWindow(_unityHwnd, 0, 0, width, height, true);
    }

    private void DetachUnityWindow()
    {
        if (!OperatingSystem.IsWindows() || _unityHwnd == IntPtr.Zero)
            return;

        if (IsWindow(_unityHwnd))
        {
            if (_unityStyleCaptured)
            {
                SetWindowLongPtr(_unityHwnd, GwlStyle, _unityOriginalStyle);
            }
            SetParent(_unityHwnd, IntPtr.Zero);
        }

        _unityHwnd = IntPtr.Zero;
        _unityStyleCaptured = false;
        _unityOriginalStyle = IntPtr.Zero;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int exStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        IntPtr hwndParent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int cmdShow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hwnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hwnd, int nIndex, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int nIndex, IntPtr value);

    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hwnd, nIndex)
            : new IntPtr(GetWindowLong32(hwnd, nIndex));
    }

    private static IntPtr SetWindowLongPtr(IntPtr hwnd, int nIndex, IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, nIndex, value)
            : new IntPtr(SetWindowLong32(hwnd, nIndex, value.ToInt32()));
    }
}
