# Current RTS content on the planet

Normal menu sessions and direct development-scene launches use **120 km radius
(240 km diameter)**, defined by `PlanetDefinition.DefaultRadius`. Terrain remains
streamed. Fine navigation is allocated as reusable 2,048 m tactical patches with
16 m cells around shared orders; coarse cube-sphere fields handle distant travel.
Large-world validation and performance measurements are recorded in
[TestResults/Planetary/LargePlanet](../TestResults/Planetary/LargePlanet/README.md).

Open `Assets/Scenes/PlanetDevelopment.unity` and enter Play. The scene now defaults
to the current RTS content rather than the old tank-only demonstration. `MassiveBattle.unity` keeps the original main menu. Base Building RTS, Swarm
Survival and AI Test now create planetary sessions. Deploy Stress Armies retains
the original flat million-unit path.

## Scene lighting and planetary layer blending

Terrain uses an HDRP custom pass before transparents, with the real scene sun,
exposure and directional shadow buffers. Menu sessions reuse and restore the
original BattleLightingRig; direct launches create an equivalent bounded rig.
Buildings cast shadows onto terrain; unit shadows remain disabled.

Original terrain-library transition width, boundary warp, patch scale and patch
strength now drive body-local 3D noise. Normal maps, height blending and existing
decal inputs remain active. Scaled-space celestial bodies use physical occlusion
against the main planet, including partial limbs and floating-origin shifts.

[Visual repair validation and measured rendering cost](../TestResults/Planetary/VisualRepair/README.md)
records 134 passing checks and the remaining army performance limitations.

## Reused content and controls

- The current `RtsCatalog` and its 31 unit definitions, health, speed, traversal
  classes, altitude and production/technology requirements.
- `ArtUnitRenderer`'s existing near/far vertex banks, texture arrays and eight-view
  impostors. GPU culling writes three indirect draw lists. There are no unit
  GameObjects or CPU unit mirrors.
- Existing building definitions, meshes, materials, gate leaves and UI portraits.
  The starting base has a Command Centre, Barracks, Light Vehicle Depot and Solar
  Array. Initial army records use the current Engineer, Scout and Infantry types.
- The production `Front.uxml`, theme, fonts, catalogue/resource templates and
  `RtsUiController` panel lifecycle. Its original `BattleApp` constructor delegates
  to the same implementation; the planet supplies build/refresh/state callbacks.
- Existing `RtsBuilding`, `QueueOrder`, `RtsFaction`, `ResourceWallet` and technology
  records. Army simulation remains GPU authoritative; bounded base infrastructure
  is CPU controlled, as in the flat RTS.

Click/drag selects units; Shift adds and Ctrl removes. Right-click issues a move
order, X stops, and Escape clears selection. Click a building to open its context.
Use the construction catalogue, R to rotate the preview, click terrain to place,
or right-click to cancel. Building context supports training, partial queue
refunds, research, upgrades and pause/resume. F switches RTS/first-person control
while army/base ticks continue.

The six-face minimap reuses biome colours, displays structures and camera focus,
and lets you click to navigate the globe. Its unit-density/fog overlays are still
pending. The resource icons and catalogue portraits come from the current RTS UI.

Buildings align to radial up and use a rigid foundation raised to the sampled
footprint. Placement checks dry land/coast/deposit rules, slope, footprint
overlap, construction reach, resources and required technology. Construction
stamps obstacles into the GPU navigation costs and invalidates shared fields.
Production searches for a compatible clear exit and appends a stable GPU slot;
it waits instead of silently overwriting units when capacity or exits are full.
Placement and spawn occupancy use a GPU query returning one uint; unit positions
are never copied into the CPU base simulation.

Mixed selections reserve separate fields for infantry, light/heavy vehicles,
aircraft and vessels. Assignment first plans every group and rejects the entire
order if capacity or destination traversal rules fail. Only 42 aggregate uint
counters return to the CPU on command events, including five movement-class
counts. They are synchronous command-time reads, not a unit-state mirror.

## Bounds and outstanding migration

The normal planet scene includes 256 ambient zombies. Menu sessions use the
selected army capacity plus the configured ambient-zombie allocation. RTS also
starts a small red infantry force. Swarm reuses `SwarmSchedule`, including 600
seconds of preparation, a 30-second warning and a 1000-attacker first wave that
doubles after clearance. Zombies use the existing definition, shared geometry,
walk animation, green flesh shading and GPU LOD rendering.

`RtsHudLayout` is now the common main HUD for both backends; `RtsConstructionPanel`
is the common virtualized eight-category catalogue. The planet also has a
technology dependency graph, pause/menu/map controls, repeat production,
repair/power/sell and upgrade cancellation. Menu return destroys all session GPU
buffers and restores the original menu camera. No hidden flat army is allocated.

Planet climate uses matching integer-hashed 3D noise on CPU/GPU. Body-local
triplanar sampling reuses the original nine-layer `TerrainSurfaceLibrary`, normal
maps, height competition and roughness data. The existing resource artwork uses a
six-face spatial lookup; combat deaths use a bounded 512-entry GPU decal ring and
face-cell winner indices with original splatter/remains/wreck albedo and normals.
The temporary casualty index keeps the newest indexed stamp. Nuclear scorch marks now use separate persistent surface paint, so deaths and later impacts cannot recycle them. Geometric deformation is implemented; roads remain pending.

The encounter path keeps GPU snapshots, linked cell lists, bounded local contact
separation, target acquisition, damage accumulation, statistics and free-slot
recycling on GPU. CPU reads aggregate army counts and bounded building damage.
AI Test currently uses opposing army advances; it is **not yet the original
strategic base-building planner**. The original profile choices, expansion,
noise/hearing behavior, formations, transport/scouting/supply operations,
projectiles/splash and fog/sensors are not fully migrated. Unsupported HUD actions
are visibly disabled. Hits currently apply catalogue damage directly; this is not
full original combat parity. First-person building/interior collision and vehicle
possession also remain pending.

The base adapter reserves 128 building identities and at least 4096 army slots;
menu allocation preserves the chosen capacity up to one million. Dead army slots
can be reused by GPU encounter spawning; normal production still appends within
the reserved headroom. Bounded local acquisition examines up to 32 links in each
of nine neighbour cells, so dense-cell targeting and long-range weapons still need
parity work. These bounds are not a certified million-unit planetary result.

## Validation and reproducibility

`-planet-rts-validation -planet-rts -planet-output DIR` runs an explicit 2560x1440
standalone scene and shared-HUD action check. It captures world and UI separately
and composites screenshots, checks radial building poses, construction, current
unit training and GPU record identity, cancellation, technology completion and
continuing ticks in first person. These calls exercise HUD action handlers,
not synthetic physical mouse events.

