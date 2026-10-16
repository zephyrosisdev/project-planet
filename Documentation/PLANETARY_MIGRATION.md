# Planetary migration

The existing RTS remains in `Assets/Scenes/MassiveBattle.unity`. The opt-in
`Assets/Scenes/PlanetDevelopment.unity` is a migration fixture. Open it directly,
then Play. Generate it using **Massive Army > Planetary > Create Development
Scene** if necessary. WASD/middle drag move the surface focus, Q/E rotate, wheel
zooms from units to the globe. Click/drag selects units; right-click orders them.
F toggles an initial physical first-person avatar; mouse looks, WASD walks and
Space jumps. Health, oxygen and starter inventory appear in first-person mode.
The army keeps simulating during mode switches.
The normal startup/build scene list is preserved.
The scene now defaults to the [current RTS models, buildings and shared HUD](PLANETARY_RTS.md),
with construction, production queues and research. The tank-only fixture remains
available for reproducible performance comparisons.

## Repository audit and reuse boundary

| System | Existing assumption / implementation | Migration and reuse |
| --- | --- | --- |
| Coordinates | `WorldTopology.cs` / `.hlsl`: wrapped XZ torus, Vector2 metres, nearest repeated image | Planet positions use int64 sectors plus double offsets. Cube UVs are surface addresses, never metres. Keep flat topology for legacy matches. |
| Navigation | `GpuNavigation.cs`, `Navigation.compute`: 18 command groups, shared full-grid scalar integration, ping-pong Jacobi relaxation, 8 neighbours, five movement cost layers (per faction in base mode), convergence flags/readback, 32² downsampled lookahead | Reuse the compute solver through `PLANET_NAV`. Six independent NxN faces have explicit transformed seams, four neighbours and great-circle edge distances. No per-unit A*. |
| Hierarchy | `BuildCoarse` samples the already-computed fine integration field; it is not an independent hierarchical search | Planet adapter retains six coarse fields per group. Actual regional refinement and coarse-first search remain necessary before large-radius/dense navigation. |
| Unit state | `ArmyData.cs` / `ArmyCommon.hlsl`: 64-byte records with float2 position, previous, velocity, destination, yaw/turret angles and stable slot ID | The default scene reuses the current RTS catalogue/model banks; the comparison fixture retains tanks. Both reuse the record. Float2 becomes face+UV atlas address. A planet/frame belongs to the army buffer, not an individual Transform. Full gameplay kernels must use explicit surface conversions. |
| Movement / crowds | `Battle.compute`, `GpuCrowd.cs`, `Crowd.compute`, `ArmyTerrain.hlsl`: planar steering, toroidal spatial/contact bins, planar segments, hull clearance and density pressure | Development movement takes a GPU flow-field descent direction and a tangent/geodesic step. Production avoidance, contacts, formation slots and recovery still require seam-aware bins and transport of headings. Do not switch the production solver early. |
| Terrain / biomes | `WorldGeneration.cs`, `WorldLandforms.cs`, `Battlefield.cs`: periodic 2D landforms, erosion/hydrology, water tables, nav costs, textures and strategic sites; CPU TerrainNode traversal and repeated patch GameObjects | Reuse Biome IDs, colours, layer blends, surface textures and asset library. Directional terrain is seamless and deterministic, with matching CPU/HLSL evaluation. Existing toroidal catchments cannot simply be copied six times. |
| Ocean | `BattleApp.cs`: HDRP infinite WaterSurface at global Y=0; `Battlefield.cs`: flat clipped inland hydrology | Development ocean is a GPU procedural spherical shell at radius+oceanHeight. Detailed waves, transparency, coast foam and inland spherical hydrology are pending. |
| Rendering | `ArmyRenderer.cs`, `Units.shader`, `ArtUnits.shader`, `UnitGeometry.hlsl`: indirect GPU unit draws, planar yaw, global-up impostors, nearest torus image, terrain texture lookup | Reuse indirect rendering and mesh assets. Planet fixture constructs tangent bases on GPU and renders relative to observer origin. Existing lighting/LOD/shadows and model catalogue must be adapted next. |
| Camera / picking | `RtsCamera.cs`: orthographic camera, XZ focus, Euler yaw around Y, height intersection by y ray equation | Development perspective camera transports a tangent basis while panning, moves focus on sphere and picks a bounded mathematical height surface. Poles have no longitude basis singularity. |
| Selection / commands | `GpuBattle.cs`, `RtsGpu.cs`, `RtsInterface.cs`, `Battle.compute`: screen rectangle is reusable; unit projection, ray-ground result, formation right vector and destination are planar | Flat selection/HUD remain functional. Planet GPU click/drag selection, add/remove, move/stop, per-movement shared fields and the shared Front HUD are connected. Formation/contact steering remains pending. |
| Buildings | `BuildingGrid.cs`, `RtsWorldPlacement.cs`, `RtsPresentation.cs`, `RtsStrategy.cs`: torus tiles, four Y-yaw rotations, XZ footprints, foundation max Y, world-axis picking bounds | Current building models, catalogue, timers, costs, upgrades and IDs are connected through PlanetRtsBase. Radial foundations, footprint/slope/water/deposit checks, GPU occupancy and obstacle stamping are implemented; full grid links and defence combat remain pending. |
| Economy / technology | `RtsDefinitions.cs`, `RtsEconomy.cs`, `RtsWorld.cs`: wallet, timed production, research and power largely independent of surface shape | PlanetRtsBase reuses wallet/building/queue/faction state, scoped cube-surface nodes, timed training/research and compatible GPU spawns. Full modifiers, influence sectors, logistics links and rally following remain pending. |
| AI | `RtsAiPlanner.cs`, `RtsAiInfrastructure.cs`, `AiAreaGpu.cs`: planar sector/cost queries, outpost routes and torus distances | Preserve strategy and paid production. Replace spatial queries through cube-grid adapter. |
| Weapons / effects | `Battle.compute`, `Rts.compute`, `CombatPresentation.cs`, `ShotTracers.hlsl`: XZ spatial bins, global-Y projectile height, torus projectile wrap, planar ranges, vertical blasts/decal normals | Preserve damage, weapon traits, events and pools; replace position, gravity, occlusion and range calculations. Scaled space never supplies weapon positions. |
| Fog / radar | `FogOfWar.compute`, `VolumetricFogOfWar.cs`, `FogWakeField.cs`: 2D visibility grid and world-up volumes | Existing fog remains in flat scene. Planet visibility and volumetric rendering need an independent migration and <=3 ms p95 acceptance. No claim of planetary fog support yet. |

