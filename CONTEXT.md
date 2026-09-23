# AIRI Mayor

The in-game AI mayor: a Gameface chat UI talks to a C# loop that enqueues construction and city tools onto the simulation thread. Players install the mod and paste an API key; there is no external agent process.

## Language

### Runtime

**Agent**:
The in-game mayor runtime: one session, one model, tools queued onto the simulation thread.
_Avoid_: MCP client, external agent process, apeira

**Model-facing surface**:
The tools and text the model is allowed to call or see.
_Avoid_: HTTP route, backend handler, catalog row (those may exist without being model-facing); engine system and type names; HTTP routes in descriptions or result notes; loop jargon (autonomous continuation, on the surface)

**Mayor playbook**:
The city-building playbook baked into the system prompt (city building, utility networks, transit lines).
_Avoid_: engineering skills under `.agents/` or `~/.agents/`

**Traffic governance**:
The mayor's congestion loop over existing tools: a time advance plus fresh reads plus topology QA, `list_networks` ranked by congestion or traffic volume, existing road writes, then `set_simulation` advance and re-measure.
_Avoid_: a new traffic tool, adding lanes to every local street, treating degree-1 dead ends as automatic errors

**Wait simulation**:
Reading and setting the simulation clock. `get_simulation` reads whether the city is paused and the current speed. `set_simulation` can advance in-game time and then restore the previous speed and pause, or pause the city immediately. The player owns the clock. An advance result is wait mechanics only: hours/completed/targetReached/note. It carries no city snapshot; the model builds the snapshot itself from read tools (demand, notifications, city_services, budget) after every advance.
_Avoid_: forced pause as the product runtime, polling, a nested overview/problems digest on the advance result, waitedMs, SimWait internals

**Context budget**:
How many tokens the loop treats as the window. Always the player-set WindowTokens; the loop never parses the model name. The request shape follows the player-set API kind (Chat Completions or Responses).
_Avoid_: Endpoint, provider, or model name as the source of the window

**Compaction**:
Summarizing older turns when estimated tokens reach the compact threshold.
_Avoid_: deleting the session, starting a new chat

**Mayor mandate**:
The loop-owned current city plan. Declared with `set_plan`. Player messages and autonomous continuation leave it in place; only `set_plan` replaces it.
_Avoid_: a statutory plan, compact JSON as the live plan, gating the tool catalog, the mandate prompting a time advance

### Construction

**Road sketch**:
The intended road hierarchy, connections, approximate routes and constraints for a site.
_Avoid_: a sequence of construction calls, Bezier control points as through-points

**Road blueprint**:
A resolved, versioned road layout with curves, heights and explicit connections, reviewed before construction.
_Avoid_: a picture alone, a guarantee of native placement approval, the live city

**Site snapshot**:
The observed roads, terrain, water, land and obstacles relevant to one planning area at a particular time.
_Avoid_: diagnostic map files as current city authority

**Construction record**:
The completed, unfinished and unverified work for a particular blueprint revision, including actual constructed roads.
_Avoid_: assuming an apply request means construction succeeded

**Prefab**:
An exact named game asset. The Agent picks one before placing or building.
_Avoid_: service `role` as a `place_building` argument

**place_building**:
The write that places one standalone prefab and pose.
_Avoid_: `find_placement`, `find_infrastructure_candidate`, preview-then-commit

**build_network**:
The write that constructs a linear network between endpoints. Distinct from placing a building.
_Avoid_: `build_road`, `place_road`, `build_bridge` as a current tool

**Ground**:
Default road mode: follow terrain; reject water and steep grades instead of rewriting the route.
_Avoid_: implied bridge, auto-elevate

**Grade-separated**:
Explicit road mode for a bridge, elevated road, or tunnel. The model must ask for it.
_Avoid_: `build_bridge`, silent promotion from a failed ground path

**Native validation**:
The game's ordinary placement and apply checks. The product does not bypass them.
_Avoid_: Anarchy, `force`, collision bypass

**Auto-connect**:
Placement-owned follow-up that attaches matching water, sewage, or low-voltage networks.
_Avoid_: Agent-drawn pipes or cables as the happy path

**Network**:
Roads, pipes, and cables as linear infrastructure.
_Avoid_: "road" as a synonym for every utility line

**Typed network**:
A top-level linear edge classified as road, fresh water, sewage, or low-voltage from native prefab layers. Auto-connect, list, demolish, and topology QA share that identity.
_Avoid_: a second utility-versus-road taxonomy; high-voltage as part of this set

**Isolated component**:
A connected set of typed-network edges that does not reach an outside connection (roads) or a road edge (pipes and cables).
_Avoid_: treating every degree-1 dead end as isolated

**Road facts**:
Native per-prefab road identity on `list_prefabs`, `list_networks` and `map_text`: UI-group category, speed limit, car-lane count, highway-rules flag, zoning flag.
_Avoid_: width as the highway test; a new traffic tool

**Operational area**:
An owner-linked lot polygon on a facility (storage or extractor). The current product expands only.
_Avoid_: district, a standalone area with no owner

**Specialized-industry hub**:
An independently placeable building prefab that declares an extractor Operational area.
_Avoid_: extractor facility, decorative or animated equipment inside an extraction site

**Transit stop**:
An existing passenger or cargo boarding object (station sub-stop or roadside stop). Listed for line tools; not a `place_building` role.
_Avoid_: inventing a stop by placing a transport building role

**Transit line**:
An ordered loop of transit stops applied through the native route tool. Vehicles stay native.
_Avoid_: Gameface `transportLines$` as the write path; a vehicle or production tool

### Perception and authority

**MAP_TEXT**:
Budgeted semantic-vector text from `map_text`. Spatial evidence, not construction approval.
_Avoid_: heightmap, 8×8 samples as the Agent interface

**Map image**:
Undistorted PNG overview from `map_image`: citywide by default, zoomed extent with x+z+radius or xMin/zMin/xMax/zMax bounds. OSM Carto look: scale-dependent density of land, water, parks, buildings, roads, and rails. Names stay in MAP_TEXT. Appears only when the player enabled visual tools.
_Avoid_: street names in the PNG; stop-to-stop transit overlays; Carto or QGIS as the product renderer; model-parsed GeoJSON; treating the map as construction approval

**Player permission**:
A durable setting that shows or hides a write tool (demolition, spending Development Points, visual tools).
_Avoid_: per-call `force`, a confirmation modal after the setting is already on

**Development tools**:
Default-off diagnostics (`replace_road_type`, `debug_zone_blocks`, `debug_network_course`, `save_game`). Not a permission bypass.
_Avoid_: anarchy mode, debug as always-on
