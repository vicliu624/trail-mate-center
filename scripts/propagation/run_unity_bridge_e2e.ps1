param(
    [string]$Configuration = "Debug",
    [string]$UnityExePath = "",
    [string]$UnityProjectPath = "unity/TrailMateCenter.Unity",
    [string]$LogDirectory = "artifacts/propagation-regression",
    [string]$BridgeMode = "namedpipe",
    [string]$PipeName = "TrailMateCenter.Propagation.Bridge",
    [int]$SmokeDurationSeconds = 60,
    [int]$ProbeTimeoutSeconds = 120,
    [int]$UnityStartDelaySeconds = 6
)

$ErrorActionPreference = "Stop"

function Resolve-UnityExe {
    param([string]$Explicit)

    if (-not [string]::IsNullOrWhiteSpace($Explicit) -and (Test-Path $Explicit)) {
        return (Resolve-Path $Explicit).Path
    }

    $envPath = [Environment]::GetEnvironmentVariable("TRAILMATE_UNITY_EDITOR")
    if (-not [string]::IsNullOrWhiteSpace($envPath) -and (Test-Path $envPath)) {
        return (Resolve-Path $envPath).Path
    }

    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor\2022.3.20f1\Editor\Unity.exe",
        "C:\Program Files\Unity\Editor\Unity.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Unity Editor not found. Set -UnityExePath or TRAILMATE_UNITY_EDITOR."
}

function Assert-LogContains {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Message
    )

    if (-not (Test-Path $Path)) {
        throw "Log file not found: $Path"
    }

    if (-not (Select-String -Path $Path -Pattern $Pattern -Quiet)) {
        throw $Message
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$unityProjectAbs = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $UnityProjectPath))
if (-not (Test-Path $unityProjectAbs)) {
    throw "Unity project path not found: $unityProjectAbs"
}

$unityExe = Resolve-UnityExe -Explicit $UnityExePath
$probeProject = Join-Path $repoRoot "tools/TrailMateCenter.Propagation.BridgeProbe/TrailMateCenter.Propagation.BridgeProbe.csproj"
if (-not (Test-Path $probeProject)) {
    throw "Bridge probe project not found: $probeProject"
}

$logRoot = Join-Path $repoRoot $LogDirectory
if (-not (Test-Path $logRoot)) {
    New-Item -ItemType Directory -Path $logRoot | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$unityLog = Join-Path $logRoot "unity_bridge_e2e_$timestamp.log"
$probeLog = Join-Path $logRoot "desktop_bridge_probe_$timestamp.log"

$unityProcess = $null
try {
    $env:TRAILMATE_UNITY_SMOKE_SECONDS = [Math]::Max(10, [Math]::Min(300, $SmokeDurationSeconds)).ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $env:TRAILMATE_PROPAGATION_UNITY_BRIDGE_MODE = $BridgeMode
    $env:TRAILMATE_PROPAGATION_UNITY_PIPE_NAME = $PipeName
    $env:TRAILMATE_BRIDGE_PROBE_TIMEOUT_SECONDS = [Math]::Max(30, [Math]::Min(600, $ProbeTimeoutSeconds)).ToString()
    $env:TRAILMATE_BRIDGE_PROBE_ATTACH_RETRIES = "40"

    Write-Host "[bridge-e2e] Starting Unity in batch play mode..."
    $unityProcess = Start-Process -FilePath $unityExe -ArgumentList @(
        "-batchmode",
        "-nographics",
        "-projectPath", $unityProjectAbs,
        "-executeMethod", "TrailMateCenter.Unity.EditorTools.PlayModeSmokeRunner.Run",
        "-logFile", $unityLog
    ) -WorkingDirectory $repoRoot -PassThru

    Start-Sleep -Seconds ([Math]::Max(2, [Math]::Min(30, $UnityStartDelaySeconds)))

    if ($unityProcess.HasExited) {
        throw "Unity exited before probe execution. Check log: $unityLog"
    }

    Write-Host "[bridge-e2e] Running desktop bridge probe..."
    $probeArgs = @(
        "run",
        "--project", $probeProject,
        "--configuration", $Configuration
    )

    $probeOutput = & dotnet @probeArgs 2>&1
    $probeOutput | Out-File -FilePath $probeLog -Encoding UTF8

    if ($LASTEXITCODE -ne 0) {
        throw "Bridge probe failed with exit code $LASTEXITCODE. Check log: $probeLog"
    }

    Assert-LogContains -Path $probeLog -Pattern "Ack push_request" -Message "Missing push_request ack in probe log."
    Assert-LogContains -Path $probeLog -Pattern "Ack push_result" -Message "Missing push_result ack in probe log."
    Assert-LogContains -Path $probeLog -Pattern "Ack set_active_layer" -Message "Missing set_active_layer ack in probe log."
    Assert-LogContains -Path $probeLog -Pattern "Ack set_camera_state" -Message "Missing set_camera_state ack in probe log."
    Assert-LogContains -Path $probeLog -Pattern "\[probe\] Summary" -Message "Missing probe summary output."

    Write-Host "[bridge-e2e] Probe checks passed."

    if (-not $unityProcess.WaitForExit(180000)) {
        throw "Unity did not exit after smoke duration. Check log: $unityLog"
    }

    if ($unityProcess.ExitCode -ne 0) {
        throw "Unity exited with code $($unityProcess.ExitCode). Check log: $unityLog"
    }

    if (-not (Test-Path $unityLog)) {
        throw "Unity log not found: $unityLog"
    }

    Assert-LogContains -Path $unityLog -Pattern "\[PlaySmoke\] Completed\. ExitCode=0" -Message "Unity play smoke did not complete successfully."

    Write-Host "[bridge-e2e] Success"
    Write-Host "[bridge-e2e] Unity log: $unityLog"
    Write-Host "[bridge-e2e] Probe log: $probeLog"
}
finally {
    if ($unityProcess -and -not $unityProcess.HasExited) {
        try {
            Stop-Process -Id $unityProcess.Id -Force
        } catch { }
    }
}
