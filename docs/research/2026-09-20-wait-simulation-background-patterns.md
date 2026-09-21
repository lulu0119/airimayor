# Wait / background-task patterns in opencode, pi, and Codex CLI

Date: 2026-09-20. Status: research note (evidence, not authority). Current clock contract is [ADR-0029](../adr/0029-player-owned-clock.md); this note is evidence for a possible async wait, not that decision.
Question: how do other agent CLIs implement wait/sleep/wait-for-condition, stay responsive while waiting, and avoid conflicting with concurrent user activity — and what applies to our `wait_simulation` problem (blocking wait, queued user messages, game possibly already running)?

## 1. opencode (sst/opencode)

Mechanism: no blocking `sleep` tool as a primitive. Long work goes to background-task tools; a foreground wall-clock `sleep` is treated as a smell and short-circuited.

- Agent-facing background tools: `bash` with `run_in_background`, `task` with async mode, plus `monitor`, `tasklist`, `taskstop` for lifecycle. Each background task writes full output to `<data-dir>/tasks/<task_id>.out`; the ack returns the task id and output path.
- Lifecycle across modes: interactive (TUI/chat), non-interactive (flow steps, headless CLI, ACP), cron-driven. Non-interactive drain waits on background tasks and logs `still waiting on background tasks` (id, kind, age) every 60s — observability only, never shortens the wait.
- Sleep-shim: in a non-interactive run with pending non-monitor background tasks, a foreground bash whose sole effect is wall-clock wait (`sleep <n>`, optionally followed by one echo) is NOT executed; it returns without sleeping so the drain, not the sleep, paces the run.
- Stale-task guard: `backgroundTasks.stallThreshold` (default 30m; 0/negative disables). Progress signal for a subagent task = most recent message on its own session; a task with no subagent session is never "stalled". Stall detection must not become a backdoor time bound on `monitor` (monitors are deliberately not time-bounded; `max_events` is a count, not a timeout).
- Concurrency conflicts: core opencode has no file-target overlap detection; the community `AutomatorAlex/opencode-background-tasks` plugin adds opt-in target overlap detection, isolated git worktrees, child-session guardrails (children cannot re-delegate), and parent-session question relay.

Sources:
- `docs/background-tasks.md` contract (forks mirror the same doc): https://github.com/ashutoshsinghpr7/opencode-ai-go/blob/main/docs/background-tasks.md
- Background blocking session API (`POST /api/session/{sessionID}/background`): https://opencode.ai/v2/docs/api/session/v2-session-background
- Community overlap/worktree plugin: https://github.com/AutomatorAlex/opencode-background-tasks
- `wait_for` readiness-plugin pattern (HTTP/TCP/command poll): https://github.com/chncaesar/opencode-waitfor

Responsiveness: the foreground turn ends at the background-launch ack; the user can keep talking while the task runs, then `monitor`/`background_output` (optionally blocking) retrieves the result. System notifies on completion.

## 2. pi agent (badlogic/pi-mono, now earendil-works/pi)

Mechanism: deliberately NO `sleep` tool and NO background bash. "No background bash. Use tmux. Full observability, direct interaction." No sub-agents in core either (spawn `pi` via tmux or build your own sub-agent extension).

- Turn model: `runLoop` in `packages/agent/src/agent-loop.ts` — inner loop drains tool calls + steering messages; outer loop continues only when queued follow-up messages arrive after the agent would stop (`getFollowUpMessages`). Two distinct injection points: `getSteeringMessages` (mid-turn steer) vs `getFollowUpMessages` (next-turn queue).
- Interruptibility: every tool `execute(id, args, signal, onUpdate)` receives an `AbortSignal`; `signal?.aborted` is checked before/after preflight and between sequential calls, and abort produces an `"Operation aborted"` error result. The subagent example kills the child proc on abort (`SIGTERM`, then `SIGKILL` after 5s).
- Partial-result streaming: tools can push `tool_execution_update` partial results while running, so a long wait can report progress without ending the call.
- Conflict avoidance: none built-in for external processes — by design it pushes that to tmux/extensions (`beforeToolCall`/`afterToolCall` hooks can block or rewrite; `withFileMutationQueue` serializes file mutation in examples). Philosophy: four core tools (read, write, edit, bash), no permission popups, run in a container.

