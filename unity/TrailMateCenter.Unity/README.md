# TrailMateCenter.Unity

Unity companion project for the TrailMateCenter desktop app (Windows x64).

## Requirements
- Unity 2022.3 LTS (tested with 2022.3.20f1)
- Windows x64
- TextMeshPro essentials (Unity will prompt to import on first open)

## How to Run (Editor)
1. Open this folder in Unity Hub.
2. Play the scene (no manual scene needed; runtime builds the scene automatically).
3. Unity will start the bridge server (NamedPipe/TCP) and wait for the desktop app.
4. Start the desktop app and click **Attach Unity** in the Propagation tab.

## Build (Windows x64)
1. File -> Build Settings
2. Platform: Windows
3. Target: x86_64
4. Build as `TrailMateCenter.Unity.exe` (recommended)

## Default Bridge Config
Config file: `Assets/StreamingAssets/trailmate_unity_config.json`

Runtime environment variables take precedence over the JSON file for bridge settings:
`TRAILMATE_PROPAGATION_UNITY_BRIDGE_MODE`, `TRAILMATE_PROPAGATION_UNITY_PIPE_NAME`,
`TRAILMATE_PROPAGATION_UNITY_TCP_HOST`, `TRAILMATE_PROPAGATION_UNITY_TCP_PORT`,
and `TRAILMATE_PROPAGATION_UNITY_VIEWPORT_ID`.

```
{
  "bridge": {
    "transport": "namedpipe",
    "pipeName": "TrailMateCenter.Propagation.Bridge"
  }
}
```

## GeoTIFF Support (Optional)
Unity loads PNG/JPG directly. For GeoTIFF:
- Option A (recommended): Convert to PNG on the server and send the PNG URI in `push_result`.
- Option B (local conversion): Enable `rasterConversion` and install GDAL (`gdal_translate`, `gdalinfo`, `gdalwarp`).

Example:
```
"layers": {
  "rasterConversion": {
    "enable": true,
    "gdalTranslatePath": "C:\\OSGeo4W64\\bin\\gdal_translate.exe",
    "gdalInfoPath": "C:\\OSGeo4W64\\bin\\gdalinfo.exe",
    "gdalWarpPath": "C:\\OSGeo4W64\\bin\\gdalwarp.exe",
    "outputFormat": "png",
    "cacheDirectory": "C:\\TrailMateCenter\\raster_cache",
    "timeoutMs": 10000,
    "enableReprojection": true,
    "targetCrs": "EPSG:3857",
    "preferredBand": 1,
    "autoScaleToByte": true,
    "maxTextureSize": 4096,
    "generateMipMaps": true
  }
}
```

## Terrain-Conforming Overlays
`layers.conformToTerrain=true` builds a mesh that samples the terrain heightfield.
`layers.overlayResolution` controls density (default 256). Higher = better fit, heavier CPU.

For tiled rasters:
- `layers.enableTiledRaster=true` enables on-demand tile loading
- `layers.tileRefreshIntervalSeconds` controls refresh cadence
- `layers.tileConformingResolution` controls per-tile mesh density
- `layers.maxActiveTiles` caps active tile count

## push_result Metadata Alignment
Unity now supports optional per-layer metadata in `payload.modelOutputs.rasterLayers`:

```json
"modelOutputs": {
  "meanCoverageRasterUri": "file:///D:/runs/run_001/coverage_mean.tif",
  "rasterLayers": [
    {
      "layerId": "coverage_mean",
      "rasterUri": "file:///D:/runs/run_001/coverage_mean.tif",
      "bounds": { "minX": 0, "minZ": 0, "maxX": 3000, "maxZ": 3000 },
      "crs": "LOCAL_TERRAIN_M",
      "minValue": -130.0,
      "maxValue": -70.0,
      "noDataValue": -9999.0,
      "valueScale": 60.0,
      "valueOffset": -130.0,
      "classBreaks": [-120.0, -110.0, -100.0, -90.0, -80.0],
      "tileTemplateUri": "file:///D:/runs/run_001/tiles/coverage_mean/{z}/{x}/{y}.png",
      "minZoom": 0,
      "maxZoom": 6,
      "tileSize": 256,
      "unit": "dBm",
      "palette": "default"
    }
  ]
}
```