Global-up usage inside mesh builders is usually correct **model-local** up. It
must not be blanket-replaced. Gameplay-facing up, XZ distance/indexing, world
height, axis-aligned footprint bounds and vertical projectile/effect logic are
the actual migration sites.

## Implemented foundation

`UniversePosition` normalizes offsets into half-open 1,048,576 m sectors, subtracts
integer sectors before converting offsets to floats, and retains millimetre
precision at large sector IDs. `ReferenceFrameManager` composes position,
rotation, linear/angular velocity and rotational velocity contributions. It
can transfer a pose between ship/planet/universe frames without changing its
universe state. `FloatingOrigin` only rebases after a configurable threshold;
authoritative state is unchanged. `ScaledSpace.Project` and
`ScaledSpaceRenderer` compress a body's centre and radius together and render
cheap distant spheres. The development scene includes a second distant body.
These visual poses never enter gravity or navigation. Detailed terrain streaming
and a seamless near/far transition are still pending.

`CubeSphere` and `PlanetTopology.hlsl` share the same 24 directed seam transforms.
Four-neighbour connectivity avoids ambiguous diagonal corner jumps. Navigation
and rendering have unrelated resolutions. Costs contain five movement classes.
The adapter owns group fields; unit state remains GPU-authoritative. The fixture
now supports [GPU selection and independent move/stop orders](PLANETARY_COMMANDS.md),
including safe shared-field reuse and rejection when command capacity is full.

The initial surface used uniform GPU procedural cube-sphere triangles. An
adaptive implicit-CBT renderer now adds conforming longest-edge bisection,
GPU subtree populations, culling, split/merge and indirect land/ocean draws.
The uniform renderer remains available for comparison. See the
[adaptive terrain implementation and explicit limits](PLANETARY_CBT.md).
Terrain noise remains a functional deterministic fixture rather than the
original continent/erosion generator. Biome colours/IDs are reused.

## Performance and acceptance

Normal menu RTS, Swarm and AI-test sessions now launch the planetary adapter; the original flat simulation remains available for explicit validation and the million-unit stress path. See PLANETARY_RTS.md for the larger-world streaming stage and remaining gameplay ports.
Planet integration storage is six times a same-face-resolution flat grid. Two
float fields require `6*N*N*groups*8` bytes; 18 groups at N=512 alone cost 216 MiB.
Five movement layers add 30 MiB. A planet cannot achieve metre navigation merely
by increasing N; regional high-resolution grids are required. The current
fixture reserves eighteen shared command fields and relaxes only pending fields.
Its initial demonstration order occupies one field. Selection and orders read
only 42 aggregate counters; event latency at scale still needs measurement.
It is not a validated million-unit planetary battle.

Do not promote the development fixture to the production RTS until equivalent
standalone runs establish median/p95 frame time, simulation throughput and
measured memory, reject >5% army regressions and satisfy the fog budget. Keep the
2560x1440 RTX 3070 target and one-million-unit stress entry point. Historical
benchmark reports are not evidence for this conversion.

## Next integration stages

1. Finish production GPU surface state, seam-aware spatial/contact bins,
   formations, selection, combat/fog and spherical building occupancy together.
   Keep an equivalent flat standalone baseline for each simulation change.
2. Extend the implemented bounded CBT terrain to sparse deep allocation and
   regional precision for realistic planets. Add shared height/material maps
   and local observer collision regions. Current depth limits and saturation
   remain explicit; do not treat the prototype as a complete Earth-scale renderer.
