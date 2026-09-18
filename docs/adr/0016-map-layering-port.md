# Port road layering, defer zoning fills and labels

Status: layering solver still stands. Label, zoning-fill, and pretty-maps-offline deferrals superseded by [0020](./0020-osm-carto-map-image.md).

`map_image` ports the grade-separation solver from
HamsterPark/cs2-carto-citymap (MIT): stroke union by good continuation,
spatial-hash crossings, longest-path layer relaxation, branch merges. Drawn
layer by layer, bridges stack instead of flattening into false
intersections. Palette and road-class order mirror its style module.

Ported selectively, not fully: zoning fills are out. Carto exports every
zoning cell every call (hundreds of MB on a big city); parsing that into a
synchronous tool call does not fit. Labels, POI icons and hillshade are out
for the same reason plus text rendering: the model needs topology, not
print cartography. Pretty maps stay an offline job (Carto export plus QGIS
or cs2-carto-citymap), untouched by this tool.
