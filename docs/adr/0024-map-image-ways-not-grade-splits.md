# map_image ways are the road graph; elevation is paint

Status: accepted

A CS2 road is a graph of edges. An OSM way is one polyline. `map_image`
assembles native edges of the same style and width into ways, then paints
off-ground spans on those vertices. Relative elevation does not split the
line, so a ramp or a 2 m wobble cannot open a hole or a sausage of tubes.

This beat keeping grade as topology (split at ±2 m then ±4 m, then stitch)
from [0021](./0021-osm-carto-stroke-painter.md). Disk stamps, casing-then-fill,
bridge shell, and tunnel/ground/bridge order in 0021 still stand. Integer
layers in [0016](./0016-map-layering-port.md) still stack crossing ramps.
