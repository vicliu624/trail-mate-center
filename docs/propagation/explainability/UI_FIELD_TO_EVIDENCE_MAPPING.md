# UI_FIELD_TO_EVIDENCE_MAPPING

> Purpose: ensure every UI metric is explainable, traceable, and reproducible.

## 1. Scope

This mapping covers the propagation workspace defined in:

- `docs/propagation/PROPAGATION_UI_INTERACTION_SPEC.md`
- `docs/propagation/explainability/PARAMETER_REGISTRY_AND_DEFAULTS.md`
- `docs/propagation/explainability/RESULT_SCHEMA_AND_PROVENANCE.md`
- `docs/propagation/explainability/MODEL_ASSUMPTION_AND_VALIDITY_MATRIX.md`

## 2. Mapping Rules

## 2.1 Naming Conventions

- UI label: user-facing text, e.g. `Downlink RSSI`.
- VM property: Avalonia ViewModel field, e.g. `DownlinkRssiDbm`.
- Result key path: canonical JSON path in result payload, e.g. `analysis_outputs.link.downlink_rssi_dbm`.
- Parameter key: registry key from `PARAMETER_REGISTRY_AND_DEFAULTS.md`.
- Evidence key: metadata/provenance path, e.g. `provenance.dataset_bundle.dem_version`.

## 2.2 Explainability Minimum Contract (per UI field)

A field is explainable only if all items below exist:

1. `value` (numeric/text value)
2. `source` (result key path)
3. `theory_ref` (knowledge doc reference)
4. `parameter_refs` (one or more parameter keys)
5. `dataset_refs` (dataset version keys)
6. `validity_flags` and `uncertainty_flags`

## 3. Global Header Mapping

| UI Label | VM Property | Result Key Path | Evidence Key | Theory Ref | Parameter Keys | Dataset Keys |
|---|---|---|---|---|---|---|
| Task Status | `SimulationStateText` | `run_meta.status` | `run_meta.run_id` | N/A | N/A | N/A |
| Data Version Tag | `DataVersionTag` | N/A | `provenance.dataset_bundle.*` | `16_spatial_data_alignment.md` | N/A | `dem_version`, `landcover_version` |
| Model Version Tag | `ModelVersionTag` | N/A | `provenance.model_version` | `MODEL_ASSUMPTION_AND_VALIDITY_MATRIX.md` | N/A | N/A |
| Parameter Hash Tag | `ParameterHashTag` | N/A | `provenance.parameter_hash` | `PARAMETER_REGISTRY_AND_DEFAULTS.md` | all active | N/A |

## 4. Left Panel Input Mapping

| UI Field | VM Property | Parameter Key | Unit | Default Source | Affects Result Keys | Theory Ref |
|---|---|---|---|---|---|---|
| Frequency | `FrequencyMHz` | `frequency_mhz` | MHz | parameter registry | coverage, link, SINR, capacity | `01_fspl_and_link_budget.md` |
| TX Power | `TxPowerDbm` | `tx_power_dbm` | dBm | parameter registry | coverage, link | `01_fspl_and_link_budget.md` |
| Spreading Factor | `SpreadingFactor` | `uplink_sf`/`downlink_sf` | enum | parameter registry | link feasibility, capacity | `07_bidirectional_link_margin.md` |
| Environment Loss | `EnvironmentLossDb` | `cable_loss_db` + profile | dB | parameter registry | link, margin | `01_fspl_and_link_budget.md` |
| Vegetation Sparse | `VegetationAlphaSparse` | `veg_alpha_sparse` | dB/m | parameter registry | vegetation loss | `05_vegetation_attenuation.md` |
| Vegetation Dense | `VegetationAlphaDense` | `veg_alpha_dense` | dB/m | parameter registry | vegetation loss | `05_vegetation_attenuation.md` |
| Shadow Sigma | `ShadowSigmaDb` | `shadow_sigma_db` | dB | parameter registry | reliability layers | `06_shadow_fading_lognormal.md` |
| Reflection Coeff | `ReflectionCoeff` | `reflection_coeff` | ratio | parameter registry | reflection loss | `13_multipath_ground_reflection.md` |

