param(
    [Parameter(Mandatory = $true)]
    [string]$InputRaster,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$Zoom = "0-8",
    [string]$Resampling = "bilinear",
    [string]$TargetCrs = "",
    [string]$Gdal2TilesPath = "",
    [string]$GdalWarpPath = ""
)

$ErrorActionPreference = "Stop"

function Resolve-ToolPath {
    param(
        [string]$ExplicitPath,
        [string]$EnvKey,
        [string]$DefaultName
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return $ExplicitPath
    }

    $fromEnv = [Environment]::GetEnvironmentVariable($EnvKey)
    if (-not [string]::IsNullOrWhiteSpace($fromEnv)) {
        return $fromEnv
    }

    return $DefaultName
}

function Run-Tool {
    param(
        [string]$Exe,
        [string[]]$Arguments
    )

    Write-Host "[tile] $Exe $($Arguments -join ' ')"
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $Exe"
    }
}

$inputPath = (Resolve-Path $InputRaster).Path
if (-not (Test-Path $inputPath)) {
    throw "Input raster not found: $InputRaster"
}

$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path $outputPath)) {
    New-Item -ItemType Directory -Path $outputPath | Out-Null
}

$gdal2tiles = Resolve-ToolPath -ExplicitPath $Gdal2TilesPath -EnvKey "TRAILMATE_GDAL2TILES" -DefaultName "gdal2tiles.py"
$gdalwarp = Resolve-ToolPath -ExplicitPath $GdalWarpPath -EnvKey "TRAILMATE_GDALWARP" -DefaultName "gdalwarp"

$workingRaster = $inputPath
$tempWarped = ""
if (-not [string]::IsNullOrWhiteSpace($TargetCrs)) {
    $tempWarped = Join-Path $outputPath "_reprojected_tmp.tif"
    if (Test-Path $tempWarped) {
        Remove-Item -Force $tempWarped
    }

    Run-Tool -Exe $gdalwarp -Arguments @(
        "-overwrite",
        "-t_srs", $TargetCrs,
        $inputPath,
        $tempWarped
    )

    $workingRaster = $tempWarped
}

try {
    Run-Tool -Exe $gdal2tiles -Arguments @(
        "--zoom", $Zoom,
        "--resampling", $Resampling,
        "--xyz",
        "--webviewer", "none",
        $workingRaster,
        $outputPath
    )

    Write-Host "[tile] Done"
    Write-Host "[tile] Tile template URI pattern: $outputPath/{z}/{x}/{y}.png"
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($tempWarped) -and (Test-Path $tempWarped)) {
        Remove-Item -Force $tempWarped
    }
}
