# Combat surface blending, lighting and crater terrain

Ground decals now use transparent unlit albedo and a separate linear RGB normal
map. The six combat pairs are in `Assets/MassiveArmy/Resources/MassiveArmy/CombatArt/`:
`NukeCrater`, `ZombieSplatter`, `HumanRemains`, `TankWreck`, `VehicleWreck` and
`BuildingRubble`, with `Albedo.png` and `Normal.png` suffixes. The original art is
retained as source reference. Built-in imagegen produced these assets; the final
prompts are saved in `COMBAT_SURFACE_PROMPTS.json`. Seven equivalent resource pairs
are in `Assets/MassiveArmy/Resources/MassiveArmy/ResourceDecals/`; their prompts are
in `RESOURCE_SURFACE_PROMPTS.json`. They are artistic relief
normals rather than geometry-baked normals. The builder calibrates their flat
background and creates a conservative alpha occupancy mask. Both arrays retain
512-pixel BC7 textures and mipmaps; colour is sRGB and normals are linear.

The previous spatial map stored one record per terrain cell. Replacing that
record hid the older image even through transparent texels. Each occupied cell
now references a sorted list of surviving marks. The terrain shader composites
newest first with premultiplied alpha, stopping after opaque coverage. It blends
rotated normal slopes into the biome normal before HDRP evaluates lighting.
Opaque decal coverage replaces the underlying biome bump contribution; the
terrain's geometric slope is preserved. Resource deposits retain their single
spatial lookup and construction-grid alignment, with a normal sample only where
their albedo has coverage.
Remains render above craters. Transparent texels never erase lower layers.
Alpha occupancy masks skip empty image regions. The fixed 512-record budget,
including 64 reserved crater marks, and eight VFX emitters remain unchanged.
Index lists have a fixed 524,288-uint allocation, with a matching bounded update
buffer. Only decal records are indexed on the CPU; there is no CPU army mirror.
Vehicle wrecks multiply image width and height by three, including their UV
projection. Zombie and human remains retain their previous doubled size and
all existing persistent mark lifetimes.

The GPU army shaders, impostors and building shaders previously used a fixed
light direction and intensity. Their shared inexpensive lighting now reads the
actual scene sun direction, colour and intensity, sky exposure, and fixed camera
exposure. The authored lighting rig updates these globals when scene values are
edited. Near, far and billboard unit views use the same settings. Scenery and
rocket bodies also respond. This preserves instanced rendering and avoids the
cost of adding an HDRP material/light loop to every army model. Gameplay
projectile tracers and explosion particles remain intentionally emissive.

`NukeCraterHeight.asset` is a readable 256-square signed RFloat height map:
centre -1, unchanged outside 0, broad raised rim +0.85. The deterministic builder is
`Massive Army/Art/Build Crater Height Map`. Nukes queue a bounded terrain update
even when cosmetic VFX are disabled. Depth scales with image width, clamped to
2–12 metres. Overlapping bowls use their deepest displacement rather than
accumulating unlimited depth; a depressed bowl takes precedence over another
crater's rim. Nearby impact footprints are merged before mesh and navigation
updates. Changes persist for the battle. Crater decals now also persist through a reusable detail pool and a compact terrain atlas; see [Nuclear presentation](NUCLEAR_PRESENTATION.md).

Terrain heights are private to the battle, leaving the original preview/world
generation data intact. The shared GPU height/slope texture, CPU ground queries,
affected quadtree meshes, mesh normals, building foundations, scenery heights,
and navigation terrain costs update together. Building obstacle costs still pass
through the existing wall/gate system. Terrain depth and shadow passes therefore
use the same changed mesh. The original water/rock topology remains intact;
this implementation does not simulate new flooding or turn craters into hard
obstacles. Shape detail follows the existing navigation and terrain LOD sample
spacing, so small craters on very large worlds have less geometric detail.

## Surface-change validation (before the subsequent nuclear presentation refinement)

`TestResults/CombatSurface/blending-validation.json` covers actual GPU alpha
composition, transparent overlap, normal rotation, hidden records, texture import
settings and the signed height map. `lighting-validation.json` renders the actual
legacy and catalogue shaders at all three detail levels for humans, APCs, tanks
and zombies, plus building shells and scenery. It checks sun intensity, exposure
and darkness with the sun/sky disabled. The combat integration validation also
checks threefold vehicle image size, actual deformation, repeated impacts,
preserved command-centre obstacles and matching CPU/GPU ground height.

All checks passed: 33 GPU blending/import/height-map checks, 95 actual shader
lighting checks, and 40 GPU combat/terrain integration checks. This includes a
real off-camera nuclear detonation with VFX disabled and no presentation
subscriber. The final release build succeeded with zero errors. Standalone logs
contain no exceptions, shader errors, invalid kernels or skipped dispatches.

## Standalone comparison

RTX 3070, Windows 11, Unity 6000.6.4f1, release DX11 players with an explicit
2560×1440 HDRP target and a GPU completion fence. The baseline is the frozen
player from the previous combat refinement; its managed assembly hash matches
the original copied player. Identical seeds, scale, camera settings, shadows off,
five-second warmup and twenty-second measurement were used for both versions.
Both benchmark processes used Windows High priority. Dense results are the
median of three interleaved run medians/p95 values/rates/memory peaks. Base and
million overview each use one pair. Raw settings, CSVs, logs, frame captures and
summaries are in the ignored `TestResults/CombatSurface/paired/` folder.

| Scenario | Median ms before → after | p95 ms before → after | Simulation Hz before → after | Allocated MiB before → after | Reserved MiB before → after |
|---|---:|---:|---:|---:|---:|
| Dense 50,000 | 5.928 → 6.019 | 7.067 → 7.051 | 30.00 → 30.00 | 468.04 → 469.98 | 794.91 → 538.84 |
| Base 150,000 slots | 6.689 → 6.765 | 8.818 → 8.923 | 30.01 → 30.01 | 513.88 → 514.38 | 798.91 → 798.91 |
| Million-unit overview | 6.446 → 6.414 | 25.129 → 25.256 | 30.01 → 30.00 | 473.98 → 487.51 | 546.84 → 802.91 |

Dense median changes +1.54%, base median +1.14% and million p95 +0.51%; the
largest aggregate timing increase is below 2%, within the 5% overall-frame
proxy. Dense retains 50,000 deployed units and roughly 48,640 survivors; base
retains 100,008 live entities in 150,000 slots; overview retains 1,000,000 live
units with approximately 17,050 visible. No army scale was reduced. The
million-unit scenario still exceeds the 16.67 ms p95 budget in both versions.

Memory is measured peak Unity tracked allocation/reservation, not dedicated
VRAM or whole-process working set. Reservation/loading/collection vary between
runs, so these numbers are not a precise permanent-footprint estimate. Separate
army GPU timings and the 3 ms total-fog p95 target cannot be established from
these release captures. The paired performance cases cover ordinary combat,
base fog and the million-unit path; nuclear deformation itself was validated
functionally, not benchmarked against an equivalent standalone nuclear volley.