The coordinate/navigation, adaptive terrain and command editor validations remain
available under `Massive Army > Planetary`. Command checks also exercise mixed
infantry/armour/air group assignment and transactional rejection.

`-planet-fixture` retains the 32-tank comparison fixture. Existing `-planet-smoke`
and `-planet-benchmark` select that fixture automatically; add `-planet-rts` to
explicitly include current content. This keeps earlier comparisons reproducible
without presenting a small fixture as the full planetary performance target.

`-planet-menu-validation -planet-output DIR` exercises the actual original menu,
all three planetary modes, eight categories, GPU zombie motion, wave scheduling,
combat death/decal generation, slot recycling and session disposal. Test-only
unit snapshots are used solely for assertions, never in gameplay simulation.

Performance results and their limits are recorded in `PLANETARY_COMMANDS.md` and
`TestResults/Planetary`. Separate army GPU timing, million-unit planetary
simulation, full command occupancy and <=3 ms p95 fog acceptance remain unverified.

## Measured current-content scene

Fresh standalone RTX 3070 / DX11 runs at 2560x1440 include the current models,
four buildings and offscreen shared-HUD rendering/compositing. Each starts with
32 units and a scripted mixed infantry/scout move order, warms for five seconds,
and records twenty seconds. Surface camera distance is 300 m; orbit is 1700 m;
FPS uses the radial player controller while the army keeps ticking.

| View | Median ms | p95 ms | Simulation Hz | Peak tracked MiB | Peak reserved MiB |
| --- | --- | --- | --- | --- | --- |
| Surface | 3.005 | 3.576 | 30.00 | 466.48 | 541.28 |
| Orbit | 2.851 | 3.457 | 30.00 | 477.59 | 797.34 |
| FPS | 2.633 | 3.080 | 30.00 | 476.82 | 795.34 |

Raw frames, reports, run arguments and assembly fingerprints are under
`TestResults/Planetary/RTS/Benchmarks`. Tracked allocation/reservation is measured
with Unity's profiler; it does not measure dedicated VRAM. This content scene is
not equivalent to the former tank-only fixture, so these are absolute integration
measurements, not a claim of an army-simulation speedup. The equivalent command
controls and their preserved rejected runs are documented separately.

The release build succeeds with zero errors. The 21-check gameplay/HUD standalone
test passes both with `-planet-rts` and with the normal default scene. It trains
an Infantry unit from 32 to 33 active GPU records and checks its catalogue type
and health. The 42 coordinate/navigation, 14 terrain and 22 command editor checks
also pass. Captures and reports are in `TestResults/Planetary/RTS/FinalSmoke` and
`DefaultSceneSmoke`.

## October 15 menu / surface restoration

The eight construction categories and main HUD now come from the same builders
as the original flat RTS. `StartFlatValidationBattle` is an explicit historical
fixture hook used by the existing flat regression/benchmark harnesses; normal
Base/Swarm/AI menu actions always use the planet. This keeps historical comparisons
equivalent without routing normal menu sessions back into flat gameplay.

Correctness reports are in `TestResults/Planetary/Repairs`: `MenuFinal` exercises
menu launches, categories, zombie motion, death decals and recycling; `SmokeFinal`
checks current catalogue training, research and first-person continuity. Later
`MenuMelee` / `SmokeMelee` runs additionally cover physical building approach.
The core validation has 43 checks including 3D CPU/GPU climate agreement within
3.34e-6, the CBT suite 14 and command suite 22.

The measured restoration comparison is
`TestResults/Planetary/Repairs/Benchmarks/comparison.md`. Surface median/p95 rose
from 3.464/4.662 to 4.717/5.856 ms; orbit 3.175/4.043 to 3.407/4.209 ms;
first person 2.956/3.735 to 3.758/4.631 ms. All measured 29.996-30.000 simulation
Hz. Tracked allocation rose by 15-33 MiB depending on the view. These are
32-unit total-frame comparisons, not isolated simulation or million-unit passes.
The new encounter path has no equivalent old planetary baseline, so its <=5%
regression acceptance and fog budget remain unverified.

Final verification: `Repairs/MenuLatest/validation.json` passes 56 checks and
`Repairs/SmokeLatest/validation.json` passes 21. Together with core (43), CBT (14)
and commands (22), this is 156 passing checks. The final menu run includes
separate ambient/wave identities, wave-only clearance counters, physical
building damage and GPU death-slot recycling. `Repairs/README.md` indexes the
reports. The final build completed with zero errors.

## AI movement and zombie distribution follow-up

The planetary encounter now has continuous land-checked sphere spawning,
individual ambient wander goals, local gunfire hearing, alternate-step steering,
GPU cell-presence objectives and patrol recovery after target loss. Repeated
identical projected goals preserve converged fields; blocked goal cells project
to traversable cells. AI-test no longer silently pauses on command-centre loss;
normal RTS/Swarm defeats show an explicit outcome.

See `TestResults/Planetary/AI/README.md` for reproduced baseline failures, the
58-second recovery test and the equivalent-population standalone comparison.
The 4096-zombie spawn probe now has 4096 distinct positions and all 16 tested
sub-cell bins on both axes. Scoped simulation p95 improves from 0.1427 to
0.1136 ms with all 4160 fixture units preserved alive. The original strategic
base-building planner and full acoustic/formation parity remain outstanding.


### Larger worlds and bounded terrain residency (15 October 2026)

Normal menu RTS, Swarm and AI-test sessions now request a 2,000 m radius (previously
500 m): four times the radius, sixteen times the area. Navigation uses 128 rather
than 32 cells per cube-face edge, retaining the previous radius/resolution ratio
and local building inflation scale. Gravity is recalculated to retain 9.81 m/s²
at the mean surface. The camera far plane follows planet size.

`PlanetStreamedTerrain` adds a separate bounded visual residency path. A GPU heap
starts from six cube faces, refines the highest-priority visible patch, and uploads
no generated planet mesh from the CPU. The GPU emits patch descriptors and indirect
draw arguments. The existing deterministic surface function, texture library,
normal maps, height blending, climate, resource decals and combat decals are shared.
Invisible regions retain only coarse implicit coverage; revisiting reconstructs
identical surface addresses. No army records or gameplay state are evicted.

The resident budget is 384 patches, each evaluated procedurally with a 16×16 grid.
The GPU heap, patch records, statistics and draw arguments total 18,464 bytes,
independent of radius or potential tile count. Stationary cameras reuse the
resident descriptors. CPU reference traversal exists for editor validation only;
production LOD decisions and indirect counts run on the GPU. Radial skirts cover
unequal patch LOD edges. Budget exhaustion is visible in F2 statistics.