Sources:
- Agent loop (steering vs follow-up, AbortSignal checks): https://raw.githubusercontent.com/badlogic/pi-mono/main/packages/agent/src/agent-loop.ts and https://github.com/badlogic/pi-mono/blob/main/packages/agent/src/agent-loop.ts
- Coding-agent extension model + "No background bash. Use tmux": https://github.com/badlogic/pi-mono/tree/main/packages/coding-agent
- Subagent-via-spawn example (abort → SIGTERM/SIGKILL): https://github.com/badlogic/pi-mono/blob/main/packages/coding-agent/examples/extensions/subagent/index.ts

Responsiveness: responsiveness comes from abort + steering/follow-up queues, NOT from backgrounding the wait. A wait that cannot observe `AbortSignal` blocks steering.

## 3. OpenAI Codex CLI (openai/codex)

Mechanism: turn-scoped execution + explicit follow-up behavior + Goals for long-running completion conditions. No generic `sleep` tool surfaced to the model.

- Turn model: app-server contract is `turn/start`, same-turn `turn/steer` (guarded by `expectedTurnId`), and separate `interrupt`. Community teardown notes the app-server "does not provide the durable product FIFO needed for run-this-after"; queueing lives above Codex (cf. OpenCode V2 admission/promotion reference).
- Follow-up behavior setting: `Queue` vs `Steer` (Settings → General → Follow-up behavior). Known sharp edge (#31874): queued follow-ups can release at an intermediate internal turn boundary rather than waiting for task-level completion — users ask for an explicit "queue for next turn" vs "queue until task completion" split.
- Typing-while-running: explicit feature request (#14693) for `queue_only_while_running` / `disable_typing_interrupts` — ordinary typing must never interrupt or re-focus the active turn; only an explicit interrupt/queue shortcut should affect it. (Closed/completed; direction is opt-in non-interruptible runs.)
- Goals (≥0.128.0, `/goal`, `/goal pause|resume|clear`): thread-scoped persistent objective with completion contract. Continuation is event-driven and conservative — only at safe boundaries: turn finished, no other work pending, NO user input queued, thread idle. Interruptions pause the Goal; plan-only work never triggers continuation; a continuation turn with no tool call suppresses the next one (no spin). Budget-limit stops work and summarizes instead of completing.
- Execution policy/approvals: sandbox + approval policies bound what background/long commands may do; interrupted-then-compacted threads have regressed to stricter policies in bug reports, so lifecycle transitions must preserve the policy explicitly.

Sources:
- Goals cookbook (continuation boundaries, budget, evidence-based completion): https://developers.openai.com/cookbook/examples/codex/using_goals_in_codex
- Queue-vs-steer regression (#31874) and no-interrupt mode (#14693): https://github.com/openai/codex/issues/31874, https://github.com/openai/codex/issues/14693
- App-server turn/steer/interrupt + queue-above-Codex analysis: https://github.com/OpenAgentsInc/openagents/blob/main/docs/teardowns/2026-07-15-codex-app-server-client-support-analysis.md
- Exec policy overview: https://github.com/openai/codex/blob/main/docs/execpolicy.md

Responsiveness: interrupt is first-class (`turn_aborted { reason: interrupted }`, partial work survives as resumable boundary); steering and queued-next-turn are visibly different intents.

## What applies to our Wait Simulation problem

Our symptoms: `wait_simulation` blocks the turn; user messages queue invisibly; if the game clock is already running the wait can fight live game activity.

Applicable patterns, cheapest first:

1. Split wait into launch + poll (opencode shape). Keep `wait_simulation` as the synchronous short-wait path, but add an async variant (launch returns a wait id; `wait_status`/`wait_cancel` poll) so the turn ends and the user stays responsive. Never implement the wait as an uninterrupted host `sleep` — poll the game clock each interval so the wait observes aborts and clock state.
2. Make every wait abort-aware (pi shape). Thread the existing cancellation/interrupt signal through the poll loop; on abort restore speed/pause (we already do) AND return a partial result (ticks elapsed, clock state) instead of an error, so steering survives.
3. Distinguish steer vs queue (pi + Codex shape). While a wait is in flight, a user message should either steer (abort wait, act) or queue (let wait finish, then act) — never silently merge. At minimum, announce the queued message when the wait ends.
4. Guard the already-running conflict (Codex Goals shape). Before advancing time, snapshot clock state; if the game is already unpaused/running, either refuse the timed wait with the observed state or convert to observe-only (no speed change), and only auto-continue while the thread is idle with no user input queued. Continuation must be evidence-checked (target tick reached?) with a budget cap, not an open loop.
5. Keep the conflict surface small: no overlap detection framework needed — one clock, one owner. The wait owns speed/pause only for its duration and always restores; document that timed waits require taking clock ownership, otherwise use observe-only.
