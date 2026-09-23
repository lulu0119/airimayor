# Road sketches compile to reviewable network blueprints

Status: accepted

Road planning will use an explicit connection graph with horizontal curves and
vertical profiles. The Agent owns topology, ramp connections and over/under
intent; a bounded deterministic planner owns detailed geometry. Direction fields
generate local streets inside selected regions, not interchange connectivity.
One immutable blueprint revision must supply both preview and construction.
This supersedes the exclusion of alignment and route generation in [0004](0004-linear-networks.md),
while preserving explicit bridge/tunnel intent and native validation.

The native construction seam accepts resolved world-space cubic courses and
explicit node/edge connections. It must not refit or lift these courses. Applied
network identities come from the native preview-to-original mapping, observed
after Apply; proximity is not proof of construction. Existing single-segment
road tools remain until the blueprint replacement is implemented and accepted.
The implementation and live gates are tracked only in [open work](../open-work.md).

## Considered options

- Direction fields alone cannot express which crossing roads connect or the
  directed ramp graph of an interchange.
- Reconstructing the curve at write time can build something different from the
  reviewed blueprint.
- Reporting success when Apply is requested does not establish which objects
  were created, replaced or split, and cannot support reliable continuation.