This sparse patch path is an incremental streaming foundation, not a claim that
the original watertight concurrent binary-tree algorithm has become fully paged.
The existing GPU CBT renderer remains available with `-planet-cbt`; future work
can place its triangle refinement inside bounded resident regions. This path
currently regenerates analytic geometry, not disk-backed height/collision tiles.
Only the observing camera needs visual detail; offscreen armies continue GPU
simulation against independent navigation data.

Standalone controls: `-planet-radius 120000 -planet-streamed`; use `-planet-cbt`
with the same radius for a renderer comparison. Historic smoke, AI, crowd and
interest fixtures retain their original radii unless explicitly overridden so
before/after measurements remain comparable.

The global grid stays capped at resolution 128. Each active shared order owns a
128 x 128 tangent patch generated at the requested destination. The existing GPU
navigation integrator solves that patch, using physical building footprints and
terrain samples. Eighteen patch slots use approximately 3.38 MiB, independent of
planet surface area. Pole and cube-seam movement does not require CPU unit state.
Ground movement outside a solved patch retains coarse routing and exact local
terrain/building checks. This is destination-centred tactical routing, not a
complete resident corridor network for arbitrary intercontinental travel.

Large worlds use three bounded GPU hash levels for target searches, with
nominal cell widths of 128, 512 and 2,048 m. Melee and strategic weapons select
the appropriate level without scanning a planet-sized fine grid. Starter zombies are distributed in a
local annulus so the base is populated on a much larger globe; the uniform-world
million-zombie fixture retains its original distribution. Distant zombies retain
persistent GPU records and staggered coarse simulation. Detailed movement uses
the compact GPU active list; dormant movement dispatches only the current 1/32
cohort rather than launching a movement thread over every unit slot.

Strategic construction/expansion AI, full original formations/logistics and fog
remain pending. The optional restored pressure solver still needs regional
pressure/contact pages; coarse strategic objectives and hearing need further
large-world refinement. No full AI parity is claimed by the size change.

### Zombie areas of interest

Large populations now use GPU interest management. Detailed steering, target
search and firing dispatch indirectly over a compact active list. The camera's
surface focus (600–1,200 m), surviving regular troops of either side, and
surviving buildings keep surrounding regions active. Buildings also stamp their
weapon reach into the source map. Orbit zoom does not wake the entire planet.

Distant zombie slots retain health, identity, destinations, wave membership and
strategic presence. One of 32 cohorts advances each simulation tick using elapsed
simulation ticks, including when a region wakes again. They keep travelling or
wandering; no zombies are deleted, converted to CPU objects, or silently removed
from the requested population. Only active slots enter detailed spatial/contact
lists and combat snapshots. Retained targets cannot read stale dormant snapshots.
Small populations keep the existing full-update path to avoid scheduler overhead.
The activation threshold is 8,192 live zombies.

Interest storage is bounded: three uints per army slot (activity, compact index,
last simulation tick), two source/activity grids each capped at 512×512×6 uints, and six indirect
argument/statistic words. Normal orchestration reads aggregate counts, never a
CPU unit mirror. Coarse movement is not a substitute for the still-pending
regional fine navigation at the 120 km radius target. Dense activity around many
separate player armies necessarily costs more than isolated distant hordes.

The standalone comparison uses `-planet-interest-validation` and
`-planet-interest-count 50000` or `1000000`, with `-planet-no-interest` as control.
All requested zombie records remain alive during timing in both runs; the fixture
holds health/cooldowns high and concentrates the 64 regular troops at the base,
so this measures interest scheduling without combat deaths changing population.
It is not a claim of a complete million-unit weapons battle at 60 FPS.

The shared region mask evaluates interest neighbours once per region, then each
zombie reads one cached result. Detailed neighbour inputs use read-only SRVs to
stay within D3D11's eight-UAV limit; startup checks compute-kernel support. The
long AI regression and a separate offscreen combat probe verify that detailed
steering and firing execute, rather than merely testing activity flags.

Final standalone comparisons preserved all 1,000,000 zombies plus 64 troops.
Frame median/p95 improved from 166.40/170.94 ms to 4.68/7.96 ms in the distributed
horde fixture. Current had 74,588 detailed and 925,412 coarse zombies, with no
recorded frame exceeding 16.67 ms during the 15-second recording window.
The 50,000-zombie comparison also passed the 5% simulation regression gate.
Full scope, memory, cadence, catchup timing and discarded exploratory runs are
documented in the linked report; these are not full-battle or fog certifications.


## Restored crowd steering (enabled with accepted performance tradeoff)

The earlier planetary adapter only ported pursuit and contact repulsion. The
original flat production path still contains the pressure and predictive traffic
solver; those features were not removed to implement area-of-interest scheduling.
`PlanetCrowd.hlsl` now ports its fixed-point predicted density, smoothed CIC mass,
unilateral nonnegative pressure and filtered gradient onto cube-sphere cells.
`PlanetAvoidance.hlsl` ports predictive passing, persistent passing sides, queue
braking, arrival yielding, separation and finite acceleration onto world tangents.
A finer GPU contact grid bounds the neighbour work. Density shares the spatial
passes; eight pressure iterations use compacted tiles and a reusable command
buffer. Distant zombies retain the existing staggered update path.

Restored pressure and predictive local avoidance now run by default. On 15 October
2026 the user explicitly accepted the previously measured extra simulation cost
(about 0.37 ms per million-unit tick), overriding the 5% regression gate for this
solver. `-planet-legacy-steering` remains available for equivalent controls;
`-planet-restored-crowd` explicitly selects the default. The earlier gate and
measurements remain preserved in `TestResults/Planetary/Crowd/README.md`.

Two independent bases, faction wallets and bounded construction/research/training
planning now run in AI Test. Normal RTS runs the enemy planner. AI Test starts
with an overview of both bases; 1 and 2 focus the respective faction. Original
catalogue definitions, queues, wallet rules and GPU unit spawns are reused.
Advanced flat-world compound/clearance planning is still a separate migration.

Ambient zombies retain GPU spawn anchors and choose wander destinations within
75 m. Both initial bases have 1.5 km spawn refuges and ambient movement protection
for the first three simulated minutes. Scheduled Swarm attackers keep their
separate identity and preparation schedule. Distant records stay GPU authoritative.

N restores the original cursor-test nuke (respecting UI hit blocking). Completed
nuclear silos provide a targeting action with fuel/isotope cost, reload and a
12-second simulation delay. Radial army damage executes on the GPU and the
original crater textures and NuclearBurst VFX graph are reused. Nukes now also deform shared terrain geometry; see the terrain update below. Permanent crater atlas baking, save-game persistence and missile interception still need migration.

