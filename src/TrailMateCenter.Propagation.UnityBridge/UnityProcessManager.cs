using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TrailMateCenter.Services;

public sealed class UnityProcessManager : IPropagationUnityProcessManager, IDisposable
{
    private readonly ILogger<UnityProcessManager> _logger;
    private readonly UnityProcessOptions _options;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly object _syncRoot = new();
    private Process? _process;
    private PropagationUnityProcessState _processState = PropagationUnityProcessState.Stopped;
    private bool _disposed;

    public UnityProcessManager(ILogger<UnityProcessManager> logger)
    {
        _logger = logger;
        _options = UnityProcessOptions.FromEnvironment();

        if (_options.ManagedExternally)
        {
            SetProcessState(PropagationUnityProcessState.ExternalManaged, null, _options.ResolutionMessage);
            _logger.LogInformation("Unity process manager running in external mode. {Message}", _options.ResolutionMessage);
        }
        else
        {
            _logger.LogInformation(
                "Unity process manager configured. Executable={Executable}, Source={Source}",
                _options.ExecutablePath,
                _options.ExecutableSource);
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public int? ProcessId
    {
        get
        {
            lock (_syncRoot)
            {
                return _process is { HasExited: false } process ? process.Id : null;
            }
        }
    }

    public PropagationUnityProcessState ProcessState
    {
        get
        {
            lock (_syncRoot)
            {
                return _processState;
            }
        }
    }

    public event EventHandler<PropagationUnityProcessStateChangedEventArgs>? ProcessStateChanged;

    public async Task<PropagationUnityProcessSnapshot> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_options.ManagedExternally)
            {
                SetProcessState(PropagationUnityProcessState.ExternalManaged, null, _options.ResolutionMessage);
                return BuildSnapshot(_options.ResolutionMessage);
            }

            var adoptedProcess = await CleanupAndSelectUnityProcessAsync(cancellationToken);
            if (adoptedProcess != null)
            {
                lock (_syncRoot)
                {
                    _process = adoptedProcess;
                }

                ConfigureProcessTracking(adoptedProcess);
                SetProcessState(PropagationUnityProcessState.Running, adoptedProcess.Id, "Unity process already running.");
                _logger.LogInformation(
                    "Adopted already-running Unity process. PID={Pid}, Executable={Executable}",
                    adoptedProcess.Id,
                    _options.ExecutablePath);
                return BuildSnapshot("Unity process already running.");
            }

            if (_process is { HasExited: false } running)
            {
                SetProcessState(PropagationUnityProcessState.Running, running.Id, "Unity process already running.");
                return BuildSnapshot("Unity process already running.");
            }

            SetProcessState(PropagationUnityProcessState.Starting, null, "Starting Unity process.");
            var process = StartUnityProcessCore();

            lock (_syncRoot)
            {
                _process = process;
            }

            SetProcessState(PropagationUnityProcessState.Running, process.Id, "Unity process started.");
            _logger.LogInformation("Unity process started. PID={Pid}, Executable={Executable}", process.Id, _options.ExecutablePath);

            await Task.CompletedTask;
            return BuildSnapshot("Unity process started.");
        }
        catch (Exception ex)
        {
            SetProcessState(PropagationUnityProcessState.Faulted, null, $"Failed to start Unity process: {ex.Message}");
            _logger.LogError(ex, "Failed to start Unity process.");
            throw;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_options.ManagedExternally)
            {
                SetProcessState(PropagationUnityProcessState.ExternalManaged, null, _options.ResolutionMessage);
                return;
            }

            Process? process;
            lock (_syncRoot)
            {
                process = _process;
            }

            if (process is null || process.HasExited)
            {
                SetProcessState(PropagationUnityProcessState.Stopped, null, "Unity process already stopped.");
                lock (_syncRoot)
                {
                    _process = null;
                }
                return;
            }

            SetProcessState(PropagationUnityProcessState.Stopping, process.Id, "Stopping Unity process.");
            _logger.LogInformation("Stopping Unity process. PID={Pid}", process.Id);

