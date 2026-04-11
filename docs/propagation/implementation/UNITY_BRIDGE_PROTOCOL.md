# UNITY_BRIDGE_PROTOCOL

> 目标：定义 TrailMateCenter 桌面端与 Unity 进程之间的通信协议。  
> 读完本文件后，Unity 同学可直接实现桥接服务并完成首轮联调。

## 1. 范围与约束

- 协议方向：
- 桌面端 -> Unity：`attach_viewport`、`push_request`、`push_result`、`set_active_layer`、`set_camera_state`、`heartbeat`
- Unity -> 桌面端：`ack`、`map_point_selected`、`profile_line_changed`、`bridge_state`、`layer_state_changed`、`diagnostic_snapshot`、`camera_state_changed`
- 传输层（二选一）：
  - NamedPipe（默认）
  - TCP（可切换）
- 消息格式：`UTF-8` + `每行一条 JSON（NDJSON）`
- 当前版本：`v1`（以当前实现代码为准）

## 2. 连接拓扑与启动方式

桌面端 `UnityProcessPropagationBridge` 是客户端角色，会主动连接 Unity 进程暴露的服务端端口/管道。

### 2.1 默认模式（NamedPipe）

- 桌面端连接管道名：`TrailMateCenter.Propagation.Bridge`
- Unity 需要先启动 NamedPipe 服务端，再等待桌面端连接。

### 2.2 TCP 模式

- 桌面端连接目标：`127.0.0.1:51110`
- Unity 需要先启动 TCP Server 并监听该地址。

### 2.3 桌面端环境变量

| 变量 | 默认值 | 说明 |
|---|---|---|
| `TRAILMATE_PROPAGATION_UNITY_BRIDGE_MODE` | `namedpipe` | 可选：`namedpipe` / `tcp` |
| `TRAILMATE_PROPAGATION_UNITY_PIPE_NAME` | `TrailMateCenter.Propagation.Bridge` | NamedPipe 名称 |
| `TRAILMATE_PROPAGATION_UNITY_TCP_HOST` | `127.0.0.1` | TCP 主机 |
| `TRAILMATE_PROPAGATION_UNITY_TCP_PORT` | `51110` | TCP 端口 |
| `TRAILMATE_PROPAGATION_UNITY_CONNECT_TIMEOUT_MS` | `5000` | 建连超时 |
| `TRAILMATE_PROPAGATION_UNITY_ACK_TIMEOUT_MS` | `2000` | 单条消息等待 ACK 超时 |
| `TRAILMATE_PROPAGATION_UNITY_HEARTBEAT_ENABLED` | `true` | 是否启用心跳 |
| `TRAILMATE_PROPAGATION_UNITY_HEARTBEAT_INTERVAL_MS` | `3000` | 心跳间隔 |
| `TRAILMATE_PROPAGATION_UNITY_RECONNECT_BACKOFF_MS` | `1000,2000,5000,10000` | 重连退避序列 |

## 3. 帧格式（必须遵守）

- 每条消息是一行完整 JSON，以 `\n` 结尾。
- 不要跨行输出一个 JSON。
- 不要求固定字段顺序。
- 未识别字段应忽略（前向兼容）。

示例（单行）：

```json
{"type":"ack","correlation_id":"abc123","payload":{"action":"push_request","run_id":"run_001","detail":"ok","timestamp_utc":"2026-02-26T10:10:00+00:00"}}
```

## 4. 桌面端 -> Unity 消息（Unity 需处理）

桌面端发送的外层 Envelope（字段名是 `camelCase`）：

```json
{
  "type": "attach_viewport | push_request | push_result | set_active_layer | set_camera_state | heartbeat",
  "correlationId": "string",
  "runId": "string",
  "timestampUtc": "ISO-8601",
  "payload": { }
}
```

### 4.1 `attach_viewport`

用途：告诉 Unity 当前 UI 绑定的视图槽位。

`payload`：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `viewport_id` | string | 是 | 例如 `propagation-main-slot` |

