# Propagation 2D Workbench Architecture

## 1. Decision

The propagation product is redefined as:

> `2D analysis workbench + path profile + explainability cards`

Unity is no longer a dependency of the target product architecture.
3D embedding, Unity bridge transport, external viewport hosting, and camera synchronization are removed from the long-term design baseline.

This is not a temporary fallback. It is the primary architecture for a professional planning and decision system.

## 2. Product Goal

The product is not a terrain viewer. It is an analysis system that must:

- compute radio propagation with explainable physical and statistical models
- compare alternatives for relay placement and network configuration
- expose uncertainty, assumptions, evidence, and calibration state
- support investigation workflows rather than only show one heatmap
- remain stable, testable, auditable, and evolvable over years

The center of gravity is analytical clarity, not immersive rendering.

## 3. Why 2D Is the Correct Primary Architecture

The knowledge base under `docs/propagation/knowledge` is dominated by:

- path-based physical reasoning
- raster-based spatial outputs
- probability and uncertainty surfaces
- multi-scenario comparison
- optimization scoring and evidence chains

These are naturally expressed by:

- map layers
- path profile charts
- breakdown panels
- comparative tables
- evidence and provenance views

They are not inherently dependent on a 3D engine.

2D also improves:

- operational stability
- deterministic rendering
- automation and regression testing
- export fidelity
- auditability of numeric outputs
- maintainability of the desktop codebase

## 4. Architectural Principles

- Analysis-first: every visual element must map back to a model output or evidence item.
- No hidden state: the visible result must be reproducible from persisted inputs, model versions, and data versions.
- One computation, many views: map, profile, cards, metrics, and exports all derive from the same result package.
- Layer composability: every output becomes an explicit analytical layer with metadata, legends, and provenance.
- Long-horizon evolution: new models, data sources, and optimization methods must plug in without redesigning the UI shell.
- Explicit explainability: assumptions, uncertainty, calibration state, and result validity are first-class citizens.
- 2D-native interaction: interactions are designed for map analysis and profile inspection, not translated from 3D camera behavior.

## 5. Complete Product Surface

The workbench is composed of six coordinated surfaces:

1. `Mode bar`
2. `Control panel`
3. `Map workbench`
4. `Analysis panel`
5. `Bottom investigation strip`
6. `Task and evidence rail`

Each surface is persistent and part of the complete product, not a minimal subset.

## 6. Information Architecture

### 6.1 Mode Bar

Top-level modes remain domain-oriented:

- `Coverage`
- `Interference`
- `Relay Optimization`
- `Advanced Modeling`
- `Calibration & Validation`

Mode switching changes active layers, analysis cards, and default tools.
It does not reset AOI, selected path, selected node set, or current scenario unless the user explicitly changes scenario scope.

### 6.2 Control Panel

The left control panel is a structured scenario editor, not a loose form.

Sections:

- `Scenario`
- `Radio Profiles`
- `Environment Models`
- `Uncertainty`
- `Optimization`
- `Execution`
- `Output Layers`
- `Saved Presets`

It must support:

- scenario presets and named templates
- asymmetric uplink and downlink parameters
- model selection and parameter visibility by mode
- validation hints and unit-aware editing
- dirty state with exact recomputation scope

### 6.3 Map Workbench

The center canvas is a 2D analytical map, not a decorative preview.

It contains:

- base map and terrain tint
- DEM-derived hillshade and slope overlays
- node, gateway, relay, and candidate markers
- coverage raster layers
- reliability contour layers
- LOS and multi-hop path overlays
- interference and conflict overlays
- optimization candidate ranking layers
- AOI and constraint geometry
- profile source path and sample handles
- annotation and bookmark overlays

### 6.4 Analysis Panel

The right panel is a persistent explanation surface.

Card families:

- `Link Summary`
- `Loss Breakdown`
- `Coverage Probability`
- `Interference & Capacity`
- `Optimization Score`
- `Uncertainty`
- `Data & Model Provenance`
- `Warnings & Validity`

Cards are mode-aware but use a shared composition system.

### 6.5 Bottom Investigation Strip

The bottom strip is not optional. It is required for serious analysis.

Tabs:

