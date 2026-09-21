# Research: AI for urban planning in 2026

**Status:** frozen. Snapshot of 21 September 2026. Real-world city planning, not game agents. **[M]** = measured or stated in the cited source. **[O]** = interpretation of those sources.

Question: how do academia and industry use AI to *assist* urban planning, and where does AI actually *implement* a plan?

## Answer

In 2026 AI is a planning-support layer, not an autonomous planner. Academia generates layouts, forecasts, documents, and stakeholder simulations; industry ships option-explorers and GIS copilots; cities run digital twins and (where still live) traffic/emergency brains. Statutory land-use, public process, and approval stay with humans. The mature seams are morphology generation, environmental surrogates, document RAG, GeoAI extraction, and signal recommendations. The thin seams are statutory land-use, equity-aware evaluation, GIS-agent reliability, and any loop that writes a plan into the legal city without a planner in the loop. 2016–2020 “smart city OS” brands are not 2026 practice: Sidewalk Quayside ended 2020; Delve sunsets onto Google Maps Platform Earth; Alibaba City Brain product URLs 404 on this date.

## 1. Four jobs that get called “urban planning”

These jobs are routinely collapsed in marketing. They are different products. [O]

| Job | What the model actually does | Typical 2026 stack | Who signs |
| --- | --- | --- | --- |
| **Sense / forecast** | Predict traffic, crime, heat, mobility under data scarcity | Spatio-temporal encoders + LLMs (UrbanGPT); GeoAI in GIS | Analyst |
| **Generate form** | Street networks, parcels, massing, 3D layouts, floor plans | Constrained diffusion / DRL; TestFit / Forma / Finch; Esri Urban *from zoning rules* | Designer |
| **Language work** | Retrieve codes, draft/evaluate plans, answer the public | Domain RAG agents (PlanGPT); Esri / Autodesk assistants | Planner |
| **Operate the existing city** | Signal timing, incident routing, flood/landslide alerts | Google Green Light; HK 城市大脑 (building); City Brain as 2016–18 lineage | Traffic / emergency engineer |

