# Synthetic Front - GPU army architecture

Unity 6000.6.4f1 / HDRP 17.6.0. The reference image guides the cyan/red synthetic-war presentation; its scene complexity is not a rendering guarantee.

## Run and controls

Open `Assets/Scenes/MassiveBattle.unity`, enter Play, choose 50K and deploy. If the scene is missing, use **Massive Army -> Prepare HDRP and Battle Scene** once. The setup selects HDRP for every quality level and enables HDRP water and frame timings. The sample template scene is preserved.

Drag/click selects cyan tanks; Shift adds and Ctrl removes. Right-click moves in a compact grid; right-drag previews a front edge whose width and angle determine columns, rows and facing. A then click attack-moves; Attack then click picks a red target; S stops; Space pauses; Escape opens settings; F1 toggles the developer panel. Camera controls are arrows, W/D, Alt+A/S, middle drag, wheel and Q/E. The sector minimap focuses the camera. M opens the biome atlas. Settings persist seed, sensitivity and VSync. A new battle regenerates seed-specific terrain and buffers.

## Authority and layout

No soldier or tank has a GameObject, Transform, MonoBehaviour, Rigidbody, NavMeshAgent or ECS entity. CPU systems own input, camera, terrain generation, compact commands, allocation and rendering submission. All authoritative live unit state remains on the GPU, with no CPU array of live units. High-level enemy AI reads a 4x4 sector reduction every eight seconds and changes one army objective.

`UnitState` is explicitly laid out as 64 bytes in C# and HLSL:

| Byte offset | Field | Bytes |
|---:|---|---:|
| 0 | XZ position | 8 |
| 8 | Previous XZ position | 8 |
| 16 | XZ velocity | 8 |
| 24 | Individual destination | 8 |
| 32 | Chassis angle | 4 |
| 36 | Turret angle | 4 |
| 40 | Weapon cooldown | 4 |
| 44 | Integer health | 4 |
| 48 | Packed team/type (8 bits each), strategic sequence (16 bits) | 4 |
| 52 | Alive, selection, movement, arrival and recovery flags | 4 |
| 56 | Target index (0xffffffff means none) | 4 |
| 60 | Shared flow group (0xffffffff means none) | 4 |

The stable buffer index is the unit ID. Archetypes are separate 64-byte records containing movement class, weapon, speed, health, damage, cooldown, aim, range, model, sprite and LOD parameters. Movement writes complete snapshots into the other unit buffer and swaps ownership. Selection, targeting, firing and damage operate on the current buffer at separate dispatch boundaries. No pass writes a neighbour's health. Atomic integer damage uses a separate four-byte accumulator; a later pass applies and clears it. Death clears flags and releases the group. Master slots are not compacted per death.

## Army scale and deployment

Battlefield side length starts at `max(2048, ceil(sqrt(activeUnits) * 5.2 * 2 / 64) * 64)`. Maps above 4,096 units use 512-squared navigation; smaller maps use 256-squared. The spatial grid scales with map size, capped at 256-squared. Before GPU allocation, generation checks actual usable deployment capacity and grows the map by 20% if necessary. Camera overview, zoom limits and far clipping follow the resulting size.

Each ground movement layer is flood-filled with diagonal corner checks, retaining its largest connected component. A shared clearance distance adds a soft route penalty around banks and rocks. Spawn candidates lie on a unique 5.2-unit lattice and have 2.3 units of terrain clearance, covering the visible tracks' half-diagonal with 0.1 beyond GPU movement queries. Opposing sides receive balanced unique positions on the connected landmass. Initial hull alignment leaves at least 1.8 units beyond the 3.4-unit length. Isolated summits and islands are excluded. A load-only position table is uploaded once and released; immutable terrain metadata is not a live unit mirror. Initial destinations use unique opposing deployment positions.

## Fixed-step pipeline

Simulation runs at 30 Hz with at most three catch-up steps per rendered frame. Under overload simulation can fall behind wall time; FPS alone is not simulation throughput. Visibility and rendering run every rendered frame.

