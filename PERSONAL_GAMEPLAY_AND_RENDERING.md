# Personal gameplay and large-planet rendering

## Controls

- `I`: inventory. Drag owned items onto the ten action-bar slots; `1`ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Å“`0` equips them.
- Left mouse: fire/attack, or hold to use a consumable. Bandages/medical items take time. Guns and bows require their specified ammunition.
- `E`: nearby supplied shop/pub, or a nearby citizen. Talk, buy, or recruit from that panel.
- `O`: name an organisation (gang, business, security company, club), and give recruited guards follow/call, wait, or guard orders.
- Existing `F8` prototype deployment now includes the one-seat SF-QB1 quad alongside the jeep and biped mech. Ordinary RTS selection/move/stop also forwards orders to the bounded detailed vehicle pool.
- Research panel: wheel zooms at the cursor; drag the background or use middle/right drag to pan. FIT restores the whole graph. Spectators can inspect without purchasing research.

## Items and shops

`Assets/MassiveArmy/Resources/MassiveArmy/PersonalItems.json` defines 37 items: firearms, blades, bows/crossbow, ammunition, food/drink, medical supplies and tools. Optional mesh/material fields use Resources paths; unspecified equipment uses a cube. Custom use behaviours can register through `PlanetDevelopment.RegisterPersonalItemAction` and an item useAction id.

New buildable venues are gun, blade, pawn and black-market shops, plus pubs. Purchases consume real shop inventory and player credits, and pay the settlement treasury. Shops need power and logistics deliveries. There is no free weapon stock: service weapons and ammunition must be produced/supplied. AI settlement planning can add supplied gun/blade shops and pubs. Gun laws can be toggled in civic security.

Weapons have individual serial keys and provenance. Some pawn, black-market and gang weapons are stolen and may link to an existing murder case. Nearby police searches can seize contraband and create player evidence/wanted records. This is an initial evidence system, not a complete player trial/prison flow. Bow attacks currently use the same finite-ammunition ray attack as firearms; projectile flight is not modelled.

Recruitment costs 50 credits plus a spare firearm, and is capped at four guards. Guards are GPU army actors; contracts contain identity and order metadata only. No CPU army pose/health mirror or per-unit GameObjects are introduced. Guards currently remain on their recruitment planet. Pub contacts include the employed bartender. Organisation formation records identity/membership; a full business-management interface is not included.

Health, hunger and thirst meters appear on foot. Food/drink replenishes needs; empty needs damage health. Existing oxygen handling remains. Inventory is carried between player control sessions.

## Research

Automatic base strategy already selected research with prerequisites, resources and appropriate laboratories. Supplied, powered labs now attempt research independently of the broader economy-ready gate. Lab construction still respects economic readiness. The panel reports queued research; this does not guarantee that an unsupplied settlement can research.

The graph uses dependency depth with separate branch lanes, fixed-size cards and routed edges, inside a zoom/pan viewport. Cyclic dependencies fail validation instead of silently creating a broken layout.

## Terrain and precision

The planet definition has **Experimental GPU Double Precision**, disabled by default, and **GPU Double Minimum Radius** (default 2,097,152 metres). Each planet makes its own decision. Enabled bodies above the threshold on Direct3D11/12 use native double calculations for selected terrain-position reconstruction and cloud shell/radial calculations. Keep the option off on hardware without double shader support. It does not convert the entire renderer, noise textures or heightmap storage to FP64. Native double arithmetic can be expensive, particularly on consumer GPUs; performance has not been certified.

The normal path retains camera-relative/compensated positioning. Roads now carry high/low residual positions and a 0.22m surface clearance. This prevents losing small offsets during conversion to planet-sized float vertices; it does not increase the hardware depth buffer bit count.

The Terrain Surface Library adds distance texture blending, starting distance, altitude cooling and cliff-angle settings. Successive texture bands increase repeat size by eight and crossfade adjacent albedo scales, while normal/height sampling stays on one band to limit cost. Wrapped phases are calculated in CPU double before passing camera-relative coordinates to the shader. Cliff thresholds default to 11ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Å“47 degrees (about five degrees lower than the previous slope range). Biomes/snow now cool above sea level; border-noise scale defaults to 192 instead of 96. Existing assets expose these controls live. Planet size multipliers and the user cloud preset were not changed by this work.

