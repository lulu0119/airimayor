# Bounded map_image forces the Game CRS

Status: accepted

Bounded `map_image` (a requested x+z+radius or xMin/zMin/xMax/zMax extent)
forces the Carto export into the Game CRS and clips the requested game
range directly, with no projection math. It beat mirroring Carto's
Transform (the installed build's `Coord.Shift`/`Transform.Apply` shapes
differ from the mirrored signatures, and its own code never calls them, so
there is no working writer pattern to copy) and empirical bbox-affine
fitting (no game↔export correspondences exist without Carto's
cooperation). Citywide keeps the player's projection settings; only
bounded requests pay for the forced export. A bounded export that does not
come back in meters fails loudly instead of clipping in the wrong space,
and bounded frames always sample native water because they are game meters
by construction.
