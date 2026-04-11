# Propagation Implementation Docs

This folder contains implementation-level docs for the propagation feature.

## Core Docs

1. `../PROPAGATION_2D_WORKBENCH_ARCHITECTURE.md`
2. `MODULE_BOUNDARY.md`
3. `PROPAGATION_VIEWMODEL_PROPERTY_CHECKLIST.md`
4. `PROPAGATION_GRPC_DTO_DRAFT.md`
5. Runtime scripts in `scripts/propagation/README.md`

## Legacy / Historical Docs

These documents are retained for historical context only. They are no longer the target architecture for the propagation product.

1. `UNITY_BRIDGE_PROTOCOL.md` (legacy, encoding damaged)
2. `UNITY_BRIDGE_PROTOCOL_V2.md`
3. `UNITY_EMBEDDING_AND_STUB_QUICKSTART.md`
4. `UNITY_PRODUCTION_INTEGRATION_PLAN.md`
5. `E2E_REGRESSION_QUICKSTART.md`

## Recommended Read Order

1. `PROPAGATION_VIEWMODEL_PROPERTY_CHECKLIST.md`
2. Read `../PROPAGATION_2D_WORKBENCH_ARCHITECTURE.md` for the target product and desktop architecture.
3. Read `MODULE_BOUNDARY.md` for project structure and ownership boundaries.
4. Read `PROPAGATION_VIEWMODEL_PROPERTY_CHECKLIST.md` for desktop VM fields and command flow.
5. Read `PROPAGATION_GRPC_DTO_DRAFT.md` for API DTO contracts.

Do not use the Unity protocol documents as the baseline for new implementation work.