Value mapping rule used by shader:
- `physical_value = encoded_value * valueScale + valueOffset`
- `encoded_value` is sampled from raster texture channel `R` in `[0,1]`
- ramp index uses `minValue/maxValue` in physical domain

If `valueScale/valueOffset` is not provided and `minValue/maxValue` is outside `[0,1]`,
Unity assumes raster is normalized and auto-uses:
- `valueScale = maxValue - minValue`
- `valueOffset = minValue`

If `rasterLayers` is missing, Unity falls back to:
- full terrain extent for bounds
- metadata extraction from `*.json`/`*.meta.json` sidecar
- `gdalinfo -json` for GeoTIFF when available

## Geo-Reference Mapping
Unity supports `AOI + terrain CRS` mapping from config:

```json
"geoReference": {
  "terrainCrs": "EPSG:3857",
  "terrainMinX": 13600000.0,
  "terrainMinZ": 4560000.0,
  "terrainMaxX": 13603000.0,
  "terrainMaxZ": 4563000.0,
  "enableAoiBoundaryMask": true,
  "aoiMinX": 13600400.0,
  "aoiMinZ": 4560120.0,
  "aoiMaxX": 13602680.0,
  "aoiMaxZ": 4562870.0
}
```

When raster CRS differs from terrain CRS, Unity uses:
1. `gdalwarp -t_srs <target>`
2. metadata bounds from reprojected raster
3. AOI/terrain domain mapping to world coordinates

## Layer Stack and Controls
`set_active_layer` payload supports multi-layer stack:

```json
{
  "layer_ids": ["coverage_mean", "interference"],
  "layer_visibility": { "coverage_mean": true, "interference": true },
  "layer_opacity": { "coverage_mean": 0.65, "interference": 0.35 },
  "layer_order": ["interference", "coverage_mean"],
  "run_id": "run_001"
}
```

HUD provides:
- layer visibility toggles
- opacity sliders
- active legend with min/max/unit
- structured evidence text blocks

## Scene Geometry Overlay
Unity supports optional geometry payload in `push_result`:

```json
"sceneGeometry": {
  "relayCandidates": [{ "id": "r1", "x": 1200, "z": 980, "score": 0.72 }],
  "relayRecommendations": [{ "id": "best_1", "x": 1500, "z": 1220 }],
  "profileObstacles": [{ "id": "ridge_03", "x": 1430, "z": 1180 }],
  "profileLines": [
    { "id": "p1", "points": [{ "x": 1100, "z": 900 }, { "x": 1800, "z": 1400 }] }
  ]
}
```

## Interaction Events
Unity now emits:
- `map_point_selected`
- `profile_line_changed`
- `interaction_event` with:
  - `measurement_completed`
  - `annotation_added`
  - `hotspot_stats`
  - `profile_curve_summary`
- `error_report` for layer load/mapping/conversion problems

## Performance
Current optimizations:
- texture LRU cache (`performance.textureCacheCapacity`)
- max texture size downscale (`layers.rasterConversion.maxTextureSize`)
- optional mipmap generation (`layers.rasterConversion.generateMipMaps`)
- tiled on-demand loading (`layers.enableTiledRaster`)
- tile texture cache with bounded active tile set (`layers.maxActiveTiles`)
- overlay mesh LOD near/far (`overlayMeshLodNear`, `overlayMeshLodFar`, `overlayMeshLodDistance`)
- progressive layer status updates (`loading -> ready/failed`)

## Notes
- The desktop app will embed the Unity window by matching:
  - Window class: `UnityWndClass`
  - Window title: `TrailMateCenter.Unity`
  - Allowed process name: `TrailMateCenter.Unity`
- You can override via environment variables on the desktop app side.
- Bridge message reference: `docs/propagation/implementation/UNITY_BRIDGE_PROTOCOL_V2.md`
- Tile generation and e2e scripts: `scripts/propagation/README.md`
