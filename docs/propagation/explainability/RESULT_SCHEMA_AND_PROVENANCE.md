# RESULT_SCHEMA_AND_PROVENANCE

> 目的：定义结果结构和证据链，保证可追溯、可复算、可审计。

## 1. 结果对象分层

- `run_meta`：运行元信息
- `input_bundle`：输入快照
- `model_outputs`：模型输出
- `analysis_outputs`：分析派生结果
- `provenance`：证据链
- `quality_flags`：质量与告警标记

## 2. 结果 JSON 最小结构（示例）

```json
{
  "run_meta": {
    "run_id": "sim_20260226_001",
    "status": "completed",
    "started_at": "2026-02-26T10:00:00Z",
    "finished_at": "2026-02-26T10:00:08Z",
    "duration_ms": 8123
  },
  "input_bundle": {
    "aoi_id": "aoi_rescue_01",
    "node_set_version": "nodeset_20260226_a",
    "parameter_set_version": "paramset_20260226_01"
  },
  "model_outputs": {
    "mean_coverage_raster": "outputs/sim_001/coverage_mean.tif",
    "reliability_95_raster": "outputs/sim_001/reliability_95.tif",
    "interference_raster": "outputs/sim_001/interference.tif"
  },
  "analysis_outputs": {
    "top_relay_candidates": ["R12", "R03", "R27"],
    "summary": {
      "coverage_area_km2": 12.4,
      "blind_area_ratio": 0.18,
      "avg_margin_db": 9.6
    }
  },
  "provenance": {
    "dataset_bundle": {
      "dem_version": "dem_20260220_v1",
      "landcover_version": "lc_20260218_v3"
    },
    "model_version": "prop_core_0.3.0",
    "git_commit": "abc1234",
    "parameter_hash": "sha256:..."
  },
  "quality_flags": {
    "assumption_flags": ["single_knife_edge"],
    "validity_warnings": []
  }
}
```

## 3. 必填字段清单（P0）

- `run_id`
- `parameter_set_version`
- `parameter_hash`
- `dataset_bundle.dem_version`
- `dataset_bundle.landcover_version`
- `model_version`
- `quality_flags.assumption_flags`
- `quality_flags.validity_warnings`

## 4. 证据链要求

每条结果必须回答：

1. 用了哪份数据？
2. 用了哪个参数集？
3. 用了哪个模型版本？
4. 是否存在有效性告警？

## 5. 存储与导出

- 原始结果：JSON + 栅格文件（GeoTIFF）。
- 摘要结果：SQLite 索引（便于检索）。
- 报告输出：PDF/HTML 必须嵌入 provenance 摘要。

## 6. 用在哪一方

- 数据层：记录数据版本
- 传播计算层：输出模型结果字段
- 网络仿真层：输出干扰/容量字段
- 优化决策层：输出推荐与评分分解
- UI/可视化层：Evidence 面板读取并展示
- 校准学习层：读取历史结果做对比回归
