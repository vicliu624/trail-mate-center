# Map annotation font sources

TMAP font generation uses these source fonts to build small, content-addressed
16 px, 2 bpp LVGL font resources. Source fonts are build inputs, not files to copy
to the device. The existing Noto Sans CJK SC source uses `OFL.txt`; the additional
Noto families use `NotoSans-OFL.txt`.

`map-font-sources.json` records the pinned upstream revision, download URLs and
SHA-256 hashes of the additional sources. Generated resources include license
files and their source hashes.

The generator collects POI names, searchable aliases and available administrative
paths. It checks glyph coverage before publishing the package. Unsupported
characters produce `missing-map-glyphs.json` and stop publication. Private-use
characters have no universal meaning and must not be replaced with invented
glyphs. A successful glyph coverage check does not prove correct shaping of
Arabic or Indic text on the device.

Font resources must be installed under `trailmate/packs/fonts/<resource-id>/`
on the SD card alongside the corresponding TMAP. TMAP sections 52 and 53 record
the required resources, hashes, file sizes and estimated RAM use.
