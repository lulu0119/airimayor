# map_text and map_image stay separate tools

Status: accepted

A merge into one `city_map` tool with a `format` switch was tried and
reversed before shipping: per-format vision gating, dual response types,
and a "both" assembly made the interface wider than two small tools with
one shared range convention. `map_text` serves budgeted MAP_TEXT v1
vector text; `map_image` serves the undistorted Carto raster. The vision
switch never appears in model-facing text: `map_image` is simply absent
without visual tools. The LOCAL_MAP v1 tag is renamed MAP_TEXT v1 to match.