The capital-ship blockout now has a walkable bridge, corridor, command room,
engine/vehicle areas and two-hatch airlock. A terrain-fitted ramp provides exterior
access. The observer capsule collides with ship geometry and bounded building
shells; occupants walk in ShipFrame through translation, rotation and rebasing.
F enters walking from RTS, operates an aimed nearby hatch/seat, and leaves the
pilot seat. Tab returns to RTS; an aboard pose is retained. H remains a quick
pilot shortcut. Hatches prevent occupied closure and reopen on intrusion.
Cabin lighting enables at most two nearby lights with local exposure adaptation.
Docking, gunner stations, full ship impact/damage and finished art remain pending.
[Ship interior validation and performance](../TestResults/Planetary/ShipInterior/README.md).

This is not full AI/movement parity: formation parking/entry, route-progress
recovery, separate hard contact projection, advanced compound/clearance planning,
logistics, scouting and transport still need migration. The menu now uses a 120 km radius with regional tactical navigation;
the restored crowd solver still needs regional pressure/contact pages. No full-weapons million-unit battle or fog pass is claimed.

Current gameplay restoration validation: [GameplayRestore](../TestResults/Planetary/GameplayRestore/README.md).

### Pilot trajectory display

The pilot seat now shows a planet-frame orbit-plane map, current position, periapsis/apoapsis altitude, eccentricity, orbital period, radial/tangential speed and circular/escape speed. A forward-view prograde direction cue helps align thrust. Predictions update at 5 Hz and assume coasting without thrust or braking. The HUD distinguishes surface intersection, escape and terrain-clear bound orbits; it does not add atmosphere drag, other-body gravity or autopilot. See `TestResults/Planetary/OrbitHud/README.md` for scope and validation.

### Camera at zero and client origins

RTS, ground FPS and pilot cameras now anchor at Unity world zero on each presentation, while sector/double universe and frame-local simulation positions remain unchanged. Ground observer movement/contact stays in double precision; nearby ship/building colliders are reconstructed in the local render context. Each future multiplayer client can use its own origin. The two-observer checks validate the coordinate contract; networking itself is not implemented. See `Documentation/PLANETARY_PRECISION_AND_MULTIPLAYER.md` for replication rules and remaining GPU/native precision limits.

### Terrain, gameplay scale and flight HUD update

The gameplay default is now 240 km in diameter (120 km radius), with 240 m terrain amplitude and level starter regions. TargetRadius=120000 remains available for explicit stress/precision fixtures; their army scale is not reduced. Nukes now edit the shared canonical height function with persistent bowl/rim craters, rebinding GPU height consumers and invalidating navigation/streaming. Goal rebuilds after terrain edits use bounded asynchronous GPU readbacks; newer orders supersede stale completions. The two original nuclear VFX emitters are prepared during loading. The bounded edit set holds 32 persistent craters and merges repeated impacts at the same location; once full, new distinct impacts still damage/scorch but report the deformation budget limit. Unedited movement, rendering and GPU culling retain the original height shader variant; a single crater uses two small vector constants, while multiple edits share a separate 7.2 KB constant buffer uploaded only on terrain changes; edited queries first reject a conservative region bound, then use the spatial crater mask. The gameplay world uses the existing multiscale combat search to preserve strategic weapon reach. Edits currently last for the session; serialization/network replication remains future work.

The pilot retains the 2D orbit map and additionally renders a depth-tested translucent green future conic in the 3D cockpit view. It updates at 5 Hz, stops at a predicted surface/ocean impact and assumes no thrust/braking. The line carries 3D waypoint rings that shrink with distance, showing the path ahead. One 160-point buffer and one procedural draw are used for the single piloted ship. The forecast is a coasting path, not a collision-free corridor. L toggles planet-relative orbital attitude hold: it captures the current arbitrary roll/pitch/yaw offset in the orbital frame, transports it as the ship orbits, ignores rotation input while held and leaves translational thrust/braking available. It does not stabilize the orbit itself.

See `TestResults/Planetary/TerrainFlight` for verification, render captures and equivalent million-unit performance comparisons.


### Accepted terrain-deformation cost (16 October 2026)

Geometric nuke craters are enabled by default following explicit user acceptance of the measured million-zombie tradeoff. The equivalent 120 km-radius standalone comparison preserved 1,000,000 zombies plus 64 troops: simulation median/p95 changed from 1.1490/1.3171 ms to 1.1653/1.6457 ms (+1.42%/+24.95%). This exceeds the normal 5% gate; it is an accepted exception, not a passing 5% result. The crater run's frame median/p95 were 6.3612/8.0218 ms, at 29.30 simulation Hz, with 491.36 MiB tracked allocation and 795.34 MiB reservation. These are distributed-horde measurements, not full-weapons or fog certification. Use -planet-no-deform-terrain to retain blast damage/VFX/scorching without adding geometric craters; -planet-deform-terrain explicitly enables them. See the TerrainFlight report for all rejected iterations and the full comparison.

GPU model culling now rejects conservative offscreen and fully planet-occluded bounds before sampling terrain height. A 25-case reference comparison preserves visible unit IDs and LODs across both supported test radii, zero/one/32 crater states, naval surfaces, large/high-altitude units and surface/space views.


### Persistent nuclear scorch (16 October 2026)

Nuclear marks no longer use the 512-entry casualty ring or its single-winner cell index. Two fixed, six-face 1024-square RGBA8 GPU atlases accumulate nuclear burn colour and body-space normal relief. Burn transmission multiplies and opacity accumulates, so a later transparent edge cannot clear a previous mark. Normal perturbations accumulate with a fixed bound. All crossed cube faces are painted, with hemisphere rejection to prevent through-planet projection. A conservative tile rectangle restricts each GPU update to the impact region; painted-cell flags skip texture samples elsewhere. Initialization happens during session loading. Neither impact history nor per-unit state is mirrored on the CPU.

Atlas storage is 48 MiB plus a 384 KiB GPU cell lookup and bounded CPU lookup metadata; it does not grow with impact count. Painting has no 512-impact eviction limit, and works with geometric deformation disabled. The geometric 32-crater budget is separate and unchanged. Paint currently lasts for the session. Atlas resolution is fixed: very large planets lose scorch detail as metres per texel increase; Earth-scale surface detail would require streamed local paint pages. No planet-size change accompanies this decal repair. Earth's actual mean diameter is approximately 12,742 km; that repair retained the then-current 12 km gameplay diameter.

See `TestResults/Planetary/Scorch` for overlap, seam, retention, actual casualty-ring recycling, rendered terrain and standalone measurements.

