# UNITY_PRODUCTION_INTEGRATION_PLAN

## 1. Target

This document defines the production-grade Unity integration plan for the propagation feature.
It replaces "minimal demo integration" with a full engineering implementation path.

Target outcome:

- Desktop app hosts a real Unity viewport in the propagation tab.
- Unity process is managed by the desktop app lifecycle.
- Real bridge protocol is reliable, observable, and versioned.
- Unity renders terrain + multi-layer propagation outputs + interaction callbacks.
- End-to-end flow supports operation, debugging, and release hardening.

## 2. Scope

In scope:

- Unity process orchestration from desktop app.
- Window embedding and input/focus behavior.
- IPC bridge reliability and protocol evolution.
- Data ingest path for simulation outputs and metadata.
- Unity rendering architecture for propagation layers.
- Interaction sync from Unity to ViewModel.
- Diagnostics, failure handling, and QA matrix.

Out of scope:

- Physics model algorithm implementation in Unity compute engine itself.
- Backend algorithm correctness proof.

## 3. System Architecture

### 3.1 Desktop Side (Avalonia)

Modules:

1. `PropagationViewModel`
2. `IPropagationSimulationService` (gRPC client)
3. `IPropagationUnityBridge` (NamedPipe/TCP)
4. `UnityViewportHost` (HWND host)
5. `UnityProcessManager` (new, required)

Responsibilities:

- Start/stop Unity process.
- Attach Unity window to host.
- Push request/result payload.
- Reflect bridge status and interaction events in UI.

### 3.2 Unity Side

Recommended runtime modules:

1. `BridgeServer`  
Receives commands and returns ACK/events.

2. `SceneCoordinator`  
Owns global mode state and layer switch state machine.

3. `TerrainSubsystem`  
Loads terrain mesh/tiles and controls LOD.

4. `LayerRenderingSubsystem`  
Renders mean coverage, reliability, interference, capacity, margin layers.

5. `ProfileSubsystem`  
Handles profile line interaction and profile geometry extraction.

6. `NodeOverlaySubsystem`  
Renders gateway/relay/node symbols and labels.

7. `DiagnosticsSubsystem`  
FPS, memory, layer load timing, bridge latency, last command status.

### 3.3 Data and Result Boundary

Unity should not parse massive raw payload over IPC line-by-line.
Production strategy:

- IPC carries control metadata and lightweight numbers.
- Heavy raster/vector data is referenced by URI/path and loaded by Unity asynchronously.
- All large files must be versioned and hash-traceable.

## 4. Window Embedding Strategy (Production)

Current implementation:

- `UnityViewportHost` uses Win32 re-parenting with `SetParent`.

Production hardening requirements:

1. Identify Unity window by PID first, title/class second.
2. Prevent attaching wrong window when multiple Unity instances exist.
3. Handle minimize/restore and DPI scaling changes.
4. Detach safely on shutdown to avoid orphaned child windows.
5. Add fallback mode: if attach fails, continue in "bridge-only mode" with warning.

Implementation additions:

- `UnityProcessManager` in desktop app:
  - spawn process
  - wait for bridge readiness
  - publish window metadata env vars
  - kill child process on app exit
- `UnityViewportHost`:
  - attach timeout
  - explicit attach state events
  - "reattach on window recreation" support

## 5. Bridge Protocol Evolution

Current protocol is functional but minimal.
Production version should define `v1.1` with strict contracts.

### 5.1 Message Classes

Desktop -> Unity:

- `attach_viewport`
- `push_request`
- `push_result`
- `set_active_layer` (new)
- `set_camera_state` (new)
- `shutdown` (new)

Unity -> Desktop:

- `ack`
- `bridge_state`
- `map_point_selected`
- `profile_line_changed`
- `camera_state_changed` (new)
- `layer_state_changed` (new)
- `diagnostic_snapshot` (new)

### 5.2 Reliability Rules

1. Every command must return ACK.
2. ACK contains original `correlation_id`.
3. ACK includes `accepted|rejected|deferred`.
4. Heartbeat message every 2-5 seconds.
5. Desktop reconnect backoff: `1s, 2s, 5s, 10s`.
6. Protocol version negotiation at connect time.

### 5.3 Payload Rules

1. Keep command payload small.
2. Large arrays/rasters use external file references.
3. Use UTC timestamps in ISO-8601.
4. Unknown fields are tolerated.
5. Schema changes are additive only.

## 6. Unity Rendering Plan

### 6.1 Core Layers

Layer set:

1. Mean coverage
2. Reliability p95
3. Reliability p80
4. Interference
5. Capacity
6. Link margin