示例：

```json
{
  "type": "attach_viewport",
  "correlationId": "ad9f3f8f0f3f4e46b57a384cbd8db5cc",
  "runId": "",
  "timestampUtc": "2026-02-26T10:12:30+00:00",
  "payload": {
    "viewport_id": "propagation-main-slot"
  }
}
```

### 4.2 `push_request`

用途：推送一次仿真请求参数给 Unity（可用于先渲染参数状态）。

`payload` 对应 `PropagationSimulationRequest`（`camelCase`）：

| 字段 | 类型 | 说明 |
|---|---|---|
| `requestId` | string | 请求 ID |
| `mode` | int | 0:CoverageMap, 1:InterferenceAnalysis, 2:RelayOptimization, 3:AdvancedModeling |
| `sourceRunId` | string/null | 来源 runId（可为空） |
| `frequencyMHz` | number | 频率 |
| `txPowerDbm` | number | 发射功率 |
| `uplinkSpreadingFactor` | string | 例如 `SF10` |
| `downlinkSpreadingFactor` | string | 例如 `SF10` |
| `environmentLossDb` | number | 环境损耗 |
| `vegetationAlphaSparse` | number | 稀疏植被系数 |
| `vegetationAlphaDense` | number | 密集植被系数 |
| `shadowSigmaDb` | number | 阴影衰落 sigma |
| `reflectionCoeff` | number | 反射系数 |
| `enableMonteCarlo` | bool | 是否启用蒙特卡洛 |
| `monteCarloIterations` | int | 蒙特卡洛迭代次数 |
| `demVersion` | string | DEM 数据版本 |
| `landcoverVersion` | string | 土地覆盖版本 |
| `surfaceVersion` | string | 地表参数版本 |

示例：

```json
{
  "type": "push_request",
  "correlationId": "3f8d8055725447cb99f9488d3271bb72",
  "runId": "run_20260226_101530",
  "timestampUtc": "2026-02-26T10:15:30+00:00",
  "payload": {
    "requestId": "req_20260226_101530",
    "mode": 0,
    "sourceRunId": null,
    "frequencyMHz": 915.0,
    "txPowerDbm": 20.0,
    "uplinkSpreadingFactor": "SF10",
    "downlinkSpreadingFactor": "SF10",
    "environmentLossDb": 6.0,
    "vegetationAlphaSparse": 0.3,
    "vegetationAlphaDense": 0.8,
    "shadowSigmaDb": 8.0,
    "reflectionCoeff": 0.2,
    "enableMonteCarlo": false,
    "monteCarloIterations": 120,
    "demVersion": "dem_20260220_v1",
    "landcoverVersion": "lc_20260218_v3",
    "surfaceVersion": "surface_default_v1"
  }
}
```

### 4.3 `push_result`

用途：推送完整仿真结果给 Unity（渲染图层/证据卡片/剖面联动）。

`payload` 对应 `PropagationSimulationResult`（`camelCase`），顶层结构：

- `runMeta`
- `inputBundle`
- `modelOutputs`
- `analysisOutputs`
- `provenance`
- `qualityFlags`

建议 Unity 首阶段至少解析以下字段：

- `runMeta.runId`
- `runMeta.status`（int）
- `analysisOutputs.link.*`
- `analysisOutputs.lossBreakdown.*`
- `analysisOutputs.coverageProbability.*`
- `analysisOutputs.network.*`
- `analysisOutputs.profile.*`
- `modelOutputs.*RasterUri`
- `provenance.datasetBundle.*`
- `provenance.modelVersion`
- `provenance.parameterHash`
- `qualityFlags.assumptionFlags`
- `qualityFlags.validityWarnings`

示例（节选）：

