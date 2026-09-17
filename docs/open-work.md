# Open work

Current inventory. Vocabulary: [CONTEXT.md](../CONTEXT.md). Decisions: [adr/](./adr/). How to update this tree: [AGENTS.md](./AGENTS.md). Frozen audits stay in dated `ops/` files and are not this list.

## Not implemented

Code still missing.

None.

## Awaiting live acceptance

Code exists; a previous save is not the final gate. Close the game before DLL redeploy. Mac cannot `dotnet build` without `CSII_TOOLPATH`; Windows compile is a gate before live acceptance.

- `wait_simulation` returns hours/completed/targetReached only, no overview/problems digest; the model self-pulls state via `list_notifications`, `city_services`, `local_map` etc. Needs a long-save live run proving it still observes and acts.
- Auto-connect: road-carried water/sewage/LV attach as short perpendicular on matched lane; utilities work (Windows in-game).
- Specialized-industry loop from hub through extractor area to vehicles and production — not yet proven in a live city.
- Traffic governance as a product loop. The accepted-run intervention persisted, but traffic notifications stayed at 2 and the same-road congestion/volume aggregates worsened after one simulated hour.
- Agent slim-down: baked playbook (skill/context-block/interleave code gone), ApiKind + player WindowTokens (vision On/Off, no Auto), wait digest from CS2MCP, autonomy without fake user messages (per-turn MaxRounds 30, autonomous turns uncapped while Continuous is on), dev-gated hotreload. Windows compile is the gate; then a new-city run proves the loop still builds.
- Responses API path (`GetResponsesClient().AsIChatClient()`): compiles per MEAI docs but never run live; Chat Completions stays the proven path until a live Responses run succeeds.
- Chat UI rewrite (docked panel with en-US/zh-HANS chrome, bindings catalog, event-sourced transcript, tool rows): Windows `dotnet build` + webpack/`tsc` pass; needs in-game look/feel acceptance — docking offsets, stick-to-bottom scroll, live tool running/done rows, composer focus vs game hotkeys, and a language-switch check.
