# Blueprint revisions build in dependent steps

Status: accepted

One `build_network` call with a blueprint id builds every course of that
revision inside a single run record, in dependency order: referenced roads
first, then the ramps, streets and segments that anchor to them. Steps share
one native tool pipeline; each step observes its own preview-to-permanent
mapping before the next step is queued, so the run knows which real roads
each course became.

A player message, stop request or timeout stops the run after the current
native step, never inside it, and never touches the clock. Repeating the
same blueprint id and version resumes the unfinished steps instead of
rebuilding; a completed revision returns its existing roads. The bridge
watchdog extends while steps keep completing and only aborts a run whose
current step goes quiet, which keeps per-step stall detection without
applying one short timeout to a whole district.

Native micro-adjusts smaller than 2 m shift the next anchored endpoint;
larger drift, expired site fingerprints, or changed anchors stop the run
for a fresh review instead of silently rebuilding a different layout.
Whole-run rollback and road demolition stay out: built roads stand.

## Considered Options

- **The model polls one step per call.** Rejected: the work unit is the
  reviewed revision, and per-step polling reintroduces segment-by-segment
  construction through a second interface.
- **Atomic whole-run transactions with rollback.** Rejected: native
  construction cannot be unbuilt without demolishing the player's city,
  so partial progress with explicit resume is the honest contract.
- **Re-solving geometry at build time.** Rejected: construction must
  consume the reviewed courses, or the preview stops describing what
  gets built.
