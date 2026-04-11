# PROPAGATION_VIEWMODEL_PROPERTY_CHECKLIST

> 目标：为 `PropagationViewModel` 提供可直接编码的属性与命令清单，确保与 UI 规格、结果 schema、可解释性映射一致。

## 1. 适用范围

对应文档：

- `docs/propagation/PROPAGATION_UI_INTERACTION_SPEC.md`
- `docs/propagation/explainability/UI_FIELD_TO_EVIDENCE_MAPPING.md`
- `docs/propagation/explainability/RESULT_SCHEMA_AND_PROVENANCE.md`

目标代码文件：

- `src/TrailMateCenter.App/ViewModels/PropagationViewModel.cs`

## 2. 开发约定

- 使用 `CommunityToolkit.Mvvm` 的 `[ObservableProperty]` 生成属性。
- 所有“来自服务结果”的属性必须注明 `Result Key Path`。
- 所有“输入参数”属性必须注明 `Parameter Key`。
- 可解释性字段（版本、哈希、告警）必须常驻可绑定属性。

## 3. 领域类型（建议先定义）

```csharp
public enum PropagationMode
{
    CoverageMap = 0,
    InterferenceAnalysis = 1,
    RelayOptimization = 2,
    AdvancedModeling = 3,
}

public enum SimulationTaskState
{
    Idle = 0,
    Queued = 1,
    Running = 2,
    Paused = 3,
    Completed = 4,
    Failed = 5,
    Canceled = 6,
}
```

## 4. 属性清单

## 4.1 页面状态与模式

| Property | Type | Direction | Source | Notes |
|---|---|---|---|---|
| `SelectedPropagationMode` | `PropagationMode` | UI <-> VM | local state | 对应顶部模式导航 |
| `IsParametersDirty` | `bool` | VM -> UI | local state | 参数修改后置 true |
| `IsSimulationRunning` | `bool` | VM -> UI | task state | Running/Paused 状态显示 |
| `SimulationStateText` | `string` | VM -> UI | `run_meta.status` | 顶栏状态文案 |
| `CanRunSimulation` | `bool` (derived) | VM -> UI | local + task state | 命令可用性 |
| `CanPauseSimulation` | `bool` (derived) | VM -> UI | task state | 命令可用性 |
| `CanCancelSimulation` | `bool` (derived) | VM -> UI | task state | 命令可用性 |

## 4.2 左侧输入参数（Quick + Advanced）

| Property | Type | Parameter Key | Unit | Default |
|---|---|---|---|---|
| `FrequencyMHz` | `double` | `frequency_mhz` | MHz | 915 |
| `TxPowerDbm` | `double` | `tx_power_dbm` | dBm | 20 |
| `SpreadingFactor` | `string`/enum | `uplink_sf`/`downlink_sf` | enum | SF10 |
| `EnvironmentLossDb` | `double` | `cable_loss_db` (+profile) | dB | 6 |
| `VegetationAlphaSparse` | `double` | `veg_alpha_sparse` | dB/m | 0.3 |
| `VegetationAlphaDense` | `double` | `veg_alpha_dense` | dB/m | 0.8 |
| `ShadowSigmaDb` | `double` | `shadow_sigma_db` | dB | 8 |
| `ReflectionCoeff` | `double` | `reflection_coeff` | ratio | 0.2 |
| `UplinkProfile` | `string` | composite | N/A | preset |
| `DownlinkProfile` | `string` | composite | N/A | preset |

附加建议字段：

- `SelectedPresetName`
- `UseMonteCarlo`
- `MonteCarloIterations`
- `SelectedOptimizationAlgorithm`

## 4.3 地图/3D 交互上下文

| Property | Type | Source | Notes |
|---|---|---|---|
| `SelectedPointX` | `double?` | Unity/UI event | 当前点查询坐标 |
| `SelectedPointY` | `double?` | Unity/UI event | 当前点查询坐标 |
| `SelectedNodeId` | `string?` | Unity/UI event | 当前选中节点 |
| `ProfileLineStart` | `Point?` | `ProfileLineChanged` | 剖面起点 |
| `ProfileLineEnd` | `Point?` | `ProfileLineChanged` | 剖面终点 |
| `CameraStateJson` | `string?` | `CameraStateChanged` | 可选：镜头持久化 |

## 4.4 右侧分析卡输出（Result-driven）

| Property | Type | Result Key Path |
|---|---|---|
| `DownlinkRssiDbm` | `double?` | `analysis_outputs.link.downlink_rssi_dbm` |
| `UplinkRssiDbm` | `double?` | `analysis_outputs.link.uplink_rssi_dbm` |
| `DownlinkMarginDb` | `double?` | `analysis_outputs.link.downlink_margin_db` |
| `UplinkMarginDb` | `double?` | `analysis_outputs.link.uplink_margin_db` |
| `IsLinkFeasible` | `bool?` | `analysis_outputs.link.link_feasible` |
| `Reliability95` | `double?` | `analysis_outputs.reliability.p95` |
| `Reliability80` | `double?` | `analysis_outputs.reliability.p80` |
| `FsplDb` | `double?` | `analysis_outputs.loss_breakdown.fspl_db` |
| `DiffractionLossDb` | `double?` | `analysis_outputs.loss_breakdown.diffraction_db` |
| `VegetationLossDb` | `double?` | `analysis_outputs.loss_breakdown.vegetation_db` |
| `ReflectionLossDb` | `double?` | `analysis_outputs.loss_breakdown.reflection_db` |
| `ShadowLossDb` | `double?` | `analysis_outputs.loss_breakdown.shadow_db` |
| `SinrDb` | `double?` | `analysis_outputs.network.sinr_db` |
| `ConflictRate` | `double?` | `analysis_outputs.network.conflict_rate` |
| `MaxCapacity` | `int?` | `analysis_outputs.network.max_capacity_nodes` |

