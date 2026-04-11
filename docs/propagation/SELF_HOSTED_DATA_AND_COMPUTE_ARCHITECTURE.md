# SELF_HOSTED_DATA_AND_COMPUTE_ARCHITECTURE

> 面向 `TrailMateCenter` 的“无 GEE 依赖”数据与计算架构，可直接开工。

## 1. 目标与范围

本方案目标是让系统在不依赖 Google Earth Engine 的前提下，完成以下能力：

- 多源地理数据接入（DEM、土地覆盖、植被密度、地表参数）。
- 本地或自托管环境下的数据预处理与版本管理。
- 传播/网络仿真计算服务化。
- Avalonia 主程序与 Unity 3D 的稳定联动。
- 实测数据驱动的参数校准闭环。

本方案不包含：

- 云厂商专属托管服务的强绑定设计。

## 2. 总体原则

- 无 GEE 强依赖：核心数据处理和核心计算必须可在本地运行。
- 数据可追溯：每个仿真结果都能追溯到数据版本、参数版本、模型版本。
- 模型可插拔：传播模型、干扰模型、校准模型独立注册。
- 计算可扩展：从单机并行平滑升级到多机任务调度。
- 结果可解释：输出必须包含损耗分解与可信度信息。

## 3. 总体架构（无 GEE 方案）

```text
┌────────────────────────────────────────────────────────┐
│ TrailMateCenter.App (Avalonia)                        │
│  - 传播页参数输入 / 任务管理 / 2D预览                  │
└───────────────────────┬────────────────────────────────┘
                        │ gRPC
┌───────────────────────▼────────────────────────────────┐
│ Propagation API Service (.NET)                        │
│  - 任务编排 / 参数校验 / 结果聚合                      │
└───────────┬───────────────────────┬────────────────────┘
            │                       │
┌───────────▼───────────┐   ┌───────▼────────────────────┐
│ Data Pipeline (Python) │   │ Simulation Core (.NET)     │
│ - 数据抓取/清洗/切片    │   │ - 传播模型/网络仿真/优化    │
└───────────┬───────────┘   └───────┬────────────────────┘
            │                       │
┌───────────▼───────────────────────▼────────────────────┐
│ Spatial Data Lake + Catalog                            │
│ - COG/GeoParquet/SQLite Catalog/Tile Cache             │
└───────────┬─────────────────────────────────────────────┘
            │
┌───────────▼─────────────────────────────────────────────┐
│ Unity Propagation Runtime                               │
│ - 3D 地形 / 覆盖层 / 干扰层 / 剖面分析                    │
└─────────────────────────────────────────────────────────┘
```

## 4. 模块拆解与输入输出

## 4.1 数据接入模块（Data Ingest）

职责：

- 拉取 DEM、土地覆盖、植被/树冠等原始数据。
- 记录来源、下载时间、许可证信息、哈希校验。

输入：

- AOI（GeoJSON/WKT）。
- 时间范围（开始/结束日期）。
- 数据源清单（Earthdata/Copernicus/ESA 等）。

输出：

- `data/raw/...` 原始文件（GeoTIFF/NetCDF/HDF）。
- `data/catalog/source_manifest.json` 下载清单。

建议工具：

- Python `requests`、`pystac-client`、`rasterio`。
- 命令行 `gdalinfo` 做元数据校验。

## 4.2 预处理模块（ETL + Normalize）

职责：

- 统一坐标系、重采样、裁切 AOI、分类映射、切片。
- 构建运行时高性能数据格式（COG + tile index）。

输入：

- `data/raw` 原始栅格。
- 标准化规则（CRS、分辨率、NoData、类别映射）。

输出：

- `data/processed/dem/*.tif`（COG）。
- `data/processed/landcover/*.tif`（COG）。
- `data/processed/derived/*.tif`（坡度/曲率/树冠密度等派生层）。
- `data/catalog/dataset_registry.sqlite`（版本目录）。

关键规则：

- 统一栅格 CRS：推荐 `EPSG:3857`（UI 一致）或区域性 `UTM`（计算更稳定）。
- 统一像元对齐：严格 grid alignment，避免路径采样偏移。
- 土地覆盖映射：所有数据源 class code 映射到统一内部枚举。

## 4.3 数据目录与版本模块（Catalog + Lineage）

职责：

- 管理数据集版本、覆盖范围、时间戳、license、checksum。
- 支持按 AOI 和时间查询可用数据版本。

输入：

- 预处理产物元数据。

输出：

- `dataset_registry.sqlite`（表建议见第 7 节）。
- 版本锁定清单（每次仿真会引用一组 version id）。

