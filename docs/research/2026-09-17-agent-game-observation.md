# Research: inference-time game agents on structured APIs (2026 survey)

Scope, corrected 2026-09-17: only **inference-time harnesses driving games through structured APIs** — our route (in-process loop, tool calls, no training, no pixels-first). Training-model work (SIMA 1/2 training, grounding-model pretraining, Minecraft RL) is deliberately excluded; Vision stays only as a cautionary bound. **[M]** = measured in the cited source; **[O]** = author interpretation.

## 1. Closest systems: construction/management sims over APIs

- **FLE — Factorio Learning Environment (Mar 2025, NeurIPS 2025 poster).** The nearest neighbor to our mayor: a construction/management sim driven through a high-level Python API + persistent REPL namespace (the agent's program *is* its cumulative knowledge) + a Python object model of entities with complete positional and relationship data. Now ships **MCP support** and a GraphQL schema. [M] Frontier models show short-horizon skill but fail spatial reasoning, error correction, and building on prior work; lab-play caps at electronic-circuit manufacturing.
- **Prime Agent (Aug 2026).** Open-source long-horizon harness evaluated on FLE, among others. [M] On Factorio: iterative refinement yields continuous technology progression, dedicated subagents enable parallelized work. Mechanism, not model: persistent kernels, recursive sessions, prompts/memories/skills/subagent specs kept as typed versioned state, full trajectory capture. Thesis: a fixed model plus information management plus test-time compute reaches strategies the bare model cannot.
- **Claude Plays Pokémon (Anthropic Twitch, Feb 2025–).** Screenshot + controller actions + a **scratchpad memory file the model edits itself** + save states; loop is capture → send → think → act → repeat. Community reimplementations add exactly our missing half: ROM memory reads (structured state), a tool registry, and a `summary_generator` for context summarization. The official run's lesson is about memory architecture, not vision.
- Reading for us: all three converge on the same shape — small fixed tool/API surface, model-editable persistent notes, summarization as a first-class component, refinement loops over programs/plans rather than single-shot calls.

Sources: [FLE paper](https://arxiv.org/abs/2503.09617) · [FLE repo (MCP, agents)](https://github.com/JackHopkins/factorio-learning-environment) · [Prime Agent](https://arxiv.org/pdf/2608.23552v1) · [clawdplayspokemon](https://github.com/jnaranja/clawdplayspokemon) · [starter + memory_reader](https://github.com/davidhershey/ClaudePlaysPokemonStarter)

## 2. Structured state beats raw input on our genre (benchmarks)

- **Orak (12 real games over MCP, Jun 2025).** Uniform `get-state → reflection/planning → step`. [M] Reflection+planning beats zero-shot on sim/strategy; <8B models score ~0 on the complex half. 11,990 DeepSeek-R1 expert trajectories released.
- **StarDojo (Stardew Valley, Jul 2025).** Closest genre analog that ships both: screenshot + textual state, with text-only agents on a local 7×7 tile grid. [M] Best 12.7%; errors 42% visual / 21% multimodal-reason / 21% long-plan. Ablate text-only vs +map vs +screenshot before claiming any observation change helps.
- Reading for us: keep full dumps out of context; name-lists + counts + deltas carry the decisions.

Sources: [Orak](https://arxiv.org/html/2506.03610) · [StarDojo](https://arxiv.org/abs/2507.07445)

## 3. Pixels: the cautionary bound (kept short on purpose)

- **SIMA 2 (DeepMind, paper Dec 2025, update Aug 2026).** Pixels→keys/mouse VLA at ~65% vs 86% human, with admitted short memory, weak long-horizon verification, imprecise clicks.
- **Cradle on Cities: Skylines (ICML'25) — the direct mayor datapoint.** [M] Roads 4/5, power 5/5, zones ≥90% 4/5, **water 1/5**; modal failure is unconnected pipes — topology invisible in screenshots. External evidence for API state on networks + screenshots as layout backup only.
- **OSWorld 2.0 (Jun 2026).** [M] Best 20.6% on 108 long-horizon workflows; agents lose constraints, miss mid-task arrivals, skip verification, collapse on hidden state — the same failure list as our traffic/utility loops.
- **OSWorld-Human (MLSys 2026).** [M] Best agents take 2.7–4.3× necessary steps; 10–15k uncached prompt tokens per step; fixes are action grouping, rollback, history compression.

Sources: [SIMA 2](https://arxiv.org/abs/2512.04797) · [Cradle §4.2](https://arxiv.org/abs/2403.03186) · [OSWorld 2.0](https://arxiv.org/abs/2606.29537) · [OSWorld-Human](https://arxiv.org/abs/2506.16042)

## 4. Harness engineering (2026): the loop-complexity doctrine

- **Agent = Model + Harness** ([Osmani, Apr 2026](https://addyosmani.com/blog/agent-harness-engineering/), after Anthropic's long-running-apps writeup). Leverage sits on the right-hand side: same model, different harness, Top-30→Top-5 swings on Terminal Bench.
- **Ratchet.** Every line in AGENTS.md traces to a specific past failure; add constraints only on failure, remove when the model outgrows them. (Our playbook lines already work this way; keep the discipline when adding more.)
- **Work backwards from behaviour.** Each harness component must name the behaviour it delivers or come out. (Answers "is the loop too complex": compaction, digest, autonomy each carry one — overflow, staleness, idle hands.)
- **Context-rot trio.** Compaction + tool-call offloading (big results to files, head/tail in context) + progressive disclosure (skills reveal tools when needed). Plus: full context resets with a structured handoff when compaction alone stops working.
- **Hooks: success silent, failures verbose.** Typecheck-gating in code agents is our native-validation-gating: the game validates, the loop only hears about rejection. Already our shape — don't duplicate it in prompts.
- **Ten focused tools beat fifty overlapping ones.** Every tool description is stamped into every request. (Quantify our definition tax before merging reads.)

## 5. Spatial upgrades, cheapest first (unchanged)

Region-pick from cheap stats → annotated crop with numeric labels (Set-of-Mark: GPT-4V RefCOCOg 25.7%→86.4%, [paper](https://arxiv.org/abs/2310.11441)) → dedicated grounder only if clicks stay imprecise. Never a full-res panorama every turn. Our LOCAL_MAP ([0006](../adr/0006-budgeted-local-map.md)) is already this philosophy.

## Takeaways for the open observation design

1. Direction B (cluster reads, e.g. economy) + untouched wait digest matches every on-route system: structured diffs by question, one freshness authority.
2. Steal from FLE/Prime: model-visible persistent notes are cheap; refinement-over-plan beats single-shot; type and version whatever the loop carries.
3. Steal from harness doctrine: measure the tool-definition tax first; every new prompt line needs a failure behind it; hooks over nagging.
4. Eval before claims: StarDojo-style ablations (text / +map / +screenshot) and LMGame-Bench-style toggles ([ICLR'26](https://proceedings.iclr.cc/paper_files/paper/2026/hash/83a4ea71b13bc86308a2bd0b5e07fb61-Abstract-Conference.html)).
