# Open work

Current inventory. Vocabulary: [CONTEXT.md](../CONTEXT.md). Decisions: [adr/](./adr/). How to update this tree: [AGENTS.md](./AGENTS.md). Frozen audits stay in dated `ops/` files and are not this list.

## Not implemented

Code still missing.

- Native map source (`RequestHandlers.MapSource.cs`): skeleton plus `Network`/`Building` collectors exist but are not yet serving `map_image`; `Route` curve port and `Form`/`Elevation` port are missing, Carto reflection stays the serving adapter.

## Awaiting live acceptance

Code exists; a previous save is not the final gate. Close the game before DLL redeploy. Mac cannot `dotnet build` without `CSII_TOOLPATH`; Windows compile is a gate before live acceptance.

- Specialized-industry loop from hub through extractor area to vehicles and production — not yet proven in a live city.
- Traffic governance as a product loop. The accepted-run intervention persisted, but traffic notifications stayed at 2 and the same-road congestion/volume aggregates worsened after one simulated hour.
- Responses API path (`GetResponsesClient().AsIChatClient()`): first live run clean on Responses + muse-spark via Console Go (tool calls fine, 0 errors); needs a long multi-turn run before it shares default-path status with Chat Completions.
- Chat UI rewrite (docked panel with en-US/zh-HANS chrome, bindings catalog, event-sourced transcript, tool rows): Windows `dotnet build` + webpack/`tsc` pass; needs in-game look/feel acceptance — docking offsets, stick-to-bottom scroll, live tool running/done rows, composer focus vs game hotkeys, and a language-switch check.
- `map_image` (Carto rasterize with ported road layering, vision-gated): needs Windows `dotnet build` plus a live run with Carto installed proving the PNG attaches in seconds, overpasses stack, chat preview shows, and no 400 pairing errors in following turns (needs a fresh session; old histories carry the pre-fix interleaved image).
- `map_image` bounded extents force the Game CRS and clip the game range directly (no Carto Transform dependency); bounded frames always sample native water. Needs a live run proving a bounded call returns a zoomed PNG instead of Unavailable.
- Cleanup after map acceptance passes: remove the temporary dev-gated `map_image` block, `[DEBUG-mapex1]` lines, `DebugPhase`, and `map-export-debug.log`.
- Responses API with vision (no per-turn fallback by decision): first live Responses run attached `map_image`/`screenshot` images with clean following turns; still needs a long vision run before it replaces Chat Completions as the vision path.
- Tool image preview (thumbnail data URI on the tool done event, rendered in the tool row): needs in-game acceptance — screenshot/`map_image` rows show the image, text-only rows unchanged, session switch still hydrates.
- Thinking-model `reasoning_content` echo (request pipeline re-attaches stored reasoning to Chat Completions assistant messages): needs a live thinking-model run proving the turn after a text answer no longer 400s and cache ratios stay green.
