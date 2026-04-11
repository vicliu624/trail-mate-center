# UNITY_BRIDGE_PROTOCOL_V2

This document is the authoritative bridge protocol for the current implementation.

- Scope: Desktop App <-> Unity process (NDJSON over NamedPipe/TCP)
- Version: v2 (2026-02)
- Encoding: UTF-8, one JSON object per line, newline-delimited

## 1. Transport

Desktop side environment variables:

- `TRAILMATE_PROPAGATION_UNITY_BRIDGE_MODE` = `namedpipe` or `tcp`
- `TRAILMATE_PROPAGATION_UNITY_PIPE_NAME` (default `TrailMateCenter.Propagation.Bridge`)
- `TRAILMATE_PROPAGATION_UNITY_TCP_HOST` (default `127.0.0.1`)
- `TRAILMATE_PROPAGATION_UNITY_TCP_PORT` (default `51110`)
- `TRAILMATE_PROPAGATION_UNITY_CONNECT_TIMEOUT_MS` (default `5000`)
- `TRAILMATE_PROPAGATION_UNITY_ACK_TIMEOUT_MS` (default `2000`)

Unity config: `Assets/StreamingAssets/trailmate_unity_config.json`

## 2. Envelope

Desktop -> Unity envelope fields are camelCase:

```json
{
  "type": "attach_viewport | push_request | push_result | set_active_layer | set_camera_state | heartbeat",
  "correlationId": "string",
  "runId": "string",
  "timestampUtc": "ISO-8601",
  "payload": {}
}
```

Unity -> Desktop event fields use `type` + `payload`.

## 3. Desktop -> Unity Commands

## 3.1 attach_viewport

Payload:

```json
{
  "viewport_id": "propagation-main-slot"
}
```

## 3.2 push_request

Payload is serialized from `PropagationSimulationRequest` (camelCase keys).

## 3.3 push_result

Payload is serialized from `PropagationSimulationResult` (camelCase keys).

Important keys consumed by Unity:

- `modelOutputs.meanCoverageRasterUri`
- `modelOutputs.reliability95RasterUri`
- `modelOutputs.reliability80RasterUri`
- `modelOutputs.interferenceRasterUri`
- `modelOutputs.capacityRasterUri`
- `modelOutputs.rasterLayers[]` metadata:
  - `layerId`, `rasterUri`, `bounds`, `crs`
  - tiled source: `tileTemplateUri`, `minZoom`, `maxZoom`, `tileSize`
  - `minValue`, `maxValue`, `noDataValue`
  - `valueScale`, `valueOffset`, `classBreaks`, `unit`, `palette`
- `sceneGeometry`:
  - `relayCandidates[]`, `relayRecommendations[]`, `profileObstacles[]`, `profileLines[]`

## 3.4 set_active_layer

Supports both single-layer and multi-layer controls.

Payload fields:

- `layer_id`: primary layer id (legacy compatible)
- `layer_ids`: ordered active layers
- `layer_visibility`: object map `{ layerId: bool }`
- `layer_opacity`: object map `{ layerId: number(0..1) }`
- `layer_order`: array for draw order (low -> high sibling index)
- `run_id`: run id

Example:

```json
{
  "layer_id": "coverage_mean",
  "layer_ids": ["coverage_mean", "interference"],
  "layer_visibility": {
    "coverage_mean": true,
    "interference": true,
    "capacity": false
  },
  "layer_opacity": {
    "coverage_mean": 0.65,
    "interference": 0.35
  },
  "layer_order": ["interference", "coverage_mean"],
  "run_id": "sim_20260227_101500_1234"
}
```

Example raster layer metadata with tiled source and class breaks:

```json
{
  "layerId": "coverage_mean",
  "rasterUri": "outputs/run_001/coverage_mean.tif",
  "tileTemplateUri": "outputs/run_001/tiles/coverage_mean/{z}/{x}/{y}.png",
  "minZoom": 0,
  "maxZoom": 6,
  "tileSize": 256,
  "bounds": { "minX": 0, "minZ": 0, "maxX": 3000, "maxZ": 3000 },
  "crs": "LOCAL_TERRAIN_M",
  "minValue": -130,
  "maxValue": -70,
  "valueScale": 60,
  "valueOffset": -130,
  "classBreaks": [-120, -110, -100, -90, -80],
  "unit": "dBm",
  "palette": "default"
}
```

## 3.5 set_camera_state

Payload:

```json
{
  "x": 1200,
  "y": 1800,
  "z": 900,
  "pitch": 20,
  "yaw": 45,
  "roll": 0,
  "fov": 55
}
```

## 3.6 heartbeat

Payload can be empty or include `viewport_id`.

## 4. Unity -> Desktop Events

## 4.1 ack

```json
{
  "type": "ack",
  "correlation_id": "...",
  "payload": {
    "action": "set_active_layer",
    "run_id": "sim_...",
    "detail": "layer coverage_mean",
    "timestamp_utc": "2026-02-27T10:20:18.338Z"
  }
}
```

## 4.2 bridge_state

Payload:

- `attached`: bool
- `message`: string

## 4.3 layer_state_changed

Payload:

- `run_id`
- `layer_id`
- `state` (`loading|ready|failed|...`)
- `progress` (optional)
- `transition_ms` (optional)
- `message`
- `timestamp_utc`

## 4.4 diagnostic_snapshot

Payload:

- `fps`
- `frame_time_p95_ms`
- `gpu_memory_mb`
- `layer_load_ms`
- `tile_cache_hit_rate`
- `message`
- `timestamp_utc`

## 4.5 camera_state_changed

Payload:

- `x`,`y`,`z`,`pitch`,`yaw`,`roll`,`fov`
- `message`
- `timestamp_utc`

## 4.6 map_point_selected

Payload:

- `x`,`y`,`node_id`

## 4.7 profile_line_changed

Payload:

- `start_x`,`start_y`,`end_x`,`end_y`

## 4.8 interaction_event

Payload:

- `event_type`
- `data` (event-specific object)
- `timestamp_utc`

Current event types emitted by Unity:

- `measurement_completed`
- `annotation_added`
- `hotspot_stats`
- `profile_curve_summary`

## 4.9 error_report

Payload:

- `category` (`layer_error|layer_warning|...`)
- `source` (layer id / module)
- `message`
- `run_id`
- `timestamp_utc`

## 5. Compatibility Rules

- Unity parser accepts both camelCase and snake_case for key result sections where practical.
- Unknown fields must be ignored.
- Missing optional sections must not crash rendering.

## 6. Validation Checklist

1. Start Unity and Desktop, attach viewport successfully (`ack` + `bridge_state`).
2. Push one fake run result containing `rasterLayers` and `sceneGeometry`.
3. Switch to multi-layer mode via `set_active_layer` with `layer_ids`.
4. Confirm Unity sends `layer_state_changed` and diagnostics.
5. Perform Ctrl+Click / Shift+Click / A+Click / H+Click and confirm `interaction_event` flow.
6. Force a bad raster path and confirm `error_report` event.
