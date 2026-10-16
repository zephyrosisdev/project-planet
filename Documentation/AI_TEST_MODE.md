# AI test / spectator mode

Choose **AI TEST / SPECTATE** beside RTS and Swarm in the main menu. Cyan and
Red both run base AI; the spectator has no construction, production, research,
rally or army-command controls. Camera movement, either-base focus, Full Map,
pause, atlas, settings and the GPU lab remain available. Clicking either team's
building opens its read-only statistics and queue. The resource bar follows the
observed team. Both faction summaries show armies, tech, power, expansion and
defence counts. Strategy profiles are chosen independently in the menu; Balanced
is the default for both. Terrain settings and seed are retained for repeat runs.

There are 100,000 ambient zombies by default, just as in regular RTS. This mode
does not start survival waves. All three sides are rendered on the spectator's
map, with separate minimap contact bits for overlapping Cyan, Red and zombies.
The volumetric fog-of-war pass is not created. AI scouting, radar, targeting and
stealth still use their normal intel buffers: spectator visibility does not
grant omniscient combat knowledge to either AI.

Both planners use normal building costs, wallets, queues, research prerequisites,
power, production, upgrades and placement rules. They prioritise income, renewable
backup power, industry, laboratories and armed defences; expand toward additional
nodes before remaining deposits cover less than about three minutes of output;
and retire exhausted extractors. Outposts extend build coverage along a coarse
land route. Routing here uses the existing bounded wall-preview A* over immutable
map metadata for base planning; it does not replace army pathfinding.

Wall perimeters are sized around the local buildings and production exits,
with 3-cell gates on all four faces. Plans must validate their entire perimeter;
illegal foundations are not skipped to leave a broken wall. They reserve gate
approaches and at least two navigation cells plus one placement tile of building
clearance (40m on a standard 4096m world). A bounded construction flood fill checks
both coarse navigation occupancy and the fine foundation grid, including every
ground-production bay's connection to the outside. Later AI buildings must preserve
those routes. All four gates must be completed, powered and traversable before the
walls close. The AI can try a different anchor when a crowded base is unsuitable.
Perimeter reservations leave room in the existing 512-building pool for both
economies; ordinary catalogue planning still builds armed turrets. Subsequent
outposts can be fortified too. Unavailable coast,
resource or technology placements do not block research/production. Search origins
and rings advance between AI cycles, and capital is reserved for command upgrades
so production cannot hold a faction permanently in its starting tier. The planners
attempt every feasible unlocked catalogue building rather than granting buildings
or technology for free.

Planning runs every three gameplay seconds, with the two faction cycles staggered
across frames and a 96-candidate placement budget per cycle. CPU ownership remains
limited to existing structures, resources, economy and compact commands. Armies
stay GPU authoritative. Test-mode attack/exploration orders use ten existing shared
flow groups (two teams × five movement classes). Every twelve seconds new troops
join the same objective without resetting the route sequence or an arrived unit's
slot. Exploration only changes after at least four assigned ground troops exist,
60% of them are within 96m of the objective, and the order is at least 45 simulation
seconds old. Discovered structures remain targets until removed; a changing economy
or expansion plan cannot redirect the army mid-march. Production rallies use this
same persistent objective. Regular Red AI uses aggregate rally-arrival counts for
exploration, retaining its existing individual movement orders.
There are no CPU unit lists, individual AI agents, per-unit objects or new movement
algorithms. Other modes retain their player ownership and million-unit stress path;
regular Red AI benefits from the planner fixes.

Combat destruction of an AI outpost is now remembered. The AI excludes new
construction within 160m of that failed site (including offset retries), cancels
that anchor's unfinished fortification plan, and directs existing troops there
as a persistent clearance objective. Multiple dangerous sites are handled in
order. Voluntary sales do not count as attacks. Other safe deposits, laboratories,
command upgrades and normal technology prerequisites continue to be considered.
Further forward posts must preserve capital for command upgrades and unlocked
research/strategic buildings, including missile silos. Three unresolved loss sites
pause new forward posts until the clearance backlog shrinks; existing safe deposits
can still be developed. Cleared-site history is pruned past 64 entries, while
unresolved sites are retained.

A GPU query of existing spatial-grid cells checks the 200m area once per simulation
second, only while clearance is active. It returns four aggregate counters: visible
hostiles of either other faction, friendly armed ground units, currently observed
cells and total area cells. It uses the AI team's real fog buffer even in spectator
mode. Empty, stale or inadequately scouted reports cannot approve construction.
Rebuilding requires no visible hostiles, at least four armed ground units, at least
90% current scouting coverage, consecutive fresh safe reports over 12–60 simulation
seconds (increasing with losses), and a 30–120 second loss cooldown. Cleared sites
retain their loss history; a later attack starts clearance again.

Clearance reinforcements use real production costs, at most one queue addition per
AI cycle, a material spending quota capped at 400 and refilled at 5 per simulation
second, and a minimum 200-material reserve. The quota grants no resources. Ground
combat units and scouts are preferred, and production upgrades are deferred during
clearance so replacements cannot consume all capital. In normal RTS the Red AI's
existing ground troops use reserved enemy flow group 1, keeping player route groups
available; spectator mode uses its existing movement-class groups. The GPU movement
solver, army ownership and population limits are unchanged. Checks and performance
records for this policy are under `TestResults/AiClearance`.

Production exits now check team navigation costs and fine foundation/hull occupancy.
The GPU fallback also validates an exact spawn candidate before accepting a coarse
cell centre, so an open coarse wall cell cannot spawn a unit inside a solid wall.
This check is confined to the existing base-building variant; non-base stress
movement remains unchanged. Validation for these corrections is under
`TestResults/AiAccess`, including a bounded 16-vehicle gate-egress fixture.

**EXPORT BALANCE SNAPSHOT** writes JSON under `Application.persistentDataPath/
BalanceTests`, including settings/seed/profiles, match time/outcome, team economy,
tiers/tech, structure/queue counts, aggregate armies/zombies and measured Unity
allocated/reserved memory. A snapshot is also saved at match outcome. It contains
no CPU unit-state copy and measured memory is not a dedicated-VRAM estimate.

Validation reports are in `TestResults/AiTest`. The accelerated Editor fixture uses
ordinary costs and wallets and disables damage to infrastructure; it tests planner
progression, not match balance. It reached tier 5 on both sides with 25/23 technologies,
multiple fortified outposts and replacement extractors after its depletion sequence.
The report records exact structure counts. The standalone fixture checks normal-speed starts,
default zombies, visibility/input isolation, snapshot export and return to regular RTS.

The standalone benchmark accepts `-benchmark-scenario ai-test` for a full-map test
view. Use the same seed, settings, profiles, camera and preparation when comparing
balance or performance. Offscreen benchmark captures exclude screen-space UI; army
GPU/total-fog scope values must remain unavailable when the recorder cannot measure
them. No army scale is lowered to report a pass.

The recorded early-development test view measured 8.035ms median / 10.880ms p95
at 30Hz simulation, with 100,008 live actors and 377.72/804.16 MiB measured Unity
allocated/reserved memory. Equivalent million-unit p95 changed +0.02%, with its
simulation-frame median steady; the raw frame median crosses a bimodal sampling
boundary, explained in `TestResults/AiTest/RESULTS.md`. The million-unit 60 FPS
target remains unmet, and missing army GPU/total-fog scopes remain uncertified.
