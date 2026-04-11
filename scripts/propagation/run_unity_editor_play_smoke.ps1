param(
    [string]$UnityExePath = "",
    [string]$ProjectPath = "unity/TrailMateCenter.Unity",
    [double]$DurationSeconds = 8,
    [int]$TimeoutSeconds = 300,
    [string]$LogDirectory = "artifacts/propagation-regression"
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
        "C:\Program Files\Unity\Editor\Unity.exe",
        "D:\Program Files\Unity\Hub\Editor\2022.3.20f1\Editor\Unity.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Unity Editor not found. Set -UnityExePath or TRAILMATE_UNITY_EDITOR."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$projectAbs = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))
if (-not (Test-Path $projectAbs)) {
    throw "Unity project path not found: $projectAbs"
}

$unityExe = Resolve-UnityExe -Explicit $UnityExePath
$logRoot = Join-Path $repoRoot $LogDirectory
if (-not (Test-Path $logRoot)) {
    New-Item -ItemType Directory -Path $logRoot | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$unityLog = Join-Path $logRoot "unity_editor_play_smoke_$timestamp.log"

$env:TRAILMATE_UNITY_SMOKE_SECONDS = [Math]::Max(2, [Math]::Min(60, $DurationSeconds)).ToString([System.Globalization.CultureInfo]::InvariantCulture)

Write-Host "[play-smoke] Unity: $unityExe"
Write-Host "[play-smoke] Project: $projectAbs"
Write-Host "[play-smoke] Log: $unityLog"

$process = Start-Process -FilePath $unityExe -ArgumentList @(
    "-batchmode",
    "-nographics",
    "-projectPath", $projectAbs,
    "-executeMethod", "TrailMateCenter.Unity.EditorTools.PlayModeSmokeRunner.Run",
    "-logFile", $unityLog
) -WorkingDirectory $repoRoot -PassThru

if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    try { Stop-Process -Id $process.Id -Force } catch { }
    throw "Unity play smoke timed out after $TimeoutSeconds seconds. Check log: $unityLog"
}

$exitCode = $process.ExitCode
Write-Host "[play-smoke] ExitCode=$exitCode"

$effectiveLog = $unityLog
if (-not (Test-Path $effectiveLog)) {
    $fallbackLog = Join-Path $env:LOCALAPPDATA "Unity\Editor\Editor.log"
    if (Test-Path $fallbackLog) {
        $effectiveLog = $fallbackLog
        Write-Host "[play-smoke] Using fallback log: $effectiveLog"
    } else {
        throw "Unity log not found: $unityLog (fallback also missing: $fallbackLog)"
    }
}

$logText = Get-Content -Raw $effectiveLog
if ($logText -match "error CS[0-9]+") {
    Write-Host "[play-smoke] Found C# compile error(s) in Unity log."
}
if ($logText -match "No valid Unity Editor license found") {
    Write-Host "[play-smoke] Unity license is not activated. Activate license in Unity Hub first."
}

if ($exitCode -ne 0) {
    throw "Unity play smoke failed with exit code $exitCode. Check log: $effectiveLog"
}

Write-Host "[play-smoke] Success"
