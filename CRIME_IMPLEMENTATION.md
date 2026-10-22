# Current work: collision, society, crime and countries

User scope (all active; later messages add requirements):
- Fix sliding while idle on ramps, falling through ramps, interiors and garden plots.
- Persistent social ties for citizens/AI (family, friends, rivals, gangs); compact GPU-side army cohorts.
- Probabilistic crime weighted toward hardship, also possible for wealthy people. Most poor people law-abiding. Growing settlements lose small-community protection.
- Adult addiction, job loss, begging, theft, burglary, mugging, robbery; dealers lease/own warehouses/workshops/houses, buy fictional precursor commodities, manufacture and sell near shops.
- Adult independent sex workers and exploited workers in unsafe neighbourhoods; independent work lawful by default, coercion/pimp exploitation a crime. Adult only.
- Police random proximity searches, arrests or armed resistance, fleeing/gang reinforcement/response. Weapons and ammunition must come from actual purchases/stock; no magically armed gangs.
- County jail/holding cells -> real court hearing -> capacity-limited modular federal prison. Own paid AI planner for fences/gates/cells/wings/recreation yards/food/rehabilitation and release. Not required in fresh settlements.
- Robbery/homicide bodies persist at death location until GPU citizen/police proximity discovery; only discovered bodies get reports/hearses.
- Customs can seize interplanetary contraband cargo, finite manifest and confiscation records.
- Configurable number of countries (human suggested four, wants more than just two); neutral initially, alliances, declarations of war; military border entry without alliance is an act of war. Trade menu and optional AI auto-trade.

Constraints: AGENTS.md GPU-authoritative army, no CPU unit poses/health mirrors or per-unit GameObjects. Compare equivalent standalone before/after simulation changes; >5% army simulation regression rejected. Preserve million stress path and settings. No subagents authorized.

Work completed in sources, NOT COMPILED OR VALIDATED YET:
- PlanetPlayerCollision: walkable contacts discard downward gravity before projection (fix idle slope sliding); floor snap filters walkable normals; overlap/cast scratch arrays 32.
- PlanetBuildingLod: cached plotRadius; PlanetModularBuildings: collision/interior pool prioritizes observer inside building plot regardless of visibility/model bounds. Needs more testing (pool still four).
- NEW SettlementCrime.cs: saved crime/social/gang/business/case/prison models, weighted risk, daily biography/economics, conserved legal purchases, fictional production, black-market leasing, recruitment, street destinations, arrests, court/release, body-discovery hooks. File currently references fields and enum members not added yet, so will not compile until hooks implemented. Needs real GPU encounters, custody containment, dealer purchase flow, weapons/ammunition, prison planner, UI, save/migration and tests.
- No other new-scope implementation yet. Do not claim completed.

Baseline launched from existing compiled player BEFORE changes:
TestResults/Planetary/Crime/Baseline, -planet-interest-validation -planet-interest-count 1000000. exec session 98835; poll for completion. Existing player still prior foundations implementation (Unity 6000.6.4f1 DX11).

Architecture decisions in progress:
- Existing resident biographies/economics CPU once/day; live movement, proximity searches/combat/corpse discovery stay GPU.
- Add separate bounded SettlementCrime.compute only dispatched when crime/undiscovered bodies present; use GPU spatial hash and bounded event drain/ack, no per-frame poses readback.
- Existing CivicAgent 48B has spare pad; flags currently 1 outdoor,2 officer,4 death acknowledged,8 combat engaged. Plan crime corpse flags 256 undiscovered/512 corpse; custody/flee/fight separate. CrimeAgent buffer can store cooldown/status/pending event, not CPU mirror. Drain per-agent pending events with generation acknowledgement to retain overflow.
- Existing ReadDeaths immediate zombie events; crime murder should emit own GPU event and set death-ack flag to avoid mislabelling as zombies. Hold dead resident slot until hearse collected to preserve body. Add DeathRecord.discovered/default true, discoveredAt/by; murder/robbery false and hearses must filter discovered.
- Four-country refactor not started. Current country ids 0/1, zombie team 2. Proposed configurable 2..8 countries and move zombie team to 31, with explicit masks and existing group indices 15+country. Must audit existing team==2 assumptions, shaders, faction arrays, nav, UI, spawning, save and validation fixtures. Performance fixtures should explicitly retain two countries/war to remain equivalent to old baseline; run separate four-country correctness fixture.
- Diplomacy neutral/allied/war with compact GPU hostility masks. Military incursion checks only non-zombie units, bounded nearest-country territory check; avoid CPU unit mirror/new full-million pass.

