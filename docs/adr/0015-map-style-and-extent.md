# Light map style with bounded extents

Status: accepted

`map_image` renders a light cartographic style (land, water, beige
footprints, hierarchy-colored roads, transit lines; no labels or POI icons)
instead of a dark debug view, matching what Carto's own QGIS output looks
like. Road hierarchy comes from Carto's own Category attribute; Volume rides
along as the next overlay input.

The tool accepts an optional map range (same convention as `map_text`).
Ranges are game XZ, converted with Carto's own Transform (the exact function
its writers use), so clipping lands in the right place under any projection
settings. If the Transform API is unresolvable, bounded calls fail loudly
and citywide keeps working. Labels and POI icons
stay deferred: text rendering does not belong in this rasterizer.
