# Propagation Scripts

## 1. Build Tile Pyramid

`build_tile_pyramid.ps1` generates XYZ tiles from a source raster.

Example:

```powershell
powershell -ExecutionPolicy Bypass -File ./scripts/propagation/build_tile_pyramid.ps1 `
  -InputRaster D:\runs\run_001\coverage_mean.tif `
  -OutputDirectory D:\runs\run_001\tiles\coverage_mean `
  -Zoom 0-10 `
  -TargetCrs EPSG:3857
```

Optional environment variables:

- `TRAILMATE_GDAL2TILES`
- `TRAILMATE_GDALWARP`

## 2. End-to-End Regression Run

`run_e2e_regression.ps1` starts:

- mock simulation service (in-process via app env)
- real Unity bridge
- Unity stub server (optional)
- desktop app

Example:

```powershell
powershell -ExecutionPolicy Bypass -File ./scripts/propagation/run_e2e_regression.ps1 -Configuration Debug -UseUnityStub
```

Generated logs are written to `artifacts/propagation-regression` as `*.out.log` / `*.err.log`.

## 3. Unity Bridge E2E (Batch + Probe)

`run_unity_bridge_e2e.ps1` starts Unity in batch play mode and runs
`tools/TrailMateCenter.Propagation.BridgeProbe` to validate:

- `attach_viewport`
- `push_request`
- `push_result`
- `set_active_layer`
- `set_camera_state`

Example:

```powershell
powershell -ExecutionPolicy Bypass -File ./scripts/propagation/run_unity_bridge_e2e.ps1 `
  -Configuration Debug `
  -SmokeDurationSeconds 60
```

Generated logs are written to `artifacts/propagation-regression` as:

- `unity_bridge_e2e_*.log`
- `desktop_bridge_probe_*.log`

## 4. Build Unity Windows Player

`build_unity_windows_player.ps1` runs Unity in batchmode and builds the Windows x64 player to the desktop app's auto-discovery path by default:

- default output: `unity/TrailMateCenter.Unity/Builds/Windows64/TrailMateCenter.Unity.exe`
- execute method: `TrailMateCenter.Unity.EditorTools.WindowsPlayerBuilder.Build`

Example:

```powershell
powershell -ExecutionPolicy Bypass -File ./scripts/propagation/build_unity_windows_player.ps1
```

Development build example:

```powershell
powershell -ExecutionPolicy Bypass -File ./scripts/propagation/build_unity_windows_player.ps1 -Development -Clean
```

## 5. Unity Editor Play Smoke

`run_unity_editor_play_smoke.ps1` runs Unity in batchmode and enters play mode once via
`TrailMateCenter.Unity.EditorTools.PlayModeSmokeRunner`.

Example:

```powershell
powershell -ExecutionPolicy Bypass -File ./scripts/propagation/run_unity_editor_play_smoke.ps1 `
  -UnityExePath "C:\\Program Files\\Unity\\Hub\\Editor\\2022.3.20f1\\Editor\\Unity.exe" `
  -DurationSeconds 10
```