## 4.4 运行时采样模块（Sampler Service）

职责：

- 提供统一采样接口：高程、土地覆盖、树冠密度、地表参数。

输入：

- 经纬度/投影坐标路径点序列。
- 采样策略（步长、插值方式）。

输出：

- 剖面采样结果（数组）。
- 路径分类长度统计（每类穿越距离）。

核心接口建议：

```csharp
public interface ITerrainSampler
{
    double SampleElevation(double x, double y);
    TerrainProfile SampleProfile(Point2D from, Point2D to, double stepMeters);
}

public interface ILandCoverSampler
{
    LandCoverClass SampleClass(double x, double y);
    PathCoverStats AccumulatePathStats(Point2D from, Point2D to, double stepMeters);
}
```

## 4.5 传播计算模块（Propagation Engine）

职责：

- 计算 FSPL、LOS、绕射、植被衰减、阴影衰落、反射损耗。
- 输出双向链路裕量与概率覆盖。

输入：

- 节点参数（频率、功率、天线、SF/BW/CR）。
- 环境采样结果。
- 模型参数（`α_class`、`σ` 等）。

输出：

- 覆盖栅格（平均 Pr、可靠性概率、裕量等级）。
- 单点链路分解（可解释字段）。

## 4.6 网络仿真模块（Network Simulation）

职责：

- 计算多节点干扰、SINR、冲突概率、容量估算。
- 输出中继候选方案评分。

输入：

- 节点集合、业务负载参数、传播结果场。

输出：

- 干扰图、容量图、TopN 方案。

## 4.7 校准模块（Calibration）

职责：

- 根据实测 RSSI + GPS 轨迹拟合模型参数。

输入：

- 观测数据集（带时间、位置、链路方向、RSSI、SNR）。
- 当前参数种子值。

输出：

- 新参数集（`α_class`、`σ`、反射系数等）。
- 误差报告（MAE/RMSE/分场景误差）。

## 4.8 可视化模块（Unity Runtime）

职责：

- 3D 地形、覆盖图、概率图、干扰图、容量图渲染。
- 剖面分析与路径分解展示。

输入：

- 计算服务输出的栅格、矢量、统计结果。

输出：

- 可交互 3D 视图状态。
- 截图/报告素材。

## 5. 推荐技术栈（自托管优先）

数据处理层：

- Python 3.11+
- `rasterio`, `xarray`, `numpy`, `geopandas`, `pyproj`
- GDAL CLI（`gdalwarp`, `gdal_translate`, `gdalbuildvrt`）

计算服务层：

- .NET 8
- `Grpc.AspNetCore`
- `NetTopologySuite`
- `CommunityToolkit.HighPerformance`（可选）

存储层：

- 栅格：COG（GeoTIFF）
- 瓦片缓存：本地文件缓存（按 z/x/y 或内部 tile id）
- 元数据目录：SQLite
- 结果快照：Parquet/JSON + PNG/GeoTIFF

可视化层：

- Avalonia（主壳）
- Unity（3D 专项）

## 6. 目录结构建议（可直接建仓）

```text
docs/propagation/
  SELF_HOSTED_DATA_AND_COMPUTE_ARCHITECTURE.md
  PROPAGATION_TAB_IMPLEMENTATION_PLAN.md

src/
  TrailMateCenter.Propagation.Api/
  TrailMateCenter.Propagation.Core/
  TrailMateCenter.Propagation.Contracts/
  TrailMateCenter.Propagation.Calibration/

tools/
  propagation-data-pipeline/
    ingest/
    preprocess/
    validators/
    configs/

data/
  raw/
  processed/
  derived/
  catalog/
  cache/
  outputs/
```

## 7. 数据目录表结构（SQLite）

`datasets`：

- `dataset_id` TEXT PRIMARY KEY
- `dataset_type` TEXT（dem/landcover/treecover/surface）
- `source_name` TEXT
- `source_url` TEXT
- `license` TEXT
- `crs` TEXT
- `resolution_m` REAL
- `bbox_wkt` TEXT
- `time_start` TEXT
- `time_end` TEXT
- `checksum` TEXT
- `created_at` TEXT
- `file_path` TEXT

`simulation_runs`：

- `run_id` TEXT PRIMARY KEY
- `model_version` TEXT
- `param_hash` TEXT
- `dataset_bundle` TEXT（JSON）
- `status` TEXT
- `started_at` TEXT
- `finished_at` TEXT
- `output_path` TEXT

`calibration_runs`：