## 5. Right Panel Mapping (Analysis & Results)

## 5.1 Link Card

| UI Label | VM Property | Result Key Path | Theory Ref | Parameter Keys | Dataset Keys | Uncertainty / Validity |
|---|---|---|---|---|---|---|
| Downlink RSSI | `DownlinkRssiDbm` | `analysis_outputs.link.downlink_rssi_dbm` | `07_bidirectional_link_margin.md` | `frequency_mhz`, `tx_power_dbm`, `downlink_sf` | DEM + Landcover | `quality_flags.validity_warnings` |
| Uplink RSSI | `UplinkRssiDbm` | `analysis_outputs.link.uplink_rssi_dbm` | `07_bidirectional_link_margin.md` | `frequency_mhz`, `tx_power_dbm`, `uplink_sf` | DEM + Landcover | `quality_flags.validity_warnings` |
| Link Feasible | `IsLinkFeasible` | `analysis_outputs.link.link_feasible` | `07_bidirectional_link_margin.md` | uplink/downlink profile keys | DEM + Landcover | `analysis_outputs.link.margin_guardrail` |
| Reliability | `Reliability95` / `Reliability80` | `analysis_outputs.reliability.p95` / `p80` | `08_coverage_probability.md` | `shadow_sigma_db` | DEM + Landcover | `analysis_outputs.reliability.confidence_note` |

## 5.2 Loss Breakdown

| UI Label | VM Property | Result Key Path | Theory Ref | Parameter Keys | Dataset Keys |
|---|---|---|---|---|---|
| FSPL | `FsplDb` | `analysis_outputs.loss_breakdown.fspl_db` | `01_fspl_and_link_budget.md` | `frequency_mhz` | N/A |
| Diffraction | `DiffractionLossDb` | `analysis_outputs.loss_breakdown.diffraction_db` | `03_knife_edge_diffraction.md` | `fresnel_clearance_threshold` | `dem_version` |
| Vegetation | `VegetationLossDb` | `analysis_outputs.loss_breakdown.vegetation_db` | `05_vegetation_attenuation.md` | `veg_alpha_sparse`, `veg_alpha_dense` | `landcover_version` |
| Reflection | `ReflectionLossDb` | `analysis_outputs.loss_breakdown.reflection_db` | `13_multipath_ground_reflection.md` | `reflection_coeff` | `surface_version` (if used) |
| Shadow | `ShadowLossDb` | `analysis_outputs.loss_breakdown.shadow_db` | `06_shadow_fading_lognormal.md` | `shadow_sigma_db` | N/A |

## 5.3 Coverage Probability

| UI Label | VM Property | Result Key Path | Theory Ref | Parameter Keys | Evidence Key |
|---|---|---|---|---|---|
| 95% Reliable Area | `Coverage95Percent` | `analysis_outputs.coverage_probability.area_p95_km2` | `08_coverage_probability.md` | `shadow_sigma_db` | `provenance.parameter_hash` |
| 80% Reliable Area | `Coverage80Percent` | `analysis_outputs.coverage_probability.area_p80_km2` | `08_coverage_probability.md` | `shadow_sigma_db` | `provenance.parameter_hash` |

## 5.4 Network Metrics

| UI Label | VM Property | Result Key Path | Theory Ref | Parameter Keys | Dataset Keys |
|---|---|---|---|---|---|
| SINR | `SinrDb` | `analysis_outputs.network.sinr_db` | `09_sinr_and_lora_interference.md` | `noise_floor_dbm`, SF profile | coverage field |
| Conflict Rate | `ConflictRate` | `analysis_outputs.network.conflict_rate` | `09_sinr_and_lora_interference.md` | load + SF profile | coverage field |
| Max Capacity | `MaxCapacity` | `analysis_outputs.network.max_capacity_nodes` | `10_network_capacity_aloha.md` | `duty_cycle_limit`, `packet_interval_s` | N/A |

## 6. Bottom Tabs Mapping

## 6.1 Profile Tab

