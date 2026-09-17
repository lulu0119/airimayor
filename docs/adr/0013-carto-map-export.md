# Carto-backed map export, not a native renderer

Status: accepted

`map_image` renders the citywide overview PNG through the Carto mod
(reflection on `Carto.IO.IO.Export(Carto.IO.Options)`, silent flags, then
in-process rasterization of the fresh GeoJSON vectors). It beat two real
alternatives: a native renderer (a large new module duplicating what Carto
already extracts) and CS2MapView (same author line, but not on Paradox Mods
and its renderer is a GUI viewer with no headless entry).

Consequences: the tool needs Carto installed and shares the vision switch
with screenshot. Without Carto it returns unavailable; screenshot still
works. v1 renders vector structure (roads, buildings) only; raster overlays
stay deferred. Freshness is enforced: only post-export GeoJSON is rendered,
never a stale map.