```json
{
  "type": "push_result",
  "correlationId": "4f9e17e7f8ce4f7f9c9e1cf8fd6650dd",
  "runId": "run_20260226_101530",
  "timestampUtc": "2026-02-26T10:16:25+00:00",
  "payload": {
    "runMeta": {
      "runId": "run_20260226_101530",
      "status": 3,
      "startedAtUtc": "2026-02-26T10:15:30+00:00",
      "finishedAtUtc": "2026-02-26T10:16:25+00:00",
      "durationMs": 55000,
      "progressPercent": 100.0,
      "cacheHit": false
    },
    "modelOutputs": {
      "meanCoverageRasterUri": "file:///runs/run_20260226_101530/coverage_mean.tif",
      "reliability95RasterUri": "file:///runs/run_20260226_101530/reliability_p95.tif",
      "reliability80RasterUri": "file:///runs/run_20260226_101530/reliability_p80.tif",
      "interferenceRasterUri": "file:///runs/run_20260226_101530/interference.tif",
      "capacityRasterUri": "file:///runs/run_20260226_101530/capacity.tif"
    },
    "analysisOutputs": {
      "link": {
        "downlinkRssiDbm": -118.7,
        "uplinkRssiDbm": -116.2,
        "downlinkMarginDb": 7.3,
        "uplinkMarginDb": 9.8,
        "linkFeasible": true,
        "marginGuardrail": "edge"
      },
      "lossBreakdown": {
        "fsplDb": 128.4,
        "diffractionDb": 7.1,
        "vegetationDb": 9.6,
        "reflectionDb": 1.8,
        "shadowDb": 6.0
      },
      "coverageProbability": {
        "areaP95Km2": 4.82,
        "areaP80Km2": 7.54
      },
      "network": {
        "sinrDb": 8.6,
        "conflictRate": 0.14,
        "maxCapacityNodes": 127
      },
      "profile": {
        "distanceKm": 6.2,
        "fresnelRadiusM": 9.4,
        "marginDb": 7.3,
        "mainObstacle": {
          "label": "ridge_03",
          "v": 0.56,
          "ldDb": 8.1
        }
      }
    },
    "provenance": {
      "datasetBundle": {
        "demVersion": "dem_20260220_v1",
        "landcoverVersion": "lc_20260218_v3",
        "surfaceVersion": "surface_default_v1"
      },
      "modelVersion": "prop_core_0.3.0",
      "gitCommit": "abc1234",
      "parameterHash": "p_6bf9dc"
    },
    "qualityFlags": {
      "assumptionFlags": ["single_knife_edge", "no_multipath_default"],
      "validityWarnings": []
    }
  }
}
```

### 4.4 `set_active_layer`

用途：切换 Unity 侧当前可视图层。
`payload`：
| 字段 | 类型 | 说明 |
|---|---|---|
| `layer_id` | string | 例如 `coverage_mean` / `reliability_95` / `interference` |
| `run_id` | string | 可为空，建议使用当前 runId |

示例：
```json
{
  "type": "set_active_layer",
  "correlationId": "a6c9f3b0c8b546f6a6b3b4a4e5ab2b7d",
  "runId": "run_20260226_101530",
  "timestampUtc": "2026-02-26T10:20:10+00:00",
  "payload": {
    "layer_id": "reliability_95",
    "run_id": "run_20260226_101530"
  }
}
```

### 4.5 `set_camera_state`

用途：桌面端下发 Unity 镜头状态。
`payload`：
| 字段 | 类型 | 说明 |
|---|---|---|
| `x` | number | 相机坐标 X |
| `y` | number | 相机坐标 Y |
| `z` | number | 相机坐标 Z |
| `pitch` | number | 俯仰角 |
| `yaw` | number | 偏航角 |
| `roll` | number | 翻滚角 |
| `fov` | number | 视场角 |

示例：
```json
{
  "type": "set_camera_state",
  "correlationId": "d8f77b6d1a2449b3a4b2c8f4d1e2a5f9",
  "runId": "run_20260226_101530",
  "timestampUtc": "2026-02-26T10:21:30+00:00",
  "payload": {
    "x": 1200.0,
    "y": 1800.0,
    "z": 900.0,
    "pitch": 20.0,
    "yaw": 45.0,
    "roll": 0.0,
    "fov": 55.0
  }
}
```

