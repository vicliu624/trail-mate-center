# Real OSM multipolygon regression fixtures

These extracts contain original nodes, ways, tags, roles, IDs and versions. No
geometry or member roles were repaired. Tests run offline and do not download data.

Data: © OpenStreetMap contributors, licensed under ODbL 1.0:
https://www.openstreetmap.org/copyright

- `relation-7815642-v4.osm.pbf`: Étang du Bois d'Amour, only way 547270181
  tagged as an inner member. Its closed geometry is valid. Independent libosmium
  4.3.2 bindings assemble one outer ring and no holes from these original members.
- `relation-6730991-v8.osm.pbf`: Forêt de Loudéac, seven outer ways, seventeen
  inner ways and forty-one node members. All references are present, but outer
  endpoints have odd degrees and libosmium cannot assemble a nonempty area.
- `relation-6730991-v9.osm.pbf`: updated complete relation; libosmium assembles
  five shells and fifteen holes and GEOS reports valid geometry.

The first two fixtures were extracted from Geofabrik Bretagne, downloaded on
2026-10-22, with snapshot timestamp 2026-10-20T20:23:47Z:
https://download.geofabrik.de/europe/france/bretagne-latest.osm.pbf

Original regional PBF SHA-256:
`5663BFC0F505B59C0D035FA5C81AD51F37CD2E127557717C29700F12FDF855E7`.

The v9 fixture was fetched from the OSM full-relation API on 2026-10-22:
https://api.openstreetmap.org/api/0.6/relation/6730991/full
Relation v9 timestamp: 2026-10-20T22:19:51Z.

Related report: https://github.com/vicliu624/trail-mate-center/issues/5
