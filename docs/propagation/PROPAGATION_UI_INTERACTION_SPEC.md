# PROPAGATION_UI_INTERACTION_SPEC

> 传播规划页面 UI 与交互实现规格（贴合参考效果图，可直接开工）

## 1. 目标

本规格用于指导 `TrailMateCenter` 中“传播”页面的界面与交互实现，目标是构建“战术指挥台”风格的专业工作台，而非传统表单页。

关键要求：

- 贴合参考视觉：深色战术主题、顶部模式导航、中央 3D 主视图、左右决策面板、底部剖面任务带。
- 业务可执行：支持参数输入、仿真触发、结果解释、优化决策、证据追溯。
- 结构可扩展：支持后续加入干扰分析、中继优化、高级建模、校准能力。

## 2. 页面骨架（固定五区）

## 2.1 顶部模式导航栏（Top Mode Bar）

位置：页面最上方，横向全宽。

功能：

- 模式切换按钮组：
  - `Coverage Map`
  - `Interference Analysis`
  - `Relay Optimization`
  - `Advanced Modeling`
- 全局操作：
  - `Start`（开始）
  - `Pause`（暂停）
  - `Cancel`（取消）
  - `Export`（导出）
- 状态提示：
  - 当前任务状态（Idle / Running / Cached / Failed）
  - 数据版本简码
  - 模型版本简码

交互要求：

- 模式切换不重置 AOI、相机视角、选中节点。
- 模式切换只替换主图层与右侧分析项，不触发自动重算（除非参数变化）。

## 2.2 左侧控制面板（Control Panel）

位置：主视图左侧固定宽度面板，支持折叠。

一级分组：

- `Quick Parameters`
- `Run Simulation`
- `Advanced`
- `Presets`

字段（最小实现）：

- `FrequencyMHz`
- `TxPowerDbm`
- `SpreadingFactor`
- `EnvironmentLossDb`
- `UplinkProfile`
- `DownlinkProfile`
- `VegetationAlphaSparse`
- `VegetationAlphaDense`
- `ShadowSigmaDb`
- `ReflectionCoeff`

按钮：

- `Run Simulation`
- `Optimize Relays`
- `Analyze Interference`
- `Monte Carlo`

交互要求：

- 分组可折叠，默认仅展开 `Quick Parameters` 与 `Run Simulation`。
- 参数修改后进入 `Dirty` 状态，提示“结果已过期，需重算”。

## 2.3 中央主战场视图（Main Battlefield View）

位置：页面中央最大区域，视觉权重最高。

内容：

- 3D 地形主画布（Unity Runtime）
- 覆盖热力叠加层
- 节点标注（Base Station / Node A / Node B）
- 候选中继点标注（Potential Relay Site）
- 传播半透明球罩
- 路径虚线（LOS / Multi-hop / Reflection）
- 图例卡（左上：信号等级；右上：可靠性等级）

交互要求：

- 单击地形任一点：更新右侧分析卡与底部剖面统计。
- 拖拽节点：移动节点并进入局部重算预览态。
- Shift + 拖拽：绘制剖面 A-B 线。
- 鼠标滚轮：缩放；右键拖拽：旋转视角；中键拖拽：平移。

## 2.4 右侧分析面板（Analysis & Results）

位置：主视图右侧固定宽度面板。

固定卡片顺序：

- `Link Card`
  - Downlink RSSI
  - Uplink RSSI
  - Reliability
- `Loss Breakdown`
  - FSPL
  - Diffraction
  - Vegetation
  - Reflection
  - Shadow
- `Coverage Probability`
  - 95% Reliable
  - 80% Reliable
- `Network Metrics`
  - SINR
  - Conflict Rate
  - Max Capacity
- `Tasks / Evidence`
  - Data Version
  - Model Version
  - Parameter Hash

交互要求：

- 所有指标支持悬浮提示（数据来源、公式说明）。
- 指标变化高亮（例如值变化时短暂闪烁 500ms）。

## 2.5 底部剖面任务带（Bottom Profile & Operations Strip）

位置：主视图下方，横向全宽。

标签页：

- `Profile`
- `Tasks`
- `Metrics`
- `Evidence`

Profile 页内容：

- 剖面小视图（节点、路径、关键障碍）
- 损耗贡献条（FSPL/绕射/植被/阴影）
- 快速指标条（Distance / Margin / SINR / Reliability）

Tasks 页内容：

- 任务队列表格（状态、进度、耗时、缓存命中）
- 可操作项（暂停、恢复、取消、重试）

Evidence 页内容：

- 当前结果引用的数据集版本
- 模型参数快照
- 输出文件链接（GeoTIFF/PNG/CSV/JSON）

## 3. 模式差异矩阵

## 3.1 Coverage Map

- 主图层：平均覆盖热图 + 可靠性叠层
- 右侧重点：Link Card、Loss Breakdown、Coverage Probability
- 底部重点：Profile

## 3.2 Interference Analysis

- 主图层：干扰热图 + 冲突分区
- 右侧重点：Network Metrics（SINR/Conflict/Capacity）
- 底部重点：Metrics（干扰分布直方图）

