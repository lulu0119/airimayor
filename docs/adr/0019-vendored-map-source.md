# Vendored map source, not Carto reflection

Status: native source still stands. “Rasterizer reused unchanged” superseded by [0021](./0021-osm-carto-stroke-painter.md). Stop-to-stop `Route` overlay superseded by [0023](./0023-map-image-no-route-overlay.md).

`map_image` grows a native `MapSource` module (`Mod/CS2MCP/RequestHandlers.MapSource.cs`)
that emits `MapStroke`/`MapPolygon` directly in game meters. It serves first
with the Carto path as fallback; after live acceptance the reflection adapter,
the Game CRS forcing, and the meter heuristic go.

The seam covers every Carto `System` kind
(`Network, Building, Route, Area, Zoning, PointOfInterest, Raster`). The
native source collects `Network` (road centerlines plus name-matched rails,
per-feature mean-curve elevation), `Building` (lot-size footprints), and
`Route` (stop-to-stop loops); it serves first with the Carto export as
fallback. Still deferred with per-kind reasons, not dropped: the exact
on-road route path, circular footprints, and district/lot boundaries (`Area`);
`Zoning` fails on data volume (hundreds of MB per call, ADR-0016 holds even
vendored); `PointOfInterest` needs text/icon rendering (ADR-0016); `Raster`
hillshade stays offline (ADR-0013).

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
