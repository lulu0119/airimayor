# map_image is geometry; names stay in map_text

Status: accepted

`map_image` is OSM Carto geometry only: land, water, parks, buildings, roads,
rail. Street, station, and district names stay in `map_text`. Labels on the PNG
hid interchange geometry the vision path must see, and the agent already has
names on the text channel ([0017](./0017-map-text-map-image-split.md)). This
beat keeping OSM-style labels in the raster
([0020](./0020-osm-carto-map-image.md)). The look target and native source in
0020/0019 still stand for geometry.
