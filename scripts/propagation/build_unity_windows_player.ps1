param(
    [string]$UnityExePath = "",
    [string]$ProjectPath = "unity/TrailMateCenter.Unity",
    [string]$OutputPath = "",
    [switch]$Development,
    [switch]$Clean,
    [int]$TimeoutSeconds = 3600,
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

function Stop-RunningPlayer {
    param([string]$PlayerExePath)

    $processName = [System.IO.Path]::GetFileNameWithoutExtension($PlayerExePath)
    if ([string]::IsNullOrWhiteSpace($processName)) {
        return
    }

    $running = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) {
        return
    }

    $ids = ($running | Select-Object -ExpandProperty Id) -join ', '
    Write-Host "[unity-build] Stopping running player process: $processName ($ids)"
    $running | Stop-Process -Force
    Start-Sleep -Seconds 1
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$projectAbs = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))
if (-not (Test-Path $projectAbs)) {
    throw "Unity project path not found: $projectAbs"
}

$unityExe = Resolve-UnityExe -Explicit $UnityExePath
$defaultOutput = Join-Path $projectAbs "Builds/Windows64/TrailMateCenter.Unity.exe"
$outputAbs = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $defaultOutput
} else {
    $candidate = [Environment]::ExpandEnvironmentVariables($OutputPath.Trim())
    if ([System.IO.Path]::IsPathRooted($candidate)) {
        [System.IO.Path]::GetFullPath($candidate)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $repoRoot $candidate))
    }
}

$logRoot = Join-Path $repoRoot $LogDirectory
if (-not (Test-Path $logRoot)) {
    New-Item -ItemType Directory -Path $logRoot | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$unityLog = Join-Path $logRoot "unity_windows_player_build_$timestamp.log"
$executeMethod = if ($Development) {
    "TrailMateCenter.Unity.EditorTools.WindowsPlayerBuilder.BuildDevelopment"
} else {
    "TrailMateCenter.Unity.EditorTools.WindowsPlayerBuilder.Build"
}

$env:TRAILMATE_UNITY_PLAYER_OUTPUT = $outputAbs
$env:TRAILMATE_UNITY_PLAYER_CLEAN = $Clean.IsPresent.ToString()
if ($Development) {
    $env:TRAILMATE_UNITY_PLAYER_DEVELOPMENT = "true"
} else {
    Remove-Item Env:TRAILMATE_UNITY_PLAYER_DEVELOPMENT -ErrorAction SilentlyContinue
}

Write-Host "[unity-build] Unity: $unityExe"
Write-Host "[unity-build] Project: $projectAbs"
Write-Host "[unity-build] Output: $outputAbs"
Write-Host "[unity-build] Development: $($Development.IsPresent)"
Write-Host "[unity-build] Clean: $($Clean.IsPresent)"
Write-Host "[unity-build] Log: $unityLog"

Stop-RunningPlayer -PlayerExePath $outputAbs

$process = Start-Process -FilePath $unityExe -ArgumentList @(
    "-batchmode",
    "-quit",
    "-nographics",
    "-projectPath", $projectAbs,
    "-executeMethod", $executeMethod,
    "-logFile", $unityLog
) -WorkingDirectory $repoRoot -PassThru

if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    try { Stop-Process -Id $process.Id -Force } catch { }
    throw "Unity Windows player build timed out after $TimeoutSeconds seconds. Check log: $unityLog"
}

$exitCode = $process.ExitCode
Write-Host "[unity-build] ExitCode=$exitCode"

if (-not (Test-Path $unityLog)) {
    $fallbackLog = Join-Path $env:LOCALAPPDATA "Unity\Editor\Editor.log"
    if (Test-Path $fallbackLog) {
        $unityLog = $fallbackLog
        Write-Host "[unity-build] Using fallback log: $unityLog"
    }
}

if ($exitCode -ne 0) {
    throw "Unity Windows player build failed with exit code $exitCode. Check log: $unityLog"
}

if (-not (Test-Path $outputAbs)) {
    throw "Unity reported success but output executable was not found: $outputAbs"
}

Write-Host "[unity-build] Success"
Write-Host "[unity-build] Player: $outputAbs"
