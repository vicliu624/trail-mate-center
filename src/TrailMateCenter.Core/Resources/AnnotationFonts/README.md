# Map annotation font sources

TMAP font generation uses these source fonts to build small, content-addressed
16 px, 2 bpp LVGL font resources. Source fonts are build inputs, not files to copy
to the device. The existing Noto Sans CJK SC source uses `OFL.txt`; the additional
Noto script families use `NotoSans-OFL.txt`. Noto Emoji and Jomolhari retain their
own OFL files. Jigmo2 and Jigmo3 are unmodified sources from the official
2025-09-12 archive and retain `Jigmo-LICENSE.txt` (CC0). Jigmo supplies supplementary
CJK glyphs; Jomolhari supplies the GB/T 20542 precomposed Tibetan glyphs found in
the existing China package. Full source fonts remain computer-side inputs.

`map-font-sources.json` records download URLs and SHA-256 hashes. Noto script
fonts use the top-level pinned revision; Jomolhari and Noto Emoji URLs pin their
Google Fonts revision individually. The Jigmo URL identifies its dated archive.
Generated resources include license files and their source hashes.

Noto Sans Lisu covers the Fraser-script administrative names found during the
China administrative-boundary upgrade. It uses the pinned Noto revision and
`NotoSans-OFL.txt`, and only the required glyphs enter device resources.

The generator collects POI names, searchable aliases and available administrative
paths. It checks glyph coverage before publishing the package. Unsupported
characters produce `missing-map-glyphs.json` and stop publication. Private-use
characters have no universal meaning and must not be replaced with invented
glyphs. A successful glyph coverage check does not prove correct shaping of
Arabic or Indic text on the device.

Font resources must be installed under `trailmate/packs/fonts/<resource-id>/`
on the SD card alongside the corresponding TMAP. TMAP sections 52 and 53 record
the required resources, hashes, file sizes and estimated RAM use.