Rendering strategy:

- Terrain mesh rendered once.
- Active layer rendered as shader-driven overlay texture.
- Layer switch uses crossfade transition `150-250ms`.
- Shared color LUT registry for consistent legend semantics.

### 6.2 Terrain and Spatial Data

1. DEM tile loading pipeline.
2. Optional contour overlay generation.
3. Landcover mask support for vegetation visualization.
4. Spatial alignment checks:
   - CRS
   - resolution
   - origin
   - extent

### 6.3 Interaction

1. Hover/click on terrain:
   - returns map point event
   - optionally ray-hit metadata (elevation, slope)
2. Profile mode:
   - drag line A->B
   - returns start/end coordinates
3. Node selection:
   - returns selected node id

## 7. State Machine

Unified state machine (desktop + Unity coordination):

1. `NotStarted`
2. `ProcessStarting`
3. `BridgeConnecting`
4. `BridgeConnected`
5. `ViewportAttached`
6. `Ready`
7. `RunningSimulationSync`
8. `RenderingResult`
9. `Degraded`
10. `Terminated`

Transition guards:

- cannot enter `ViewportAttached` before `BridgeConnected`
- cannot push result when bridge disconnected
- degraded mode allows UI continuity without process crash

## 8. Observability and Operations

### 8.1 Metrics

Desktop metrics:

- bridge RTT (ack latency)
- connect retry count
- attach time
- command timeout count

Unity metrics:

- FPS
- frame time p95
- GPU memory
- active layer load time
- tile cache hit rate

### 8.2 Logs

Required log tags:

- `[PropagationBridge]`
- `[UnityViewportHost]`
- `[UnityProcessManager]`
- `[UnityLayerRenderer]`

All logs include:

- run id
- correlation id (if applicable)
- timestamp
- severity

## 9. Security and Safety

1. Restrict file URI roots (workspace/output only).
2. Validate all inbound payload ranges.
3. Reject suspicious path traversal.
4. Limit message size per frame.
5. Do not execute arbitrary commands from IPC payload.

## 10. Test Matrix

### 10.1 Unit Tests

- bridge parser
- ACK correlation
- timeout/reconnect logic
- env var option parser

### 10.2 Integration Tests

1. Desktop + UnityStubServer:
   - attach
   - push request
   - push result
   - receive point/profile event
2. Desktop + real Unity build:
   - window embed
   - resize behavior
   - focus and input

### 10.3 Resilience Tests

- Unity process crash during run
- bridge disconnect mid-stream
- malformed message injection
- missing raster file

### 10.4 Performance Tests

- 1k x 1k raster layer switch latency
- repeated mode switching
- long-session memory stability (>= 2h)

## 11. Delivery Milestones

### Milestone A: Process + Bridge Production Foundation

Deliverables:

- `UnityProcessManager`
- protocol version handshake
- reconnect and heartbeat
- structured logs and metrics

Exit criteria:

- attach success rate >= 99% on target environment
- reconnect works after forced disconnect

### Milestone B: Real Embedding UX Hardening

Deliverables:

- robust window detection by PID
- DPI/resize/focus correctness
- degraded mode fallback banner

Exit criteria:

- no orphan window after repeated open/close cycles

### Milestone C: Full Rendering and Layer Control

Deliverables:

- six-layer rendering pipeline
- profile interaction roundtrip
- node overlay and selection

Exit criteria:

- layer switch < 300ms median
- event roundtrip < 150ms median local

### Milestone D: QA and Release Gate

Deliverables:

- automated integration scripts
- resilience test report
- release checklist signed

Exit criteria:

- all critical tests pass
- no blocker in known issues

## 12. Immediate Implementation Backlog

Priority 0:

1. Add `UnityProcessManager` project/module.
2. Bind process lifecycle to propagation tab open/close.
3. Add bridge heartbeat and reconnect telemetry.

Priority 1:

1. Extend protocol with `set_active_layer`.
2. Add layer mode controls from UI to Unity.
3. Add diagnostics snapshot event and panel display.

Priority 2:

1. Integrate real Unity rendering package.
2. Implement async raster load and cache.
3. Finalize profile interaction geometry sync.

## 13. Definition of Done

Feature is considered done when all are true:

1. User can run propagation and see real Unity-rendered layers in embedded viewport.
2. Map click/profile interactions roundtrip into right-side analytics cards.
3. Bridge remains stable under disconnect/reconnect and process restart.
4. Release build passes integration + resilience + performance baseline.
5. Evidence/provenance data stays consistent between desktop cards and Unity scene.

