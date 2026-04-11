# PROPAGATION_GRPC_DTO_DRAFT

> 目标：提供可直接落地的 gRPC/DTO 草案，保证 UI 字段、结果 schema、可解释性证据链三者一致。

## 1. 适用范围

对齐文档：

- `docs/propagation/implementation/PROPAGATION_VIEWMODEL_PROPERTY_CHECKLIST.md`
- `docs/propagation/explainability/UI_FIELD_TO_EVIDENCE_MAPPING.md`
- `docs/propagation/explainability/RESULT_SCHEMA_AND_PROVENANCE.md`

建议代码位置：

- `src/TrailMateCenter.Propagation.Contracts/Protos/propagation.proto`
- `src/TrailMateCenter.Propagation.Api/*`

## 2. 服务接口（RPC）

| RPC | 请求 | 响应 | 说明 |
|---|---|---|---|
| `StartSimulation` | `StartSimulationRequest` | `StartSimulationResponse` | 提交仿真任务 |
| `GetSimulationStatus` | `GetSimulationStatusRequest` | `SimulationStatusResponse` | 轮询任务状态 |
| `StreamSimulationUpdates` | `StreamSimulationUpdatesRequest` | `stream SimulationUpdateEvent` | 推送进度与阶段事件 |
| `GetSimulationResult` | `GetSimulationResultRequest` | `SimulationResultResponse` | 获取完整结果 |
| `StartOptimization` | `StartOptimizationRequest` | `StartOptimizationResponse` | 启动中继优化 |
| `StartCalibration` | `StartCalibrationRequest` | `StartCalibrationResponse` | 启动校准任务 |
| `CancelJob` | `CancelJobRequest` | `CancelJobResponse` | 取消任务 |

## 3. Proto 草案（核心片段）