Scorch repair verification passed 149 checks, including a real overlapping second blast with 600 zombie deaths and 525 paint operations in the atlas fixture. The equivalent D3D11 1440p million-zombie / 120 km-radius comparison (one geometric crater, paint inactive) measured frame median/p95 6.675/8.028 ms before and 6.483/7.859 ms after; simulation median/p95 1.2036/1.4014 ms before and 1.1923/1.3314 ms after, at 29.24/29.35 Hz. Unity allocated/reserved memory was 491.86/795.34 MiB before and 491.12/795.34 MiB after; these profiler figures exclude total VRAM, including the additional atlas storage described above. The new steady-state army regression gate passed. This does not measure a painted-view GPU cost, blast-time GPU cost, full battle, or fog cost. Prior accepted geometric-crater exception remains unchanged.


### Restored large gameplay planet (16 October 2026)

The gameplay default is restored to 120,000 m radius (240 km diameter), matching the earlier explicit radius preference. Both menu-created RTS/Swarm/AI Test sessions and direct PlanetDevelopment scene launches use this default. The 6 km threshold for streaming, regional navigation and sparse combat remains independent of the gameplay radius, so smaller regression fixtures retain their existing behavior. Terrain relief, starter plateaus, geometric nuke deformation and persistent scorch paint remain enabled. The fixed scorch atlas has lower local resolution on this larger planet; it does not allocate a larger global texture.

Restore verification: standalone build succeeded; 150 existing menu, gameplay and ship checks passed. All three menu modes explicitly verify a 120,000 m radius. Reports: `TestResults/Planetary/Restore120`.


### Pilot rockets, larger N-key crater and explosion attraction

While seated at the pilot controls, hold left mouse to fire conventional rockets through the cockpit sight. The two exterior launch tubes alternate, aiming at the sight's terrain intersection when visible; otherwise shots leave along the ship's forward axis. Rockets are ballistic, inherit the muzzle's ship velocity, accelerate for two seconds, then coast under planetary gravity. They are not homing missiles. The original rocket mesh and CombatBurst VFX are reused, with a small procedural orange exhaust streak for readability against space. The pilot HUD shows reload state and the count airborne. Fire interval is 0.6 seconds, capacity is 16 live shots, and a full pool rejects new shots rather than replacing existing ones. No per-army-unit GameObjects or CPU unit mirror is introduced.

Projectile positions and velocities are maintained in double precision in the canonical body frame, then rebased for rendering. Swept terrain/ocean tests and non-allocating physics rays detect terrain and building hits, excluding the firing ship. Surface impacts apply a 24 m-radius / 450-damage GPU blast and a smaller conventional VFX burst. They do not consume persistent nuclear crater slots. Every rocket has a 45-second simulation fuse, including outward shots, and continues after leaving the pilot seat. A distant space timeout produces a local visual explosion without applying a false surface blast on the planet underneath. Pause freezes projectiles. Eight reusable explosion emitters bound cosmetic cost; a simultaneous ninth burst may replace an earlier visual, while damage still applies.

The original N-key terrain nuke now makes a 360 m-radius crater (720 m across), compared with 120 m previously; centre deformation is approximately 57.6 m. Persistent scorch and presentation follow the larger crater scale. Its 120 m damage radius remains unchanged. Silo nukes retain their catalogue sizes.

Explosions now set nearby ambient zombies' exact canonical destination in the existing event-only GPU blast pass: within 600 m for rockets and at least 2 km for nuclear blasts. This includes dormant zombies, which keep their staggered coarse updates outside areas of interest rather than being promoted to full simulation. Existing combat target priorities remain, and both protected starting-base regions are still excluded during startup. Normal wandering returns to the original bounded home range after arrival. There is no new per-tick hearing loop, CPU zombie list, or global wake-up. The earlier ordinary-hearing path used coarse cell centres and a 150 m cutoff, and strategic blasts did not submit an explosion destination; those limitations explained the lost convergence at large radii.

Verification and equivalent million-unit measurements are recorded in `TestResults/Planetary/ShipWeapons`.

Ship-weapons verification passed 204 functional checks plus two 14-check million-zombie interest/performance runs. The equivalent 120 km-radius / one-120 m-crater comparison retained 1,000,000 zombies plus 64 troops: frame median/p95 6.205/8.095 ms before, 6.821/8.187 ms after; simulation median/p95 1.1748/1.3593 ms before, 1.2114/1.3992 ms after (+3.12%/+2.94%), at 29.30/29.15 Hz. Unity allocated/reserved memory was 491.80/795.34 MiB before and 491.40/795.34 MiB after. The new 360 m-crater run measured frame 6.641/7.682 ms, simulation 1.1975/1.3494 ms and 29.30 Hz, with 491.46/795.34 MiB tracked memory. Both new army gates passed against this baseline. These timings exclude live rocket/VFX battles; the accepted older geometric-crater exception remains recorded separately. The standalone fixture additionally confirmed visible exhaust and impact particles, 18 fired/18 exploded projectiles, no survivors after fuses, 57.6 m N-crater centre deformation, exact epicentre pursuit and preserved startup refuges.


### Assisted flight and Space landing/takeoff

Normal ship flight now defaults to assistance: gravity compensation, velocity damping, near-surface terrain-clearance hold (minimum 60 m pivot clearance), roll levelling and terrain look-ahead. W/S selects forward/reverse motion, A/D strafes, R/C commands climb/descent, B brakes and Shift boosts. Mouse pitch still directs deliberate forward climbs/descents. V toggles assistance. L enables the existing planet-relative orbital attitude lock with assistance off; disabling L leaves orbital/coasting flight available until V enables assistance again. The old orbital prediction remains a coasting reference; the green coasting arc is shown with assistance off, while landing uses its own green/red approach preview.

Hold K while piloting in space for forward transit at 100,000 km/s; no thrust key is required. The cockpit switches to km/s at high speeds and derives its warp label from the compiled speed setting. Warp uses the full elapsed flight timestep so it does not slow down with the normal 50 ms physics cap. Release K to immediately restore the saved normal-flight velocity and continue the selected assisted or orbital flight mode. Braking, pausing, losing input focus or leaving the seat cancels transit. J now docks/undocks, freeing K for the held boost. Transit is unavailable while landed, docking or performing assisted landing/takeoff, and below the greater of the atmosphere height or terrain relief plus 6 km. Swept sphere entry stops transit before crossing that boundary, and existing station hull sweeps remain active. Planet handoff transfers the saved normal velocity so releasing K after a handoff cannot retain transit momentum. This is a bounded speed override for the single player ship; no army simulation or unit representation changes.