## 4.5 底部 Profile 输出

| Property | Type | Result Key Path |
|---|---|---|
| `ProfileDistanceKm` | `double?` | `analysis_outputs.profile.distance_km` |
| `ProfileFresnelRadiusM` | `double?` | `analysis_outputs.profile.fresnel_radius_m` |
| `ProfileMarginDb` | `double?` | `analysis_outputs.profile.margin_db` |
| `ProfileMainObstacleLabel` | `string?` | `analysis_outputs.profile.main_obstacle.label` |
| `ProfileMainObstacleV` | `double?` | `analysis_outputs.profile.main_obstacle.v` |
| `ProfileMainObstacleLdDb` | `double?` | `analysis_outputs.profile.main_obstacle.ld_db` |

## 4.6 任务队列与证据链

| Property | Type | Result/Evidence Key |
|---|---|---|
| `ActiveTasks` | `ObservableCollection<PropagationTaskItemViewModel>` | local/task cache |
| `SelectedTask` | `PropagationTaskItemViewModel?` | local state |
| `DataVersionTag` | `string` | `provenance.dataset_bundle.*` |
| `ModelVersionTag` | `string` | `provenance.model_version` |
| `ParameterHashTag` | `string` | `provenance.parameter_hash` |
| `AssumptionFlags` | `ObservableCollection<string>` | `quality_flags.assumption_flags` |
| `ValidityWarnings` | `ObservableCollection<string>` | `quality_flags.validity_warnings` |
| `EvidenceOutputLinks` | `ObservableCollection<string>` | `model_outputs.*` |

## 5. 命令清单

| Command | Type | Trigger | CanExecute 条件 | Service Call |
|---|---|---|---|---|
| `RunSimulationCommand` | `IAsyncRelayCommand` | Run 按钮 / `R` | `CanRunSimulation` | `StartSimulation` |
| `PauseSimulationCommand` | `IAsyncRelayCommand` | Pause 按钮 / `P` | `IsSimulationRunning` | `PauseSimulation` |
| `CancelSimulationCommand` | `IAsyncRelayCommand` | Cancel 按钮 | Running/Queued/Paused | `CancelSimulation` |
| `OptimizeRelaysCommand` | `IAsyncRelayCommand` | Optimize 按钮 | latest result exists | `RunOptimization` |
| `AnalyzeInterferenceCommand` | `IAsyncRelayCommand` | Interference 按钮 | latest result exists | `RunInterferenceAnalysis` |
| `RunMonteCarloCommand` | `IAsyncRelayCommand` | Monte Carlo 按钮 | Advanced mode + config valid | `RunMonteCarlo` |
| `ExportResultCommand` | `IAsyncRelayCommand` | Export 按钮 / `Ctrl+E` | completed result exists | `ExportResult` |

## 6. 事件订阅清单

必须处理的服务事件：

- `SimulationProgressUpdated`
- `SimulationCompleted`
- `SimulationFailed`
- `ResultLayersUpdated`
- `MetricsUpdated`

必须处理的 Unity 事件：

- `MapPointSelected`
- `ProfileLineChanged`
- `CameraStateChanged`

## 7. 建议代码骨架

```csharp
public sealed partial class PropagationViewModel : ViewModelBase
{
    [ObservableProperty] private PropagationMode _selectedPropagationMode;
    [ObservableProperty] private bool _isSimulationRunning;
    [ObservableProperty] private bool _isParametersDirty;

    [ObservableProperty] private double _frequencyMHz = 915;
    [ObservableProperty] private double _txPowerDbm = 20;
    [ObservableProperty] private double _vegetationAlphaSparse = 0.3;
    [ObservableProperty] private double _vegetationAlphaDense = 0.8;
    [ObservableProperty] private double _shadowSigmaDb = 8;

    [ObservableProperty] private double? _downlinkRssiDbm;
    [ObservableProperty] private double? _uplinkRssiDbm;
    [ObservableProperty] private double? _sinrDb;

    public IAsyncRelayCommand RunSimulationCommand { get; }
    public IAsyncRelayCommand PauseSimulationCommand { get; }
    public IAsyncRelayCommand CancelSimulationCommand { get; }
}
```

## 8. 实现验收清单

1. UI 规格中出现的核心字段都有对应 VM 属性。
2. 每个结果字段都能映射到 result key path。
3. 每个输入字段都能映射到 parameter key。
4. 任务状态、版本标签、参数哈希在 UI 可见。
5. `Run -> Progress -> Completed/Failed` 事件链闭环可演示。