## 3.3 Relay Optimization

- 主图层：候选中继点 + TopN 方案覆盖差分
- 右侧重点：方案评分与收益代价分解
- 底部重点：Metrics（方案对比图）

## 3.4 Advanced Modeling

- 主图层：Monte Carlo 稳定性与不确定区纹理
- 右侧重点：95%可靠性、置信区间、校准偏差
- 底部重点：Evidence（参数版本与校准记录）

## 4. 视觉系统规范

## 4.1 色彩语义

- `Strong`：绿色
- `Moderate`：黄色
- `Weak`：橙色
- `Poor`：红色
- `No Signal / Interference`：灰色

可靠性色标：

- 95%：绿色高亮
- 80%：黄色高亮

干扰色标：

- 低干扰：黄绿
- 中干扰：橙
- 高干扰：红

## 4.2 样式原则

- 深色背景 + 半透明玻璃卡片。
- 卡片边框细亮线（军事面板风）。
- 操作按钮使用梯度高亮与明确 hover/pressed 状态。
- 数据卡字体层级清晰：标题 > 主指标 > 次指标 > 注释。

## 5. 状态机（页面级）

页面状态：

- `Idle`
- `Dirty`
- `Running`
- `Paused`
- `Completed`
- `Failed`

状态迁移：

- `Idle -> Dirty`：参数变更。
- `Dirty -> Running`：点击 Run。
- `Running -> Paused`：点击 Pause。
- `Paused -> Running`：点击 Resume。
- `Running -> Completed`：任务成功。
- `Running -> Failed`：任务失败。
- `Completed -> Dirty`：任何参数变更。

UI 行为约束：

- `Running` 时禁用会破坏一致性的参数项（例如数据源切换）。
- `Failed` 显示可重试按钮与错误详情折叠区。

## 6. 快捷键与操作效率

- `1/2/3/4`：切换四大模式。
- `R`：Run Simulation。
- `P`：Pause/Resume。
- `Esc`：取消当前绘制/选区。
- `Shift + Drag`：绘制剖面线。
- `Ctrl + E`：导出当前结果快照。

## 7. 组件命名与绑定建议（Avalonia）

视图文件：

- `src/TrailMateCenter.App/Views/PropagationView.axaml`
- `src/TrailMateCenter.App/Views/PropagationView.axaml.cs`

ViewModel：

- `src/TrailMateCenter.App/ViewModels/PropagationViewModel.cs`

关键属性（示例）：

- `SelectedPropagationMode`
- `IsSimulationRunning`
- `IsParametersDirty`
- `FrequencyMHz`
- `TxPowerDbm`
- `SpreadingFactor`
- `EnvironmentLossDb`
- `DownlinkRssiDbm`
- `UplinkRssiDbm`
- `Reliability95`
- `Reliability80`
- `SinrDb`
- `ConflictRate`
- `MaxCapacity`
- `ActiveTasks`
- `SelectedTask`

关键命令（示例）：

- `RunSimulationCommand`
- `PauseSimulationCommand`
- `CancelSimulationCommand`
- `OptimizeRelaysCommand`
- `RunMonteCarloCommand`
- `AnalyzeInterferenceCommand`
- `ExportResultCommand`

## 8. 与计算层的事件契约（UI 角度）

UI -> Service：

- `StartSimulation`
- `PauseSimulation`
- `CancelSimulation`
- `GetLatestResult`
- `RunOptimization`
- `RunCalibration`

Service -> UI：

- `SimulationProgressUpdated`
- `SimulationCompleted`
- `SimulationFailed`
- `ResultLayersUpdated`
- `MetricsUpdated`

Unity -> UI：

- `MapPointSelected`
- `ProfileLineChanged`
- `CameraStateChanged`

## 9. 异常与空态设计

空态：

- 无 AOI：提示“请先选择仿真区域”。
- 无节点：提示“请先放置至少一个发射节点”。
- 无结果：显示最近一次成功任务入口。

异常态：

- 数据缺失：显示缺失数据层名称与补救建议。
- 版本不兼容：显示建议重算与迁移说明。
- 服务离线：显示重连按钮与离线只读模式。

## 10. 可用性验收标准（UI）

- 30 秒内完成“改参数 -> 跑仿真 -> 看结果”闭环。
- 用户可在 2 次点击内看到当前点的损耗分解。
- 模式切换后 500ms 内完成主图层切换（不含重算）。
- 任务状态可见且可控（暂停/恢复/取消）成功率 100%。

## 11. 第一版实现边界（建议）

先实现：

- 固定五区布局。
- 四模式切换框架。
- Coverage 与 Interference 两个模式可用。
- 右侧四个核心结果卡（Link/Loss/Probability/Network）。
- 底部 Profile + Tasks 两个标签页。

第二版再补：

- Relay Optimization 完整 TopN 方案对比。
- Advanced Modeling（Monte Carlo 与校准展示）。
- Unity 双向交互增强（拖点局部重算与路径编辑）。

---

本规格优先保证“视觉方向与决策效率”一致，再分阶段补齐复杂能力。开发实现以 Avalonia 主壳 + Unity 专项视图联动为准。