            try
            {
                if (!process.CloseMainWindow())
                {
                    process.Kill(entireProcessTree: _options.KillEntireProcessTree);
                }
                else
                {
                    var exited = await Task.Run(() => process.WaitForExit(_options.StopTimeoutMs), cancellationToken);
                    if (!exited)
                    {
                        process.Kill(entireProcessTree: _options.KillEntireProcessTree);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // process already exited.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Graceful stop failed, force killing Unity process. PID={Pid}", process.Id);
                try
                {
                    process.Kill(entireProcessTree: _options.KillEntireProcessTree);
                }
                catch
                {
                    // ignore
                }
            }

            lock (_syncRoot)
            {
                _process = null;
            }
            SetProcessState(PropagationUnityProcessState.Stopped, null, "Unity process stopped.");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // ignore dispose failures
        }

        _disposed = true;
        _lifecycleLock.Dispose();
    }

    private Process StartUnityProcessCore()
    {
        if (string.IsNullOrWhiteSpace(_options.ExecutablePath))
            throw new InvalidOperationException(_options.ResolutionMessage);
        if (!File.Exists(_options.ExecutablePath))
            throw new FileNotFoundException("Unity executable path does not exist.", _options.ExecutablePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            Arguments = _options.Arguments,
            WorkingDirectory = ResolveWorkingDirectory(_options.ExecutablePath, _options.WorkingDirectory),
            UseShellExecute = false,
            CreateNoWindow = _options.CreateNoWindow,
            RedirectStandardOutput = _options.RedirectLogs,
            RedirectStandardError = _options.RedirectLogs,
        };

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        process.Exited += OnProcessExited;

        if (!process.Start())
            throw new InvalidOperationException("Process.Start returned false.");

        ConfigureProcessTracking(process);

        return process;
    }

    private void ConfigureProcessTracking(Process process)
    {
        process.EnableRaisingEvents = true;
        process.Exited -= OnProcessExited;
        process.Exited += OnProcessExited;

        if (!_options.RedirectLogs)
            return;

        try
        {
            if (process.StartInfo.RedirectStandardOutput)
            {
                process.OutputDataReceived -= OnProcessOutputDataReceived;
                process.OutputDataReceived += OnProcessOutputDataReceived;
                process.BeginOutputReadLine();
            }

            if (process.StartInfo.RedirectStandardError)
            {
                process.ErrorDataReceived -= OnProcessErrorDataReceived;
                process.ErrorDataReceived += OnProcessErrorDataReceived;
                process.BeginErrorReadLine();
            }
        }
        catch (InvalidOperationException)
        {
            // Existing externally discovered processes do not expose redirected streams.
        }
    }

    private async Task<Process?> CleanupAndSelectUnityProcessAsync(CancellationToken cancellationToken)
    {
        var matches = FindMatchingUnityProcesses();
        if (matches.Count == 0)
            return null;

        Process? keep = null;
        foreach (var candidate in matches.OrderBy(static process => process.StartTime))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (keep == null)
            {
                keep = candidate;
                continue;
            }

            await TerminateProcessAsync(candidate, cancellationToken);
        }