3. Add hierarchical/regional navigation, deterministic spherical landforms,
   hydrology/resource distribution and the existing terrain surface assets.
4. Integrate the initial player mode/FPS, radial gravity, inventory and oxygen
   with a real base and local collision region; add vehicle possession using
   stable GPU IDs. The current avatar is a kinematic capsule with mathematical
   terrain contact plus bounded native ship/building collision. The ship now has
   a walkable blockout, F-operated hatches and pilot-seat interaction; other
   vehicle interiors remain pending. Never mirror the army to drive a possessed unit.
5. Ship-local walking, inertial flight, gear and airlocks now use those coordinate
   contracts. Extend this to docking, weapons and celestial streaming/scaled rendering.
   See `TestResults/Planetary/ShipInterior/README.md` for the tested scope.

Validation is available through **Massive Army > Planetary > Validate Coordinates
and GPU Navigation**, producing `TestResults/Planetary/validation.json`. Bounded
GPU readbacks in that fixture are test observations, not CPU simulation state.
GPU movement covers all 24 directed seams at midpoint and both corner ends (72
cases) and a complete four-face globe circuit without respawning between orders.
It also walks a physical FPS avatar beyond a full circumference from poles,
equator and corners, checking surface contact and camera orientation, and orbits
the RTS camera across both poles through multiple floating-origin rebases. The
standalone smoke harness captures surface, orbit and FPS views with an explicit
GPU render fence and checks that army ticks continue in first-person mode.
The development build uses the existing tank mesh and nominal 5.2 m initial
formation spacing, but production avoidance/contact resolution and final
formation arrival are not yet integrated. The face+UV unit address is a small
planet fixture representation; realistic planets need regional metre coordinates
and per-buffer frame metadata before production migration.

## Fresh standalone preservation measurements

Both builds use the same current project and flat scene; the baseline substitutes
the original `Navigation.compute`, saved in `TestResults/Planetary` before the
comparison. Current restores the shared solver with the opt-in planetary
variant. Both are release/DX11 on RTX 3070 at 2560x1440, shadows on, unthrottled,
with a five-second warmup and twenty-second recording. The 50K world is 4096 m;
the 1M world is 16384 m in both builds. An initial 1M run at 4096 m correctly
rejected insufficient deployment space; the army count was not reduced.

| Army | Median ms baseline -> current | p95 ms baseline -> current | Simulation Hz baseline -> current | Peak allocated MiB baseline -> current | Peak reserved MiB baseline -> current |
| --- | --- | --- | --- | --- | --- |
| 50,000 | 7.87 -> 8.07 | 9.75 -> 10.04 | 29.96 -> 30.00 | 437.77 -> 442.70 | 794.59 -> 794.59 |
| 1,000,000 | 25.02 -> 24.53 | 26.88 -> 26.82 | 30.00 -> 29.98 | 447.67 -> 443.49 | 794.59 -> 538.53 |

Both sampled frame-time changes are within 5%. The 50K scenario meets the p95
60 FPS frame-time target; the 1M scenario fails it in **both** builds. All initial
units remain alive throughout each sample. This verifies preservation of the
existing full-scale path, not achievement of the final planetary performance
target. Memory is Unity tracked allocation/reservation, not dedicated VRAM;
reservation variability is not attributed to an optimization.

GPU timing counters were unavailable in these release runs. Separate simulation
GPU regression and <=3 ms p95 fog acceptance therefore remain **unverified**.
Raw CSV/settings/memory/images and comparison assertions are in
`TestResults/Planetary/Benchmarks`; run `python Tools/compare_planetary_benchmarks.py`
to regenerate the report. Planetary production promotion remains gated on the
missing acceptance measurements and gameplay migration.




### Crowd restoration follow-up

The movement/crowd row above describes the initial migration audit. A spherical
port of the original pressure and predictive traffic algorithms is now available
behind `-planet-restored-crowd` / `PlanetDevelopment.restoredCrowd`, with GPU-only
unit state and retained interest scheduling. It is opt-in because the measured
median simulation regression exceeds the AGENTS.md 5% limit; human approval is
required before enabling it by default. See `TestResults/Planetary/Crowd/README.md`
for the paired measurements and remaining formation/contact/strategic AI work.

### 120 km playable-world follow-up (15 October 2026)

Normal menu RTS, Swarm and AI Test sessions, and direct PlanetDevelopment launches,
now use the confirmed **120,000 m radius**. Terrain is streamed, with bounded
16 m tactical navigation around shared orders and coarse routing between patches.
Physical construction clearance and rally distances no longer scale with coarse
planet cells. Three GPU spatial hash levels preserve nearby and strategic weapon
searches; compact active movement and staggered dormant cohorts retain every GPU
unit record. The original flat million-unit stress entry remains available.

See `TestResults/Planetary/LargePlanet/README.md` for functional results, exact
controlled/ordinary-priority performance measurements and remaining limits.
The optional restored crowd-pressure path still requires regional pressure/contact
pages and its earlier performance gate remains separate. Full original strategic
AI, formations, logistics and fog acceptance are not claimed by this size change.
