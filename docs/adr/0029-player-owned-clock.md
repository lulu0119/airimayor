# Player-owned clock

Status: accepted

The mayor does not seize the simulation as its runtime. `wait_simulation` advances the requested in-game hours at a high run speed, then restores the previous speed and pause. If the player changes the clock mid-wait, the wait hands over and leaves the simulation as-is. The model-facing result is wait mechanics only (`hours`, `completed`, `targetReached`, `note`); it carries no city snapshot — the model builds that from the read tools after the wait. Writes are not gated on pause.

This beat forcing pause as the product runtime, attaching an overview/problems digest to the wait result, and treating wait as a mandate/verifying phase ([ADR-0028](0028-mayor-mandate.md)).

## Considered Options

- **Force pause as the product runtime.** Rejected: the player owns the clock; a mayor that leaves the city paused is not playing with them.
- **Return a nested city snapshot on the wait result.** Rejected: a bundled overview/problems digest trained the model to skip fresh reads.
- **Gate writes until the city is paused.** Rejected: construction already enqueues onto the simulation thread; pause is not a second permission layer.
