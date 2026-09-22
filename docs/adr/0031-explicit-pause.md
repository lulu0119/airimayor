# Explicit pause

Status: accepted

`get_simulation` reads whether the city is paused and the current speed. `set_simulation` can advance 1–8 in-game hours (default 1) or pause immediately. An advance still restores the previous speed or pause; pause is not what happens after every action. During an advance, any clock change, including pause, hands the advance back and leaves the clock as the player set it. A player message ends an advance and restores the previous clock. This replaces the “no pause tool” part of [ADR-0029](0029-player-owned-clock.md). Forced pause as the product runtime stays rejected. The advance result still carries no city snapshot.

## Considered Options

- **No pause tool, only advance.** Rejected: the mayor sometimes needs the city to sit still, and the player can resume.
- **Pause after every action.** Rejected: the player owns the clock; an advance returns it to the previous state.