```proto
syntax = "proto3";

package propagation.v1;

service PropagationService {
  rpc StartSimulation(StartSimulationRequest) returns (StartSimulationResponse);
  rpc GetSimulationStatus(GetSimulationStatusRequest) returns (SimulationStatusResponse);
  rpc StreamSimulationUpdates(StreamSimulationUpdatesRequest) returns (stream SimulationUpdateEvent);
  rpc GetSimulationResult(GetSimulationResultRequest) returns (SimulationResultResponse);
  rpc StartOptimization(StartOptimizationRequest) returns (StartOptimizationResponse);
  rpc StartCalibration(StartCalibrationRequest) returns (StartCalibrationResponse);
  rpc CancelJob(CancelJobRequest) returns (CancelJobResponse);
}

enum JobState {
  JOB_STATE_UNSPECIFIED = 0;
  JOB_STATE_QUEUED = 1;
  JOB_STATE_RUNNING = 2;
  JOB_STATE_PAUSED = 3;
  JOB_STATE_COMPLETED = 4;
  JOB_STATE_FAILED = 5;
  JOB_STATE_CANCELED = 6;
}

enum PropagationMode {
  MODE_UNSPECIFIED = 0;
  MODE_COVERAGE_MAP = 1;
  MODE_INTERFERENCE_ANALYSIS = 2;
  MODE_RELAY_OPTIMIZATION = 3;
  MODE_ADVANCED_MODELING = 4;
}

message StartSimulationRequest {
  string request_id = 1;
  PropagationMode mode = 2;
  Aoi aoi = 3;
  repeated Node nodes = 4;
  RadioProfile radio_profile = 5;
  ModelProfile model_profile = 6;
  OutputLayerSelection output_layers = 7;
  DatasetSelector dataset_selector = 8;
  string parameter_set_version = 9;
}

message StartSimulationResponse {
  string run_id = 1;
  JobState initial_state = 2;
}

message GetSimulationStatusRequest { string run_id = 1; }

message SimulationStatusResponse {
  string run_id = 1;
  JobState state = 2;
  double progress_pct = 3;
  string stage = 4;
  bool cache_hit = 5;
  string message = 6;
}

message StreamSimulationUpdatesRequest { string run_id = 1; }

message SimulationUpdateEvent {
  string run_id = 1;
  JobState state = 2;
  double progress_pct = 3;
  string stage = 4;
  string timestamp_utc = 5;
}

message GetSimulationResultRequest { string run_id = 1; }

message SimulationResultResponse {
  RunMeta run_meta = 1;
  InputBundle input_bundle = 2;
  ModelOutputs model_outputs = 3;
  AnalysisOutputs analysis_outputs = 4;
  Provenance provenance = 5;
  QualityFlags quality_flags = 6;
}

message AnalysisOutputs {
  LinkOutput link = 1;
  ReliabilityOutput reliability = 2;
  LossBreakdownOutput loss_breakdown = 3;
  CoverageProbabilityOutput coverage_probability = 4;
  NetworkOutput network = 5;
  ProfileOutput profile = 6;
  OptimizationOutput optimization = 7;
  UncertaintyOutput uncertainty = 8;
  CalibrationOutput calibration = 9;
}

message LinkOutput {
  double downlink_rssi_dbm = 1;
  double uplink_rssi_dbm = 2;
  double downlink_margin_db = 3;
  double uplink_margin_db = 4;
  bool link_feasible = 5;
  string margin_guardrail = 6;
}

message ReliabilityOutput {
  double p95 = 1;
  double p80 = 2;
  string confidence_note = 3;
}

message LossBreakdownOutput {
  double fspl_db = 1;
  double diffraction_db = 2;
  double vegetation_db = 3;
  double reflection_db = 4;
  double shadow_db = 5;
}

message CoverageProbabilityOutput {
  double area_p95_km2 = 1;
  double area_p80_km2 = 2;
}

message NetworkOutput {
  double sinr_db = 1;
  double conflict_rate = 2;
  int32 max_capacity_nodes = 3;
  repeated InterferenceLevelBin interference_levels = 4;
}

message InterferenceLevelBin {
  string label = 1;
  double ratio = 2;
}

message ProfileOutput {
  double distance_km = 1;
  double fresnel_radius_m = 2;
  double margin_db = 3;
  MainObstacle main_obstacle = 4;
}

message MainObstacle {
  string label = 1;
  double v = 2;
  double ld_db = 3;
}

message OptimizationOutput {
  repeated RelayPlan topn = 1;
}

message RelayPlan {
  string plan_id = 1;
  double score = 2;
  double coverage_gain = 3;
  double reliability_gain = 4;
  double blind_area_penalty = 5;
  double interference_penalty = 6;
}

message UncertaintyOutput {
  double ci_lower = 1;
  double ci_upper = 2;
  double stability_index = 3;
}

message CalibrationOutput {
  double mae_before = 1;
  double mae_after = 2;
  double mae_delta = 3;
  double rmse_before = 4;
  double rmse_after = 5;
  double rmse_delta = 6;
  string calibration_run_id = 7;
}

message RunMeta {
  string run_id = 1;
  JobState status = 2;
  string started_at_utc = 3;
  string finished_at_utc = 4;
  int64 duration_ms = 5;
  double progress_pct = 6;
  bool cache_hit = 7;
}

message InputBundle {
  string aoi_id = 1;
  string node_set_version = 2;
  string parameter_set_version = 3;
}

message ModelOutputs {
  string mean_coverage_raster_uri = 1;
  string reliability_95_raster_uri = 2;
  string reliability_80_raster_uri = 3;
  string interference_raster_uri = 4;
  string capacity_raster_uri = 5;
}

message Provenance {
  DatasetBundle dataset_bundle = 1;
  string model_version = 2;
  string git_commit = 3;
  string parameter_hash = 4;
}

message DatasetBundle {
  string dem_version = 1;
  string landcover_version = 2;
  string surface_version = 3;
}

message QualityFlags {
  repeated string assumption_flags = 1;
  repeated string validity_warnings = 2;
}

message Aoi { string geojson = 1; }

message Node {
  string node_id = 1;
  double lon = 2;
  double lat = 3;
  double altitude_m = 4;
  double antenna_height_m = 5;
}

message RadioProfile {
  double frequency_mhz = 1;
  double tx_power_dbm = 2;
  double tx_gain_dbi = 3;
  double rx_gain_dbi = 4;
  string uplink_sf = 5;
  string downlink_sf = 6;
}

message ModelProfile {
  bool enable_fresnel = 1;
  bool enable_vegetation = 2;
  bool enable_reflection = 3;
  bool enable_monte_carlo = 4;
  int32 monte_carlo_iterations = 5;
  double shadow_sigma_db = 6;
  double veg_alpha_sparse = 7;
  double veg_alpha_dense = 8;
  double reflection_coeff = 9;
}

message OutputLayerSelection {
  bool include_coverage = 1;
  bool include_reliability = 2;
  bool include_interference = 3;
  bool include_capacity = 4;
  bool include_profile = 5;
}

message DatasetSelector {
  string dem_version = 1;
  string landcover_version = 2;
  string surface_version = 3;
}

message StartOptimizationRequest { string run_id = 1; }
message StartOptimizationResponse { string optimization_job_id = 1; }
message StartCalibrationRequest { string run_id = 1; string calibration_dataset_id = 2; }
message StartCalibrationResponse { string calibration_job_id = 1; }
message CancelJobRequest { string job_id = 1; }
message CancelJobResponse { bool canceled = 1; }
```

