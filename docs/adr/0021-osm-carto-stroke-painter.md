# OSM Carto line painter, not Bresenham squares

Status: accepted. Grade-as-topology (split at ±2 m / ±4 m) superseded by
[0024](./0024-map-image-ways-not-grade-splits.md). Disk stamps, casing-then-fill,
bridge shell, and tunnel/ground/bridge order still stand.

`map_image` paints roads with OSM Carto’s line algorithm in-process: round
cap/join (disk stamps), casing-then-fill tubes, black bridge shell, tunnel
then ground then bridge. Native ECS stays the only source. This beat keeping
the square Bresenham painter from the Carto-era rasterizer
([0019](./0019-vendored-map-source.md)) and embedding Mapnik. Ways and
elevation-as-paint are in 0024. The grade-separation solver in
[0016](./0016-map-layering-port.md) and the look target in
[0020](./0020-osm-carto-map-image.md) still stand.