- `calibration_id` TEXT PRIMARY KEY
- `input_dataset_id` TEXT
- `param_before` TEXT
- `param_after` TEXT
- `mae_before` REAL
- `mae_after` REAL
- `rmse_before` REAL
- `rmse_after` REAL
- `created_at` TEXT

## 8. gRPC 契约（最小可开工版本）

服务：

- `StartSimulation(SimulationRequest) -> SimulationJob`
- `GetSimulationStatus(JobId) -> SimulationStatus`
- `GetCoverageResult(JobId) -> CoverageResult`
- `StartCalibration(CalibrationRequest) -> CalibrationJob`
- `GetCalibrationStatus(JobId) -> CalibrationStatus`

`SimulationRequest` 核心字段：

- `aoi`
- `nodes`
- `radio_profile`
- `model_profile`
- `dataset_selector`
- `output_layers`

`CoverageResult` 核心字段：

- `mean_coverage_raster_uri`
- `reliability_coverage_raster_uri`
- `interference_raster_uri`
- `capacity_raster_uri`
- `link_budget_samples`

## 9. 实施清单（可开工）

## 9.1 第 0 阶段（1 周）：脚手架与标准

- 建立 `TrailMateCenter.Propagation.*` 项目骨架。
- 建立 `tools/propagation-data-pipeline` 脚本工程。
- 固化 CRS/分辨率/NoData/类别映射标准。
- 初始化 `dataset_registry.sqlite`。

交付物：

- 可运行的空服务。
- 可执行的数据校验脚本模板。

## 9.2 第 1 阶段（2 周）：数据流水线打通

- 打通 DEM + LandCover 数据下载与预处理。
- 完成 AOI 裁切、重投影、COG 输出。
- 完成统一土地覆盖枚举映射。

交付物：

- 至少 1 个 AOI 的标准化数据包。
- 数据目录可查可追溯。

## 9.3 第 2 阶段（2 周）：传播基础能力

- 实现 LOS + FSPL + 单刀锋绕射。
- 接入路径累计植被损耗。
- 输出单点链路损耗分解。

交付物：

- 覆盖栅格与链路明细 JSON。
- 传播计算单元测试。

## 9.4 第 3 阶段（2 周）：网络级能力

- 实现 SINR、冲突概率、容量估算。
- 实现山脊候选点提取 + 贪心部署优化。

交付物：

- 干扰/容量图层。
- TopN 中继方案报告。

## 9.5 第 4 阶段（2 周）：UI 与 3D 联动

- Avalonia 传播标签页接入 gRPC。
- Unity 读取结果图层并联动展示。
- 完成任务队列与结果回放。

交付物：

- 从参数输入到 2D/3D 展示的端到端流程。

## 9.6 第 5 阶段（持续）：校准与精度提升

- 导入实测数据。
- 拟合 `α_class`、`σ`。
- 输出校准前后误差对比。

交付物：

- 校准报告与参数版本管理机制。

## 10. 验收标准（开工版）

- 功能验收：
  可在无 GEE 环境下完成一次完整仿真并生成可视化结果。
- 工程验收：
  每次仿真均可追溯数据版本、参数版本、模型版本。
- 性能验收：
  `10km x 10km @ 50m` 基础覆盖计算在 8 核 CPU 下 `<= 3s`。
- 质量验收：
  关键模型单元测试通过，结果字段完整。

## 11. 风险与应对

- 风险：数据源分类体系不一致。
  应对：统一 `LandCoverClass` 映射表，版本化管理。
- 风险：高分辨率数据导致计算慢。
  应对：分层缓存 + 分块并行 + 预计算派生层。
- 风险：参数初值不准确导致误差大。
  应对：尽早引入实测校准流程，不等待功能“全部完成”后再校准。

## 12. 立即执行的首批任务（本周）

1. 新建 `src/TrailMateCenter.Propagation.Contracts` 定义 gRPC DTO 与模型枚举。
2. 新建 `src/TrailMateCenter.Propagation.Core` 实现 `ITerrainSampler`、`ILandCoverSampler`。
3. 新建 `tools/propagation-data-pipeline` 并完成 DEM + LandCover 预处理脚本。
4. 新建 `data/catalog/dataset_registry.sqlite` 并写入首个 AOI 数据清单。
5. 在 `TrailMateCenter.App` 增加传播页任务入口并打通 `StartSimulation` 调用。

---

这份文档是“无 GEE 依赖”的启动蓝图。后续如需，我可以继续补一份 `DATA_SOURCE_LICENSE_MATRIX.md`，把每个数据源的许可、署名、更新频率、分辨率和适用场景整理成执行表。