| UI Element | VM Property | Result Key Path | Theory Ref | Notes |
|---|---|---|---|---|
| Path Distance | `ProfileDistanceKm` | `analysis_outputs.profile.distance_km` | `02_los_terrain_obstruction.md` | derived from selected path |
| Fresnel Overlay | `ProfileFresnelRadiusM` | `analysis_outputs.profile.fresnel_radius_m` | `04_fresnel_zone_clearance.md` | render-only + value |
| Main Obstacle Point | `ProfileMainObstacle` | `analysis_outputs.profile.main_obstacle` | `03_knife_edge_diffraction.md` | include v and Ld |
| Margin Strip | `ProfileMarginDb` | `analysis_outputs.profile.margin_db` | `07_bidirectional_link_margin.md` | show uplink/downlink both |

## 6.2 Tasks Tab

| UI Element | VM Property | Result Key Path | Evidence Key |
|---|---|---|---|
| Task Progress | `SelectedTask.Progress` | `run_meta.progress_pct` | `run_meta.run_id` |
| Cache Hit | `SelectedTask.IsCacheHit` | `run_meta.cache_hit` | `provenance.parameter_hash` |
| Duration | `SelectedTask.DurationMs` | `run_meta.duration_ms` | `run_meta.started_at`, `finished_at` |

## 6.3 Evidence Tab

| UI Element | VM Property | Result Key Path | Evidence Key |
|---|---|---|---|
| Data Version | `EvidenceDataVersion` | N/A | `provenance.dataset_bundle.*` |
| Model Version | `EvidenceModelVersion` | N/A | `provenance.model_version` |
| Parameter Snapshot | `EvidenceParameterSet` | `input_bundle.parameter_set_version` | `provenance.parameter_hash` |
| Output Files | `EvidenceOutputLinks` | `model_outputs.*` | file existence check |

## 7. Mode-Specific Additional Mapping

| Mode | Additional UI Field | Result Key Path | Theory Ref | Evidence Required |
|---|---|---|---|---|
| Interference Analysis | Interference Heatmap Legend | `analysis_outputs.network.interference_levels` | `09_sinr_and_lora_interference.md` | `noise_floor_dbm`, node set version |
| Relay Optimization | TopN Plan Score | `analysis_outputs.optimization.topn[*].score` | `12_relay_optimization_algorithms.md` | `score_w_*`, candidate set version |
| Advanced Modeling | Monte Carlo CI | `analysis_outputs.uncertainty.ci_*` | `14_monte_carlo_uncertainty.md` | MC seed, iteration count |
| Advanced Modeling | Calibration Delta | `analysis_outputs.calibration.mae_delta` | `15_parameter_calibration.md` | calibration run id |

## 8. Required Schema Extensions (P0)

Current sample in `RESULT_SCHEMA_AND_PROVENANCE.md` is minimal.  
To fully support explainable UI, add these keys:

- `analysis_outputs.link.*`
- `analysis_outputs.loss_breakdown.*`
- `analysis_outputs.coverage_probability.*`
- `analysis_outputs.network.*`
- `analysis_outputs.profile.*`
- `analysis_outputs.optimization.*`
- `analysis_outputs.uncertainty.*`
- `analysis_outputs.calibration.*`
- `run_meta.progress_pct`, `run_meta.cache_hit`

## 9. Tooltip Contract (UI)

Every metric tooltip should include:

1. `Definition`: what this metric means.
2. `Formula`: short equation or model name.
3. `Parameters`: active parameter keys and values.
4. `Data`: dataset versions used.
5. `Warnings`: validity + uncertainty flags.
6. `Last Updated`: run id + timestamp.

## 10. Acceptance Checklist

- Every visible metric can resolve to exactly one result key path.
- Every result key path has at least one theory reference.
- Every high-impact metric has parameter refs and dataset refs.
- Evidence tab can reproduce the run context without source code.
- If provenance is missing, UI shows `Not Explainable Yet` warning state.

## 11. Where This Document Is Used

- UI/VM implementation (`PropagationViewModel` property binding)
- API contract design (`analysis_outputs` payload fields)
- QA test case generation (explainability coverage)
- Report export module (evidence section population)
