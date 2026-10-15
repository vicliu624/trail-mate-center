# Desktop navigation and contour workflow

Open the existing map-pack builder, then choose **制作导航与等高线地图 / Build
navigation and contour map**. This opens a separate outdoor workflow, preserving
the existing raster export settings. Both Chinese and English resources are
provided. An existing outdoor TMAP can also be opened for preview without Python
or a producer setup.

1. Choose a local `.osm.pbf`/`.osm` source. The current source selection is carried
   over from the old builder. Select hiking or cycling. Optionally add/remove
   up to 32 compatible regional sources; ordinary controls do not require JSON.
2. Optionally include elevation and contours. Choose a named HGT/HGT.gz tile,
   enter source attribution and select contour interval. The crop starts from
   the main map selection and is editable. Tile-name coverage checks prevent
   launching an out-of-tile crop and expand the correction controls. HGT import
   and contour extraction run on the host, not the MCU.
3. Choose output parent and a folder name. Existing output is never overwritten.
   The destination is shown before creation. The build button is disabled with
   a specific missing-input message until the required configuration is ready.

Advanced settings contain SDK tools/Python selection, page size and landmark
count. The tools folder is detected from TMAP_TOOLS_DIRECTORY, an app-local
tmap-tools/share directory, installed share/tmap/tools, or a sibling SDK source
tree. Python is detected from PYTHON/PATH, excluding Windows Store aliases.
If setup is missing, Advanced starts expanded. Choose the actual SDK tools and
Python executable; they are explicit host dependencies, not bundled runtimes.

Production shows honest stage text with an indeterminate progress bar rather
than a fabricated percentage. Import/packing CPU work runs off the UI thread.
Cancel stops the producer tree and waits for staging cleanup. Closing during a
job requests cancellation and closes after it finishes; it does not abandon a
hidden export. Failure retains inputs and exposes copyable diagnostic details.
Success retains the output path, byte/node/edge summary and sticky preview/open-
folder actions. Change the name to produce another copy.

## Finished map preview

The preview reads the actual outdoor-v2 `.tmap`, including header/directory and
visited-page CRC32C checks. Roads are teal, contour vectors brown. Use the wheel
to zoom, drag to pan, double-click/Fit map to reset, and layer checkboxes to
compare content. No synthetic basemap is drawn. The preparation panel's earlier
selection outline is explicitly labelled a schematic, not the finished preview.

The desktop reader has an eight-page cache, a 64 MiB physical-read budget,
bounded retained points and sampled road/contour counts. Large-map sampling is
disclosed in the preview; it never modifies the file. It is a content preview,
not a second navigation engine or a simulation of live access rules. Corrupt,
unsupported, cancelled or over-budget previews do not invalidate an already
published output. The legacy v1 main-map provider remains separate.

## Data and coverage boundaries

This GUI currently imports one HGT tile per job; it does not automatically
download global DEMs or silently mosaic terrain. Crop and source attribution are
explicit. The terrain crop does not clip the original road source; roads beyond
supplied elevation retain unknown heights. Prepared outdoor documents and
advanced producer composition remain available through Core/CLI. The HGT job
keeps original source size/hash/reference and generated terrain.json provenance
in manifest.json. It closes the generated source handle before publishing on
Windows. Devices still need the independent outdoor-v2 reader; generating this
file does not update the Trail Mate firmware.

## Validation

Release app and CLI builds pass. The full Center suite passes 133/133, with no
failures or skips, against the final GUI, crop validation and preview reader.
Headless Avalonia tests run the actual localized XAML at 1020×820 Chinese and
820×600 English, input gating, cancellation, close-during-job, retry, existing
map reading and additional sources. Binary fixtures check reverse road geometry,
contour decode, header/directory/page corruption, read budgets and cancellation.

The reproducible smoke tool invokes the same GUI command with actual selected
original OSM way data and the real Skadi N47E009 tile:

```sh
dotnet run --project tools/NavigationMapSmoke -c Release -- \
  ROAD_SOURCE HGT SDK_TOOLS PYTHON NEW_OUTPUT_ROOT [EXISTING_OUTDOOR_TMAP]
```

`artifacts/navigation-ui-real-smoke-v2/gui-evidence.json` records the successful
final run, package hash, statuses and 15 UI-dispatch heartbeats during production.
The produced file has two road nodes/two directed edges and 47 preview contour
chunks. The existing whole Liechtenstein file also previews: 20,432 nodes,
51,736 directed edges, sampled 995 roads and 494 contour chunks. Screenshots
are before.png/completed.png/preview.png/real-country-preview.png in that folder.
These are actual headless Skia renders, not design mockups. Native OS file-picker
interaction and physical-display/DPI behavior have not been manually tested.

The independent C++ SDK also routes across this GUI-produced file successfully:
`sdk-route-evidence.json` records edge `[0]`, two retained states and cost 4449
from the runtime navigation probe. This checks producer-to-SDK interoperability;
the small selected way is not a regional routing or MCU memory benchmark.
The completed-window screenshot verifies that preview/open-folder actions remain
visible in the fixed footer. Localized layout tests also cover the 820×600
minimum window size, where the form scrolls while the footer stays available.

## Numeric elevation profiles

The finished outdoor preview now opens an elevation-profile dialog. Enter two
longitude/latitude endpoints and set sampling spacing and noise threshold. The
curve includes distance/height axes, ascent/descent, extrema, valid-distance
coverage, DEM resolution and sample count. It reads numeric DEM from the actual
file with a bounded page cache and at most 4096 retained samples by default.
The interface calculates a straight terrain section, not a planned trail route.
Unknown elevations remain gaps and never contribute an invented height change.
Long sections that exceed the budget ask the user to increase sampling spacing.
Cancellation and closing cancel background work; SD/file reads do not run on the
UI thread.

`artifacts/navigation-profile-wiki-en-axis` contains the current English profile
screenshot and real numeric reference. The independent C++ default/compact probes
pass 48 comparisons including reverse direction, partial coverage, sample limits
and Busy. See the SDK's `docs/ELEVATION_PROFILES.md` and
`docs/real-profile-evidence.json` for memory and compiler-stack evidence. Wiki
screenshots are English; the application also supports Chinese.