Space is edge-triggered while piloting: on the ground it attempts an 80 m lift to a hover, checking overhead clearance; in flight it smoothly follows the curved approach to the exact displayed green landing pose, levels the hull and stops linear/angular velocity at touchdown. Takeoff eases upward 80 m over 3.5 seconds; landing takes 3.5–12 seconds according to distance. Red previews cannot land. Sites must be dry, within 600 m ground clearance and below 180 m/s, with slope at most 8 degrees and no more than 2.5 m departure from the fitted ground plane over the 54 by 100 m footprint. The landing corridor uses conservative swept hull boxes and sampled canonical terrain. Obstacles appearing after a green preview are rechecked at activation. Terrain edits invalidate a cached landing approval. The HUD shows the approach side view, clearance, speed, status and reasons for rejection, alongside a full-size world footprint/hull outline and curved approach. Position easing and smooth rotation remove the instant jump. During the transition, repeated Space presses and flight-mode/manual steering inputs cannot override the assist; pausing freezes it and leaving the seat lets it continue. Hull sweeps and terrain footprint checks run every transition step; a newly obstructed path stops at the last safe pose with a blocked status.

Landed poses are body-fixed and ignore thrust, steering, braking and orbital-lock inputs; the gear stays deployed through the normal controls. Takeoff through Space releases the lock. Hover assistance continues while the pilot leaves the seat; physical orbital flight remains available with assistance off. Double-precision body-local positions and camera-at-zero rendering are retained. Landing scans are bounded to one ship and run at 5 Hz while piloted, with additional validation on landing activation; the GPU-authoritative army architecture is unchanged.

