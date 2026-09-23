# Linear networks are not buildings

Status: accepted

Roads, pipes, and cables are native network transactions, not placed objects, so they cannot share `place_building`. The write is `build_road`. Road prefabs take `ground` (default) or `grade-separated`; other networks do not take a road mode.

`ground` preflights water and slope on the terrain-adjusted course and rejects the route. It never moves endpoints or promotes the request to a bridge or tunnel. `grade-separated` is the explicit combined intent for bridge, elevated, and underground segments, and needs both endpoint elevations with at least one nonzero.

The original exclusion of alignment and procedural routing is superseded by
[0032](0032-road-sketch-blueprints.md). Blueprint construction is being implemented;
until its acceptance gate passes, the single-segment product write retains its
existing behavior. Hidden promotion from ground to bridge/tunnel remains prohibited.

## Considered Options

- **Silent promotion to a bridge when ground fails.** Rejected: that hides the player's intent and makes failures undiagnosable.
- **One write tool for buildings and networks.** Rejected: the native transactions are different.
