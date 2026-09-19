# Research: escaping local optima and reusing diverse solutions in LLM game agents

Status: frozen 2026-09-19. Question: how should our Cities: Skylines 2 C# agent (tools: `place_building` / `build_road` / zone writes, all enqueued to sim thread) stop copying one successful block and start trying diverse global plans?

## Findings

### 1. Quality-Diversity: keep an archive of elites, not one winner
MAP-Elites partitions a behavior-descriptor space into cells and keeps the best solution per cell, so search returns a repertoire of diverse high-performers rather than a single optimum; novelty search with local competition was shown to beat pure objective search on deceptive landscapes — directly analogous to "one good block" vs a portfolio of district layouts.
- Source: [Mouret & Clune, MAP-Elites (arXiv:1504.04909)](https://arxiv.org/abs/1504.04909) — "illuminates the search space ... returns a collection of high-performing solutions."
- Source: [QD survey, Rojas 2026](https://www.mdpi.com/2227-7390/14/7/1091) — "shifting the focus from identifying a single global optimum to illuminating a repertoire of elite solutions."
- Source: [Dominated Novelty Search 2025](https://arxiv.org/pdf/2502.00593v1) — drop-in local-competition replacement for the MAP-Elites grid, better on high-dimensional/discontinuous descriptor spaces.

### 2. Voyager: automatic curriculum + skill library + self-verification compounds diversity
Voyager's three parts are (1) an automatic curriculum that maximizes exploration ("discover as many diverse things as possible", described as in-context novelty search), (2) an ever-growing skill library of executable code indexed by description embedding, (3) iterative prompting with environment feedback and self-verification; result: 3.3x more unique items, 2.3x longer travel, 15.3x faster tech-tree unlock vs baselines; removing the curriculum for a random one drops discovered items by 93%.
- Source: [Voyager paper (arXiv:2305.16291)](https://arxiv.org/abs/2305.16291) — "automatic curriculum ... skill library ... iterative prompting mechanism."
- Source: [Voyager project page](https://voyager.minedojo.org/) — "curriculum ... can be perceived as an in-context form of novelty search."
- Source: [Voyager repo](https://github.com/MineDojo/Voyager) — skills are "temporally extended, interpretable, and compositional."

### 3. Generative Agents (Sims): reflection + plan-then-act breaks reactive copying
Agents store a natural-language memory stream, periodically synthesize higher-level reflections (triggered when importance scores exceed threshold), then plan top-down and recursively refine; ablation shows observation, planning, and reflection each contribute critically to believability — the reflection step is what turns repeated observations into new plans instead of repeating the last successful action.
- Source: [Generative Agents (arXiv:2304.03442)](https://arxiv.org/abs/2304.03442) — "synthesize those memories over time into higher-level reflections, and retrieve them dynamically to plan behavior."
- Source: [ACM UIST'23 version](https://dl.acm.org/doi/10.1145/3586183.3606763) — "Reflections are higher-level, more abstract thoughts ... generated periodically."

### 4. AlphaStar league / population-based training: exploiters force the main agent off its optimum
AlphaStar keeps one Main Agent plus Main Exploiters (attack the current main) and League Exploiters (attack the whole league); TStarBot-X/SCC add diversified exploiter inits and statistic-z conditioning so the main agent cannot settle on one build; final output is the Nash mixture of complementary, least-exploitable strategies. Lesson: diversity comes from adversarial roles, not from sampling temperature.
- Source: [AlphaStar league explainer](https://www.gamedeveloper.com/design/how-alphastar-became-a-starcraft-grandmaster) — "Main Agents ... Main Exploiters ... League Exploiters."
- Source: [ROA-Star, NeurIPS 2023](https://proceedings.neurips.cc/paper_files/paper/2023/file/94796017d01c5a171bdac520c199d9ed-Paper-Conference.pdf) — "goal-conditioned exploiters ... spotting weaknesses ... greatly improved compared to the unconditioned exploiters."
- Source: [SCC (arXiv:2012.13169)](https://arxiv.org/pdf/2012.13169v3) — "multiple main agents ... provide more diversity to the league, thus making the trained agents more robust."

### 5. BALROG / NetHack: zero-shot LLMs fail at systematic exploration without harness help
BALROG (BabyAI, Crafter, TextWorld, Baba Is AI, MiniHack, NetHack) finds top LLMs decent on easy games but flat on NetHack (best 1.5% progression); diagnosed weakness is systematic exploration, not knowledge. NetPlay (first LLM zero-shot NetHack agent) needed predefined skills + past-interaction tracking + event-interrupt + detailed context to work at all; RND intrinsic-reward exploration was the earlier RL fix for the same environment.
- Source: [BALROG (ICLR 2025)](https://proceedings.iclr.cc/paper_files/paper/2025/file/f0b1515be276f6ba82b4f2b25e50bef0-Paper-Conference.pdf) — "significant weakness in the models' ability to explore ... all models flat line with NetHack."
- Source: [NetPlay (arXiv:2403.00690)](https://arxiv.org/abs/2403.00690) — "prompts the LLM to choose from predefined skills and tracks past interactions ... detects important game events to interrupt running skills."
- Source: [NLE (NeurIPS 2020)](https://proceedings.neurips.cc/paper_files/paper/2020/file/569ff987c643b4bedf504efda8f786c2-Paper.pdf) — "RND encourages agents to visit unfamiliar states ... proven effective for hard exploration games."

### 6. Temperature alone is shallow; structure diversity at prompt level
Higher temperature adds token noise but outputs "differ in wording but not in substance"; persona modifiers as sampling cues + chain-of-thought + explicit revise-to-be-distinct steps beat temperature scaling (which at 1.5+ mostly degrades quality); two peer models beat 100-sample self-consistency at 1/40 cost — i.e. prefer multi-candidate propose-and-select with different roles over cranking temperature. Self-consistency (majority vote over diverse reasoning paths) converges; for diversity we want the inverse: sample diverse plans, then select with an evaluator.
- Source: [LLM idea-diversity study 2026 (arXiv:2602.20408)](https://arxiv.org/pdf/2602.20408) — "higher temperature brings only slight improvement in idea diversity while often producing nonsensical answers"; "ordinary personas ... serving as diverse sampling cues" + CoT gives highest diversity.
- Source: [Stochastic Sampling is Epistemically Shallow (arXiv:2607.20464)](https://arxiv.org/pdf/2607.20464v1) — "two peer models beat 100-sample self-consistency at ~1/40th the cost."
- Source: [Self-Consistency (arXiv:2203.11171)](http://arxiv.org/abs/2203.11171) — "samples a diverse set of reasoning paths ... then selects the most consistent answer" (use the mechanism, invert the vote for diversity).
- Source: [G2 guided generation (EMNLP 2025)](https://aclanthology.org/2025.emnlp-main.713.pdf) — Diversity Guide encourages novelty vs prior answers while Dedupe Guide suppresses repetition.

## Actionable suggestions for this project (tool-surface specific)

1. **Plan-then-commit (Generative-Agents style).** Require one plan turn (zone balance / traffic / budget targets + district descriptor) before any `place_building`/`build_road`/zone batch; write tools validate, the loop only hears rejections. Kills reactive block-copying at the seam.
2. **Tabu list / action-history dedupe.** Keep last-N committed footprints (building prefab + grid cell + road topology hash) in ToolQueueSystem context; reject or penalize near-duplicates (G2 dedupe-guide analog). Cheap, no model change.
3. **Diversity bonus in evaluator loop.** Score each candidate district on novelty = distance in descriptor space (density mix, road class mix, land-use split) à la MAP-Elites/RND; keep best-per-cell archive of approved layouts for retrieval instead of one "good block" in prompt.
4. **Multi-candidate propose-and-select.** Sample 3 plans with rotated personas/constraints (e.g. transit-first, green-budget, industrial-split), then a native-validation + evaluator pass picks one — personas-as-sampling-cues beats temperature; never rely on temperature alone.
5. **Explicit goal decomposition with constraints.** Automatic-curriculum analog: each turn the planner must name the weakest city metric (zone balance / congestion / budget) and target it; forbid repeating the previous turn's target unless its metric is still worst.
6. **Exploiter role (AlphaStar-lite, inference-time).** A second prompt role critiques the proposed plan for fragility (single road artery, utility bottleneck) and must propose one distinct alternative; main planner must address or adopt. No training needed.
7. **Skill-library accumulation (Voyager-lite).** Persist verified district programs (road grid + zone + service placement) indexed by descriptor embedding; retrieve top-k dissimilar-but-relevant skills as in-context examples to compound repertoire without catastrophic re-copy.

## Sources (index)

- https://arxiv.org/abs/1504.04909 · https://www.mdpi.com/2227-7390/14/7/1091 · https://arxiv.org/pdf/2502.00593v1
- https://arxiv.org/abs/2305.16291 · https://voyager.minedojo.org/ · https://github.com/MineDojo/Voyager
- https://arxiv.org/abs/2304.03442 · https://dl.acm.org/doi/10.1145/3586183.3606763
- https://www.gamedeveloper.com/design/how-alphastar-became-a-starcraft-grandmaster · https://proceedings.neurips.cc/paper_files/paper/2023/file/94796017d01c5a171bdac520c199d9ed-Paper-Conference.pdf · https://arxiv.org/pdf/2012.13169v3
- https://proceedings.iclr.cc/paper_files/paper/2025/file/f0b1515be276f6ba82b4f2b25e50bef0-Paper-Conference.pdf · https://arxiv.org/abs/2403.00690 · https://proceedings.neurips.cc/paper_files/paper/2020/file/569ff987c643b4bedf504efda8f786c2-Paper.pdf
- http://arxiv.org/abs/2203.11171 · https://arxiv.org/pdf/2602.20408 · https://arxiv.org/pdf/2607.20464v1 · https://aclanthology.org/2025.emnlp-main.713.pdf
