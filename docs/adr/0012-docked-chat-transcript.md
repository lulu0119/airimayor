# Docked chat panel with an event-sourced transcript

Status: accepted

The chat UI is a docked right-side panel (RoadBuilder SidePanel shape), not a floating draggable window: it coexists with game panels at a fixed place instead of covering the 3D view. One `mods/bindings.ts` catalogs every C# binding; one pure `transcript.ts` reducer owns the message log — snapshots hydrate it on session switch, live events only append, so the two C# sources cannot diverge in the UI. Tool calls render as collapsible rows; start/done pairing relies on executor emission order, with the outcome color carried on the done event. Plain pre-wrap text, no markdown.
