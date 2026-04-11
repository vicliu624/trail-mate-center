# MODULE_BOUNDARY

> 目的：明确传播功能的模块边界与依赖规则，保证后续开发不会再次把计算/桥接逻辑耦合回 UI 壳层。

## 1. 当前模块划分

传播相关代码当前拆分为 4 个核心项目 + 1 个可选 Mock 项目：

1. `TrailMateCenter.Propagation.Contracts`
2. `TrailMateCenter.Propagation.Client`
3. `TrailMateCenter.Propagation.UnityBridge`
4. `TrailMateCenter.App`（UI 壳层）
5. `TrailMateCenter.Propagation.Adapters.Mock`（离线/测试适配）

## 2. 各模块职责

### 2.1 `TrailMateCenter.Propagation.Contracts`

职责：

- 定义领域契约（DTO/枚举/接口）
- 定义传播计算服务接口：`IPropagationSimulationService`
- 定义 Unity 桥接口：`IPropagationUnityBridge`

边界要求：

- 只放“稳定抽象”，不放网络、IO、UI 代码
- 不依赖 `App`、不依赖 Avalonia

当前文件：

- `src/TrailMateCenter.Propagation.Contracts/PropagationContracts.cs`
- `src/TrailMateCenter.Propagation.Contracts/IPropagationSimulationService.cs`
- `src/TrailMateCenter.Propagation.Contracts/IPropagationUnityBridge.cs`

### 2.2 `TrailMateCenter.Propagation.Client`

职责：

- gRPC 客户端调用与 DTO 映射
- proto 管理与代码生成

边界要求：

- 仅实现 `IPropagationSimulationService`
- 不引用 `App`、不包含 UI 状态逻辑
- 不直接依赖 Unity 桥接代码

当前文件：

- `src/TrailMateCenter.Propagation.Client/GrpcPropagationSimulationService.cs`
- `src/TrailMateCenter.Propagation.Client/Protos/propagation.proto`

### 2.3 `TrailMateCenter.Propagation.UnityBridge`

职责：

- 与 Unity 进程通信（NamedPipe/TCP）
- 消息协议收发、ACK 关联、事件回传

边界要求：

- 仅实现 `IPropagationUnityBridge`
- 不负责传播算法计算
- 不直接调用 ViewModel

当前文件：

- `src/TrailMateCenter.Propagation.UnityBridge/UnityProcessPropagationBridge.cs`

### 2.4 `TrailMateCenter.App`

职责：

- UI 与交互状态编排（View/ViewModel）
- DI 组合根（决定具体实现）

边界要求：

- 不实现 gRPC 细节，不实现进程桥接协议
- 只通过 `Contracts` 抽象访问能力

关键文件：

- `src/TrailMateCenter.App/ViewModels/PropagationViewModel.cs`
- `src/TrailMateCenter.App/App.axaml.cs`

### 2.5 `TrailMateCenter.Propagation.Adapters.Mock`

职责：

- 提供 Fake 版本的传播计算服务与 Unity 桥接
- 供离线演示、联调占位、回退策略使用

边界要求：

- 只能依赖 `Contracts`
- 不依赖 `App`，避免反向耦合

当前文件：

- `src/TrailMateCenter.Propagation.Adapters.Mock/FakePropagationSimulationService.cs`
- `src/TrailMateCenter.Propagation.Adapters.Mock/FakePropagationUnityBridge.cs`

## 3. 依赖方向（必须遵守）

允许依赖图：

```text
TrailMateCenter.App
  -> TrailMateCenter.Propagation.Contracts
  -> TrailMateCenter.Propagation.Client
  -> TrailMateCenter.Propagation.UnityBridge
  -> TrailMateCenter.Propagation.Adapters.Mock (optional)

TrailMateCenter.Propagation.Client
  -> TrailMateCenter.Propagation.Contracts

TrailMateCenter.Propagation.UnityBridge
  -> TrailMateCenter.Propagation.Contracts

TrailMateCenter.Propagation.Adapters.Mock
  -> TrailMateCenter.Propagation.Contracts
```

禁止依赖：

- `Contracts` -> 任何其他传播项目
- `Client` -> `App` / `UnityBridge` / `Adapters.Mock`
- `UnityBridge` -> `App` / `Client` / `Adapters.Mock`
- `Adapters.Mock` -> `App` / `Client` / `UnityBridge`

## 4. DI 组装规范

组合根统一放在 `App`：

- `IPropagationSimulationService` 默认绑定 `GrpcPropagationSimulationService`
- `IPropagationUnityBridge` 默认绑定 `UnityProcessPropagationBridge`
- 需要离线模式时，可切换到 Mock 实现（不改 ViewModel）

建议做法：

- 使用环境变量开关（示例：`TRAILMATE_PROPAGATION_USE_MOCK=true`）
- 仅在 `App.axaml.cs` 修改注入映射，不在业务层写 `if/else`

## 5. 变更准入规则

新增功能时应遵循：

1. 新增字段/能力，先改 `Contracts`。
2. 协议变更，先改 `Client` 或 `UnityBridge`。
3. UI 展示与状态流，只改 `App`（ViewModel/View）。
4. 演示占位逻辑，只改 `Adapters.Mock`。

拒绝模式：

- 在 `App` 里直接写 gRPC DTO 映射代码。
- 在 `App` 里直接写 NamedPipe/TCP 协议解析。
- 在 `Client` 或 `UnityBridge` 内部直接操作 Avalonia 控件。

## 6. 版本与兼容策略

推荐版本策略：

- `Contracts` 作为版本锚点（优先保持向后兼容）
- `Client` 与后端 proto 同步演进
- `UnityBridge` 协议升级时，通过文档声明协议版本增量

新增字段原则：

- 优先“追加字段”，避免破坏旧字段含义
- 对缺失字段提供默认值，保证老结果可读

## 7. 提交与评审清单

每个传播功能 PR 至少回答以下问题：

1. 改动属于哪个模块？
2. 是否跨越了禁止依赖边界？
3. `Contracts` 是否需要同步升级？
4. 是否提供了 Mock/真实实现双路径可用性？
5. 是否更新了对应实现文档（如 gRPC DTO、Unity 协议）？

