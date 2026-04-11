# TrailMateCenter.Propagation.BridgeProbe

Command-line probe for validating the real desktop Unity bridge transport.

The probe sends:

- `attach_viewport`
- `push_request`
- `push_result`
- `set_active_layer`
- `set_camera_state`

and prints ACK + telemetry summaries.

Typical usage from repo root:

```powershell
dotnet run --project tools/TrailMateCenter.Propagation.BridgeProbe/TrailMateCenter.Propagation.BridgeProbe.csproj
```

Optional environment variables:

- `TRAILMATE_PROPAGATION_UNITY_BRIDGE_MODE`
- `TRAILMATE_PROPAGATION_UNITY_PIPE_NAME`
- `TRAILMATE_BRIDGE_PROBE_TIMEOUT_SECONDS`
- `TRAILMATE_BRIDGE_PROBE_ATTACH_RETRIES`
- `TRAILMATE_BRIDGE_PROBE_VIEWPORT_ID`
- `TRAILMATE_BRIDGE_PROBE_HOLD_MS`
- `TRAILMATE_BRIDGE_PROBE_RASTER_URI`
- `TRAILMATE_BRIDGE_PROBE_RASTER_PATH`
