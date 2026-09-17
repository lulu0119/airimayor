# Research: how other in-game agents observe game worlds (2026 survey)

Primary-source survey on **agent observation of game worlds**: structured state vs pixels vs hybrid spatial abstractions, plus 2026 results on long-horizon and efficiency limits. Focus: what to steal for the CS2 mayor's observation stack (wait digest, cluster reads, LOCAL_MAP, screenshots). **[M]** = measured in the cited source; **[O]** = author interpretation.

## 1. Structured state still wins for sim/strategy

- **Voyager + Mineflayer (Minecraft).** The model gets compact named state (`Biome, Time, Nearby blocks, Nearby entities nearest→farthest, Health/20, Hunger/20, xyz, Equipment, Inventory dict`) plus last error/chat, never raw pixels or voxels. [M] 63 unique items/160 iters (3.3× baselines), tech-tree up to 15.3× faster.
- **Orak (12 real games over MCP, Jun 2025).** Uniform loop `get-state → reflection/planning → step` with abstracted actions. [M] Reflection+planning beats zero-shot on sim/strategy; models <8B score ~0 on Pokémon/Minecraft/Stardew/StarCraft/Slay-the-Spire. 11,990 DeepSeek-R1 expert trajectories released.
- Reading for us: text-state + skill abstraction beats raw inputs on exactly our genre; keep full dumps out of the context, send name-lists + counts + deltas.