### 4.6 `heartbeat`

用途：桌面端心跳保活与 RTT 测量。Unity 需返回 `ack`。
`payload` 可为空或携带 `viewport_id`。

示例：
```json
{
  "type": "heartbeat",
  "correlationId": "a1f8f4a2f3b14a7db0cf0c9f7f6b1d3a",
  "runId": "",
  "timestampUtc": "2026-02-26T10:20:30+00:00",
  "payload": {
    "viewport_id": "propagation-main-slot"
  }
}
```

## 5. Unity -> 桌面端消息（Unity 需发送）

### 5.1 `ack`（强制）

每收到桌面端一条命令，Unity 必须回一条 `ack`。  
最关键字段：`correlation_id`，必须回传收到的 `correlationId` 值。

`ack` 推荐格式：

```json
{
  "type": "ack",
  "correlation_id": "3f8d8055725447cb99f9488d3271bb72",
  "payload": {
    "action": "push_request",
    "run_id": "run_20260226_101530",
    "detail": "accepted",
    "timestamp_utc": "2026-02-26T10:15:30+00:00"
  }
}
```

注意：

- 这里是 `correlation_id`（snake_case），不是 `correlationId`。
- 如果 `correlation_id` 不匹配，桌面端会当成未 ACK，2 秒后走超时路径。

### 5.2 `map_point_selected`

用途：Unity 端点选覆盖图位置后，回传给桌面端用于链路预算联动显示。

```json
{
  "type": "map_point_selected",
  "payload": {
    "x": 1532.2,
    "y": 2241.5,
    "node_id": "relay_03"
  }
}
```

### 5.3 `profile_line_changed`

用途：Unity 端更新剖面线后，回传桌面端更新文本/证据区域。

```json
{
  "type": "profile_line_changed",
  "payload": {
    "start_x": 1024.0,
    "start_y": 2048.0,
    "end_x": 2024.0,
    "end_y": 2448.0
  }
}
```

### 5.4 `bridge_state`

用途：Unity 主动报告桥状态（可选，但建议实现）。

```json
{
  "type": "bridge_state",
  "payload": {
    "attached": true,
    "message": "viewport ready"
  }
}
```

### 5.5 `layer_state_changed`

用途：Unity 报告当前激活图层。
```json
{
  "type": "layer_state_changed",
  "payload": {
    "run_id": "run_20260226_101530",
    "layer_id": "reliability_95",
    "state": "loading | ready | failed",
    "progress": 45.0,
    "transition_ms": 180.0,
    "message": "layer switched",
    "timestamp_utc": "2026-02-26T10:20:11+00:00"
  }
}
```

#### 5.5.1 Layer loading state machine (recommended)

States:
- `loading`: assets/tiles are being loaded or uploaded to GPU
- `rendering`: optional intermediate state while shaders warm up
- `ready` / `active`: fully visible
- `failed` / `error`: load failed

Rules:
1. Emit `state=loading` immediately after receiving `set_active_layer`.
2. `progress` can be 0..1 or 0..100. The desktop normalizes both.
3. When the layer becomes visible, emit `state=ready` (or `active`) with `progress=100`.
4. `transition_ms` should measure the total time from the first `loading` to `ready`.
5. On failure, emit `state=failed` with a `message` that can be shown in UI.

Example sequence (NDJSON):
```json
{"type":"layer_state_changed","payload":{"run_id":"run_20260226_101530","layer_id":"coverage_mean","state":"loading","progress":0,"transition_ms":null,"message":"loading tiles","timestamp_utc":"2026-02-26T10:20:11+00:00"}}
{"type":"layer_state_changed","payload":{"run_id":"run_20260226_101530","layer_id":"coverage_mean","state":"loading","progress":0.45,"transition_ms":null,"message":"loading tiles","timestamp_utc":"2026-02-26T10:20:11+00:00"}}
{"type":"layer_state_changed","payload":{"run_id":"run_20260226_101530","layer_id":"coverage_mean","state":"ready","progress":100,"transition_ms":180,"message":"layer ready","timestamp_utc":"2026-02-26T10:20:11+00:00"}}
```

