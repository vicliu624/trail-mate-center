using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TrailMateCenter.Services;

public sealed class PropagationServiceProcessManager : IDisposable
{
    private readonly ILogger<PropagationServiceProcessManager> _logger;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private Process? _process;
    private bool _disposed;

    public PropagationServiceProcessManager(ILogger<PropagationServiceProcessManager> logger)
    {
        _logger = logger;
    }

    public async Task<PropagationServiceStartResult> EnsureServiceReadyAsync(string endpoint, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return new PropagationServiceStartResult(false, "invalid-endpoint", $"Invalid propagation service endpoint: {endpoint}");
        }

        if (await IsEndpointReachableAsync(uri, cancellationToken))
            return new PropagationServiceStartResult(true, "already-running", "Propagation service endpoint is already reachable.");

        if (!IsManagedLocalEndpoint(uri))
        {
            return new PropagationServiceStartResult(
                false,
                "remote-endpoint-unreachable",
                $"Propagation service endpoint {endpoint} is unreachable and is not a managed local endpoint.");
        }

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (await IsEndpointReachableAsync(uri, cancellationToken))
                return new PropagationServiceStartResult(true, "already-running", "Propagation service endpoint became reachable.");

            if (_process is { HasExited: false })
            {
                var healthy = await WaitForEndpointAsync(uri, TimeSpan.FromSeconds(4), cancellationToken);
                return healthy
                    ? new PropagationServiceStartResult(true, "adopted-managed-process", "Managed propagation service process is running.")
                    : new PropagationServiceStartResult(false, "managed-process-unhealthy", "Managed propagation service process is running but endpoint is still unavailable.");
            }

            var launchSpec = ResolveLaunchSpec();
            if (launchSpec is null)
            {
                return new PropagationServiceStartResult(
                    false,
                    "service-binary-missing",
                    "Propagation service executable was not found. Build TrailMateCenter.Propagation.Service first.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = launchSpec.FileName,
                Arguments = launchSpec.Arguments,
                WorkingDirectory = launchSpec.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };
            process.Exited += OnProcessExited;

            if (!process.Start())
            {
                return new PropagationServiceStartResult(false, "service-start-failed", "Process.Start returned false when launching the propagation service.");
            }

            _process = process;
            _logger.LogInformation(
                "Started propagation service. PID={Pid}, FileName={FileName}, Arguments={Arguments}",
                process.Id,
                launchSpec.FileName,
                launchSpec.Arguments);

            var ready = await WaitForEndpointAsync(uri, TimeSpan.FromSeconds(6), cancellationToken);
            return ready
                ? new PropagationServiceStartResult(true, "service-started", $"Propagation service started at {endpoint}.")
                : new PropagationServiceStartResult(false, "service-start-timeout", $"Propagation service process started but {endpoint} did not become reachable in time.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure propagation service readiness.");
            return new PropagationServiceStartResult(false, "service-start-exception", ex.Message);
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

        _process?.Dispose();
        _lifecycleLock.Dispose();
        _disposed = true;
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process process)
            return;

        _logger.LogInformation("Propagation service exited. PID={Pid}, ExitCode={ExitCode}", process.Id, process.ExitCode);
        if (ReferenceEquals(_process, process))
            _process = null;
    }

    private static bool IsManagedLocalEndpoint(Uri uri)
    {
        var host = uri.Host;
        return uri.Scheme is "http" or "https"
            && (string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<bool> WaitForEndpointAsync(Uri uri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await IsEndpointReachableAsync(uri, cancellationToken))
                return true;

            await Task.Delay(250, cancellationToken);
        }

        return await IsEndpointReachableAsync(uri, cancellationToken);
    }

    private static async Task<bool> IsEndpointReachableAsync(Uri uri, CancellationToken cancellationToken)
    {
        var port = uri.Port > 0 ? uri.Port : uri.Scheme == "https" ? 443 : 80;
        using var client = new System.Net.Sockets.TcpClient();
        var connectTask = client.ConnectAsync(uri.Host, port);
        var completed = await Task.WhenAny(connectTask, Task.Delay(500, cancellationToken));

        if (completed != connectTask)
        {
            _ = connectTask.ContinueWith(
                static task => _ = task.Exception,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
            return false;
        }

        try
        {
            await connectTask;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static PropagationServiceLaunchSpec? ResolveLaunchSpec()
    {
        var explicitPath = Environment.GetEnvironmentVariable("TRAILMATE_PROPAGATION_SERVICE_EXE");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var spec = CreateLaunchSpec(explicitPath.Trim());
            if (spec is not null)
                return spec;
        }

        var baseDirectory = AppContext.BaseDirectory;
        foreach (var candidate in EnumerateCandidatePaths(baseDirectory))
        {
            var spec = CreateLaunchSpec(candidate);
            if (spec is not null)
                return spec;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string baseDirectory)
    {
        yield return Path.Combine(baseDirectory, "TrailMateCenter.Propagation.Service.exe");
        yield return Path.Combine(baseDirectory, "TrailMateCenter.Propagation.Service.dll");

        foreach (var root in EnumerateSearchRoots(baseDirectory))
        {
            yield return Path.Combine(root, "src", "TrailMateCenter.Propagation.Service", "bin", "Debug", "net8.0", "TrailMateCenter.Propagation.Service.exe");
            yield return Path.Combine(root, "src", "TrailMateCenter.Propagation.Service", "bin", "Debug", "net8.0", "TrailMateCenter.Propagation.Service.dll");
            yield return Path.Combine(root, "src", "TrailMateCenter.Propagation.Service", "bin", "Release", "net8.0", "TrailMateCenter.Propagation.Service.exe");
            yield return Path.Combine(root, "src", "TrailMateCenter.Propagation.Service", "bin", "Release", "net8.0", "TrailMateCenter.Propagation.Service.dll");
        }
    }

    private static IEnumerable<string> EnumerateSearchRoots(string startDirectory)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (seen.Add(current.FullName))
                yield return current.FullName;

            current = current.Parent;
        }
    }

    private static PropagationServiceLaunchSpec? CreateLaunchSpec(string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
            return null;

        var fullPath = Path.GetFullPath(candidatePath);
        if (!File.Exists(fullPath))
            return null;

        var extension = Path.GetExtension(fullPath);
        var workingDirectory = Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory;

        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return new PropagationServiceLaunchSpec(
                "dotnet",
                $"\"{fullPath}\"",
                workingDirectory);
        }

        return new PropagationServiceLaunchSpec(
            fullPath,
            string.Empty,
            workingDirectory);
    }
}

public sealed record PropagationServiceStartResult(
    bool IsReady,
    string StatusCode,
    string Detail);

public sealed record PropagationServiceLaunchSpec(
    string FileName,
    string Arguments,
    string WorkingDirectory);