Tools/workflow from prior turn:
- Use existing Unity watcher TestResults/Planetary/request.txt; actions build, modular-profiles, modular-bake, modular-validate, civic-art, street-validate etc. Atomic write tmp.replace request.
- Ctrl+R via computer-use skill @oai/sky in node_repl. Persistent bindings sky, unityWindow=263700. Tool discover mcp__node_repl__js via ALL_TOOLS. Read skill C:/Users/leeda/.codex/plugins/cache/openai-bundled/computer-use/26.1016.51333/skills/computer-use/SKILL.md and guidance before controlling UI (prior conversation has read them).
- Build output TestResults/Planetary/Build/PlanetDevelopment.exe, report build.txt. No batchmode (licensing). Existing editor stopped on MassiveBattle. Build can trigger lengthy HDRP reimports. Do not edit sources during build; after report success wait background imports before perf.
- Start standalone via Start-Process -WindowStyle Hidden -PassThru then WaitForExit in SAME exec_command (otherwise sandbox kills GUI child). exec session poll write_stdin.
- Default sandbox Get-Process hides host Unity; require_escalated read-only can inspect processes. Do not print full Unity command line (contains access token).
- Python Windows cp1252 default: explicitly UTF-8 reads/writes. Use rg first. No recursive deletion needed.
- Many previous features uncommitted; preserve ALL existing modifications, selected model catalogue Baked 3, authored BattleAtmosphereProfile exposure.
- Builds regenerate scene object ids, ProjectSettings Windows API DX11 vs DX11+DX12 and four dynamic SDF caches. Restore only verified generated churn after all tests, not authored changes. Prior detailed validation in TestResults/Planetary/Foundations/Results.md.


2026-10-22 zoning continuation (latest user request):
- Farms and industry both peripheral: new placement keeps their whole conservative plot envelope beyond twelve road tiles from the country's original town centre.
- A symmetric six-tile clear buffer separates industry from residential, commercial and agricultural plots (192 m at the default 32 m tile size); farm-to-farm and industry-to-industry clustering remains allowed.
- New SettlementZoning infrastructure-only body-space bucket index avoids cube-face seam and floating-origin discontinuities. Built incrementally; removed buildings no longer block. Both automatic candidates and manual Validate use the rule.
- Agricultural frontier anchors now include other crop types and shared agricultural neighbours. Shops prefer commercial anchors instead of residential frontage. Starter warehouses in normal new games are peripheral; saved structures and original benchmark fixtures retain their layout.
- Corrected distillery/chemical plant to Industrial, off licence to Commercial, hydroponics to Agricultural.
- Added Editor watcher action zoning and PlanetZoningValidation. Unity compiled the sources; zoning validation PASS (80 checks). No standalone/performance acceptance claimed for the broader pending simulation changes.
- Earlier portions of this file are stale: collision has passed 7758 editor checks; crime GPU/events, prison planner and partial country scaffolding are present in sources but still need full integration and standalone acceptance. Do not claim the entire expanded crime/countries/trade task complete.


2026-10-22 crime reporting UI:
- CRIME toolbar button, settlement-window tab, police/jail/court/prison shortcuts.
- Paginated incident reports, suspects/victims/officers, timestamps, hearings/outcomes, custody stages/facilities, gang and suspected-premises lists. Saved incident camera focus and optional bounded orange minimap overlay (max32 markers, latest256records); no live unit readback.
- Resident profiles show police records; spectators additionally see underlying activity, dependencies, ties and gang. Player reports hide unreported activity and undiscovered bodies. Death causes use correct homicide/robbery/police-violence labels.
- All available country selectors in settlement window, view-only spectators; report UI never mutates simulation records.
- Unity compiled; crime-ui watcher PASS19checks at TestResults/Planetary/Crime/UI/editor.json. Includes visibility rules, callback location, paging, empty states and read-only records. This does not certify the larger unfinished crime simulation or its standalone performance.