        return keep is { HasExited: false } ? keep : null;
    }

    private List<Process> FindMatchingUnityProcesses()
    {
        if (string.IsNullOrWhiteSpace(_options.ExecutablePath))
            return [];

        var processName = Path.GetFileNameWithoutExtension(_options.ExecutablePath);
        if (string.IsNullOrWhiteSpace(processName))
            return [];

        var matches = new List<Process>();

        foreach (var candidate in Process.GetProcessesByName(processName))
        {
            try
            {
                if (candidate.HasExited)
                {
                    candidate.Dispose();
                    continue;
                }

                var candidatePath = candidate.MainModule?.FileName;
                if (!string.Equals(
                        NormalizeExecutablePath(candidatePath),
                        _options.ExecutablePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    candidate.Dispose();
                    continue;
                }

                matches.Add(candidate);
            }
            catch
            {
                try { candidate.Dispose(); } catch { /* ignore */ }
            }
        }

        return matches;
    }

    private async Task TerminateProcessAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            if (process.HasExited)
                return;

            _logger.LogWarning(
                "Cleaning up leftover Unity process before startup. PID={Pid}, Executable={Executable}",
                process.Id,
                _options.ExecutablePath);

            if (process.CloseMainWindow())
            {
                var exited = await Task.Run(() => process.WaitForExit(_options.StopTimeoutMs), cancellationToken);
                if (exited)
                    return;
            }

            process.Kill(entireProcessTree: _options.KillEntireProcessTree);
            await Task.Run(() => process.WaitForExit(_options.StopTimeoutMs), cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // process already exited.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up leftover Unity process. PID={Pid}", process.Id);
        }
        finally
        {
            try { process.Dispose(); } catch { /* ignore */ }
        }
    }

    private static string NormalizeExecutablePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
        try
        {
            return Path.GetFullPath(expanded);
        }
        catch
        {
            return expanded;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process process)
            return;

        var exitCode = process.ExitCode;
        lock (_syncRoot)
        {
            if (_process == process)
            {
                _process = null;
            }
        }

        if (_disposed)
            return;

        if (exitCode == 0)
        {
            SetProcessState(PropagationUnityProcessState.Stopped, null, "Unity process exited normally.");
            _logger.LogInformation("Unity process exited normally. ExitCode={ExitCode}", exitCode);
        }
        else
        {
            SetProcessState(PropagationUnityProcessState.Faulted, null, $"Unity process exited with code {exitCode}.");
            _logger.LogWarning("Unity process exited unexpectedly. ExitCode={ExitCode}", exitCode);
        }
    }

    private void OnProcessOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data))
            return;
        _logger.LogDebug("[UnityProcess] {Line}", e.Data);
    }

    private void OnProcessErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data))
            return;
        _logger.LogWarning("[UnityProcess] {Line}", e.Data);
    }

    private static string ResolveWorkingDirectory(string executablePath, string configuredWorkingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredWorkingDirectory))
            return configuredWorkingDirectory;
        return Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory;
    }

    private PropagationUnityProcessSnapshot BuildSnapshot(string message)
    {
        return new PropagationUnityProcessSnapshot
        {
            ProcessState = ProcessState,
            ProcessId = ProcessId,
            ExecutablePath = _options.ExecutablePath,
            IsManagedExternally = _options.ManagedExternally,
            Message = message,
            TimestampUtc = DateTimeOffset.UtcNow,
        };
    }

    private void SetProcessState(PropagationUnityProcessState processState, int? processId, string message)
    {
        lock (_syncRoot)
        {
            _processState = processState;
        }

        ProcessStateChanged?.Invoke(this, new PropagationUnityProcessStateChangedEventArgs
        {
            ProcessState = processState,
            ProcessId = processId,
            Message = message,
            TimestampUtc = DateTimeOffset.UtcNow,
        });
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UnityProcessManager));
    }

    private sealed class UnityProcessOptions
    {
        public bool ManagedExternally => string.IsNullOrWhiteSpace(ExecutablePath);
        public string ExecutablePath { get; init; } = string.Empty;
        public string ExecutableSource { get; init; } = "external";
        public string ResolutionMessage { get; init; } = string.Empty;
        public string Arguments { get; init; } = string.Empty;
        public string WorkingDirectory { get; init; } = string.Empty;
        public bool CreateNoWindow { get; init; }
        public bool RedirectLogs { get; init; } = true;
        public bool KillEntireProcessTree { get; init; } = true;
        public int StopTimeoutMs { get; init; } = 2500;

        public static UnityProcessOptions FromEnvironment()
        {
            var configuredExecutablePath = NormalizePath(Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_EXECUTABLE"));
            var resolvedExecutablePath = configuredExecutablePath;
            var executableSource = "environment";
            string resolutionMessage;

            if (string.IsNullOrWhiteSpace(resolvedExecutablePath))
            {
                resolvedExecutablePath = TryDiscoverExecutablePath();
                if (string.IsNullOrWhiteSpace(resolvedExecutablePath))
                {
                    executableSource = "external";
                    resolutionMessage = "No Unity player executable was configured or auto-discovered. Build TrailMateCenter.Unity.exe under unity/TrailMateCenter.Unity/Builds/Windows64 or set TRAILMATE_PROPAGATION_UNITY_EXECUTABLE.";
                }
                else
                {
                    executableSource = "auto-discovered";
                    resolutionMessage = $"Unity player auto-discovered at {resolvedExecutablePath}.";
                }
            }
            else
            {
                resolutionMessage = $"Unity player configured from TRAILMATE_PROPAGATION_UNITY_EXECUTABLE: {resolvedExecutablePath}.";
            }

            return new UnityProcessOptions
            {
                ExecutablePath = resolvedExecutablePath,
                ExecutableSource = executableSource,
                ResolutionMessage = resolutionMessage,
                Arguments = Normalize(Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_ARGUMENTS")),
                WorkingDirectory = NormalizePath(Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_WORKDIR")),
                CreateNoWindow = ParseBoolean(Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_CREATE_NO_WINDOW"), defaultValue: false),
                RedirectLogs = ParseBoolean(Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_REDIRECT_LOGS"), defaultValue: true),
                KillEntireProcessTree = ParseBoolean(Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_KILL_TREE"), defaultValue: true),
                StopTimeoutMs = ParseInt(Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_UNITY_STOP_TIMEOUT_MS"), 2500),
            };
        }

        private static string Normalize(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizePath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
            try
            {
                return Path.GetFullPath(expanded);
            }
            catch
            {
                return expanded;
            }
        }

        private static string TryDiscoverExecutablePath()
        {
            var repoRoot = TryFindRepoRoot(AppContext.BaseDirectory);
            if (string.IsNullOrWhiteSpace(repoRoot))
                return string.Empty;

            foreach (var candidate in EnumerateLikelyExecutablePaths(repoRoot))
            {
                if (File.Exists(candidate))
                    return NormalizePath(candidate);
            }

            var unityRoot = Path.Combine(repoRoot, "unity");
            return TryFindExecutableRecursively(unityRoot, "TrailMateCenter.Unity.exe");
        }

        private static string TryFindRepoRoot(string startDirectory)
        {
            if (string.IsNullOrWhiteSpace(startDirectory))
                return string.Empty;

            var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "TrailMateCenter.sln")) ||
                    Directory.Exists(Path.Combine(current.FullName, "unity", "TrailMateCenter.Unity")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return string.Empty;
        }

        private static IEnumerable<string> EnumerateLikelyExecutablePaths(string repoRoot)
        {
            yield return Path.Combine(repoRoot, "unity", "TrailMateCenter.Unity", "Builds", "Windows64", "TrailMateCenter.Unity.exe");
            yield return Path.Combine(repoRoot, "unity", "TrailMateCenter.Unity", "Build", "Windows64", "TrailMateCenter.Unity.exe");
            yield return Path.Combine(repoRoot, "unity", "TrailMateCenter.Unity", "Builds", "Windows", "TrailMateCenter.Unity.exe");
            yield return Path.Combine(repoRoot, "unity", "TrailMateCenter.Unity", "Build", "Windows", "TrailMateCenter.Unity.exe");
            yield return Path.Combine(repoRoot, "unity", "TrailMateCenter.Unity", "Builds", "TrailMateCenter.Unity.exe");
            yield return Path.Combine(repoRoot, "unity", "TrailMateCenter.Unity", "Build", "TrailMateCenter.Unity.exe");
            yield return Path.Combine(repoRoot, "unity", "Builds", "Windows64", "TrailMateCenter.Unity.exe");
            yield return Path.Combine(repoRoot, "unity", "Build", "Windows64", "TrailMateCenter.Unity.exe");
            yield return Path.Combine(repoRoot, "Builds", "Windows64", "TrailMateCenter.Unity.exe");
            yield return Path.Combine(repoRoot, "Build", "Windows64", "TrailMateCenter.Unity.exe");
        }

        private static string TryFindExecutableRecursively(string rootDirectory, string fileName)
        {
            if (!Directory.Exists(rootDirectory))
                return string.Empty;

            var pending = new Queue<string>();
            pending.Enqueue(rootDirectory);

            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(current, fileName, SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                    return NormalizePath(file);

                IEnumerable<string> directories;
                try
                {
                    directories = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                foreach (var directory in directories)
                {
                    if (ShouldSkipSearchDirectory(directory))
                        continue;
                    pending.Enqueue(directory);
                }
            }

            return string.Empty;
        }

        private static bool ShouldSkipSearchDirectory(string path)
        {
            var name = Path.GetFileName(path);
            return name.Equals("Library", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Logs", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Temp", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Packages", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("ProjectSettings", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("UserSettings", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals(".git", StringComparison.OrdinalIgnoreCase);
        }

        private static int ParseInt(string? value, int defaultValue)
        {
            if (!int.TryParse(value, out var parsed))
                return defaultValue;
            return Math.Max(500, parsed);
        }

        private static bool ParseBoolean(string? value, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;
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
                _ => defaultValue,
            };
        }
    }
}
