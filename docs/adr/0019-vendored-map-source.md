# Vendored map source, not Carto reflection

Status: accepted

`map_image` grows a native `MapSource` module (`Mod/CS2MCP/RequestHandlers.MapSource.cs`)
that emits `MapStroke`/`MapPolygon` directly in game meters. It replaces the
`Carto.IO.IO.Export` reflection path once it passes live acceptance; until then
the Carto path stays untouched as the serving adapter.

The seam covers every Carto `System` kind
(`Network, Building, Route, Area, Zoning, PointOfInterest, Raster`), but v1
implements only `Network` (road centerlines) and `Building` (lot-size
footprints). The rest stay deferred with per-kind reasons, not dropped:
`Route` needs the route-segment curve port; `Zoning` fails on data volume
(hundreds of MB per call, ADR-0016 holds even vendored); `PointOfInterest`
needs text/icon rendering (ADR-0016); `Raster` hillshade stays offline
(ADR-0013); `Area` needs the district/lot boundary port. Native v1 carries no
elevation, so everything draws on the ground layer until the
`Form`/`Elevation` port lands; switching before that regresses bridge stacking.

Considered: keeping reflection (version-skew `Unavailable` plus a player
install gate on a core perception tool); full 1:1 vendor (~10-13k lines
including Shapefile/GeoTIFF/Settings/Sound); pure rewrite ignoring Carto's
trimming, roundabout clipping, and circular-footprint branches. Selective port
beat all three: the rasterizer and layering solver are reused unchanged, and
game-patch fixes land in one place.

Consequences: no GeoJSON roundtrip, no projection branch (always game meters),
freshness is trivial. Carto structures are grounded in
taipei-native/Carto `Systems/NetworkSystem.cs`, `BuildingSystem.cs`,
`RouteSystem.cs`, `IO/Options.cs` (MIT); no verbatim copy in v1. After the
native path serves, delete the reflection adapter, the Game CRS forcing, and
the meter heuristic with it.
