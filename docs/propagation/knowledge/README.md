# Propagation Theory Knowledge Base

本目录用于沉淀“传播功能”涉及的理论知识，遵循：

- 一个知识点一个文档
- 每个文档都回答：理论是什么、怎么计算、在系统哪里用、用在哪一方

## 目录

1. `01_fspl_and_link_budget.md`：自由空间损耗与链路预算
2. `02_los_terrain_obstruction.md`：地形视距判定（LOS/NLOS）
3. `03_knife_edge_diffraction.md`：单刀锋绕射模型
4. `04_fresnel_zone_clearance.md`：菲涅尔区净空与附加损耗
5. `05_vegetation_attenuation.md`：植被穿越损耗模型
6. `06_shadow_fading_lognormal.md`：对数正态阴影衰落
7. `07_bidirectional_link_margin.md`：双向链路裕量
8. `08_coverage_probability.md`：覆盖概率计算
9. `09_sinr_and_lora_interference.md`：SINR 与 LoRa 干扰机制
10. `10_network_capacity_aloha.md`：ALOHA 容量估算
11. `11_ridge_detection_from_dem.md`：基于 DEM 的山脊候选提取
12. `12_relay_optimization_algorithms.md`：中继部署优化算法
13. `13_multipath_ground_reflection.md`：地面反射与多径
14. `14_monte_carlo_uncertainty.md`：蒙特卡洛不确定性仿真
15. `15_parameter_calibration.md`：参数校准与误差闭环
16. `16_spatial_data_alignment.md`：空间数据对齐与重采样

## 系统分层术语

- `数据层`：数据接入、预处理、目录与版本
- `传播计算层`：链路预算与覆盖栅格计算
- `网络仿真层`：干扰、容量、冲突估算
- `优化决策层`：候选点筛选与方案评分
- `UI/可视化层`：2D/3D 展示、解释卡片、交互
- `校准学习层`：实测回归、参数更新、误差评估

## 建议阅读顺序

`01 -> 02 -> 03 -> 04 -> 05 -> 06 -> 07 -> 08 -> 09 -> 10 -> 11 -> 12 -> 13 -> 14 -> 15 -> 16`
