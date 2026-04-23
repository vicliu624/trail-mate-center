# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project follows Semantic Versioning for release tags.

## [Unreleased]

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

[Unreleased]: https://github.com/vicliu624/trail-mate-center/compare/v0.0.2-alpha...HEAD
[0.0.2-alpha]: https://github.com/vicliu624/trail-mate-center/compare/v0.0.1-alpha...v0.0.2-alpha
[0.0.1-alpha]: https://github.com/vicliu624/trail-mate-center/releases/tag/v0.0.1-alpha