1. Selection/order events modify GPU flags and destinations.
2. Clear counts and cursors, count live positions, exclusive scan and scatter stable IDs.
3. Shared full-resolution navigation integrates at 15 Hz until GPU convergence flags indicate stability.
4. Build a fine contact grid and the crowd density-pressure field. Predict movement from terrain flow, formation destinations and local steering. Rebuild contact membership at predicted positions, then perform three damped snapshot-based contact corrections.
5. Stagger target acquisition by `(ID + tick) % slices`; retain valid targets and invalidate dead or out-of-range ones each tick.
6. Aim/fire, claim bounded gameplay shell slots or resolve direct statistical attacks.
7. Move shells and sweep against spatial candidates, the intended victim and terrain.
8. Apply atomic damage, deaths and group release.
9. At 5 Hz reduce team, live, selection, combat, shell, group, sector and maximum-cell statistics.
10. Frustum cull into three unit tiers, generate visible lists and indirect arguments, and submit a constant number of `Graphics.RenderMeshIndirect` calls.

All stages have named GPU samples, including `Army.CrowdPressure`. The HUD sums available scopes, including HDRP terrain, ocean and post-processing under rendering. This is not a whole-frame hardware measurement. CSV retains raw Unity `engine_gpu_ms`, which has sometimes been smaller than the measured rendering scope on the test machine. CPU submission time is never labelled GPU time.

## Spatial indexing and local movement

`GpuPrefixScan` performs a work-efficient Blelloch exclusive scan of 512 elements per group, recursively scans block sums, and adds offsets. It supports 1 through 1,048,576 elements including partial blocks. Counts and scatter accept arbitrary input order; no gameplay grid counts are downloaded to CPU.

Avoidance has a separate contact grid based on mapSide/4, capped at 1024-squared. Steering visits enough cells for an 8.2-unit anticipation radius, capped at four rings and a 48-candidate budget. Predictive passing normally chooses the right side, or the left when terrain blocks it. Passing requires a hull-sized gap among nearby tanks; with crowd pressure enabled, the side must also have available crowd capacity. Full queues wait instead of repeatedly pushing sideways into equally crowded lanes. Two existing flag bits retain the passing side while surrounded; a terrain obstruction or a new order resets the choice. This suppresses left/right flipping while allowing a different opening to become usable. Forward drive and existing momentum are projected out of a queue behind stationary or same-direction traffic. Steering acceleration is bounded separately along and across the route; immediate collision braking still takes priority.

The 2.1-unit contact radius covers the visible hull in any orientation. A 0.5-unit pair margin cushions dense queues. Three contact passes inspect up to 128 candidates, with a query radius covering body size and remaining solver displacement, capped at four rings. They use immutable snapshots, symmetric pair normals and sample offsets stable across ticks. Corrections are normalized by accumulated constraint weights, damped, and have a three-millimetre dead band. Corrections slide along terrain edges; unconstrained tanks take the full correction when a neighbour cannot move. Both teams participate and stopped units remain stationary.

Within seven units of an assigned formation position, tanks may pass through friendlies to park. Enemy contact remains active. Arrived tanks yield only when obstructing an incoming friendly's projected path, then return to their slots. If filled rows trap a formation member, stalled entry recovery permits passage through already parked friendlies near their own slots. Moving traffic, manually stopped units and enemies retain avoidance outside the ordinary parking exception.

All neighbour queries are bounded; extreme overcrowding can exceed sample coverage. This is not a proof of globally deadlock-free traffic. Acquisition computes its radius from attack range, rotates cell traversal, and examines at most 192 candidates. It chooses the nearest inspected enemy, not necessarily the globally nearest one. Acquisition extends the pre-movement spatial snapshot by one cell. Projectile broadphase checks neighbouring cells and always tests the intended victim. No query scales quadratically with global unit count.

## Crowd density pressure

`GpuCrowd` uses the next power of two of mapSide/8, clamped to 64-512 cells per side. Alive tanks of both teams splat fixed-point mass bilinearly at predicted next positions. A 3x3 binomial filter reduces lattice aliasing. Density is normalized to one tank per 5.2-squared world units. Eight warm-started projected Jacobi iterations solve nonnegative pressure above a 0.9 density target. Open space permits spreading; solid terrain uses a no-flux boundary. Smoothed pressure gradients resist movement into dense cells and steer toward available space.

This is a bounded approximation to a maximum-density constraint. It is not an exact incompressible fluid or a full Navier-Stokes solver, and has no attraction, surface tension or vorticity. Fine contacts protect individual hulls; finite grid and iteration budgets cannot guarantee an exact pointwise density bound. Formation parking remains the explicit overlap exception. Manually stopped tanks contribute occupancy and do not move. The current implementation targets heavy vehicles; additional movement classes would need their own terrain masks.