## 4. DTO 对齐映射（UI/Schema）

| Proto Field | JSON Key Path | ViewModel Property | Notes |
|---|---|---|---|
| `analysis_outputs.link.downlink_rssi_dbm` | `analysis_outputs.link.downlink_rssi_dbm` | `DownlinkRssiDbm` | Link Card |
| `analysis_outputs.link.uplink_rssi_dbm` | `analysis_outputs.link.uplink_rssi_dbm` | `UplinkRssiDbm` | Link Card |
| `analysis_outputs.loss_breakdown.fspl_db` | `analysis_outputs.loss_breakdown.fspl_db` | `FsplDb` | Loss Breakdown |
| `analysis_outputs.network.sinr_db` | `analysis_outputs.network.sinr_db` | `SinrDb` | Network Metrics |
| `analysis_outputs.profile.distance_km` | `analysis_outputs.profile.distance_km` | `ProfileDistanceKm` | Profile Tab |
| `provenance.parameter_hash` | `provenance.parameter_hash` | `ParameterHashTag` | Evidence |
| `provenance.dataset_bundle.*` | `provenance.dataset_bundle.*` | `DataVersionTag` | Evidence |

## 5. 错误与状态码建议

通用 gRPC Status：

- `InvalidArgument`：参数校验失败
- `FailedPrecondition`：缺少数据版本或结果前置条件
- `Unavailable`：服务不可达
- `DeadlineExceeded`：任务超时
- `Internal`：服务内部错误

业务错误码（放在响应 message/detail）：

- `E_DATASET_MISSING`
- `E_PARAM_OUT_OF_RANGE`
- `E_MODEL_NOT_SUPPORTED`
- `E_RUN_NOT_FOUND`
- `E_RESULT_NOT_READY`

## 6. 版本兼容策略

- 包名固定版本前缀：`propagation.v1`。
- 新增字段只追加、不复用 field number。
- 废弃字段使用 `reserved` 保留号位。
- UI 端必须容忍未知字段（前向兼容）。

## 7. 第一阶段实现边界（P0）

先实现以下最小闭环字段：

- `StartSimulation` + `GetSimulationStatus` + `GetSimulationResult`
- `analysis_outputs.link`
- `analysis_outputs.loss_breakdown`
- `analysis_outputs.coverage_probability`
- `analysis_outputs.network`（sinr/conflict/capacity）
- `provenance` 与 `quality_flags`

第二阶段再补：

- `optimization`
- `uncertainty`
- `calibration`
- `StreamSimulationUpdates`

## 8. 开发验收清单

1. Proto 字段可完整映射到 ViewModel 核心属性。
2. Result payload 包含可解释性所需 provenance 与 flags。
3. UI 在无 `optimization/uncertainty/calibration` 字段时不崩溃。
4. 版本号升级不影响 v1 既有字段解析。
