# Advance hands over on pause

Status: accepted

An in-progress advance keeps waiting when the simulation speed leaves 8 for another positive speed. It hands the clock back only when the speed becomes 0 (player pause, focus-loss pause, or `set_simulation` pause), and it does not write 8 back over that lower speed. `set_simulation` pause is accepted during an advance and leaves the city paused; a second advance is still rejected. The wait still restores the speed or pause captured when the advance started, so a mid-run choice of another positive speed is replaced at the end. This replaces the sentence in [ADR-0029](0029-player-owned-clock.md) that any mid-advance clock change hands the wait back.

## Considered Options

- **Hand over on any speed other than 8.** Rejected: a lower positive speed, including one the game writes when it cannot hold 8, ended the wait before the requested hours elapsed.
- **Let a second advance replace the one in progress.** Rejected: one timed run owns the target frame. Pause is the write that may interrupt it.
