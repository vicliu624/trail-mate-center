# Explainability Docs

本目录用于沉淀传播功能的“可解释性文档”，覆盖模型假设、参数治理、结果证据链、误差预算、校准协议和决策理由模板。

## 文档清单

1. `MODEL_ASSUMPTION_AND_VALIDITY_MATRIX.md`
2. `PARAMETER_REGISTRY_AND_DEFAULTS.md`
3. `RESULT_SCHEMA_AND_PROVENANCE.md`
4. `UNCERTAINTY_AND_ERROR_BUDGET.md`
5. `CALIBRATION_AND_VALIDATION_PROTOCOL.md`
6. `DECISION_RATIONALE_TEMPLATE.md`
7. `UI_FIELD_TO_EVIDENCE_MAPPING.md`

## 与系统分层关系

- 传播计算层：1,2,3,4
- 网络仿真层：1,3,4
- 优化决策层：1,2,3,6,7
- UI/可视化层：1,3,4,6,7
- 校准学习层：2,4,5

## 使用顺序（建议）

`1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7`
