# Native road course acceptance

This is the prerequisite game gate for [road blueprints](../adr/0032-road-sketch-blueprints.md),
not an acceptance test of a completed blueprint planner. The player runs this on
a new city and supplies the tool logs. Do not use Anarchy or bypass native placement
errors. Keep the player's simulation clock unchanged.

## Setup

Close the game before deploying the DLL, start a new city, and enable Development
Tools in the mod settings. `debug_network_course` appears only with that setting.
Use an unlocked road prefab from `list_prefabs` and owned land. Read the selected
area with `map_text`, `map_image`, and `list_networks` before drawing anything.

`inspect_entity` on a road returns `network.start`, `network.end`, and four exact
world-space cubic control points. Inspecting either node returns its position and
terrain height. These reads supply connection identities and heights; do not infer
them from a screenshot. The cubic at parameter `t` is
`(1-t)^3 A + 3(1-t)^2 t B + 3(1-t)t^2 C + t^3 D`.

## Course input

The diagnostic tool accepts `prefab`, `mode`, and `points`, an array of exactly
four `[x,y,z]` cubic control points. `y` is an absolute world height. The inner two
points control tangents and shape; the curve does not pass through them in general.
No terrain fitting or additional elevation is performed by the compiled-course
adapter. Native construction may adjust geometry; compare the returned result.

Optional `start` and `end` objects contain `index`, `version`, and optional `split`.
For nodes omit `split`; for edges it is the original curve's parameter in `[0,1]`.
The course endpoint must match that node or curve position in all three dimensions
within 0.05 m. References are checked again when construction definitions are created.

`ground` requires terrain-height endpoints and the existing ground preflight.
`grade-separated` requires at least one nonzero endpoint height relative to terrain,
within the current -30..60 m elevation range. Split a structure whose endpoints are
both on the ground into courses with a nonzero intermediate endpoint.

## Scenarios

Run each scenario separately. Inspect the actual entities returned by one operation
before using them as the next operation's connections.

1. **S-curve on level land.** Start at a known node, put the two interior handles on
   opposite sides of the endpoint chord, and end on dry owned land. Inspect actual
   cubic points and the bound start node. This must be a curve, not a straight chord.
2. **Join a newly created endpoint.** Use its returned node identity and position for
   the next course. Match tangent and height. Check that the roads share one node.
3. **Split an existing edge.** End a new road at an interior parameter of a fresh
   inspected edge. Verify the new junction and the affected original/replacement
   identities. Refresh references after the split; do not reuse a replaced edge.
4. **Elevated crossing without connection.** Cross a ground road with positive
   clearance. Bind only the intended endpoints. Inspect both roads: the crossing
   must not become a shared junction, and the applied height must not be lifted twice.
5. **Ramp.** Connect a ground node to an elevated mainline using a curve with matching
   endpoint positions. Verify actual shared nodes, tangent/height continuity, and
   intended travel direction with the selected prefab.
6. **Bridge and tunnel.** Use explicit elevated and buried courses with appropriate
   approaches. Verify native structure appearance, endpoint connections and actual
   heights. Placement rejection is evidence to inspect, not a reason to bypass it.
7. **Invalid inputs.** Submit a stale connection identity, a mismatched endpoint and
   a ground course crossing water. Each must fail before applying construction.

## Evidence and pass criteria

Include each diagnostic call's arguments and complete result, subsequent
`inspect_entity` reads of affected roads/nodes, and relevant native rejection logs.
For visual structure checks include the in-game tool observations in the same run.

Successful direct network writes now return `status=applied` only after the native
preview-to-permanent transition was observed. `applied` contains both newly created
roads and existing roads modified at connections, with their actual curves and
endpoint identities. Deleted originals are listed separately. A result of
`unverified` or an observation timeout must be inspected before any retry: roads may
already exist.

All six positive scenarios must demonstrate the intended geometry and connectivity,
and the negative scenarios must leave no construction behind. Native rejections,
missing mappings, changed geometry or an untested scenario keep the gate open.
Do not treat a successful C# build or pure-data test as an in-game pass.
