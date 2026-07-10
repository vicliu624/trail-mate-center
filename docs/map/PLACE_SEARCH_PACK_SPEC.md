# Trail Mate Place Search Pack

This document defines the Trail Mate Center output contract for offline place
search data. The contract is intentionally separate from map tiles and POI
overlay data.

## Concept Boundaries

Trail Mate Center is the offline geographic data compiler. Trail Mate devices do
not read raw `.osm.pbf` files at runtime. Center reads OSM PBF input while a map
pack is exported and writes device-oriented artifacts.

The exported map pack has distinct data families:

```text
maps/
  base/       Raster map tiles for rendering.
  contour/    Raster contour overlays for rendering.
  poi/        Tile-indexed POI overlay data for map display.

places/
  catalog.json
  packs/
    <pack-id>/
      manifest.json
      places.bin
      names.bin
      licenses.json

routing/
  packs/
    <pack-id>/  Future route-planning graph data.
```

`maps/poi` is a rendering overlay. It may be clipped per tile and optimized for
the current viewport.

`places/packs/<pack-id>` is a search database. It does not inherit tile clipping
or zoom-level semantics from `maps/poi`. A place pack preserves searchable names,
aliases, coordinates, categories, ranking, source identity, and attribution.

Administrative containment is metadata, not directory structure. Country,
province, city, and custom-region exports are sibling packs under
`places/packs/`; `catalog.json` tells the device which packs exist and what
bounds they cover.

## Interaction Contract

The Map Pack Builder interaction does not expose a separate place-search option.

When the export workflow has an OSM PBF source path, Center automatically writes
a place-search pack beside `maps/`. This is independent of whether the POI
overlay checkbox is enabled, because display POI and searchable places are
different data products.

Place-pack failure is reported in export status, but it does not invalidate
successfully exported raster tiles.

## Device Memory Contract

The format is optimized for low device memory, not fastest possible search.

The device should:

- Load `places/catalog.json` to discover candidate packs.
- Pick packs by bounds or user preference without loading their full contents.
- Open a selected pack's `manifest.json` for counts and binary field contracts.
- Sequentially scan `names.bin` records for a query.
- Keep only a small top-N match heap in RAM.
- Use `place_offset` from a name hit to seek into `places.bin` for the selected
  place detail.
- Deduplicate overlapping packs with `key_hash` when multiple packs contain the
  same OSM object.

No full name table, full place table, trie, or JSON parser is required on the
device search path.

## Catalog

`places/catalog.json` is small JSON metadata:

```json
{
  "version": 1,
  "format": "place-search-catalog-v1",
  "packs": [
    {
      "pack_id": "pack-001122aabbcc",
      "name": "Example Region",
      "admin_level": 4,
      "path": "packs/pack-001122aabbcc",
      "bounds": { "west": 97.0, "south": 21.0, "east": 107.0, "north": 30.0 },
      "records": { "place_count": 1000, "name_rows": 1600 }
    }
  ],
  "updated_at": "2026-07-08T00:00:00Z"
}
```

Pack IDs are filesystem-safe ASCII names built from an area slug plus a stable
short hash. The true human-readable area name stays in catalog and manifest.

## Pack Manifest

Each pack writes `manifest.json`:

```json
{
  "version": 1,
  "format": "place-search-binary-v1",
  "pack_id": "pack-001122aabbcc",
  "files": {
    "places": "places.bin",
    "names": "names.bin"
  },
  "binary": {
    "endianness": "little",
    "string_encoding": "utf-8",
    "string_length": "uint16-bytes",
    "max_string_bytes": 512,
    "coordinates": "int32-e7"
  },
  "categories": [
    { "id": 0, "name": "generic" },
    { "id": 1, "name": "admin" }
  ],
  "source": {
    "type": "osm-pbf",
    "provider": "geofabrik",
    "license": "ODbL-1.0"
  },
  "area": {
    "name": "Example Region",
    "admin_level": 4,
    "bounds": { "west": 97.0, "south": 21.0, "east": 107.0, "north": 30.0 }
  },
  "records": {
    "place_count": 1000,
    "name_rows": 1600,
    "category_counts": {
      "settlement": 300,
      "transport": 200
    }
  }
}
```

## Binary Files

All numeric fields are little-endian. Coordinates are signed `int32` values in
E7 degrees. Strings are UTF-8 with a `uint16` byte length and at most 512 bytes.

Each binary file starts with:

```text
char[8] magic
int32   version
uint64  record_count
```

`places.bin` uses magic `TMPLREC1`. Each record is:

```text
uint64  key_hash
int32   lat_e7
int32   lon_e7
uint16  rank
uint16  category_id
uint8   osm_type_id       0 unknown, 1 node, 2 way, 3 relation
int64   osm_id            -1 when unavailable
string  id
string  primary_name
```

`names.bin` uses magic `TMPLNAM1`. Each record is:

```text
uint64  place_offset      byte offset into places.bin
uint64  key_hash
int32   lat_e7
int32   lon_e7
uint16  rank
uint16  category_id
string  display_name
string  normalized
```

`key_hash` is a stable 64-bit hash of source identity. It lets the device
deduplicate the same OSM object across overlapping packs without loading a large
identity map.

## Searchable OSM Tags

The default extraction profile includes named objects from these families:

- `place=*`
- `boundary=administrative`
- `amenity=*`
- `shop=*`
- `tourism=*`
- `leisure=*`
- `historic=*`
- `natural=*`
- `railway=station|halt`
- `aeroway=aerodrome|airport`
- `public_transport=*`
- `healthcare=*`
- `emergency=*`

Records without a usable name are skipped.

The current extractor emits searchable OSM nodes that already carry coordinates.
Named ways and relations need a topology-aware extraction pass to compute safe
representative points, but the device-side pack structure already supports their
source identity through `osm_type_id` and `osm_id`.

## Names

The extractor gathers names from:

- `name`
- `name:<preferred-language>`
- `name:en`
- `name:zh`
- `int_name`
- `official_name`
- `short_name`
- `alt_name`
- `old_name`
- `local_name`

Aliases may contain semicolon-delimited values. Names are de-duplicated while
preserving a stable preference order. Search normalization lowercases text,
removes Latin combining marks, and collapses punctuation and whitespace.

## Licensing

OSM-derived packs preserve source and license metadata. Each pack writes
`licenses.json` and records OSM provenance in `manifest.json`. Device and Center
UI surfaces that expose the data must be able to attribute OpenStreetMap
contributors.
