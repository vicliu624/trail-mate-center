param(
    [string]$Configuration = "Debug",
    [switch]$UseUnityStub = $true,
    [string]$BridgeMode = "namedpipe",
    [string]$LogDirectory = "artifacts/propagation-regression"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$logRoot = Join-Path $repoRoot $LogDirectory
if (-not (Test-Path $logRoot)) {
    New-Item -ItemType Directory -Path $logRoot | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$stubOutLog = Join-Path $logRoot "unity_stub_$timestamp.out.log"
$stubErrLog = Join-Path $logRoot "unity_stub_$timestamp.err.log"
$appOutLog = Join-Path $logRoot "app_$timestamp.out.log"
$appErrLog = Join-Path $logRoot "app_$timestamp.err.log"

$unityStubProject = Join-Path $repoRoot "src/TrailMateCenter.Propagation.UnityStubServer/TrailMateCenter.Propagation.UnityStubServer.csproj"
$appProject = Join-Path $repoRoot "src/TrailMateCenter.App/TrailMateCenter.App.csproj"

$env:TRAILMATE_PROPAGATION_SIMULATION_USE_MOCK = "true"
$env:TRAILMATE_PROPAGATION_UNITY_BRIDGE_USE_MOCK = "false"
$env:TRAILMATE_PROPAGATION_UNITY_PROCESS_USE_MOCK = "true"
$env:TRAILMATE_PROPAGATION_UNITY_AUTO_ATTACH = "true"
$env:TRAILMATE_PROPAGATION_UNITY_BRIDGE_MODE = $BridgeMode
$env:TRAILMATE_PROPAGATION_UNITY_ATTACH_STRICT = "false"

$stubProcess = $null
$appProcess = $null

try {
    if ($UseUnityStub) {
        Write-Host "[e2e] Starting Unity stub server..."
        $stubProcess = Start-Process -FilePath "dotnet" -ArgumentList @(
            "run",
            "--project", $unityStubProject,
            "--configuration", $Configuration
        ) -WorkingDirectory $repoRoot -PassThru -RedirectStandardOutput $stubOutLog -RedirectStandardError $stubErrLog

        Start-Sleep -Seconds 2
        if ($stubProcess.HasExited) {
            throw "Unity stub server exited unexpectedly. Check: $stubOutLog / $stubErrLog"
        }
    }

    Write-Host "[e2e] Starting desktop app..."
    $appProcess = Start-Process -FilePath "dotnet" -ArgumentList @(
        "run",
        "--project", $appProject,
        "--configuration", $Configuration
    ) -WorkingDirectory $repoRoot -PassThru -RedirectStandardOutput $appOutLog -RedirectStandardError $appErrLog

    Write-Host "[e2e] App PID: $($appProcess.Id)"
    Write-Host "[e2e] App logs: $appOutLog / $appErrLog"
    if ($UseUnityStub) {
        Write-Host "[e2e] Unity stub logs: $stubOutLog / $stubErrLog"
    }

    Write-Host "[e2e] Use Propagation tab -> Attach Unity -> Run"
    Wait-Process -Id $appProcess.Id

    if ($appProcess.ExitCode -ne 0) {
        throw "Desktop app exited with code $($appProcess.ExitCode). See $appOutLog / $appErrLog"
    }

    Write-Host "[e2e] Completed."
}
finally {
    if ($stubProcess -and -not $stubProcess.HasExited) {
        Write-Host "[e2e] Stopping Unity stub..."
        Stop-Process -Id $stubProcess.Id -Force
    }
}
