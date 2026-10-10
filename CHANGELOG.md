# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project follows Semantic Versioning for release tags.

## [Unreleased]

## [0.0.5-alpha] - 2026-10-10

### Added

- TMAP export for OSM, text-free terrain and satellite maps, with independent zoom ranges and categorized SD-card output folders.
- Offline POI search indexes, display annotations and administrative hierarchy inside OSM packages, with matching LVGL font subsets generated from names, aliases and administrative paths.
- Local text-free terrain rendering from Tilezen Terrarium elevation data, and main-map preview of registered TMAP pixels and annotations.

### Changed

- Regional and zoom-specific indexed packages replace large collections of individual tile files. Native pixel output lets supported Trail Mate firmware read map data directly without PNG decoding.
- POI text remains separate from map pixels, while indexed display and search data replaces separate POI JSON files. Generated fonts are exported to `trailmate/packs/fonts` beside `maps/tmap` for SD installation.

## [0.0.4-alpha] - 2026-09-15

### Added

- Added local map rendering from regional OpenStreetMap `.osm.pbf` data, producing 256×256 PNG tiles in the Trail Mate SD-card layout without relying on third-party rendered basemaps.
- Added full-zoom text-free map packs with separate road, place, and facility annotations. Road annotations include simplified tile-local geometry so names can be placed on visible road segments.
- Added outdoor and urban annotation presets, with independent enable switches and zoom ranges for place names, road names, and points of interest in map-pack and offline-cache dialogs.
- Added automatic LVGL font-subset generation for exported annotation names, including font manifests, coverage ranges, and the bundled Noto font license. Required font resources are exported alongside the map for copying to an SD card.
- Added a local map-style preview showing text-free geometry and annotations with transparent text backgrounds and no white halo.

### Changed

- New map-pack configurations use unified dynamic annotations. Existing configurations retain their legacy POI and hybrid raster-label settings until explicitly changed.
- Saved map regions and resumed exports preserve annotation presets and all three annotation groups' settings. Disabling facility POIs no longer disables road or place annotations in the unified mode.
- Unified annotation packs use version 3 of the annotation manifest; legacy POI packs remain supported by the export workflow. Devices need firmware that supports the version 3 format to display the new annotations.
- Map exports stage their output before replacing the destination map and retain the previous map directory for recovery. Content-addressed font packages preserve unrelated font and language resources.

### Fixed

- Fixed local PBF exports failing on valid multipolygon boundaries with shared inner-ring edges or retraced segments. Invalid relations with complete known bounds outside the requested area are reported without blocking the export; missing members and relevant invalid geometry still fail validation.
- Fixed stale POI index levels surviving a legacy POI re-export after their zoom levels were disabled.
- Fixed the map-pack export action preparing online basemap tiles when unified local annotations were enabled but the legacy POI switch was off.

## [0.0.3-alpha] - 2026-07-04

- Added map export by administrative area.

## [0.0.2-alpha] - 2026-04-23

### Added

- Added configurable minimum and maximum zoom levels to offline cache setup and persisted the range with saved cache regions.
- Added live offline-cache status text to the saved regions dialog so auto-retry activity is visible while a cache job is running.
- Added a SQLite regression test to verify saved cache regions preserve their configured zoom range.

### Changed

- Updated saved-region cache execution, cache-health inspection, and export flows to honor the configured zoom range instead of always processing the full 0-18 range.
- Improved offline-cache completion messaging to distinguish fully complete runs from partial runs that still have unrecoverable tile failures.

### Fixed

- Fixed low-contrast text in the offline cache configuration dialog by making checkbox and hint text render consistently against the dark theme background.
- Fixed offline cache jobs stopping too early on unstable networks by automatically retrying failed tile batches within the same run before asking the operator to continue manually.

## [0.0.1-alpha] - 2026-04-19

### Added

- Initial alpha release.

[Unreleased]: https://github.com/vicliu624/trail-mate-center/compare/v0.0.5-alpha...HEAD
[0.0.5-alpha]: https://github.com/vicliu624/trail-mate-center/compare/v0.0.4-alpha...v0.0.5-alpha
[0.0.4-alpha]: https://github.com/vicliu624/trail-mate-center/compare/v0.0.3-alpha...v0.0.4-alpha
[0.0.3-alpha]: https://github.com/vicliu624/trail-mate-center/compare/v0.0.2-alpha...v0.0.3-alpha
[0.0.2-alpha]: https://github.com/vicliu624/trail-mate-center/compare/v0.0.1-alpha...v0.0.2-alpha
[0.0.1-alpha]: https://github.com/vicliu624/trail-mate-center/releases/tag/v0.0.1-alpha
