# OSM Carto is the map_image target

Status: accepted. Labels-in-PNG superseded by [0022](./0022-map-image-geometry-only.md). Geometry look still stands.

`map_image` is an OSM Carto street map: land, water, parks, buildings by
function, hierarchy-colored roads at native meter width, rail/metro/tram
distinct from bus, and scale-dependent density (roads, landuse, labels)
burned into the PNG. Density follows meters-per-pixel of the rendered
frame, not a model-facing zoom argument. Native ECS remains the only source. This beat keeping the
light no-label topology sketch ([0015](./0015-map-style-and-extent.md)) and
leaving “pretty maps” to offline Carto/QGIS
([0016](./0016-map-layering-port.md)). The grade-separation solver in 0016
still stands. `map_text` stays a separate budgeted tool
([0017](./0017-map-text-map-image-split.md)). Zoning cells stay out of the
raster (volume); landuse comes from building lots and park areas. Hillshade
and a full POI-icon set stay later.
