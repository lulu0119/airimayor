# Open work

Current inventory. Vocabulary: [CONTEXT.md](../CONTEXT.md). Decisions: [adr/](./adr/). How to update this tree: [AGENTS.md](./AGENTS.md). Frozen audits stay in dated `ops/` files and are not this list.

## Not implemented

Code still missing.

- AIRI plugin
- `map_image` hillshade
- `map_image` POI icon set
- Pedestrian/cycle-specific map classes
- Zoning-cell landuse fills (volume)

## Awaiting live acceptance

Code exists; a previous save is not the final gate. Close the game before DLL redeploy. Mac cannot `dotnet build` without `CSII_TOOLPATH`; Windows compile is a gate before live acceptance.

- Specialized-industry loop from hub through extractor area to vehicles and production — not yet proven in a live city.
 - Traffic governance as a product loop. The accepted-run intervention persisted, but traffic notifications stayed at 2 and the same-road congestion/volume aggregates worsened after one simulated hour.
 - Native road facts (`roadClass`/`speedKmh`/`carLanes`/`highwayRules`/`zonable` on `list_prefabs`/`list_networks`/`map_text`, category-first `map_image` style, `flowPercent` on `traffic`): needs a live run verifying UI group names, speed values against the wiki (Two-Lane 30, highway 100), ramp lane counts, highway rendering, and the 70 flow target.
- Responses API path (`GetResponsesClient().AsIChatClient()`): first live run clean on Responses + muse-spark via Console Go (tool calls fine, 0 errors); needs a long multi-turn run before it shares default-path status with Chat Completions.
- Chat UI rewrite (docked panel with en-US/zh-HANS chrome, bindings catalog, event-sourced transcript, tool rows): Windows `dotnet build` + webpack/`tsc` pass; needs in-game look/feel acceptance — docking offsets, stick-to-bottom scroll, live tool running/done rows, composer focus vs game hotkeys, and a language-switch check.
- Responses API with vision (no per-turn fallback by decision): first live Responses run attached `map_image`/`screenshot` images with clean following turns; still needs a long vision run before it replaces Chat Completions as the vision path.
- `map_image` road width now scales from native meters with the frame (zoomed roads match building lots; citywide stays near one pixel). Needs a live run comparing a citywide PNG to a bounded site PNG.
- OSM Carto `map_image` (rails/metro/tram as tracks and visible over water, building/park fills, tunnel vs ground vs bridge; names stay in `map_text`; no stop-to-stop transit overlay). Needs a live citywide PNG and a zoomed site PNG.
- Thinking-model `reasoning_content` echo (request pipeline re-attaches stored reasoning to Chat Completions assistant messages): needs a live thinking-model run proving the turn after a text answer no longer 400s and cache ratios stay green.
- Mayor mandate: the active plan survives autonomous continuation and player messages; a player message ends an in-progress time advance, and other tools finish the current step. The chat plan strip shows the plan (empty state and after `set_plan`), not a transcript line.
- Simulation clock: `get_simulation` reads pause and speed; `set_simulation` advance (1–8 in-game hours) restores the previous clock, and pause sets the city paused. A player speed change or pause during an advance hands the clock back immediately. Needs a live city, including an advance that starts while the city is paused and runs until the hours elapse.
