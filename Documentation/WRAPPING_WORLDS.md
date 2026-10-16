# Wrapping worlds and volumetric smog

## Using the update

Open `Assets/Scenes/MassiveBattle.unity` and enter Play. Choose **WORLD SETUP**
before starting a match. Select a layout, independent climate, world size and
seed; the seven advanced groups expose generation controls. **GENERATE PREVIEW**
is cancellable. Its terrain, water, gold deposits and cyan/red starts are the
same resolved world used by **START BASE MATCH** or **START STRESS**. Changing a
setting invalidates that preview. Failed start/progression constraints appear
in setup; the game never silently enlarges the map.

Supported widths are 2,048 / 4,096 / 8,192 / 16,384 metres. Navigation remains
256 cells for the two smaller worlds and 512 for the larger ones. Setup reports
cell size and a buffer estimate; this excludes shared textures, HDRP targets
and driver allocations. Stress deployment separately checks usable hull space.
The default 8,192 m and 16,384 m coastal worlds fit one million initial slots.

Save named presets by changing the JSON path and choosing **EXPORT**; **IMPORT**
validates the file. Match atlas exports include requested settings/seed,
resolved retry seed, generator version, starts and deposits. Biome percentages
describe achieved dry-land climate coverage. Ocean/lake coverage, forest
density and water/road length diagnostics describe actual generated outcomes.

The camera uses orthographic projection at 55 degrees. WASD and the arrow keys
move the camera; the wheel changes its size and Q/E rotate. WASD never arms attack
orders or stops units. Right-click orders remain contextual. Scrolling crosses
both world seams. **Render scale** in
graphics settings is independent of zoom, applies to a runtime HDRP asset copy
and retains its saved preference. Terrain texture-scale preferences are
preserved; sampling chooses the nearest integer number of tiles per world.

For smog tuning, stop Play and open **Massive Army → Art → Fog Look Dev**.
Enable the Scene view preview, adjust the shared `BattleVisualSettings` asset,
and inspect the moving friendly test unit and diagnostic height slice. Disable
or close the window to release its GPU resources. Entering Play also disables
the preview. Terrain Look Dev and Fog Look Dev are mutually exclusive. Terrain
Look Dev supplies temporary editor-only daylight/exposure and disables fog;
closing it restores the Scene view lighting toggle and removes those resources.

## Implementation

`WorldTopology.cs` and `WorldTopology.hlsl` share canonical positions, modular
cells, shortest displacement and nearest render-image rules. An exact
half-world displacement always chooses the negative image. Gameplay stores
each entity once; selection, resource spending and damage use the original ID.
Navigation, collision, weapons/projectiles, intelligence, transport, placement,
economy and AI queries use the same topology.

Generator 4 creates periodic continents/island clusters and coherent ridges,
then wrapped talus erosion, priority-flood catchments, stream incision, lakes,
rivers, climate, vegetation, roads and material masks. Edges are neighbours,
never artificial drainage outlets. Every traversable island retains its
component label. Safe starts require wrapped separation and paired accessible
deposits. Separated islands are checked against the actual catalogue's
transport, producer, research, material, power and building-space dependencies.
There are at most eight deterministic generation attempts.

Terrain patches and inland water have a bounded 3×3 set of images culled by the
camera. Units, scenery and buildings render their nearest image without adding
simulation copies. Maximum camera footprint keeps that image choice sufficient.
GPU interpolation uses the shortest previous/current displacement. Ocean waves
use the world-aligned periodic HDRP primary band: HDRP's secondary swell ratio
and fixed 10 m ripple period cannot close over the supported widths, so those
bands are disabled. Inland-water detail normals close independently.

Smog is a half-resolution scene-depth custom pass, including correct
orthographic ray/depth reconstruction. There is no raised concealment mesh.
Depth-aware reconstruction composites into the scene. Concealment comes from
authoritative explored/visible masks and separate darkness controls; density
holes and vortex motion cannot reveal hidden ground or enemy units.

The default look is now shallow **12 m ground mist**, concentrated near the
surface, with slow ambient drift. Motion from passing units is stronger than
the background motion: they locally disperse density, leave short-lived clear
channels and curl the edges. A faint **Visible mist density** keeps these wakes
readable around units inside their own vision disc. **Mist clear seconds**
controls visual dispersal when an area becomes visible. Both are cosmetic;
neither delays gameplay reconnaissance nor reveals concealed terrain/enemies.

A cached tileable 64³ texture stores scalar 3D density noise and a curl vector
field. Vertically varying density and irregular thickness use 1–3 octaves.
Projected unit height smoothly transitions 16/24/32 ray steps at approximately
4/12 pixels. A 96×48×96 camera-local wake field updates at 15 Hz, interpolates
between updates, retains overlapping data during scrolling/zoom, and fades
at its boundary. Eligible GPU units inject paired vortices and trailing
turbulence. Only friendlies/currently visible enemies contribute. Wakes advect
and decay without a pressure solver. Strategic views fade out wake detail.
Additional wake/cache/aggregate resources total **15.5 MiB**, below 64 MiB.

## Validation and performance acceptance

Run **Massive Army → Validate Wrapping Worlds** and **Validate GPU World
Wrapping**. The latter uses a fixed 64-slot fixture and bounded readbacks;
there is no CPU mirror of a live army. JSON reports live in `TestResults`.
`WorldBuild.RunValidated` builds a Windows DX11 player after CPU validation in
an isolated project. `Tools/validate_shaders.py` also checks compute kernels
and the custom graphics stages using FXC/DXC.

The standalone harness is opt-in:

```
SyntheticFront.exe -army-benchmark 50000 -benchmark-world-size 4096 
  -benchmark-scenario base-close -benchmark-output <absolute-directory>
```

Scenarios: `overview`, `close`, `seam`, `dense`, `base`, `base-close`,
`base-seam`. `dense` creates an opposing GPU combat fixture; it is deliberately
distinct from a normal generation/deployment benchmark. Use an explicit larger
world for the million-unit test. The harness renders a 2560×1440 HDRP target
and completes a one-pixel GPU readback every frame, so an occluded window cannot
produce false rendered-frame counts. This fence is diagnostic overhead and
makes throughput results conservative. It saves a rendered PNG, CSV, settings
JSON and tracked-memory report after five seconds of warm-up and twenty seconds
of measurement. Development builds support `-benchmark-gpu-profile`; unavailable
GPU counters are reported as unavailable, never as zero cost.

`Tools/summarize_benchmarks.py <report-directory>` computes median/p95 frame
times, simulation frequency, fog counters and tracked memory. Unity allocation
is not total dedicated VRAM. Earlier minimized-window baseline captures did
not render and are **invalid** for frame-rate acceptance or regression claims.
An equivalent rendered baseline is required to accept the ≤5% simulation
regression limit. Neither compilation nor functional checks certify 60 FPS.
Fresh results and remaining acceptance items are recorded in
[WRAPPING_WORLDS_ACCEPTANCE.md](WRAPPING_WORLDS_ACCEPTANCE.md).

Targets remain ≤16.67 ms p95 frame time at 1440p on RTX 3070 and ≤3 ms p95 total
fog GPU cost, including dense combat, base matches, close views and crossings.
If measured costs exceed these targets, reduce cosmetic work first. Never
silently change gameplay correctness, army scale or the target. `AGENTS.md`
requires future performance conflicts to be raised with their cost and an
explicit human decision.

Next playable slices: command responsiveness/pathing, combat readability,
AI competence/counterplay, then distinctive factions and replayable scenarios.
This update does not select a multiplayer architecture.