The density representation is inspired by [Aggregate Dynamics for Dense Crowd Simulation (Narain et al., 2009)](https://gamma-web.iacs.umd.edu/DenseCrowds/). All state stays GPU-only: mass, pressure pair and density/gradient use 28 bytes per grid cell (7 MiB at 512-squared). The subsystem toggle enables comparisons. **Massive Army -> Validate Crowd Density and Stability** measures deliberately compressed mixed-team traffic and a closed-gate waiting queue, reporting spacing, estimated peak density, progress and displacement jitter in `TestResults/crowd-validation.json`.

## Navigation and formation commands

Eighteen bounded route groups serve two strategic armies and sixteen concurrent player orders. Each has a full terrain-resolution ping-pong integration field. Eight-connected relaxation rejects diagonal corner cutting. A derived 32-squared field remains available for diagnostics. Movement uses the fine potential gradient and within-cell lane offsets, avoiding shared coarse cell-centre funnels. Unknown or disconnected routes wait. Verified direct paths use exact terrain-grid traversal rather than the old five-sample sector approximation.

Detour steering stays on the descending connected field through concave banks, avoiding repeated switches toward a briefly visible slot. A GPU-only eight-byte progress record stores best potential and stalled time. After several cell-travel times without improvement, recovery removes inertia and recommits to the descending route while retaining lateral passing, separation and contacts. Terrain checks include hull clearance at endpoints. This does not teleport units or relax enemy contacts. Mirrored cup tests require sixteen tanks with momentum aimed into a U-shaped bank to exit away from the goal, detour and park.

A drag defines the formation front. Exclusive scan assigns stable selected ordinals; width determines columns at 5.2-unit spacing and count determines rows. A partial last row is centered. Four bounded GPU scan batches skip blocked lattice positions and extend into rear rows, producing unique slots shared by preview and order. Up to 4,096 evenly sampled ghost markers bound drawing cost; the box includes a margin and expands to the used rows. Too few clear positions or an out-of-map footprint rejects placement. Adjacent blocked slots never merge through nearest-cell projection.

Orders read 37 exact membership counters (148 bytes) and sixteen bytes of placement counters at command time. A fully selected group can be retargeted immediately, including when all sixteen fields are occupied. Empty groups can be reused immediately. If every field contains unselected members, the UI reports the limit. Arrival and Stop release route membership. Every order clears preview state, including rejected orders. Each preview rebuilds selection ranks and reads sixteen bytes of metadata asynchronously; rapid drag updates reschedule the readback so the final box cannot remain stale.

Candidate slots are validated in the isolated preview group before a live route changes. Rejected placements preserve the previous destinations and group. Red strategic objectives use the same staging and unique-slot validation. Shared fields route to the formation centre before verified individual paths take over. Very large formations spanning separate terrain regions may need smaller orders because one field cannot independently route every disconnected slot.

## Terrain, biomes and hydrology

The seeded generator builds landforms, hydrology, climate, biomes, roads and scenery in that order. Regional masks produce mountains, rolling terrain and broad flat plains. Compressed latitude controls equatorial-to-polar temperature, altitude cools climate, and longitude, coherent moisture and rain shadow vary wet/dry regions. This is a playable climate model rather than a geographic Earth simulation.

Twenty categories cover ocean, lake, pond, river, beach, tropical rainforest, tropical seasonal forest, savanna, hot desert, semi-arid steppe, temperate grassland, temperate forest, temperate rainforest, Mediterranean scrub, wetland, boreal forest, tundra, cold desert, alpine rock and snow/glacier. Eight normalized surface layers are grass, sand, dry grass, forest floor, rock, snow, mud and gravel. Terrain meshes and units share heights; chunk skirts seal depth seams. Visual chunk existence never changes navigation.

Priority-flood drainage builds acyclic downstream links and spill elevations. Boundary-connected negative terrain becomes ocean. Enclosed catchments become lakes or ponds by area. Accumulated runoff carves rivers toward outlets. A shared inland mesh clips lake shores and draws continuous river ribbons at local elevations; HDRP renders sea level zero. Navigation uses actual water depth including elevated lakes. Water deeper than 0.8 metres blocks ground vehicles; boats need over two metres. Large boulders block ground cells.

Roads connect dry strategic sites through slope-weighted eight-connected A* with corner checks. A spanning network plus loops connects sites within each dry component. The complete footprint excludes water and a two-cell shoreline buffer; roads do not create water causeways. Disconnected regions retain separate road networks.

Biome suitability and coherent patch masks cluster scenery, leaving plains and clearings open. Seven shared indirect batches draw conifers, broadleaf trees, palms, acacias, cacti, boulders and shrubs. Records use 32 bytes plus four-byte visible indices; no per-tree/rock objects or colliders exist. M/MAP opens the atlas, with hover climate/elevation/depth, terrain/roads view, camera focus and exports. Exports include biome/water PNGs and a colour/count legend under `Application.persistentDataPath/BiomeMaps`. The atlas blocks battlefield input while open.

Landscape resources include height/slope/forest/road RGBAFloat; splat, overview, biome and two surface-weight RGBA32 maps; water level/depth/kind RGBAFloat; and five traversal layers.

## Combat and rendering

Gameplay shells use 48 bytes with 32,768 default slots. Firing claims up to four hashed free slots with atomic compare/exchange. A full pool delays the shot instead of inventing a hit. Disabling gameplay shells selects direct atomic damage for comparisons; existing shells finish. VFX only controls shell drawing.

Near tanks use generated chassis/track/turret/barrel meshes (108 triangles), medium tanks use 60 triangles, and far tanks use alpha-clipped quads with an eight-view nearest-neighbour atlas. Both teams share assets with shader tint; turret vertices rotate independently. Selected units receive a bright highlight. Scenery has static instance buffers and visibility lists. No per-unit draw calls occur. This remains a tank-only prototype; capture rules, bases, extra classes and richer artwork are deferred.

## Capacity, readback and disposal

Capacity and initial active slots are separate. Dispatches visit initial active slots, with cheap skips for dead units; unused capacity is not simulated. Visible draw counts are GPU-generated. Indirect compute dispatch has not been added because current dispatch sizes are CPU-known and it would remove no synchronization.

Normal statistics are 128 uints (512 bytes) at 5 Hz. Pending navigation adds eighteen convergence flags at up to 4 Hz. Compact command and preview readbacks are described above. Debug unit sampling is explicitly capped at 256 and is only used for bounded validation fixtures. Every GraphicsBuffer, runtime mesh, texture, material and compute clone has an owner and disposal path; asynchronous callbacks discard results after disposal.

## Validation and profiling

**Massive Army -> Run GPU Validation** records hardware/API, pass/fail and assertions in `TestResults/gpu-validation.json`. Tests cover prefix scans through 1,048,576 entries, all 50K spatial memberships, indirect arguments, picking/selection, movement/Stop, direct damage, shells and death. Movement fixtures cover head-on passing, friendly parking, enemy clearance, a 1,200-unit offset-wall route, unique rotated previews/slots, final parking, a 64-tank column and mirrored concave banks. Additional tests cover 48 rapid orders, occupied groups, rejection recovery, obstacle-adjacent slots, strategic validation, and opposing queues through a corridor and offset opening. Pointer gestures consume individual GUI events so rapid input retains its original coordinates.

Biome validation checks deterministic multi-seed generation. Deployment validation covers supported scales and difficult seeds. Million-unit spawn validation checks blocked positions, minimum spacing and exact spatial membership. Crowd validation directly measures compression and queue vibration. Consult each JSON for the actual completed assertions; configured scale support is not test evidence.

The developer HUD reports live/team/selected/visible/LOD/shell counts, destroyed units, maximum cell occupancy, estimated memory and timing availability. Benchmarks warm for five seconds and record twenty seconds of CSV plus hardware/settings JSON in `Application.persistentDataPath/Benchmarks`. They reset to an overview camera and suppress battlefield/camera input. JSON includes resolution, camera pose, field of view and frame cap. Zero timing values mean idle or unavailable, not zero cost.

Scale choices are 10K, 50K, 100K, 250K, 500K and 1M. Toggles isolate movement, navigation, grid, density pressure, acquisition, firing, shells, selection, terrain/ocean, scenery, VFX, meshes, impostors, AI and movement LOD. Distant peaceful movement can run at half rate with accumulated timestep; dense crowds remain at full movement rate. Targeting never sleeps because of camera distance. One million is a stress configuration and validated deployment scale, not a frame-rate guarantee. See `BENCHMARKS.md` for measured results and `GPU_MEMORY.md` for allocations and exclusions.
