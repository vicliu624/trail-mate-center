# UNITY_EMBEDDING_AND_STUB_QUICKSTART

> 目标：快速打通“桌面端传播页 <-> Unity 进程桥”的端到端链路。  
> 本文覆盖两部分：`UnityViewportHost` 原生窗口承载 + `UnityStubServer` 协议联调。

## 1. 当前可用能力

- 传播页主视图区已替换为原生宿主控件：`UnityViewportHost`
  - 文件：`src/TrailMateCenter.App/Views/Controls/UnityViewportHost.cs`
  - 页面接入：`src/TrailMateCenter.App/Views/PropagationView.axaml`
- 桥接通信为真实实现（非 Fake）：
  - `IPropagationUnityBridge -> UnityProcessPropagationBridge`
- 新增可运行联调骨架：
  - `src/TrailMateCenter.Propagation.UnityStubServer/Program.cs`

## 2. 宿主控件配置（桌面端）

`UnityViewportHost` 按以下优先级寻找 Unity 窗口：

1. `TRAILMATE_PROPAGATION_UNITY_HWND`（显式句柄，优先级最高）
2. `TRAILMATE_PROPAGATION_UNITY_WINDOW_CLASS` + `TRAILMATE_PROPAGATION_UNITY_WINDOW_TITLE`
3. 仅标题或仅类名（其中之一可为空）

支持值：

- `TRAILMATE_PROPAGATION_UNITY_HWND`
  - 十进制句柄，如 `65566`
  - 或十六进制，如 `0x1001E`

宿主控件轮询间隔默认 `500ms`，用于自动检测并附着窗口。

## 3. 桥接传输配置（桌面端/Stub/Unity 共用）

| 变量 | 默认值 | 说明 |
|---|---|---|
| `TRAILMATE_PROPAGATION_UNITY_BRIDGE_MODE` | `namedpipe` | `namedpipe` / `tcp` |
| `TRAILMATE_PROPAGATION_UNITY_PIPE_NAME` | `TrailMateCenter.Propagation.Bridge` | NamedPipe 名 |
| `TRAILMATE_PROPAGATION_UNITY_TCP_HOST` | `127.0.0.1` | TCP 主机 |
| `TRAILMATE_PROPAGATION_UNITY_TCP_PORT` | `51110` | TCP 端口 |

## 4. 启动 Stub Server（联调）

### 4.1 NamedPipe 模式（默认）

```powershell
$env:TRAILMATE_PROPAGATION_UNITY_BRIDGE_MODE = "namedpipe"
dotnet run --project src/TrailMateCenter.Propagation.UnityStubServer/TrailMateCenter.Propagation.UnityStubServer.csproj
```

### 4.2 TCP 模式

```powershell
$env:TRAILMATE_PROPAGATION_UNITY_BRIDGE_MODE = "tcp"
$env:TRAILMATE_PROPAGATION_UNITY_TCP_HOST = "127.0.0.1"
$env:TRAILMATE_PROPAGATION_UNITY_TCP_PORT = "51110"
dotnet run --project src/TrailMateCenter.Propagation.UnityStubServer/TrailMateCenter.Propagation.UnityStubServer.csproj
```

## 5. 启动桌面端并验证

1. 启动 `TrailMateCenter.App`（确保 `TRAILMATE_PROPAGATION_USE_MOCK=false` 或不设置）。
2. 进入传播页，点击：
   - `Attach Unity`
   - `Sync Unity` 或 `Run`
3. 观察传播页底部 `Unity Bridge` 面板：
   - `Unity bridge: attached`
   - `Ack: ...`
   - `Point: ...`（来自 `map_point_selected`）
   - `Profile: ...`（来自 `profile_line_changed`）
4. 同时观察 Stub 控制台输出，确认请求/回包均有日志。

## 6. 对接真实 Unity 的最小实现要点

Unity 侧最小闭环只需 4 件事：

1. 提供 NamedPipe/TCP 服务端（桌面端主动连入）。
2. 收到 `attach_viewport` / `push_request` / `push_result` 后，必须回 `ack`。
3. 回包中 `correlation_id` 必须与请求 `correlationId` 一致。
4. 在交互时上报：
   - `map_point_selected`
   - `profile_line_changed`

协议细节见：`docs/propagation/implementation/UNITY_BRIDGE_PROTOCOL.md`

## 7. 当前限制说明

- `UnityViewportHost` 为 Windows HWND 路径实现，跨平台嵌入尚未实现。
- Stub Server 是联调骨架，不负责实际 3D 渲染。
- 真实 Unity 渲染能力仍需 Unity 工程侧实现（地形、热力图、剖面、图层切换）。