- `Profile`
- `Metrics`
- `Compare`
- `Tasks`
- `Evidence`
- `Calibration`

This strip carries the evidence density that the map alone cannot show.

### 6.6 Task And Evidence Rail

Long-running calculations, cached runs, exports, parameter hashes, and data versions must remain visible across the session.
This avoids the failure mode where analytical state is hidden in background services.

## 7. Full Interaction Model

### 7.1 Map Interactions

- click a cell or marker to inspect a link or area sample
- drag endpoints to redefine a path
- shift-drag to draw an investigation path
- alt-click to pin an evidence point
- box-select to define compare sets
- toggle layers without recomputing when the data is already available
- switch legends between signal, margin, probability, SINR, and uncertainty

### 7.2 Profile Interactions

- inspect terrain profile along the selected path
- overlay LOS line
- overlay first Fresnel zone
- mark obstruction peaks
- mark vegetation segments and clutter segments
- show sampled path distance, elevation, obstruction angle, and margin changes
- compare uplink and downlink along the same profile
- compare baseline and candidate relay path profiles

### 7.3 Explainability Interactions

- hover a metric to reveal formula, inputs, source layer, and assumptions
- click a card to highlight the corresponding spatial evidence on the map
- click a loss component to overlay the relevant contributing segments in the profile
- click an uncertainty statistic to open its error budget panel

### 7.4 Comparison Interactions

- compare two or more runs
- compare baseline versus optimized relay plan
- compare model variants
- compare calibrated versus default parameters
- compare percentile surfaces from Monte Carlo outputs

Comparisons must work across every primary surface:

- map
- profile
- cards
- metrics
- evidence

## 8. Analytical Layer System

The 2D workbench depends on a formal layer model rather than ad-hoc overlays.

Layer categories:

- `Base`
- `Terrain-derived`
- `Coverage`
- `Probability`
- `Interference`
- `Optimization`
- `Constraints`
- `Evidence`
- `Annotations`

Every layer carries:

- layer id
- semantic category
- source run id
- spatial extent
- CRS
- resolution
- value domain
- units
- legend specification
- provenance
- visibility defaults
- blend policy

Layer rendering is data-driven. The UI must not hardcode one-off assumptions for each future model output.

## 9. Complete Result Model

All computations feed a unified result package.

The package contains:

- run metadata
- scenario snapshot
- parameter snapshot
- model version set
- data version set
- raster layers
- vector overlays
- profile samples
- per-link metrics
- per-area aggregate metrics
- uncertainty outputs
- optimization outputs
- explainability objects
- export manifest

This result package is the backbone that lets all views stay consistent.

## 10. Domain Mapping From Knowledge Base

### 10.1 `01 FSPL and Link Budget`

Surface mapping:

- Loss Breakdown card
- Profile cumulative loss plot
- Link Summary card
- Compare tab delta metrics

### 10.2 `02 LOS Terrain Obstruction`

Surface mapping:

- profile terrain cross-section
- LOS overlay
- map path obstruction highlights
- warnings card

### 10.3 `03 Knife-edge Diffraction`

Surface mapping:

- diffraction loss card
- obstruction peak markers in profile
- candidate-path comparison

### 10.4 `04 Fresnel Zone Clearance`

Surface mapping:

- Fresnel envelope in profile
- clearance summary
- obstruction severity markers

### 10.5 `05 Vegetation Attenuation`

Surface mapping:

- clutter or vegetation layer on the map
- vegetation segment overlay in profile
- loss breakdown contribution

### 10.6 `06 Shadow Fading Lognormal`

Surface mapping:

- uncertainty card
- probability contours
- percentile map layers

### 10.7 `07 Bidirectional Link Margin`

Surface mapping:

- uplink/downlink dual card
- profile dual curves
- asymmetry warnings

### 10.8 `08 Coverage Probability`

Surface mapping:

- 95% and 80% reliability layers
- area metrics
- confidence and validity indicators

### 10.9 `09 SINR and LoRa Interference`

Surface mapping:

- SINR map
- conflict rate layer
- capacity and interference cards

### 10.10 `10 Network Capacity ALOHA`

Surface mapping:

- network metrics card
- scenario compare chart
- capacity stress visualization