Technique references: [Epic distance-based detail blending](https://dev.epicgames.com/documentation/unreal-engine/adding-detail-textures?application_version=4.27), [Microsoft optional double shader support](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ns-d3d11-d3d11_feature_data_doubles).

## Verification

- Runtime and editor C# compilation.
- 163 managed catalogue/inventory/dependency-layout/construction precision checks.
- Unity asset validation: 9 checks, including generated one-seat/four-wheel/three-LOD quad and actual research graph.
- 135 compute-kernel variants and 98 rendering stages compiled, including native double variants.

Reports/scripts are in `TestResults/Gameplay`. Unity was at the menu during asset validation; live research completion, vehicle handling, visual quality and shop/recruitment interactions still need playtesting. No equivalent standalone before/after performance or memory comparison was obtained. These checks do not establish the 60 FPS, fog-cost or million-unit regression acceptance targets. Extra texture samples, event-driven weapon picks and the opt-in double path need profiling before performance acceptance.

## Crater/decal precision follow-up

Terrain decals now project from camera-relative surface positions instead of subtracting planet-sized float positions. This covers resource, casualty and recent nuclear scorch details. The opted-in native double variant carries precise cube/patch coordinates through crater distance evaluation and final mesh displacement, in both colour and depth passes. Crater normals use a short physical sample interval rather than a fixed angular interval. Nearby CPU terrain contact probes preserve their double position through crater evaluation as well. Base noise/texture storage is unchanged.

Validation: 96 GPU samples at 60 km, 6,000 km and 60,000 km radii, single/multiple craters, compared against a double reference and CPU contact samples. Maximum fixture height error 0.000001593 m; maximum local decal-position error 0.000001853 m. All 86 rendering-stage/geometry/edit/double permutations and 18 precision compute permutations compiled, plus the existing 133 gameplay compute kernels. The fixture tests arithmetic, not full visual behaviour or standalone frame time; native double rendering cost remains unmeasured.

## Texture controls, ocean, civic inspection and public house

Open **Synthetic Front Ã¢â€ â€™ Terrain Ã¢â€ â€™ Texture and distance settings** to select the live Terrain Surface Library. Under Distance texture bands and altitude:

- **Texture Scale**: overall repeat size; 2 makes features twice as large, 0.5 twice as dense.
- **Texture Band Scale**: multiplier between distance bands; default 8.
- **Texture Band Start Distance**: metres before the first distance transition; default 48.

Edits update bound materials in Play mode. The scaled/distant planet shader now also binds the distance texture parameters and wrapped phases correctly.

Planet definitions expose Ocean Wave Height, Wave Length, Wave Speed, Shore Attenuation Depth and Foam Depth. Waves are inexpensive geometric/normal waves with depth attenuation and animated shoreline foam; they do not add fluid simulation or change vehicle buoyancy. Zero wave height disables displacement. Ocean colour and depth draws share the same displacement.

Army mesh rendering now cancels observer/planet offsets before adding local model vertices, using compensated float arithmetic rather than native doubles on every army vertex. Civic meshes also cancel translation before local vertex offsets. GPU simulation formats are unchanged: this fixes render-coordinate precision, not every limitation of float simulation positions at extreme scale. Modular building centres, foundations, plots, driveways and nearby collision geometry retain double coordinates until conversion into the local render frame.

Vehicle entry prefers a movement-capable seat, and closing the on-foot inventory preserves cursor capture for driving/piloting. Contact projection preserves the precise position at every planet scale, ignores colliders belonging to the current rig, and uses stable normal probes. Vehicles resting within the landing tolerance can resume driving instead of staying in their falling state. The HUD reports a blocking collider/terrain support when stalled; R still performs recovery. Fixtures drove both detailed jeep and quad driver rigs for ten seconds at 60 km, 6,000 km and 60,000 km radii. The original live blockage has not been reproduced in a running settlement.

**Funeral Directors** is a new buildable service and is planned by automatic settlements after the graveyard. Once staffed, it becomes the hearse home. The hearse collects a body, drives it to a graveyard, waits for burial, returns along roads and completes a short final approach into a marked forecourt space. Existing graveyard-only towns retain the previous service until a director is available.

Click a living civilian or corpse in RTS mode to inspect identity, occupation/activity and death records. Hover a corpse for its name/cause/date. The context panel has FOLLOW IN RTS / STOP FOLLOWING. Moving the RTS camera manually cancels following; zoom can be used while tracking. Only an explicitly followed person's pose is read back, at a limited rate.

The modular public house includes an open bar/pool room, toilets, service room, staircase, gallery overlooking the bar and two upstairs apartments. Furniture is shared baked geometry, with portal visibility and bounded nearby interior streaming. An employed adult citizen becomes the bartender, uses the existing GPU crowd actor, follows the road route to work and is rendered at the service station when on duty. Pub hours are 11:00Ã¢â‚¬â€œ23:00 and require power, operation and employed staff; the exterior door locks when closed. Valid apartment tenants can enter after hours and can leave from inside. Apartment doors require the player's matching lease. Use E at the pub for its shop, bartender conversation and apartment rentals (5 credits per game day). A local household can also rent upstairs through the existing housing/rent system. NPC homes otherwise retain the game's abstract indoor simulation; this is not a general indoor crowd navigation system or a playable pool minigame.

New validation reports: `vehicle-motion.json`, `pub-validation.json` and `civic-gpu-validation.json` in TestResults/Gameplay. Performance acceptance still requires equivalent standalone before/after frame-time, simulation-rate and memory measurements; none is claimed here.


### Gerstner ocean and imported tank (23 October 2026)

- PlanetDefinition > Ocean surface controls wave height, wavelength, speed, steepness, shore attenuation depth, foam depth/width/strength/noise scale. Four directional Gerstner waves displace tangentially and radially, with analytic surface normals. Small waves fade at distance; shoreline attenuation suppresses waves on dry land. Colour and depth passes share the exact displacement. CPU double wrapped wave/foam phases keep the surface anchored across floating-origin rebases. Foam is a narrow slope-aware, noise-broken shoreline plus shallow-water crest foam, rather than broad sinusoidal bands. These are visual waves; no new fluid/buoyancy simulation.
- The original hi3d-military-tank-v1-original.fbx is preserved. Optimized shared Hull/Turret mesh assets and SF-TK1.prefab live in Resources/MassiveArmy/Vehicles. TankOptimized/TankSplit.fbx contains the editable split derivative. The correction uses Y up, Z forward, a ground-level pivot, and an approximately 8m total length. The turret ring provides independent yaw. The detailed derivative has 36,354 triangles (29,130 hull, 7,224 turret), versus 1,974,582 in the supplied mesh. UVs/albedo retained; duplicate attribute vertices are indexed. Simplified army/distant versions have 422/90 triangles, within the existing fixed 512/128 budgets. Small fittings are omitted from distant LODs.
- Tank uses existing bounded vehicle detail pool and tracked motor with four track contact patches, driver and AI-capable gunner seats. It is configured to spawn owned by team 0 beside the homeworld starting ship after prebuild; PlanetDevelopment.spawnStartingTank toggles this. F8 can deploy the other prototypes independently and skips already registered types. ImportedTankPack clones the currently selected art catalogue when baking, preserving its other overrides and turret UV2.x tags.
- Validation: runtime/editor C# compile; 100/100 render shader stages including terrain colour/depth float/double and vehicle proxy. Detailed tank/jeep/quad driver fixtures each ran 600 frames on radius 60,000 / 6,000,000 / 60,000,000m; tank travelled about 105m at 15m/s with all nine cases passing. Asset validation checks separate rig, axes, dimensions and GPU turret tags. Standalone median/p95, simulation throughput and measured memory comparison has not been performed, so this is not a 60FPS acceptance claim.


### RTS vehicle steering authority (23 October 2026)

Nearby registered vehicles now retain the standard GPU flow-map movement, turning, formation and stop behavior. A detailed rig no longer switches RTS orders into the separate CPU RtsInput motor; the asynchronous destination-readback bridge is removed. Local movement is only enabled for an occupied driver/pilot seat. Detailed AI rigs continue to follow GPU pose/heading and animate wheels/legs/turrets. Cargo attachment and airborne gravity remain local exceptions so parked vehicles and released cargo retain support/flight physics.

The bounded vehicle Control shader stops an old route only when first taking local authority. It preserves later GPU orders and leaves an ordinary player exit stopped, rather than resuming a stale destination. New RTS orders clear STOPPED through the normal command path. No army buffers, unit counts, flow algorithms or per-unit object allocation were added.

Validation: 25 editor/GPU checks cover all four prototype rigs (tank, mech, jeep, quad), detailed AI authority, a lateral GPU move, heading reversal, driver entry/exit and move-order preservation through local handoff. 135/135 compute shader kernels compile. Standalone median/p95, simulation rate and measured memory comparison have not been collected; no performance acceptance claim is made. The army movement kernel is unchanged, and bounded CPU RTS steering/readbacks have been removed.


### Large-planet movement rounding and sign warnings (23 October 2026)

A full 100x RTS replay reproduced accepted move orders and nonzero velocity while the stored cube-face position never advanced. Tiny angular steps were being rounded away by PlanetEncode, including for some slow-moving units on ordinary planets. The direct 60km/20m/s steering fixture did not expose this.

Planet movement now uses a compensated projective coordinate increment for small angular steps. Planet-only UnitState.previous stores a tagged low-coordinate residual (bit 28), since planetary meshes do not use flat-map render interpolation. The 64-byte army ABI, buffer capacity and GPU authority are unchanged; no per-unit buffer or CPU mirror is added. Planet unit rendering and bounded vehicle pose readbacks consume the residual, so display movement also retains the small steps. Stop, unreachable, inactivity and local-control/teleport paths preserve or reset it appropriately. Flow-vector normalization and tangent tests retain small angular directions on large bodies. Large-step/coarse movement keeps its existing path until compensation is required.

Road-sign facing uses PlanetConstructionGrid.NeighbourTangent, calculated from precise neighbouring grid positions in metres. Degenerate edges are skipped before LookRotation, rather than repeatedly logging a zero viewing vector during settlement mesh rebuilding.

Validation: production GPU Move kernel simulated .8m/s for 30 seconds across six faces at radii 60,000m, 12,000,000m and 120,000,000m. All 18 cases travelled approximately 24m (worst observed error below 7mm); 72 road-edge directions remained valid. The existing 25 steering/authority checks, 135 compute shader permutations and 100 render stages also pass. Standalone median/p95, simulation throughput and measured-memory comparisons have not been collected; target performance acceptance remains unverified.


Full-game replay at 100x accepted an infantry order, recorded nonzero velocity, and advanced its position about 257m. The fresh replay emitted zero zero-viewing-vector warnings. It also exposed that the RTS camera could cross the flight activation-altitude limit and suspend the selected world's simulation. The resident selected RTS world now stays active while its map is viewed, independent of camera zoom; other worlds retain their existing sleep policy.