### 5.6 `diagnostic_snapshot`

用途：Unity 向桌面端上报渲染与性能诊断快照。
```json
{
  "type": "diagnostic_snapshot",
  "payload": {
    "fps": 60.2,
    "frame_time_p95_ms": 21.3,
    "gpu_memory_mb": 720.5,
    "layer_load_ms": 135.0,
    "tile_cache_hit_rate": 0.86,
    "message": "stable",
    "timestamp_utc": "2026-02-26T10:22:10+00:00"
  }
}
```

### 5.7 `camera_state_changed`

用途：Unity 回传当前镜头状态。
```json
{
  "type": "camera_state_changed",
  "payload": {
    "x": 1200.0,
    "y": 1800.0,
    "z": 900.0,
    "pitch": 20.0,
    "yaw": 45.0,
    "roll": 0.0,
    "fov": 55.0,
    "message": "camera applied",
    "timestamp_utc": "2026-02-26T10:21:31+00:00"
  }
}
```

## 6. 时序（推荐实现）

### 6.1 初始化时序

1. Unity 启动并监听 NamedPipe/TCP。
2. 桌面端连接成功。
3. 桌面端发送 `attach_viewport`。
4. Unity 回 `ack`。
5. 后续等待 `push_request` / `push_result`。

### 6.2 仿真时序

1. 用户发起仿真，桌面端发送 `push_request`。
2. Unity 回 `ack`，可以先做参数面板更新或占位渲染。
3. 仿真完成后桌面端发送 `push_result`。
4. Unity 回 `ack`，更新 3D 图层。
5. Unity 用户交互时，异步发送 `map_point_selected` / `profile_line_changed`。

## 7. 超时、异常与重连语义

- 建连超时：默认 `5000ms`。
- ACK 超时：默认 `2000ms`。
- ACK 超时时，桌面端不会立即抛错，会生成一条本地 ACK（`detail = "sent (ack timeout)"`）继续流程。
- 写入失败或连接断开时：
  - 桌面端将桥状态切到 detached；
  - 未完成 ACK 的消息会失败（异常）。
- Unity 侧建议：
  - 收到命令后先快速回 ACK，再异步执行重任务；
  - 未识别命令也回 ACK（`detail = "unknown command"`），避免桌面端阻塞等待。

## 8. Unity 实施清单（可直接开工）

1. 选择传输层：
   - Windows-only 场景优先 NamedPipe。
   - 跨平台调试优先 TCP。
2. 实现单连接消息循环：
   - 按行读 JSON；
   - 解析 `type` 分发处理；
   - 每个桌面端命令都回 ACK。
3. 实现 3 个命令处理器：
   - `attach_viewport`
   - `push_request`
   - `push_result`
4. 实现 2 个交互事件上报：
   - `map_point_selected`
   - `profile_line_changed`
5. 可选实现：
   - `bridge_state`
6. 联调自检：
   - `correlation_id` 回传正确；
   - 所有数字字段可解析；
   - 发送 JSON 不换行、不分包拼接错误。

## 9. 兼容性说明

- 桌面端发送为 `camelCase`。
- Unity 回传中，`ack` 的关键字段要求 `snake_case`（尤其 `correlation_id`）。
- 桌面端对未知字段容忍，Unity 也应对未知字段容忍。
- 枚举值当前按整数传输，后续如改字符串会在协议版本中声明。

## 10. 参考实现位置

- 桌面桥接实现：`src/TrailMateCenter.Propagation.UnityBridge/UnityProcessPropagationBridge.cs`
- 请求/结果 DTO：`src/TrailMateCenter.Propagation.Contracts/PropagationContracts.cs`
- ViewModel 使用入口：`src/TrailMateCenter.App/ViewModels/PropagationViewModel.cs`