### 10.11 `11 Ridge Detection From DEM`

Surface mapping:

- candidate ridge overlay
- terrain-derived candidate extraction view
- optimization evidence panel

### 10.12 `12 Relay Optimization Algorithms`

Surface mapping:

- candidate sites
- score decomposition
- top-N solution compare panel
- baseline versus optimized deltas

### 10.13 `13 Multipath Ground Reflection`

Surface mapping:

- optional reflection contribution card
- path classification and validity notes

### 10.14 `14 Monte Carlo Uncertainty`

Surface mapping:

- percentile layers
- variance or confidence layers
- uncertainty compare tab

### 10.15 `15 Parameter Calibration`

Surface mapping:

- calibration tab
- residual summary
- parameter version lineage
- default versus calibrated compare

### 10.16 `16 Spatial Data Alignment`

Surface mapping:

- CRS and resampling evidence card
- input alignment diagnostics
- validity warnings when misalignment risk is present

## 11. Rendering Architecture

The rendering stack should remain desktop-native and 2D-native.

Recommended structure:

```text
TrailMateCenter.App
  ├─ Propagation workspace shell
  ├─ Mapsui-based spatial canvas
  ├─ Skia/Avalonia chart surfaces for profiles and compare views
  ├─ Layer legend and style system
  └─ Evidence and provenance panels
```

Rendering subsystems:

- `MapCanvasHost`
- `PropagationLayerRenderer`
- `LegendRenderer`
- `ProfileChartRenderer`
- `ScenarioCompareRenderer`
- `SelectionAndAnnotationController`

Mapsui remains the spatial container.
Charts and profile visuals should be rendered with the same desktop stack rather than a separate game-engine viewport.

## 12. Desktop Module Boundaries

Target desktop decomposition:

```text
TrailMateCenter.App
  ├─ Views
  │  ├─ PropagationWorkbenchView
  │  ├─ PropagationMapView
  │  ├─ PropagationAnalysisPanel
  │  ├─ PropagationProfileStrip
  │  ├─ PropagationEvidencePanel
  │  └─ PropagationTaskRail
  ├─ ViewModels
  │  ├─ PropagationWorkbenchViewModel
  │  ├─ PropagationMapViewModel
  │  ├─ PropagationSelectionViewModel
  │  ├─ PropagationAnalysisViewModel
  │  ├─ PropagationProfileViewModel
  │  ├─ PropagationCompareViewModel
  │  ├─ PropagationEvidenceViewModel
  │  └─ PropagationTaskViewModel
  └─ UI services
     ├─ LayerStyleRegistry
     ├─ ResultProjectionService
     ├─ ProfileInteractionService
     └─ EvidenceLinkingService

Propagation service/client layer
  ├─ scenario API
  ├─ run orchestration API
  ├─ result retrieval API
  ├─ calibration API
  └─ export API

Propagation contracts
  ├─ run dto
  ├─ layer dto
  ├─ profile dto
  ├─ evidence dto
  ├─ optimization dto
  └─ uncertainty dto
```

The present monolithic `PropagationViewModel` should be decomposed into these concerns.

## 13. Data Contracts

The contract surface must be expanded beyond "result metrics plus one heatmap".

Required DTO families:

- `ScenarioDefinition`
- `RadioProfileDefinition`
- `EnvironmentModelDefinition`
- `RunRequest`
- `RunStatus`
- `RunResultManifest`
- `RasterLayerDescriptor`
- `VectorOverlayDescriptor`
- `ProfileSampleSet`
- `LinkMetricSet`
- `AreaMetricSet`
- `OptimizationCandidate`
- `OptimizationSolution`
- `UncertaintySummary`
- `CalibrationSnapshot`
- `EvidenceReference`
- `AssumptionRecord`
- `WarningRecord`
- `ExportArtifact`

All DTOs must support provenance and version references.

## 14. Explainability Architecture

The workbench must implement explainability as a structured system, not a tooltip collection.

Explainability entities:

- `metric explanation`
- `loss component explanation`
- `assumption record`
- `input lineage`
- `model validity record`
- `uncertainty contribution`
- `decision rationale`

The UI must be able to answer:

- what formula produced this number
- what inputs and defaults were used
- what data versions were involved
- what assumptions were active
- what uncertainty applies
- why this candidate was recommended over another

## 15. Scenario System

The workbench should operate on named scenarios.

A scenario contains:

- AOI
- node inventory
- gateway and relay inventory
- radio profiles
- environment data references
- optimization constraints
- selected models and parameters
- calibration profile
- export settings

Scenario management must support:

- save
- clone
- branch
- compare
- export
- promote to baseline

## 16. Comparison System

Comparison is a first-class architecture concern.

Comparison dimensions:

- run-to-run
- baseline-to-optimized
- model-to-model
- parameter-set-to-parameter-set
- calibrated-to-default
- percentile-to-percentile

Comparison surfaces:

- synchronized map swipe or split compare
- overlaid profile compare
- metric table with delta highlighting
- ranking change panel
- evidence and assumption diff

## 17. Task Orchestration And Caching

The product must support:

- long-running runs
- incremental result availability
- cache hits by scenario and parameter hash
- run cancellation
- pause and resume where the backend supports it
- artifact export retrieval
- explicit stale-state detection after parameter edits

The UI should always show:

- run id
- stage
- progress
- cache status
- input hash
- model version
- data version

## 18. Export Architecture

Export is part of the primary design.

Supported artifact classes:

- map snapshot
- legend-inclusive report image
- profile chart image
- CSV metrics
- GeoTIFF or raster export
- vector overlay export
- JSON result package
- decision rationale report
- evidence bundle

Exports must preserve provenance.

## 19. Calibration And Validation Surface

Calibration is not a backend-only concern.

The dedicated calibration mode should expose:

- measured versus predicted links
- residual distribution
- parameter adjustments
- accepted calibration set
- model applicability range
- validation summary
- rollback history

This ensures the system evolves scientifically rather than by hidden tuning.

## 20. Evolution Without Redesign

The architecture must absorb future additions without breaking the UI shell:

- new terrain-derived layers
- additional clutter models
- new optimization solvers
- additional uncertainty methods
- time-varying network simulation
- route-aware relay planning
- field measurement ingestion
- learned surrogate models

This is enabled by:

- formal layer descriptors
- formal result manifest
- modular view models
- explanation entities
- comparison system

## 21. Decommissioning Unity

The following items are removed from the target architecture:

- `UnityViewportHost`
- `IPropagationUnityBridge`
- `IPropagationUnityProcessManager`
- Unity process lifecycle controls
- Unity-specific diagnostics and telemetry
- camera preset and synchronization flows
- named pipe or TCP bridge protocols
- Unity runtime assets under the propagation product path

Removal principle:

- no new feature may depend on Unity
- no new DTO may be shaped around Unity transport needs
- no user-facing workflow may require launching or attaching Unity

## 22. Delivery Strategy Without Sacrificing Architecture

The system should be built in increments, but every increment must land on the final architecture rather than a throwaway subset.

That means:

- create the final module boundaries first
- create the unified result package first
- create the analytical layer model first
- create the comparison and evidence hooks from the start
- avoid building one-off widgets that later need replacement

Incremental delivery is acceptable.
Architectural shortcuts that erase future evolution are not.

## 23. Immediate Refactoring Targets In The Current Codebase

Current propagation desktop code still embeds Unity assumptions in:

- `src/TrailMateCenter.App/Views/PropagationView.axaml`
- `src/TrailMateCenter.App/ViewModels/PropagationViewModel.cs`
- Unity bridge and process manager projects

Immediate architectural refactoring targets:

- remove Unity commands from the propagation toolbar
- replace the Unity viewport with a `PropagationMapWorkbench`
- split `PropagationViewModel` into map, analysis, profile, evidence, and task sub-models
- replace Unity layer state with formal 2D analytical layer state
- replace Unity diagnostic panel with data provenance and result validity panel

## 24. Source Of Truth

For future implementation decisions, the source-of-truth priority becomes:

1. `docs/propagation/knowledge/*`
2. `docs/propagation/explainability/*`
3. this document
4. DTO and module-boundary implementation docs

Legacy Unity documents remain historical references only and must not drive new design.