Design references: Hello Games documents low-flight terrain assistance and remaining in the cockpit after landing in [Atlas Rises](https://www.nomanssky.com/atlas-rises-update/). Microsoft Flight Simulator exposes [auto-hover controls](https://docs.flightsimulator.com/html/Programming_Tools/Event_IDs/Helicopter_Specific_Events.htm). These informed the assistance pattern; the animated Space approach and ground lock implement this project's requested behavior.

Validation: `TestResults/Planetary/LandingAssist`.

Assisted-flight verification passed 238 functional checks and both 14-check million-zombie runs. Equivalent D3D11 1440p / 120 km-radius / one-120 m-crater measurements retained 1,000,000 zombies plus 64 troops: frame median/p95 6.240/7.455 ms before and 6.415/7.636 ms after; simulation 1.1823/1.3714 ms before and 1.1714/1.3301 ms after (-0.92%/-3.01%), at 29.24/29.30 Hz. Unity allocated/reserved memory was 491.33/795.34 MiB before and 492.08/795.34 MiB after, excluding total VRAM. The army regression gate passes; this is not a full battle, active-flight/VFX or fog certification. Landing scans separately measured 0.1228 ms CPU p95 at 5 Hz and assisted integration 0.0020 ms p95. Detailed results and retained initial failures: `TestResults/Planetary/LandingAssist/RESULTS.md`.


### Headquarters bearings and connected mainland terrain

Every living team command centre now has a persistent coloured HQ marker with distance. Onscreen markers use a diamond; offscreen markers use an edge arrow. A sphere-segment horizon test identifies destinations behind the planet and labels them BEYOND HORIZON instead of placing a misleading pin through the surface. Markers use the original retained game UI, remain visible in RTS, walking and cockpit modes, ignore pointer input, and disappear with their HQ. Only two infrastructure records are inspected; there is no army readback or per-unit marker system. Canonical double-precision positions are converted through the existing camera-at-zero render origin.

Large gameplay planets (radius at least 20 km) now use a shared directional continental field, smooth warped mountain ridges and low-amplitude rolling plains. Mountain and hill wavelengths are specified in metres, so increasing the planet radius no longer stretches every local feature until it appears flat. The normal 120 km-radius planet uses an 1,800 m conservative relief amplitude; actual field heights stay within that bound. The gameplay atmosphere extends at least 10 km above the mean sphere so natural mountains and elevated bases remain inside breathable air. CPU surface queries and all GPU consumers use equivalent expressions. Climate, biome texture layers, height blending, normal maps, persistent scorch and geometric craters continue on that same geometry. Compact legacy fixtures retain their original terrain field; explicit zero-relief stress scenarios remain flat.

Base initialization now searches deterministically for natural dry plains before placing buildings or editing pads. Both 4.5 km-separated homes and their expansion areas must fit in one dry region with a 4 km-wide connecting corridor, with a 60 m water margin and bounded relief. The search first tries nearby areas, then a bounded global sample. Failure refuses isolated ocean-island starts. The existing small flat starter pads remain, blended into already dry ground. Seed and radius are respected; this is startup infrastructure work, not a CPU army simulation. Regional GPU navigation, local avoidance and coarse zombie simulation remain authoritative.

Streamed LOD includes the shorter-wavelength relief curvature. Patch skirts now follow the estimated local interpolation error instead of extending twice the entire planet relief amplitude below every fine patch; their previous kilometre-deep overlap created unnecessary overdraw when increasing terrain amplitude. Resident patch and army capacities are unchanged.

Validation reports: `TestResults/Planetary/HqTerrain`.

Final HQ/terrain verification passed 267 functional checks, including a final restored-player run, eight terrain seeds, 477 independent natural-land route samples, and CPU/GPU height agreement within 0.04053 m. Both factions' ground troops left their starter pads by 1.90/2.74 km. Idle HQ markers retain geometry (zero stationary redraws in the fixture), using unjittered projection.

The isolated same-session flat army gate alternated two previous-formula controls and two current builds at D3D11 1440p, 120 km radius, 1,000,000 zombies plus 64 troops and one 120 m crater. Pooled frame median/p95 was 6.257/7.782 ms before and 6.351/7.607 ms after; simulation 1.1607/1.4577 ms before and 1.1667/1.4842 ms after (+0.52%/+1.82%), with mean rates 28.95/28.95 Hz. Unity allocated/reserved peaks were 496.27/795.34 MiB before and 497.52/795.34 MiB after. Both 5% gates pass.

The nonzero-relief scenario measured frame median/p95 18.167/19.524 ms before and 7.686/9.117 ms after; simulation 1.3049/1.6401 ms before and 1.1675/1.2860 ms after, at 28.34/29.29 Hz, with 492.03/795.34 and 493.11/795.34 MiB tracked memory. This excludes total VRAM and is not a full battle/VFX/fog certification. Full raw samples, initial failed diagnostics, methodology and source restoration checks are in `TestResults/Planetary/HqTerrain/RESULTS.md`.

### Animated Space transitions verification (2026-10-16)

Standalone build succeeded with zero errors. Landing (38), occupied ship (36), gameplay (51), weapons (46) and terrain/orbital flight (14) all pass: 185 checks total. Exact landing error and twenty-second hover drift are both zero. Results: `TestResults/Planetary/LandingTransition/RESULTS.md`. GPU army code and terrain shaders were unchanged for this interaction update.

### Deterministic spherical Unity stamps (2026-10-16)

Imported twelve original Unity Terrain Sample Asset Pack heightmaps and added editable definitions in `Assets/MassiveArmy/Resources/MassiveArmy/PlanetStamps/Library.asset`. Stamp selection, reflection and quarter-turns use shared integer cube corners and the planet seed; tangent projection and smooth blending avoid face seams. The broad land/mountain masks remain, while stamps replace the former analytic ridge/hill relief. Existing 3D biomes, texture/normal/height blending, sparse GPU mesh residency, starter pads, geometric craters and GPU army simulation remain in place.

Stamps are **opt-in by user request**: `-planet-stamp-terrain` enables them for all subsequently entered planet modes, including from the original start menu. A supplied PlanetDefinition can instead enable `stampTerrain`. Newly created definitions stay off; `-planet-legacy-terrain` forces the original relief. The library Inspector provides source texture pickers and a bake button. Stop Play mode before editing/rebaking.

The first direct sampler exceeded the 5% army policy (+6.64% median simulation time); the user declined that default tradeoff and asked for optimization. The optimized implementation precomputes 3,750 immutable 48-byte corner descriptors per large planet, then uses two dot products per corner and four shared R16 samples. The descriptor GPU buffer is 180,000 bytes; there is no whole-planet height atlas, per-unit CPU state or new unit GameObject. Disabled planets bind minimal fallback resources. Native source TextAsset data is released after the CPU sample copy, and descriptor buffers are owned/disposed by each PlanetDefinition.

Matched standalone Direct3D 11 tests at 1440p on RTX 3070, 120 km radius, 1,800 m relief, identical fixed base pads, one crater and 1,000,064 total units: original shader vs optimized stamps frame median/p95 8.825/10.529 → 8.646/10.503 ms; simulation 1.1978/1.8323 → 1.1659/1.5351 ms; simulation rate 27.50 → 29.19 Hz; Unity tracked allocation/reservation 548.11/797.66 → 548.13/797.66 MiB. Two complete runs per version were pooled without outlier removal. Both enabled and disabled paths pass the 5% simulation gate. These measurements cover this fixture and are not total VRAM or a full VFX/fog-battle certification.

Final build succeeded with zero errors and **285 gameplay checks pass**. Cached/uncached terrain difference is zero; maximum CPU/GPU height discrepancy is 0.09723 m. Eight seeds preserve connected dry starting land, and both original AI factions leave their pads. Full reports, raw timings, rejected-prototype comparison and previews: `TestResults/Planetary/Stamps/RESULTS.md`.

### Saved planet preset (2026-10-16)

At the user's request, `Assets/MassiveArmy/Resources/MassiveArmy/Planets/DefaultPlanet.asset` is now the saved preset for the development scene and all original start-menu planet modes. It explicitly opts this project into stamped terrain, with radius 120,000 m, relief 1,800 m, ocean level −35 m and a 10 km breathable atmosphere. New PlanetDefinition instances still default to stamps off; the saved asset has Stamp Terrain checked. The existing command-line overrides remain available.

Each session clones the preset before applying match seeds, radius overrides or terrain edits. Menu seed selection is preserved and the saved preset stays unchanged. Scene generation/build tooling retains the asset assignment. Use **Massive Army → Planetary → Select Default Planet** to locate the asset in Unity.

Standalone verification without stamp command-line overrides: menu integration **73 checks PASS**, terrain/HQ **34 checks PASS** (107 total), and build succeeded with zero errors. Menu checks cover RTS, Swarm and AI Test using private preset copies and leaving the source asset unchanged. Reports: `TestResults/Planetary/SavedDefinition/`.


## Generated planet systems (16 October 2026)

`Assets/MassiveArmy/Resources/MassiveArmy/Planets/DefaultSystem.asset` controls the generated system. It is assigned to the development scene and loaded by menu sessions. The existing DefaultPlanet remains the home planet (120 km radius); additional bodies inherit its terrain/material settings and receive deterministic seeds, 80–160 km radii, relief and ocean levels. Match seed and system seed jointly determine the layout. Planet centres use sector/double universe positions. This is a static planetary layout; celestial orbital motion is not implemented.

The default system contains four planets, with an Inspector limit of eight. Unvisited planets have definitions and cheap distant rendering only: no army, navigation, detailed terrain, crater or stamp-field allocations. Cockpit labels show planet bearings, distances and residency state. Fly toward a planet to activate it automatically; no scene change or manual destination selection is required.

The viewed world's army and base stop ticking above 80 km altitude, with 20 km hysteresis before sleeping. No elapsed-time catch-up is applied on waking. This distance gate complements the existing GPU zombie area-of-interest solver within a planet. These settings are in metres and can be adjusted on DefaultSystem.

New local worlds prepare from 200 km before the activation boundary. Mainland search, navigation relaxation, deployment flood/eligibility maps, resource-decal lookup and starter troops are spread over frames. Only a prepared destination can take control. Ship position and inertial velocity transfer through canonical universe coordinates; the camera remains at zero. Inventory, suit health/oxygen, gear, flight-assist setting and hatch state follow the player. Orbital attitude lock is reset on changing the reference planet. Each newly visited planet starts the selected RTS/Swarm/AI Test mode with its original UI and content.

Visited worlds freeze in place: GPU army buffers, buildings, economies and terrain edits are retained. Their cameras, colliders, UI and terrain presentation are disabled. They allocate no CPU unit mirror and do not dispatch simulation while dormant. This trades bounded retained memory for exact state preservation: up to the configured planet count can become resident, and each retains the selected army capacity. There is no disk eviction or save-across-launch implementation. A future multiplayer session must derive interest from all connected observers rather than this single local pilot.

Standalone generation/travel, menu regressions, timing samples and measured memory are recorded in `TestResults/Planetary/System/`. `-planet-system-baseline` is a validation-only control restoring the old single simulated planet and its distant placeholder. It does not reduce the army population or change the GPU steering solver.


## Terrain continuity from space (16 October 2026)

The repeating mountain bands came from the old `sin(dot(direction, (19,13,17)) + seed)` mountain mask. Stamped worlds now use an irregular 3D value-noise mountain distribution, baked once into the unused fourth appearance component of the existing stamp-corner descriptors. Stamp centres receive deterministic tangent offsets and full-angle rotations. CPU queries and all GPU terrain consumers use the same four descriptor samples and interpolated mountain weight; no per-unit 3D noise evaluation was added. Compact and unstamped legacy terrain keep their existing field.

Unvisited planets now have a visual-only coarse surface cache, rather than a plain coloured sphere. `PlanetDistantTerrain` bakes the canonical terrain height and body-space normal into six 128×128 faces with one gutter texel, using `PlanetDistantBake.compute`. The 130×130×6 RGBA32F texture costs approximately 1.55 MiB of GPU storage per cached planet, plus compact stamp metadata. Gameplay definitions for unvisited planets still have no terrain edits/stamp field allocated; an isolated visual definition owns the small descriptors. There are no army, navigation or local terrain-patch allocations until approach preparation.

`ScaledSpaceRenderer` renders displaced 16/32/64-grid meshes through the same HDRP custom-pass injection point as local terrain. It reuses the terrain texture arrays, normal/height layer blending, climate, material controls, HDRP lighting and exposure. A common monotonic physical-distance depth mapping keeps scaled planets ordered, while canonical foreground terrain still occludes them. Planet colours, coastlines, relief and biome layout therefore exist from afar; local activation adds streamed geometric detail instead of replacing a coloured placeholder. The system sun direction stays fixed through handoff.

Preview caches are shared across local contexts, invalidated by terrain parameters and edit revision, and disposed with the system. Each preview owns its compute-shader binding state so interleaved edits on different planets cannot overwrite another planet's bake parameters. The detailed surface and the preview retain the same seed and height function across activation. The preview deliberately filters/interpolates a lower-resolution field; small geometric differences are expected at the detail transition.

Validation, approach/handoff captures and equivalent million-unit measurements: `TestResults/Planetary/ContinuousTerrain/RESULTS.md`.


## Generated galaxy and multi-body flight (17 October 2026)

`Resources/MassiveArmy/Planets/DefaultSystem.asset` now references `DefaultGalaxy.asset`. The latter configures the seed, 48 star systems, four planets per system, four spiral arms, binary probability, central black-hole gravitational parameter, orbital spacing, visual budgets and prediction budget. The home system is always binary. Home retains the authored 120 km radius, stamps, biome/material layers, existing bases/UI and GPU army architecture. Setting `enabledInGame` off restores the earlier static planet system. `-planet-legacy-system` selects that original fixture for regression testing; normal menu RTS/Swarm/AI Test entry uses the galaxy.

The generator creates finite, data-only definitions for 192 planets and a small celestial-node catalogue. There are no per-star GameObjects or distant armies. Only approached planets build local gameplay contexts, using the existing incremental preparation and seamless canonical-pose handoff. Only the active area simulates armies. Visited worlds retain their existing frozen GPU state: this is not disk-backed unlimited residency, and visiting many heavily populated worlds still consumes additional GPU memory. Galaxy size and army capacity must be budgeted together. Rendering retains at most eight coarse planet textures, evicting least-recently-used previews; sub-pixel planets do not trigger terrain baking.

`GalaxyModel` evaluates a prescribed Kepler hierarchy in double precision: a central black hole, orbiting stellar barycentres, mass-balanced circular binary components, and mildly eccentric circumstellar/circumbinary planetary orbits. Generated binary planets lie well outside the pair. This is a compressed gameplay-scale galaxy, not an astronomical-scale or relativistic model. The massive bodies follow prescribed ephemerides; their mutual N-body perturbations, stellar evolution, collisions and relativistic black-hole effects are not simulated. No claim of permanent orbital stability is made.

Unassisted ships use velocity Verlet and summed Newtonian gravity. The local reference planet's analytical acceleration is subtracted, so a translating planet frame is not mistaken for an inertial frame. Nearby systems include each star and planet separately; distant systems use a mass-preserving monopole, smoothly blended over six to eight system extents. The central black hole always contributes. This same model drives the numerical predictor; physical acceleration is not switched at a primary-body boundary. Flight assistance deliberately compensates gravity as before. Rockets receive the same multi-body/frame-relative acceleration. Canonical handoff adds/subtracts moving-body velocities, while the camera remains at render origin. Ship reference-frame resolution now includes the planet's inertial velocity.

The pilot HUD selects local dynamical influence by mu/r^3 (tidal timescale), avoiding selection of a distant black hole merely because the whole system shares its acceleration. The map includes other bodies in range and labels the strongest two influences. Binary forecasts use their common barycentre for display. The 3D green arc and route gates use the same numerical forecast in that moving display frame. Forecast work is spread across frames with a 0.75 ms target per slice, then refreshed after half a second; a safe integration step is atomic and can exceed that target. Complete snapshots are published, rather than mixing partial paths. Impact warnings and a finite forecast duration replace unsupported two-body claims of permanent stability.

`GalaxySky.shader` renders the generated stellar catalogue in one procedural sky draw through the existing HDRP terrain pass. Five stellar classes vary colour and physical disc size. Point stars gradually resolve into discs using angular size, so there is no separate random sky that disagrees with nearby stars. Nearby ephemerides update every frame; distant catalogue uploads require at least ten seconds and 100 km of travel, with a sixty-second refresh for a stationary observer. Camera rotation and parallax use shader uniforms without rebuilding the catalogue. Star cores use the common scaled-distance depth convention; foreground planets occlude the sky. The home system is oriented to match the authored starting sun, keeping the selected mainland in daylight without changing its terrain. The dominant star drives the original scene light. An optional second, unshadowed HDRP directional light is off by default to preserve the authored exposure and reduce cosmetic cost. Projected trajectory/landing instruments draw after tone mapping, retaining their green/red contrast on bright terrain. Spatial quaternion rotations normalize in double precision so float quaternion rounding cannot stretch large orbital offsets.

The pilot post-processing volume is completely disabled outside the pilot seat, avoiding an otherwise unnecessary HDRP colour-buffer resolve in RTS mode. Nearby star uploads are batched into one buffer update. Native sunlight direction updates require a 0.03-degree change, and unchanged colours are not assigned again; orbital poses and gravity remain updated every simulation frame.

Standalone checks and matched million-unit timings are recorded in `TestResults/Planetary/Galaxy/RESULTS.md`.

The galaxy background now includes 30,000 decorative star points and a cached nebula layer, independently configurable in DefaultGalaxy.asset. Decorative stars do not add visitable destinations or gravity sources. The nebula integrates a seeded 3D cloud/dust field into two 512×256 RGBAHalf textures (2 MiB total GPU texels), over eight small dispatches. Looking around samples the cache; substantial travel schedules a refresh with a crossfade, no more often than every 30 seconds. This distant overlay is not local volumetric fog. Star density supports 0–100,000 points; the physical travel catalogue remains 48 systems / 192 planets by default. See TestResults/Planetary/Sky/RESULTS.md for render checks and matched standalone timings.

Building and unit mesh packs can be assigned through **Massive Army > Art > Model packs**. See [MODEL_PACKS.md](MODEL_PACKS.md) for per-LOD sources, material baking, GPU budgets and switching back to defaults.