Wu et al.’s 183-paper review (2016–July 2025) maps the same split: urban design and environmental modelling are relatively mature; transportation and participatory planning are experimental; land-use planning is still fragmented. [M] [Wu 2026 JUM](https://doi.org/10.1016/j.jum.2025.12.006)

Fu et al. give the planner-facing ladder: LLM as **planning database** → **virtual assistant** → **AI–planner team** that would need professional ethics, not just better text. They treat the third level as a research agenda, not a deployed state. [M] [Fu 2025 Nature Cities](https://doi.org/10.1038/s44284-025-00261-7)

Zhang et al. (Nature Cities, 5 August 2026): generative AI can synthesize images, model aspects of behaviour, and reason across heterogeneous data, but usefulness “depends less on technical novelty than on whether generative AI can be evaluated honestly, validated in context and deployed in ways that keep human judgment and accountability at the center.” [M] [Zhang 2026 Nature Cities](https://doi.org/10.1038/s44284-026-00492-2)

## 2. Academia: how the stack actually works

### 2.1 Layout as sequential search (DRL / graphs)

Zheng et al. (Nature Computational Science, 2023; still the cited computational baseline in 2026 reviews) treat community land-use and roads as sequential decisions on a **contiguity graph**, with GNN state encoding and PPO. Reward is 15-minute-city service, ecology, and traffic. [M] On synthetic and Beijing communities (Huilongguan CP-02, Dahongmen, ~4 km²): >48.6% better spatial-efficiency metrics than baselines/experts; >18.5% better facility accessibility on renovation of existing communities; human–AI workflow >3,000× faster than fully human layout, and preferred in a 100-designer blind test. [M] They keep humans on conceptual prototyping, land ownership, and public rights of way. [M] [Zheng 2023](https://doi.org/10.1038/s43588-023-00503-5) · [code](https://github.com/tsinghua-fib-lab/DRL-urban-planning)

Intelli-Planner (arXiv, January 2026) keeps the DRL layout loop and adds an LLM for high-level objectives plus LLM-simulated stakeholder scoring. Experiments are on communities in Beijing, Chicago, Madrid, not adopted municipal master plans. Authors: “relying solely on LLMs for planning may lead to unreliable results due to issues like hallucination.” [M] [Intelli-Planner](https://arxiv.org/abs/2601.21212)

CityPlanner (arXiv, 9 September 2026, EMNLP under review): a **file-based** sandbox, not a game. Agents inspect task files, generate plans, run evaluators, revise (BuildPlan then ImprovePlan). Closest academic cousin to an executable mayor; still paper-only. [M] [arxiv:2609.09578](https://arxiv.org/abs/2609.09578)

### 2.2 Generative morphology (GAN → diffusion → latent 3D)

Wu et al.: 2019–2022 GAN-dominated parcel/road/building generation; since 2023 diffusion, LLMs, multimodal, and multi-agent systems. Asia skews GAN morphology; Europe skews LLM regulation and deliberation. [M] [Wu 2026 JUM](https://doi.org/10.1016/j.jum.2025.12.006)

He et al. (npj Urban Sustainability, 4 April 2026): a latent model encodes arbitrary 3D blocks and generates layouts for the 330 North American cities with >100,000 inhabitants. Given a road network and ~5% of blocks, it fills a realistic 3D city for street-scale simulation, socio-economic prediction, and policy what-ifs — a digital-twin bootstrap, not a zoning ordinance. [M] [He 2026](https://www.nature.com/articles/s42949-026-00369-2)

Text-to-3D City (Computer Animation and Virtual Worlds, May 2026): **plan-then-execute** — LLM City Planner writes a PCG parameter schema; a deterministic Implementer builds roads, blocks, lots, and assets with validity checks. Engine-ready geometry, not a legal plan. [M] [doi:10.1002/cav.70124](https://doi.org/10.1002/cav.70124)

Wang et al. (Computers, Environment and Urban Systems, 2025): diffusion synthesis of satellite imagery for planning scenarios. [M] cited in [Zhang 2026](https://doi.org/10.1038/s44284-026-00492-2)

Stepwise ControlNet (arXiv:2505.24260, 30 May 2025): three human-gated stages — road/land-use, building layout, then rendering — because end-to-end models “overlook the iterative nature of real-world design.” Chicago and NYC open data. Prototype. [M] [arxiv:2505.24260](https://arxiv.org/abs/2505.24260)

### 2.3 Language models that know a city, not that design one

- **UrbanGPT** (KDD 2024): spatio-temporal encoder + instruction tuning for zero-shot traffic / bike / crime-style prediction under label scarcity. NYC taxi, bike, crime pre-training. Forecast, not layout. [M] [Li 2024](https://doi.org/10.1145/3637528.3671578) · [repo](https://github.com/HKUDS/UrbanGPT)
- **CityGPT** (KDD 2025): CityInstruction + SWFT fine-tune so smaller LLMs (ChatGLM3-6B, Llama3-8B, Qwen2.5-7B) match or beat proprietary models on **CityEval** urban spatial tasks. Cognition of urban space, not plan generation. [M] [arxiv:2406.13948](https://arxiv.org/pdf/2406.13948) · [repo](https://github.com/tsinghua-fib-lab/CityGPT)
- **UrbanLLM** (EMNLP Findings 2024): Llama-2-7B decomposes urban queries and routes them to specialized spatio-temporal models. Orchestrator, not a plan writer. [M] [Jiang 2024](https://aclanthology.org/2024.findings-emnlp.98.pdf)
- **PlanGPT** (ACL 2025 Industry Track): first specialized **agent** for urban/spatial planning documents. PlanRAG + PlanLLM + WebLLM + ToolLLM. Chinese regulatory style. Four planner tasks: proposal generation, style transfer, information extraction, document evaluation. PlanGPT (Qwen2.5-7B) 76.3 overall on PlanBench vs 69.5 for the instruct base; human scores 86.67 generation / 80.00 style transfer. Authors: “successfully deployed and used in several institutions.” [M] That is document assistance, not map writing. [Zhu 2025](https://aclanthology.org/2025.acl-industry.54/) Name collision: arXiv:2606.10489 “PlanGPT” is PDDL automated planning, not urban.
- **PlanGPT-VL** (May 2025): VLM for planning maps (land use, infrastructure, zoning symbology). Critical Point Thinking “reduce[s] hallucinations”; authors still own that “complete elimination of factual errors remains challenging,” and MMMU drops after specialization. [M] [arxiv:2505.14481](https://arxiv.org/abs/2505.14481)
- **UrbanPlanBench** (Apr 2025): exam-style items from China’s registered urban planner qualification. Authors: even the strongest models “fall short of meeting professional standards” and “falter in memorizing regulations, leading to factual errors.” [M] [arxiv:2504.21027](https://arxiv.org/abs/2504.21027)
- **UrbanLLaVA** (ICCV 2025): multimodal LLM for urban spatial reasoning. [M] cited in [Zhang 2026](https://doi.org/10.1038/s44284-026-00492-2)
- **GISAgentBench** (Aug 2026): 349 practitioner GIS tasks. Strongest agent fully solves **32.7%**; failures are omitted/mis-ordered operations, not malformed calls. Named silent killer: CRS mismatch invalidates distance, area, and intersection. [M] [arxiv:2608.01645](https://arxiv.org/abs/2608.01645)

### 2.4 Participation, twins, climate

LLM agents emulate stakeholders and deliberative processes (Zhou et al. 2024 preprint on participatory planning; later cited as a line of work). Wu et al. treat this as expanding but still experimental. [M] [Wu 2026 JUM](https://doi.org/10.1016/j.jum.2025.12.006)

Singapore DUCT (City and Environment Interactions, 2026): Digital Urban Climate Twin coupling meso/micro climate with anthropogenic heat from buildings, traffic, industry, power plants; Simulation-as-a-Service + model federation + UI. Demonstrated on Singapore. Planner-facing climate what-ifs, not land-use generation. [M] [doi:10.1016/j.cacint.2026.100301](https://doi.org/10.1016/j.cacint.2026.100301)

Wang, Fu, Lyles (Computers, Environment and Urban Systems 124, 2026): GenAI for spatial regeneration is useful only if problem formulation, data, evaluation, and governance stay inside planning theory and ethics. Historical data can reproduce spatial disadvantage. [M] [NSF PAR copy](https://par.nsf.gov/servlets/purl/10653772)

## 3. Industry: what actually ships

### 3.1 AEC option explorers (site / building, not the comprehensive plan)

**Autodesk Forma** (Spacemaker lineage). 15 September 2026 AU announcement: Autodesk Assistant answers natural-language site questions; Building Layout Explorer generates and compares multifamily layouts against unit mix; Forma Street Design (beta waitlist) for early street/intersection design that then hands to Civil 3D; Civil 3D becomes a Forma Connected Client; deeper Esri ArcGIS and Fugro subsurface. Autodesk’s own bar: commodity internet-trained AI “can’t clear” the built-world bar; “oftentimes, ‘probably right’ is still wrong.” Drawing Compliance Review “surfacing potential issues earlier while professional review remains part of the process.” [M] [Autodesk 2026-09-15](https://adsknews.autodesk.com/en/news/autodesk-forma-ai-aec-connected-workflows-2026/)

Building Layout Explorer (2 June 2026): experimental generative AI for commercial Forma Site Design users with US-stored data. Trained on aggregated 3D AEC data; floor plans from massing, building type, structural material. Autodesk: some outputs will be more useful than others; professional review stays in the process. [M] [Christensen 2026-06-02](https://adsknews.autodesk.com/en/news/building-layout-explorer-in-autodesk-forma/)

Esri shipped **ArcGIS for Autodesk Forma** (25 June 2025): Living Atlas / basemaps / zoning-context layers inside Forma, then out to Revit. GIS context, not an LLM that writes the master plan. [M] [Business Wire](https://www.businesswire.com/news/home/20250625664811/en/Esri-Launches-ArcGIS-for-Autodesk-Forma-Continuing-its-Partnership-to-Bring-Spatial-Data-to-AECO-Industry)

**TestFit Site Solver**: generative feasibility from zoning (FAR, DU/acre, parking), cores, cut/fill, and pro forma. Compare schemes on yield-on-cost; export to Revit/CAD. This is developer deal-vetting, not public planning. [M] [TestFit Site Solver](https://www.testfit.io/product/site-solver)

**Finch** (copyright 2026): AI agents for “buildable floor plans grounded in your firm's standards and local codes” and test-fits from a brief. Building/site design, one level below municipal comprehensive planning. Autodesk’s 2025 Forma connections article also names Finch. [M] [finch3d.com](https://www.finch3d.com/)

**Studio Tim Fu UrbanGPT 2.0** (industry PoC, 2026): diffusion + LLM inside Rhino/Grasshopper for GFA-optimised, context-bound massing. Not a municipal system. [M] [Parametric Architecture on Alpha](https://parametric-architecture.com/urban-gpt-alpha-studio-tim-fu/) · [Fu LinkedIn 2.0](https://www.linkedin.com/posts/tim-fu_finally-excited-to-share-where-we-are-activity-7440822988914053120-Habk)

### 3.2 GIS as the planning system (scenario review, not generation)

Esri docs “What is ArcGIS Urban” (v2026.2, last-modified 15 July 2026): “Visualize zoning rules in 3D. Convert legal text into a visual representation”; “Generate plausible buildings **according to zoning regulations**.” Rule-driven scenario GIS, not a learned generator. CityEngine is the same idea in CGA procedures. GeoAI is extraction/prediction from imagery (buildings, roads, land-use change), not writing a comprehensive plan. [M] [Urban docs](https://doc.esri.com/en/arcgis-urban/latest/get-started/get-started-what-is-urban.html) · [GeoAI](https://www.esri.com/en-us/capabilities/geoai/overview)

ArcGIS Urban May 2026 (Enterprise 12.1): 3D model upload, zoning-envelope comparison, suitability analysis. Review tooling. [M] [Esri Urban May 2026](https://www.esri.com/arcgis-blog/products/announcements/announcements/whats-new-in-arcgis-urban-may-2026)

Esri × Microsoft (14 July 2025): Azure OpenAI assistants across ArcGIS; GeoAI toolbox with 90+ pretrained deep-learning models; Teams declarative agent for map search. [M] [Esri announcement](https://www.esri.com/about/newsroom/announcements/esri-collaborates-with-microsoft-to-bring-arcgis-users-new-ai-enhancements)

June 2026: Survey123, Business Analyst, and translation assistants GA; ArcGIS Pro assistant in beta (ArcPy / SQL / common actions). [M] [Esri AI assistants June 2026](https://www.esri.com/arcgis-blog/products/arcgis-online/geoai/whats-new-in-ai-assistants-june-2026)

### 3.3 Infrastructure twins and operations AI

Bentley iTwin + Cesium + Google Photorealistic 3D Tiles (partnership 9 October 2024; visualization early-access toward 2025). Context for infrastructure twins, not land-use generation. Blyncsy + Google Imagery Insights (Google Cloud Next, 9 April 2025): AI roadway-asset detection from Street View. Operations / maintenance. [M] [Bentley–Google 2024](https://www.bentley.com/news/bentley-systems-partners-with-google-to-bring-powerful-geospatial-context-and-capabilities-to-infrastructure-3/) · [Blyncsy 2025](https://www.bentley.com/news/bentley-systems-partners-with-google-to-improve-infrastructure-through-asset-analytics-2/)

**Google Green Light** (first-party, still “early research phase,” free to partner cities): AI + Maps driving trends → traffic-signal timing recommendations. City engineers review, approve, implement on existing hardware in ~5 minutes. Launched 2023; by the current product page, **100+ cities**, up to 47M car rides/month at optimized intersections; claimed potential up to 30% fewer stops and 10% fewer GHG emissions at treated intersections (early averaged points, DOE emissions model, single vehicle type). Kolkata: 13 intersections since November 2022. This is operations, not comprehensive planning. [M] [Green Light](http://sites.research.google/gr/greenlight/)

**Delve / Sidewalk Labs (sunset, not current).** `delve.sidewalklabs.com` HTTP 200-redirects to Google Maps Platform Earth with `utm_campaign=sunset`. Earth copy: generative design for siting and “building and solar designs at a community scale.” Quayside as an Alphabet urban-tech concession ended 2020; Waterfront Toronto’s 2026 homepage continues housing/public space with no Sidewalk product. [M] [Earth capabilities (Delve sunset)](https://mapsplatform.google.com/maps-products/earth/capabilities/) · [Waterfront Toronto](https://www.waterfrontoronto.ca/)

**Replica** (Sidewalk spinout): transportation analytics for agencies, not plan generation. [M] [replicahq.com](https://replicahq.com/)

## 4. Cities and states: live systems

### 4.1 China: documents, “one map,” then maybe space

*国土空间规划领域生成式人工智能应用蓝皮书（2025年）* (Tongji + Wuhan, released 27 June 2025 at the 18th China Smart City Conference, organized under MNR Spatial Planning Bureau): survey of GenAI in territorial spatial planning; current use is text generation, cognitive assistance, analysis, visualization; stated next step is spatial simulation, scheme rehearsal, monitoring/early warning. “Intelligent planning is a long-term goal; GenAI is already usable in places.” [M] [Tongji lab](https://caup-lab.tongji.edu.cn/7b/ff/c23526a359423/page.htm)

CSPON (国土空间规划实施监测网络) technical guide: build a **planning-industry large model** from NLP / vision / geospatial bases plus planning knowledge graphs; local “智能体” for auxiliary compilation, results review, monitoring, evaluation, simulation. National training, local knowledge bases. [M] [CSPON PDF](https://www.hbqj.gov.cn/szrzyhghj/zfxxgk/zc/bbmwj_1/202512/P020251217534252236548.pdf)

MNR *自然资源管理和国土空间规划“一张图”总体设计* (自然资办函〔2026〕576号, 24 March 2026): use AI/big data/cloud to unify the national “one map”; six application scenes including plan compilation/review and monitoring. Target: by end of 2027, database cluster + much higher scene intelligence. [M] secondary copy of the circular: [guoturen summary](https://www.guoturen.com/wenku-21030.html) — treat the circular itself as authority if obtained.

PlanGPT’s Chinese-document deployment is the academic/industry overlap of this policy stack. [M] [Zhu 2025](https://aclanthology.org/2025.acl-industry.54/)

### 4.2 City Brain is operations, not a master plan

Alibaba ET City Brain (Hangzhou, 2016–; 2.0 in 2018): traffic-light control, incident detection, emergency green waves; later fire, security, health, tourism. Hangzhou coverage claimed 420 km² / 1,300 lights at 2.0 launch. [M] [Alizila 2018](https://www.alizila.com/alibaba-cloud-launched-city-brain-2-0-hangzhou/) · [Caprotti & Liu 2020](https://pmc.ncbi.nlm.nih.gov/articles/PMC7607375/) On 21 September 2026, Alibaba Cloud `et-brain` and historical City Brain URLs resolved to **notfound** or a generic generative-AI solutions page. Treat 2016–2019 City Brain as **lineage**, not a confirmed 2026 product. [M] second-pass fetch.

Hong Kong (17 September 2026): first-phase **AI 城市大脑** on Government Cloud, Security Bureau emergency centre as the first scene, landslide and flood AI systems as first feeds; ~HK$200M; Q1 2028 phase-1, Q3 2028 1823 + emergency data. Explicitly modelled on Shanghai/Hangzhou; 6–9 years to maturity. Emergency governance, not statutory planning. [M] [news.gov.hk 2026-09-17](https://www.news.gov.hk/chi/2026/09/20260917/20260917_180421_271.html)

### 4.3 Helsinki / 5CC+ twins

Helsinki (May 2026 5CC+ gathering with Hamburg, Prague, Rotterdam, Vienna, Singapore): 3D city model as the UI for urban data; use cases city planning, permitting, green environment, traffic. Finland adopted **BIM-based permitting in 2026**. Goal: all built and natural environment as CityGML (1.6M trees underway). Singapore using AI to auto-generate mapping datasets (e.g. solar panels) from aerial/mobile capture. [M] [AEC Business](https://aec-business.com/latest-experiences-in-urban-digital-twins-at-5cc-helsinki-gathering/)

Helsinki Energy and Climate Atlas already operational for solar, building energy, geothermal, heat-island, stormwater; vegetation entering the twin. “No longer just a vision, it is already part of daily operations.” GeoAI still described as exploratory in the same municipal line. [M] [Mayors of Europe](https://mayorsofeurope.eu/news/how-helsinki-reads-its-own-city-it-never-stopped-building/) The city’s own “Urban planning and construction” pages describe conventional zoning, plan alerts, and resident feedback and **do not claim an AI planning product**. [M] [hel.fi](https://www.hel.fi/en/urban-environment-and-traffic/urban-planning-and-construction)

## 5. What AI is allowed to do vs what a human still owns

| AI may | Human still owns |
| --- | --- |
| Generate *alternatives* (massing, floor plates, signal timings, draft text) | Goals, values, land ownership, public process |
| Score options on stated metrics (FAR, 15-minute access, yield, UTCI) | Which metrics count, and for whom |
| Retrieve and compare codes / the local network of plans | Legal interpretation and adoption |
| Surrogate expensive physics (wind, heat, flood) for early design | Sign-off on safety-critical analysis |
| Recommend operations (Green Light; HK 城市大脑 in build) | Implementation on the live network |

Fu et al.: current ChatGPT/Copilot use in planning offices is already common for drafting and summarising, but “adequate but inconsistent”; models still misread planning jargon (Arnstein’s ladder, incrementalism). Hallucination, bias, cloud data-sovereignty, and black-box accountability are the adoption barriers they name. [M] [Fu 2025](https://doi.org/10.1038/s44284-025-00261-7)

Autodesk on Drawing Compliance Review: “surfacing potential issues earlier while professional review remains part of the process.” [M] [Autodesk 2026-09-15](https://adsknews.autodesk.com/en/news/autodesk-forma-ai-aec-connected-workflows-2026/)

Google Green Light: engineers accept or reject; no extra hardware; no user data shared with the city. [M] [Green Light](http://sites.research.google/gr/greenlight/)

Zheng et al. explicitly leave public engagement, land ownership, and rights of way to humans. [M] [Zheng 2023](https://doi.org/10.1038/s43588-023-00503-5)

## 6. Failure modes (where the evidence is thin)

1. **Wrong job.** Green Light and HK 城市大脑 (in build) are operations. 2016–18 City Brain is lineage; 2026 Alibaba product URLs 404. Treating operations AI as “AI does urban planning” overstates. [O]
2. **Visual metrics ≠ planning objectives.** Wu et al. and Wang et al.: papers still score realism, not accessibility, equity, phasing, or legal implementability. Land-use at statutory scale is the weakest GenAI domain. [M]
3. **Historical data encodes disadvantage.** Wang, Fu, Lyles 2026: regeneration models trained on past cities can reproduce spatial inequality unless the problem is formulated against planning ethics. [M]
4. **Geographic bias in foundation models.** Manvi et al. 2024 (ICML), cited by Zhang 2026: LLMs are geographically biased; Global South / informal urbanism is under-represented in the training prior. [M]
5. **Hallucinated codes and maps.** PlanGPT exists because generic LLMs fail governmental document style; text-evaluation accuracy is still 41%. UrbanPlanBench: models miss the registered-planner bar, weakest on regulations. PlanGPT-VL reduces but does not eliminate map errors. Intelli-Planner: pure-LLM planning “unreliable.” [M]
6. **GIS agents are not ready.** GISAgentBench (Aug 2026): best full success **32.7%**; silent CRS mismatch. [M]
7. **Digital twins are data platforms first.** Helsinki and Singapore spend years on CityGML, permitting, and climate coupling. AI sits on that substrate; it does not replace it. Esri Urban generates massing *from* zoning text, it does not write the text. [M]
8. **Paper-to-city gap.** Zheng’s 3,000× speedup is a lab protocol. CityPlanner is files + evaluators. PlanGPT is the rare “deployed in institutions” claim, and it is document work. Simulated LLM “residents” are not a public process (Zhou 2024; arXiv:2402.11314). [O]
9. **Brand collapse.** Quayside/Sidewalk 2020; Delve sunset 2026; City Brain URLs dead. Do not treat 2016–2020 smart-city press as 2026 practice. [M]

## 7. arXiv sweep (21 Sep 2026)

Second-pass abstracts (parallel fetch) fill in what the Atom titles only named. Does not change the four-job split.

Closest to an **executable** planner: [CityPlanner](https://arxiv.org/abs/2609.09578) is a file-based sandbox with evaluators, not a city and not a game. Evaluation: [UrbanPlanBench](https://arxiv.org/abs/2504.21027) (models miss the planner exam, especially regulations), [PlanBench-V](https://arxiv.org/abs/2606.05744), [GISAgentBench](https://arxiv.org/abs/2608.01645) (32.7% full GIS success). PlanGPT-VL and the stepwise ControlNet urban-design paper are prototypes with human checkpoints. Name collision: arXiv:2606.10489 “PlanGPT” is PDDL, ignore.

## Sources

| Source | Date | Kind |
| --- | --- | --- |
| [Wu et al., JUM, Generative AI for complex urban planning](https://doi.org/10.1016/j.jum.2025.12.006) | online 7 Jan 2026 | academic review, 183 papers |
| [Zhang et al., Nature Cities, Generative AI in urban science and practice](https://doi.org/10.1038/s44284-026-00492-2) | 5 Aug 2026 | academic review |
| [Fu et al., Nature Cities, Large language models in urban planning](https://doi.org/10.1038/s44284-025-00261-7) | 9 Jun 2025 | perspective; [QUT accepted PDF](https://eprints.qut.edu.au/257830/1/E-prints.pdf) |
| [Wang, Fu, Lyles, CEUS 124](https://par.nsf.gov/servlets/purl/10653772) | 2026 | regeneration ethics review |
| [Zheng et al., Nat Comput Sci](https://doi.org/10.1038/s43588-023-00503-5) | 11 Sep 2023 | DRL community layout |
| [He et al., npj Urban Sustainability](https://www.nature.com/articles/s42949-026-00369-2) | 4 Apr 2026 | latent 3D layout, 330 NA cities |
| [Li et al., UrbanGPT, KDD](https://doi.org/10.1145/3637528.3671578) | 2024 | ST-LLM forecast |
| [Feng et al., CityGPT, arXiv 2406.13948](https://arxiv.org/pdf/2406.13948) | May 2025 rev / KDD 2025 | spatial cognition LLM |
| [Zhu et al., PlanGPT, ACL Industry](https://aclanthology.org/2025.acl-industry.54/) | Jul 2025 | planning-document agent |
| [Jiang et al., UrbanLLM, EMNLP Findings](https://aclanthology.org/2024.findings-emnlp.98.pdf) | 2024 | query decomposition |
| [Intelli-Planner](https://arxiv.org/pdf/2601.21212v1.pdf) | Jan 2026 | LLM + DRL layout |
| [Text-to-3D City](https://doi.org/10.1002/cav.70124) | May 2026 | LLM planner + PCG |
| [Singapore DUCT](https://doi.org/10.1016/j.cacint.2026.100301) | 2026 | climate twin |
| [Autodesk Forma AU 2026](https://adsknews.autodesk.com/en/news/autodesk-forma-ai-aec-connected-workflows-2026/) | 15 Sep 2026 | product |
| [Forma Building Layout Explorer](https://adsknews.autodesk.com/en/news/building-layout-explorer-in-autodesk-forma/) | 2 Jun 2026 | product |
| [Esri ArcGIS as a Planning System Q2 2026](https://www.esri.com/en-us/industries/blog/articles/arcgis-planning-system-q2-2026-update) | Q2 2026 | product |
| [Esri Urban May 2026](https://www.esri.com/arcgis-blog/products/announcements/announcements/whats-new-in-arcgis-urban-may-2026) | May 2026 | product |
| [Esri × Microsoft AI](https://www.esri.com/about/newsroom/announcements/esri-collaborates-with-microsoft-to-bring-arcgis-users-new-ai-enhancements) | 14 Jul 2025 | product |
| [ArcGIS for Autodesk Forma](https://www.businesswire.com/news/home/20250625664811/en/Esri-Launches-ArcGIS-for-Autodesk-Forma-Continuing-its-Partnership-to-Bring-Spatial-Data-to-AECO-Industry) | 25 Jun 2025 | product |
| [TestFit Site Solver](https://www.testfit.io/product/site-solver) | fetched 2026-09-21 | product |
| [Google Green Light](http://sites.research.google/gr/greenlight/) | fetched 2026-09-21 | municipal ops |
| [Bentley–Google iTwin](https://www.bentley.com/news/bentley-systems-partners-with-google-to-bring-powerful-geospatial-context-and-capabilities-to-infrastructure-3/) | 9 Oct 2024 | product |
| [Alibaba City Brain 2.0](https://www.alizila.com/alibaba-cloud-launched-city-brain-2-0-hangzhou/) | 2018 | municipal ops (**lineage**; 2026 product URLs 404) |
| [Delve → Google Earth sunset](https://mapsplatform.google.com/maps-products/earth/capabilities/) | fetched 2026-09-21 | Sidewalk generative siting, sunset |
| [Finch](https://www.finch3d.com/) | copyright 2026 | building/site generative design |
| [Esri Urban docs 2026.2](https://doc.esri.com/en/arcgis-urban/latest/get-started/get-started-what-is-urban.html) | 15 Jul 2026 | rule-driven scenario GIS |
| [GISAgentBench](https://arxiv.org/abs/2608.01645) | 3 Aug 2026 | GIS agent eval, 32.7% |
| [UrbanPlanBench](https://arxiv.org/abs/2504.21027) | Apr 2025 | planner-exam benchmark |
| [PlanGPT-VL](https://arxiv.org/abs/2505.14481) | May 2025 | planning VLM |
| [CityPlanner](https://arxiv.org/abs/2609.09578) | 9 Sep 2026 | file-sandbox executable agent |
| [Stepwise ControlNet urban design](https://arxiv.org/abs/2505.24260) | 30 May 2025 | human-gated diffusion stages |
| [HK AI 城市大脑](https://www.news.gov.hk/chi/2026/09/20260917/20260917_180421_271.html) | 17 Sep 2026 | municipal ops |
| [MNR GenAI 蓝皮书 2025](https://caup-lab.tongji.edu.cn/7b/ff/c23526a359423/page.htm) | 27 Jun 2025 | policy survey |
| [CSPON guide](https://www.hbqj.gov.cn/szrzyhghj/zfxxgk/zc/bbmwj_1/202512/P020251217534252236548.pdf) | 2025 | policy |
| [Helsinki 5CC+](https://aec-business.com/latest-experiences-in-urban-digital-twins-at-5cc-helsinki-gathering/) | May 2026 | municipal twin |
| [hel.fi urban planning](https://www.hel.fi/en/urban-environment-and-traffic/urban-planning-and-construction) | fetched 2026-09-21 | conventional process; no AI claim |