Sources: [Voyager](https://arxiv.org/abs/2305.16291) · [action template](https://github.com/MineDojo/Voyager/blob/main/voyager/prompts/action_template.txt) · [Orak](https://arxiv.org/html/2506.03610) · [Orak repo](https://github.com/krafton-ai/Orak)

## 2. Pixel agents, and where they break in 2026

- **SIMA 1 → SIMA 2 (DeepMind, paper Dec 2025, update Aug 2026).** Pixels-only + language → keyboard/mouse, no APIs. SIMA 2 is a Gemini Flash-Lite VLA: vision+language+action in one token stream, with reasoning, dialogue, and a Gemini task-setter/reward self-improvement loop. [M] ~65% vs ~86% human; +10pp over SIMA 1 on held-out games. Admitted limits [M]: short memory (narrow context kept for latency), weak long-horizon verification, imprecise clicks.
- **OSWorld 2.0 (Jun 2026).** 108 long-horizon workflows (~1.6 human-hours, ~318 tool calls each). [M] Best (Claude Opus 4.8, max thinking) only 20.6% complete. Failure modes that mirror our traffic/utility loops: agents lose track of constraints, miss information arriving mid-task, guess instead of asking, skip verification, and collapse on hidden state they must recover.
- **OSWorld-Human (MLSys 2026).** Efficiency lens on the same benchmark. [M] Best agents take 2.7–4.3× more steps than necessary; planning/reflection calls dominate latency; p50 is 10–15k uncached prompt tokens per step. Prescribed fixes: action grouping, efficient rollback, history compression.
- **Agent S2 (2025).** Compositional split: planner + dedicated grounder (UI-TARS class), screenshots only. SOTA on OSWorld/WindowsAgentArena at the time. Lesson: never ask the reasoning model for raw coordinates; ground with a specialist.
- Reading for us: pixels generalize but are short-horizon, imprecise, and token-hungry. Our vision tools stay off by default for the right reasons; screenshots are layout sanity checks, not the decision substrate.

Sources: [SIMA 2 paper](https://arxiv.org/abs/2512.04797) · [DeepMind Aug 2026 update](https://deepmind.google/blog/from-atari-to-eve-online-building-on-15-years-of-ai-research-in-games) · [OSWorld 2.0](https://arxiv.org/abs/2606.29537) · [OSWorld-Human](https://arxiv.org/abs/2506.16042) · [Agent S2](https://arxiv.org/html/2504.00906v1)

## 3. The one direct mayor datapoint: Cradle plays Cities: Skylines

- **Cradle / GCC (BAAI, ICML'25).** GPT-4o + 6 modules; input is a video clip of the last action, output is key/mouse code; pauses pausable games while the LLM thinks. On Cities: Skylines [M]: roads closed-loop 4/5, power 5/5, zones ≥90% 4/5, **water 1/5**, population 450±224 (850±142 with ≤3 human fixes). Modal failure: unconnected water pipes — precise grounding + topology reasoning.
- Reading for us [O]: vision-only can zone and power a 1k city but fails exactly where our typed-network machinery lives (pipes/roads isolation, auto-connect). This is the strongest external evidence for API state on networks + LOCAL_MAP for layout, and for keeping screenshots as backup rather than primary.

Sources: [Cradle paper §4.2](https://arxiv.org/abs/2403.03186) · [Cradle repo](https://github.com/BAAI-Agents/Cradle)

## 4. Hybrid spatial abstractions (cheapest bridge)

- **Set-of-Mark (2023, still the trick).** Overlay segmentation masks + numeric labels so the model says "12" instead of coordinates. [M] GPT-4V RefCOCOg 25.7%→86.4%. Bottleneck is mask quality, not the model.
- **ScreenSeekeR lesson.** Planner proposes regions → crop → ground: +29pp on 4K grounding with no retraining. [O] Same shape fits the city: pick a district from cheap stats, then high-res/annotated crop only there — never a full 4K panorama.
- **StarDojo (Stardew Valley, Jul 2025).** Closest genre analog (production + living + time/weather/energy). Ships screenshot + textual state together; text-only agents get a local 7×7 tile grid. [M] Best model 12.7%; error split 42% visual / 21% multimodal-reason / 21% long-plan. Ablation-ready triple observation is the pattern to copy.
- **LMGame-Bench (ICLR 2026).** Modular harness with perception/memory/reasoning toggled independently to isolate which capability fails. Worth copying as an eval shape before we claim any observation change helps.
- Reading for us: our LOCAL_MAP ([0006](../adr/0006-budgeted-local-map.md)) is already this philosophy (budgeted semantic vectors, not raw grid). Next cheapest step is SoM-style annotation on crops, not a grounder model.

Sources: [SoM](https://arxiv.org/abs/2310.11441) · [ScreenSeekeR](https://arxiv.org/abs/2504.07981) · [StarDojo](https://arxiv.org/abs/2507.07445) · [LMGame-Bench](https://proceedings.iclr.cc/paper_files/paper/2026/hash/83a4ea71b13bc86308a2bd0b5e07fb61-Abstract-Conference.html)

## 5. Freshness and cost: observe on boundaries, not ticks

- **Pausable sim is a gift.** Cradle pauses real-time games for the LLM; StarDojo exposes pause + parallel headless instances. Our clock already belongs to the player and wait restores speed/pause — observing on decision boundaries (after wait) instead of every tick is the same idea, keep it.
- **Stale-read rule is load-bearing.** Playbook: a read right after a budget slider still shows old output; wait an hour. No surveyed system solves this except by waiting or versioning reads — supports keeping the wait digest as the freshness authority rather than a standalone snapshot tool.
- **History compression.** OSWorld-Human's fix list (action grouping, rollback, history compression) matches our compaction + MaxRounds shape; keep hot-window verbatim, archive the rest.

## Takeaways for the open observation design

1. Direction B (cluster reads, e.g. economy) + untouched wait digest is consistent with the survey: structured diffs by question, one freshness authority, no pixel dependency.
2. If a `wait(include=[…])` piggyback is ever added, gate it on timeline evidence that "wait then read-X" dominates — Orak's ablation habit, not intuition.
3. Spatial upgrades, cheapest first: region-pick from stats → annotated crop (SoM numbers) → dedicated grounder only if clicks stay imprecise. Full-res screenshots every turn is what SIMA 2 and OSWorld-Human both warn against.
4. Eval shape before claims: toggle perception/memory/reasoning like LMGame-Bench; ablate text-only vs +map vs +screenshot like StarDojo.
