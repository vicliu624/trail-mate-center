# Navigation package production and real-map resource evidence

The `navigation` command uses the independent TMAP SDK producer through Core's
`TmapNavigationExportService`. It accepts a local PBF and produces a TMAP outdoor
v2 draft graph artifact. The existing desktop/raster v1 export remains separate;
the new navigation file is not yet a raster/POI preview package for Center's v1
reader. GUI export and a separate outdoor-vector preview are now integrated;
see [desktop workflow](TMAP_NAVIGATION_GUI.md). Optional OutdoorDocumentPath now
composes SDK DEM, vector contours and administrative data with the navigation graph.

```sh
dotnet run --project tools/TmapTool -c Release -- navigation tools/TmapTool/examples/navigation.json
```

Configure PbfPath, ToolsDirectory (the installed SDK's tools directory), a real
Python interpreter, OutputDirectory, explicit TopologyEpoch, PageBytes
(1024/2048/4096), Landmarks (0..8), and TravelMode (foot/bicycle/both). Paths are
relative to the plan. Windows Store Python aliases are not usable interpreters.
The interpreter/SDK are explicit host dependencies, not device dependencies.
The SDK's top-level desktop CMake install now includes host scripts in
`<prefix>/share/tmap/tools` when `TMAP_INSTALL_TOOLS=ON` (desktop default).
Set ToolsDirectory to that installed directory; a source checkout is not
required for producer module lookup. Embedded-only SDK packages may omit it.
This path has been exercised by the actual CLI on the two original-way inputs
from the real seam experiment: installed-tool output is byte-identical to the
source-tool artifact, and default/compact SDK probes pass 20 navigation queries
across both directions and all five integer algorithms. SDK documentation
`INSTALLED_CENTER_PRODUCTION.md` records the exact evidence and small-sample scope.
Optional `OutdoorDocumentPath` refers to an SDK outdoor JSON production document,
relative to the plan. Center keeps the source open during production, records its
size/SHA-256 in `outdoorSource`, and passes it to the same SDK packer. Its supplied
graph is replaced by the PBF graph; DEM profiles and contours are produced by the
SDK. Select geometrically matching source coverage and retain source provenance.
Missing/no-data DEM samples remain explicit missing elevation; this does not
invent terrain outside supplied coverage. This option does not convert legacy
raster contour overlays into vector contours or generate satellite imagery.
Optional `ConditionAt` accepts a local ISO instant with seconds and explicit UTC
offset, for example `2026-10-12T09:00:00+02:00`. It forwards `--condition-at`
to the SDK and records the exact instant and static-snapshot policy in the
manifest. Preserve that manifest: MCU routing does not enforce time validity.
Use a distinct TopologyEpoch for each conditional snapshot. Weekly/daytime
conditions including overnight intervals are supported; unsupported calendars and expressions still fail.
Omit this field for the default profile that rejects applicable conditions.
Alternatively set `RuntimeConditions: true` to preserve supported conditional
road directions/access and endpoint barriers for runtime evaluation. It is mutually
exclusive with ConditionAt. The SDK reader requires caller-owned EdgeRuleReader
storage, explicit query UTC/local offset and runtime-rule section support; older
readers reject required graph flags. Supported node-via and via-way calendar turns
require TurnContextReader and an immutable RuntimeRoutingRules query binding. Center
records RuntimeConditions and its policy in the manifest and verifies the required
runtime producer modules. A complete DEM/contour/admin/ALT/runtime-road/barrier/
turn package can now contain 37 sections. SDK consumers must explicitly select
at least 37 directory slots; the unchanged 32-slot default returns BudgetExceeded.
The actual Center synthetic joint-feature export and interleaved SDK services
are documented in the SDK's docs/FULL_FEATURE_RESOURCES.md. A two-page 1 KiB,
37-slot workspace adds 200 bytes to the compact 32-slot workspace on the measured
host ABI. This producer capability does not prove a physical MCU memory peak.
The GUI uses the same Core export service with staged progress and cancellation.

For original regional topology, add `RegionalPbfPaths` (XML/PBF accepted despite
the legacy field name) to the plan. Relative paths resolve from the plan, and
Center keeps all sources read-only during production and hashes each in
`regionalSources`. `MaximumRegionEntities`/`MaximumRegionReferences` default to
1,000,000/8,000,000 host-only retained limits. Source overlap must agree on stable
IDs, E7 coordinates, tags and ordered references/members under the explicit
compatible TopologyEpoch. Union happens before graph segmentation, so a shape
node can become a junction when a neighbouring way is added. Coordinate proximity
does not join distinct OSM nodes. See the SDK docs/RAW_REGION_SEAMS.md for actual
Center real-way fragments, the measured disconnected control and verified
regional source/patch routing; these selected ways are not complete geographic
region extracts. Invalid limits, duplicate paths and missing producer modules
fail explicitly before publication.

The Center CLI produced `artifacts/navigation/runtime-center-v1` from explicitly
synthetic XML with Monday reverse-direction permission and Tuesday foot-gate
closure. SDK default/small cold probes verify 48 integer algorithm/mode/time/
direction queries each, including exact output edges and no-path cases. Reports
are `tmap/docs/center-runtime-native-evidence.json` and its small counterpart;
source, manifest, package and probe SHA-256 are retained. This verifies the actual
Center production path, not a real regional dataset or physical MCU peaks.
The updated CLI also produced `artifacts/navigation/runtime-turn-center-v1`, a
24,576-byte 1KiB map containing foot road closure, bicycle gate closure and a
three-way conditional foot turn. SDK default/small cold probes pass32 integer
algorithm/mode/day queries each, with exact successful directed edges[0,2,3].
At32 search/output slots with ALT and both rule readers retained, library budgets
are12,534/6,390 bytes; readAt attempts20–102 include injected Busy. Reports are
`tmap/docs/center-runtime-turn-native-evidence.json` and its small counterpart.
Input is explicitly synthetic XML; these prove actual Center production and SDK
queries, not real regional rules, SD latency or physical stack/RAM/PSRAM peaks.
The output directory must be new: publication renames a prepared sibling
directory only after both producer commands succeed. Failure/cancellation removes
that staging directory, preserves input and existing destinations, and stops
the child producer. No map source is downloaded automatically.

Output contains navigation.tmap, graph.json, sections.json and manifest.json.
The manifest records source size/SHA-256, all SDK Python tool hashes, package
size/SHA-256, profile/page/landmark options and topology identity. Unsupported
rules applicable to the selected mode fail explicitly. A foot profile excludes
cycling-only roads/rules; it does not silently drop unsupported foot conditions.
Plans without OutdoorDocumentPath supply no DEM. A document with DEM adds sampled
edge ascent/descent; the route caller still chooses whether to penalize ascent.

Integration evidence: real Monaco PBF plus an explicitly synthetic 9×9 DEM was
followed by a fully real Everest OSM XML + same-region HGT composition. That
1 KiB package is 490,496 B with 5 nodes/10 directed edges, 6 completely covered
by the cropped DEM. SDK reports `REAL_MOUNTAIN.md`,
`real-navigation-everest-report.json` and `real-everest-contour-report.json`
retain source digests, 160 navigation cases and zoom 12/14/16/18 contour budgets.
The small sparse graph is not a large-region navigation benchmark.

Earlier integration evidence: real Monaco PBF plus an explicitly synthetic 9×9 DEM was
produced through Center into a 2,604,032-byte, 1 KiB-page package with capabilities
7 (elevation, contours, routing). It has 11,722 edge terrain index records and
40,606 terrain samples. The SDK C++ inspector opened it with a 3,360-byte package
workspace. This proves composition, not actual Monaco elevations, full navigation
memory, contour rendering performance or mountain accuracy. Actual joint mountain
data/resource evidence is still required.

## Produced real source

The ConditionAt path was also exercised through this command with the same
real PBF, 1 KiB pages and four landmarks at `2026-10-12T09:00:00+02:00`.
It published a 1,782,784-byte package with SHA-256
`99ef8516b8fe50c45063a60b47d311ec4cbb746b15a5e5078602637a02ae27e7`.
The manifest retained the requested offset and snapshot policy. This verifies
the real production path, not the presence of every conditional rule form in
this extract; conditional semantic coverage comes from SDK fixture tests.

Local reproduction on 2026-10-14 used Geofabrik's archived
[Monaco 2026-10-13 PBF](https://download.geofabrik.de/europe/monaco-261013.osm.pbf)
(692,392 bytes, SHA-256
`32c409ec20a3449be6450e7e615a9d575dd7808e7326a8d087d3f4128b394a05`).
Both packages were generated by the Center command, with foot profile and four
ALT landmarks. They contain 4,681 graph nodes and 11,722 directed edges; the
largest of 95 weak components contains 4,514 nodes.

| Page capacity | Produced package bytes | Profile |
|---|---:|---|
| 4096 | 1,761,280 | foot, ALT4 |
| 1024 | 1,782,784 | foot, ALT4 |

Smaller pages increase stored page-header overhead. Resource tests used the C++
SDK reader against these actual files, not the desktop producer's process RSS.
The SDK benchmark reproduces graph.json into matching package bytes, checks
the Center manifest digest, and samples eight directed queries by node-ID
quantiles in the largest weak component (four endpoint pairs, both directions).
All failures remain in the report. The longest sampled successful route is
1,678.43 m; this is an urban regional workload, not long-distance mountain proof.

## Measured configured storage and I/O

Each query starts with cold library caches. Counts include opening the package
and initializing the graph. Highest-capacity Dijkstra (16,384 slots) completed
all queries; all successful algorithm costs agree with that exact baseline.
Every allocated label/queue/output buffer has the stated capacity.

| Algorithm | Slots tested that complete all 8 | 4 KiB host library payload | 1 KiB host library payload | Successful read bytes range, 1 KiB |
|---|---:|---:|---:|---:|
| ALT4 | 2,048 | 92,184 B | 86,040 B | 82,816–5,222,272 |
| Dijkstra | 10,000 | 410,264 B | 404,120 B | 873,344–20,382,592 |
| Floating FIFO | 10,000 | 410,264 B | 404,120 B | 1,909,632–88,052,608 |
| Integer FIFO | 10,000 | 330,104 B | 323,960 B | 1,909,632–88,048,512 |

These are tested configurations, not minimum or universal capacities. At 2,048
slots, the other three algorithms complete only two queries. At 128/512 slots,
ALT completes two/four queries; other algorithms complete none. With Busy every
second read, the 96 compared cases preserve outcomes, costs and state counts.
Integer FIFO saves representation storage, but this real workload also shows
substantial repeated relaxation and I/O compared with ALT.

Payload sums include actual host sizeof of package workspace, package/graph
controls, search cursor and configured label/queue/edge-output arrays. They
exclude allocator metadata, file adapter, query file, task stacks, GPS/display
buffers and other services. Host ABI bytes are not physical MCU RAM/PSRAM peaks.
SD latency, board CPU/energy, linked firmware Flash and task stack watermarks
still require device measurements. A few-KiB device cannot infer complete
navigation support from small control-object sizes.

Additional real runtime-road verification uses the unmodified Geofabrik
Liechtenstein 2026-10-13 PBF with RuntimeConditions=true, Foot, 1 KiB pages and
two ALT landmarks. Center produced 8,091,648 bytes, 20,432 nodes and 51,736
directed edges. The SDK checks all 36 conditional edges at four instants and
320 cold routing queries: 176 succeed, 40 have no path and 104 exhaust the
explicit 32-state budget. Current host typed payload is 12,053 bytes default
or 5,909 bytes compact for this retained workspace. No DEM is included.
See the SDK docs/REAL_RUNTIME_NAVIGATION.md and real-runtime-*-evidence.json
for provenance, per-query results, scope and excluded application storage.

Raw, reproducible evidence is in the SDK's docs/real-navigation-monaco-report.json,
real-navigation-monaco-small-report.json and real-navigation-monaco-busy-report.json.
Each contains source/tool/package/probe digests and all query/capacity results.
Reproduce with tools/benchmark_real_navigation.py and the built
tmap_navigation_resource_probe; use the artifact directory created above.

Center validation: CLI Release build succeeds; 15 navigation-export and existing
TmapTests pass, including invalid profiles, producer startup failure, staging
cleanup and existing-output preservation. SDK host/default and compact tests
and actual S3/C3 compiler resource gates are recorded separately in the SDK.
# Real combined outdoor production evidence

The Center CLI has also produced
`artifacts/navigation/liechtenstein-real-outdoor-v1` from the 2026-10-13
Liechtenstein PBF, real Mapzen Skadi N47E009 elevations and original OSM
administrative relations. The 19,218,432-byte, 28-section package contains
20,432 nodes, 51,736 directed edges, a 617 × 844 DEM and fourteen selected
country/district/municipal relations (33 polygon components).

The reproducible plan is
`artifacts/navigation-source/liechtenstein-real-outdoor-plan-v1.json`;
run `TmapTool navigation` with this plan. It references local source files and
the independent TMAP writer through the existing navigation production path.
The source document retains attribution, datum declaration and hashes.

Independent TMAP reports `docs/REAL_OUTDOOR_JOINT.md` and
`docs/real-outdoor-joint-{native,small}-evidence.json` contain 55 successful
joint cases per storage configuration, checked against source elevations and
GEOS administrative geometry. Configured storage payloads are 21,992 B and
15,848 B on the tested host ABI. These exclude application/allocator/stack
overhead and are not physical board RAM/PSRAM measurements. The shared-cache
Busy tests exposed retry progress defects that were fixed in the reader.

## Regional production from original PBF and elevation sources

`TmapTool region plan.json` invokes `TmapRegionProductionService`. Center prepares
complete regional highway ways and their referenced nodes from the original PBF,
downloads and validates multiple Tilezen/Skadi HGT tiles into a reusable cache,
resamples numeric heights onto an explicit bounded grid, then invokes its existing
`TmapNavigationExportService` and the published SDK writer. This is the Center CLI
production path; the multi-tile preparation controls are not yet exposed in the
desktop dialog. It does not derive height measurements from rendered terrain maps.

Install `tools/TmapTool/requirements-region.txt` into the explicitly selected host
Python environment. The preparation script is distributed with TmapTool. The plan
supplies `PbfPath`, `Area`, `PythonExecutable`, `SdkToolsDirectory`,
`PreparationScript`, `OutputDirectory`, `ElevationCacheDirectory`,
`GridStepDegrees`, `MaximumDemSamples`, `ContourIntervalMeters`, `Landmarks` and
`ExcludeConditionalFeatures`. Output destinations must be new directories;
publication occurs only after both preparation and SDK packing succeed.
Regional plans default to four ALT landmarks; callers can choose zero for a
smaller file. Actual query budgets still need testing. The London production
comparison uses a zero-landmark baseline and a second Center `navigation` export
with four landmarks, reusing Center's prepared roads and numeric terrain.

The default 0.001-degree grid represents at most 112 m N/S spacing, with a
four-million-sample host limit, 64-source-tile limit, 32-by-32 sample blocks,
integer-metre heights and explicit NoData gaps. A ten-metre contour interval does
not imply ten-metre horizontal or vertical accuracy. The mixed-source vertical
datum is marked unknown; no datum conversion is claimed. Source URLs, SHA-256,
dimensions, resampling policy and attribution are retained in `provenance.json`.

`ExcludeConditionalFeatures=false` retains source conditions and requires SDK
runtime compilation; unsupported conditions fail explicitly. With an explicit
`true`, Center conservatively omits conditional ways and from-ways of conditional
turn restrictions, and closes conditional barrier nodes. Counts are recorded;
those roads are not assumed open. This can reduce available routes. Complete
boundary ways can extend outside the DEM bounds, where elevation remains missing.

Raster production remains `TmapTool build`. Its optional
`Tmap.SkipDegenerateAreaRelations=true` permits omission of known, closed relation
members with no outer polygon area and records their IDs in the package's
`.source-geometry.json` report. Missing members, incomplete rings and other
topology failures still fail. The default remains strict.

`TmapTool regional-overview plan.json` extends the existing world/China Shortbread
pipeline to explicit regional polygons, country identity and package key. Center
invokes VersaTiles itself to extract z8–12 vectors from the immutable published
2026-06-08 archive, renders the text-free raster pyramid and compiles searchable
names and annotations. Its production record stores the extract hash and plan.
This overview source is generalized and has an older snapshot than the original
PBF used for the detailed London map; the two dates must not be presented as one.
The global archive is range-read rather than downloaded in full. No routing or
numeric terrain capability is inferred from this v1 overview package.

The regional producer also rejects contour candidates outside a cell's height
range before applying the SDK's marching-squares/stitching semantics. Twenty-four
frozen SDK reference grids and live SDK comparisons cover saddle cells, negative
levels, missing heights and random terrain. The host extraction has a 12-million
segment limit. `ClipContoursToRegion` defaults to true: all four cell samples must
lie inside the selected polygon. The numeric DEM remains available over its full
rectangular grid. For the 2026-10-15 Kunming build, the requested ten-metre contour
spacing exceeded the segment limit; the final plan explicitly selects twenty
metres and polygon clipping. The London package uses ten metres. Sampling spacing
and contour interval are different parameters and are both recorded.

## Regional evidence recorded on 2026-10-15

The selected London and Kunming outdoor packages were generated by Center's
`region` and `navigation` commands with the published TMAP v0.1.1 producer tools.
The London ALT4 export reuses the roads/DEM generated by Center's initial regional
production; the zero-landmark package is retained as the comparison baseline.

| Selected package | Bytes | Graph nodes | Directed edges | Numeric DEM samples | Source contour interval |
| --- | ---: | ---: | ---: | ---: | ---: |
| London, ALT4 | 370,708,480 | 762,287 | 1,794,948 | 348,090 | 10 m |
| Kunming, ALT4 | 180,269,056 | 123,352 | 315,754 | 3,249,486 | 20 m |

Full SDK page validation passes: 90,503 page reads for London ALT4 and 44,009 for
Kunming. Both use 4 KiB physical pages. A smaller compiled cache does not read
these packages: the SDK returns `BudgetExceeded` when the file page exceeds
`TMAP_CACHE_PAGE_BYTES`. Producing 1/2 KiB maps and measuring the corresponding
configuration is required before quoting smaller-cache figures.

Actual query comparisons on the tested 32-bit GCC host:

| Query/configuration | Retained states | Configured typed library payload | Route length |
| --- | ---: | ---: | ---: |
| London centre, heap, capacity 16,384 | 11,786 | 534,472 B | 1,296.88 m |
| Same London query, ALT4, capacity 2,048 | 1,804 | 84,044 B | 1,296.88 m |
| Kunming centre, heap, capacity 16,384 | 4,351 | 534,472 B | 1,524.25 m |
| Same Kunming query, ALT4, capacity 256 | 213 | 19,532 B | 1,524.25 m |

These are configured payloads, not automatic minimum allocations or whole-device
RAM. A fixed budget can still fail other queries. Successful ALT costs match the
larger-budget heap reference queries. The London index adds 24,592,384 bytes to
the zero-landmark file, trading SD storage for fewer retained search states.

The 96-column profile owner requests a measured peak 13,872-byte provider payload
plus a 160-byte owner and caller coordinates on this host. Reset releases all
provider allocations. The 320-by-240 contour viewport with 128 caller-owned
segments retains 13,336 bytes of typed library storage, excluding framebuffer,
display driver, allocator and task stacks. z12/14/16/18 queries complete, adapting
contour density to capacity; z12 can legitimately return no lines. These are not
physical MCU RAM/PSRAM peaks and do not establish that a few-KiB device can run
the complete feature set.

The real Center preview/profile controls open both finished packages. English
headless captures show London at 532.27 m distance / 26 m descent and Kunming at
672.16 m / 165 m descent, with 100% sampled coverage for these selected straight
sections. They agree with SDK profile measurements; the sections are not implicitly
planned walking routes. Source HGT seam checks compare 28,808 shared samples
across eight joins with zero height differences. Unknown mixed-source vertical
datum and the 112 m grid spacing remain explicit.

The maintenance delivery catalog and raw evidence are local generated artifacts:
`artifacts/map-delivery-20261015.json`, `artifacts/MAP_DELIVERY_20261015.txt` and
`artifacts/map-maintenance-20261015`. Source snapshots remain distinct: world,
China and Great Britain overview vectors are 2026-06-08; Kunming PBF is
2026-10-08T20:21:06Z; London PBF is 2026-10-14T20:21:12Z. Existing raster files were
fully validated, and obsolete world/China checksum sidecars were corrected after
preserving their originals. Five London relations whose closed members have no
outer polygon area, and the conditional-road exclusions, are recorded explicitly.
The runtime reader replacement in Trail Mate remains a v1
integration; device navigation/profile/contour UI wiring is separate work.

London imagery is an additional Center raster export using
`artifacts/map-maintenance-20261015/london-imagery-plan.json`: the same extraction
polygon, z16 terrain shading and z16 satellite imagery. Its isolated output is
`artifacts/london-imagery-sd`; only the fully validated terrain/satellite packages
are copied into `artifacts/london-sd`. Keep the existing z13–17 London OSM package
as the authoritative POI/annotation companion. The imagery-only export also
produces an OSM metadata companion; that file must not replace the existing full
OSM raster package. Completion, sizes and hashes are recorded by
`london-imagery-delivery.exit` and `london-imagery-sha256.json` in the maintenance
directory, and successful delivery refreshes the main catalog. Raster terrain
shading and satellite pixels do not replace the independent numeric elevation
package. Acquisition date does not establish the observation date of imagery.

The completed London z16 terrain package is 1,520,721,920 bytes; satellite is
1,522,163,712 bytes. Each contains 11,610 tiles. In addition to Center's complete
metadata-page validation, a full scan through `TmapReader.ReadTile` validates
every covered pixel payload CRC in both delivered files. The English
`london-imagery-preview.png` shows tiles read back from the finished packages.

