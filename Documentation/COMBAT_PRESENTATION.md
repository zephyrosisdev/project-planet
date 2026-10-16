# Combat rockets, bursts and ground remains

Strategic missiles now draw a pointed rocket mesh with four fins, oriented along their GPU flight path. Actual nuclear detonations leave scorch/crater artwork. Zombie, infantry, tank and other vehicle deaths leave their respective ground marks, and destroyed buildings leave rubble. Connected projectile tracers and shell impacts trigger sparks and a small explosion burst. Deaths and strategic detonations trigger coloured bursts through native Visual Effect Graph assets.

Ordinary remains are visual marks. Nuclear impacts now also deform the shared ground height field, meshes and terrain navigation costs; see [Combat surfaces](COMBAT_SURFACES.md) for the current implementation and its validation. Selling a building does not create rubble. Intercepted nuclear missiles do not create ground craters.

## Assets

All paths are relative to the repository root. Six generated transparent source images are saved in `Assets/MassiveArmy/Resources/MassiveArmy/CombatArt/`: `NukeCrater.png`, `ZombieSplatter.png`, `HumanRemains.png`, `TankWreck.png`, `VehicleWreck.png`, and `BuildingRubble.png`. The importer uses 512-pixel BC7 textures with alpha and mipmaps; source alpha is preserved. There is no runtime dependency on the image generator.

The same folder contains `Rocket.asset`, `CombatSparks.vfx` and `CombatBurst.vfx`, plus deterministic 64-pixel `CombatSparkTexture.asset` and `CombatDustTexture.asset`. The rocket is a shared mesh rendered from the existing 32-slot GPU missile buffer. Sparks are thin additive quads aligned with their velocity, with 32 particles per impact, randomized fast outward/upward motion and gravity. The former round secondary impact flash is removed. Destruction uses a 24-particle additive flame system and a separate 12-particle brown alpha dust system in the same VisualEffect component. Dust drifts upward/outward, slows through drag, expands and fades over 4.5 seconds; it uses a procedural soft noise texture rather than a bright default particle circle. Rebuild these native assets with **Massive Army → Art → Build Combat Rocket and VFX Graphs**. `CombatArtBuilder` uses the installed VFX Graph editor API through reflection; regeneration depends on that package API, while normal runtime playback uses serialized graph assets.

## Fixed budgets and visibility

Army positions, health, damage and death remain GPU authoritative. Presentation reads only a bounded event stream at up to 20 Hz, with one asynchronous request in flight: 128 impact events, 128 death events, 256 reserved strategic events, plus one header record (513 × 32 bytes). No full unit readback or per-unit GameObject is introduced. Actual deaths emit a minimal record once when their alive flag is cleared. A separate fixed 128-thread GPU pass resolves death artwork categories, size, ground height and exact camera visibility before readback; the army-wide damage kernel does not sample terrain or archetypes for cosmetics. Existing building destruction callbacks provide building effects.

Impact GPU events are rejected outside the camera or current visible fog before entering the stream. Death capture uses a conservative ground-view radius and visible fog first, then exact camera rejection in the bounded finalization pass. Strategic ground marks are retained for later discovery; bursts are started only in the camera and visible fog. All terrain marks are camera and fog culled on display. Moving the camera or changing the effects toggle reculls immediately; a stationary camera refreshes fog and lifetime visibility at 10 Hz, or sooner when events arrive. Idle emitters are deactivated and only live effects update their pause state and rebased position. Coordinates and render positions use the existing toroidal topology.

There are 512 detailed terrain marks: 64 reusable crater slots and 448 ordinary records. Crater history and its distant terrain texture persist for the whole battle; see [Nuclear presentation](NUCLEAR_PRESENTATION.md). Ordinary records overwrite their oldest mark when full. Lifetimes are 450 (7.5 minutes) for zombie splatter, 900 (15 minutes) for infantry remains, 1800 (30 minutes) for tank wrecks, 1500 (25 minutes) for other vehicle wrecks and 2400 (40 minutes) for rubble, with a final five-second fade. These are ten times the original lifetimes. Zombie and humanoid marks are twice their original width and depth; vehicle wreck images are now three times their original width and depth. Ordinary marks may still be replaced earlier when their fixed pool is reused. Crater slots are restored from permanent history when revisited. Pausing freezes the decal clock.

Marks blend in the existing terrain pass, using a two-layer 16 m spatial index and bounded sorted overlap lists rather than individual decal objects or draw calls. The map uses 0.5 MiB at 4096 m and 8 MiB at 16384 m, with separate fixed index-list/update buffers. Transparent pixels preserve underlying marks. Albedo and rotated normal maps now feed terrain lighting; empty image regions and opaque coverage reject unnecessary samples. See [Combat surfaces](COMBAT_SURFACES.md) for current budgets and measurements. The performance tables below record the earlier presentation/refinement versions.

Eight pooled VisualEffect components are fixed at startup: four spark emitters, two ordinary burst emitters and two reserved strategic emitters. Spark and ordinary flame systems each have a 128-particle capacity, ordinary dust systems have a 64-particle capacity, and each nuclear graph has a 98-particle capacity (1092 maximum across the pool). Actual emission is 32 sparks or 24 flame + 12 dust particles. The ordinary destruction emitter remains occupied until its dust fades; saturation drops excess ordinary bursts instead of creating more objects. Nuclear emitters reuse the oldest active cloud when necessary so new blasts retain their flash, shock front and covering dust. Empty crater/remains layers are skipped before terrain index sampling. The settings toggle **Combat effects and remains** controls both decals and these effects.

## Cursor nuke test

Press **N** over battlefield ground to detonate instantly at the terrain raycast under the cursor. This is a real 120 m-radius, 5000-damage GPU detonation using the same blast logic as missile impacts; it bypasses launch/flight/interception and can damage friendly units and buildings. It works while paused: GPU damage and crater creation occur immediately, and the test emitter plays on real time while the rest of the simulation and decal clock remain paused. HUD controls, menus, text fields and benchmark mode ignore the shortcut. The HUD lists `N: TEST NUKE`. No live missile slots or per-unit CPU data are overwritten.

## Validation

Run **Massive Army → Validate Combat Presentation**. The editor fixture kills four actual GPU units (zombie, infantry, tank, vehicle), expires a cosmetic projectile, detonates a strategic missile and verifies the emitted event categories. A separate full 32-missile volley checks reserved strategic event capacity. It checks outward mesh normals, real VFX particle spawning, all ground-mark categories, the fixed emitter count and saturation under 700 death events. A real shell hit verifies unchanged damage after switching projectile world/grid reads to SRV aliases, keeping the zombie variant within DirectX 11's eight-UAV limit. A screenshot is saved to `TestResults/CombatPresentation/combat-preview.png`; JSON results and VFX diagnostics are saved alongside it. These are generated local artifacts covered by the existing ignore rule.

The refinement validation passed 33 checks, captured 41 actual GPU events, observed 140 particles and kept the emitter pool fixed at eight. It also verifies doubled zombie/humanoid mark size, tenfold lifetimes, two destruction particle systems, real paused test-nuke damage, correct cursor position and HUD rejection. The warmed capture `sparks-dust-preview.png` hides the HUD to expose the flying sparks and dispersed plume.

`combat-shader-validate` exports the generated VFX shaders and checks that the intended nonzero spark/dust velocity ranges survive serialization. This caught a graph-builder issue: space-aware VFX vector slots use wrapper structs, and assigning a bare Vector3 silently serialized a zero range. The builder now converts these values to the slot's actual type. This fixes particle overlap that previously made effects look round and stationary. The original direct impact flash is also removed. The compiled-motion check passed after the fix.

## Standalone comparison procedure

Use the editor request actions `combat-build-baseline` before source changes and `combat-build-current` afterwards to freeze release Windows DX11 players. Raw comparison builds, player logs, frames, CSV telemetry and memory reports remain under the ignored local `TestResults/CombatPresentation/` folder. The benchmark explicitly renders an HDRP target at 2560×1440 and waits for a GPU readback fence, so hidden-window submission counts cannot masquerade as rendered frames. Each run warms up for five seconds and measures twenty seconds, with shadows disabled in both versions.

```powershell
SyntheticFront.exe -army-benchmark 50000 -benchmark-scenario base-close -benchmark-world-size 4096 -ambient-zombies 100000 -benchmark-output <folder> -screen-width 2560 -screen-height 1440 -screen-fullscreen 0 -force-d3d11 -logFile <log>
```

Use `-benchmark-scenario dense` for concentrated combat and `-army-benchmark 1000000 -benchmark-scenario overview -benchmark-world-size 16384` for the unchanged million-unit stress path. Current-player `*-combat.json` reports provide evidence that ordinary effects were actually exercised. Simulation rate is the measured tick delta divided by elapsed time. Memory reports are peak Unity tracked allocation/reservation, not dedicated VRAM or whole-process working set.

## Initial implementation comparison — 2026-10-13

RTX 3070, Unity 6000.6.4f1, Windows 11, release DX11 players, explicit 2560×1440 HDRP target. The final cohort interleaved frozen baseline and final players for each repeat. Both processes used Windows High priority to reduce scheduler interference. This is a controlled comparison setting, not a change to normal game process priority. Base-close and dense results aggregate three repeats as the median of the three run medians, p95 values, simulation rates and memory peaks. Overview uses one repeat per version.

The base-close request uses 50,000 capacity plus the existing 100,000 ambient zombies: 150,000 slots, approximately 100,008 live entities and four camera-visible units. Dense uses 50,000 deployed units with approximately 48,640 survivors and 15,690 visible at the end. Overview retains 1,000,000 active/live units at 16384 m, with approximately 17,060 in the camera view and the remaining army still simulated. Army scale, camera setup, quality, seeds and simulation options were equivalent between versions.

| Scenario | Median frame ms, before → after | p95 frame ms, before → after | Simulation Hz, before → after | Peak allocated MiB, before → after | Peak reserved MiB, before → after |
|---|---:|---:|---:|---:|---:|
| Base close, three pairs | 7.345 → 7.359 | 9.378 → 9.393 | 30.00 → 30.04 | 508.63 → 513.07 | 798.91 → 798.91 |
| Dense combat, three pairs | 6.488 → 6.744 | 7.598 → 7.332 | 29.99 → 30.00 | 458.97 → 463.48 | 538.84 → 538.84 |
| Million-unit overview | 7.750 → 7.217 | 25.482 → 26.318 | 30.00 → 30.00 | 472.94 → 465.32 | 802.91 → 546.84 |

The largest final aggregate median increase is dense combat at 3.95%; the largest aggregate p95 increase is overview at 3.28%. Both are within the 5% overall-frame proxy used conservatively for the regression gate. Release GPU marker readings were unavailable (zero), so these results do not isolate army GPU time or establish the ≤3 ms total-fog target. Fog shaders were not changed. Million-unit p95 exceeds 16.67 ms in both versions; the stress path is preserved, but the full 60 FPS acceptance target remains unproven. Allocation/reservation peaks vary with loading and collection; the overview memory difference is not a guaranteed saving.

Dense final runs read 9,988 / 10,011 / 10,001 valid GPU events, retained 448 ordinary marks and started 358 / 358 / 356 pooled bursts. They dropped 18,269 / 18,309 / 18,292 excess ordinary bursts. This demonstrates actual effects under saturation while holding the pools fixed. The inactive-effects base and overview runs captured no combat events.

| Run | Median ms | p95 ms | Simulation Hz | Peak allocated MiB | Peak reserved MiB |
|---|---:|---:|---:|---:|---:|
| Baseline base-close 1 | 7.345 | 9.383 | 30.002 | 512.97 | 798.91 |
| Baseline base-close 2 | 7.193 | 9.378 | 30.005 | 505.09 | 798.91 |
| Baseline base-close 3 | 7.427 | 9.274 | 30.002 | 508.63 | 800.91 |
| Final base-close 1 | 7.334 | 9.393 | 30.045 | 513.07 | 799.91 |
| Final base-close 2 | 7.610 | 8.926 | 29.998 | 513.51 | 798.91 |
| Final base-close 3 | 7.359 | 9.479 | 30.040 | 505.85 | 798.91 |
| Baseline dense 1 | 6.783 | 7.598 | 30.000 | 458.97 | 538.84 |
| Baseline dense 2 | 6.241 | 7.640 | 29.994 | 454.63 | 538.84 |
| Baseline dense 3 | 6.488 | 7.213 | 29.994 | 467.25 | 794.91 |
| Final dense 1 | 6.757 | 7.309 | 29.999 | 463.04 | 538.84 |
| Final dense 2 | 6.744 | 7.332 | 30.006 | 468.42 | 794.91 |
| Final dense 3 | 6.438 | 7.878 | 30.004 | 463.48 | 538.84 |

Individual pairs show more variation than the aggregates, including some differences above 5%; the aggregate is not a guarantee for every run or machine condition. Final raw evidence is under `TestResults/CombatPresentation/final-paired/`, including `summary.json`, images, telemetry, logs and settings for both versions. Earlier rejected iterations remain under `pre-opt/`, `pre-opt2/` and `pre-opt3/`; their dense medians exceeded the threshold against the corresponding earlier cohort. They motivated fewer emitters, cached visibility, empty-layer skipping and deferred death preparation. Background variability also affected the frozen baseline, which is why the final report uses fresh interleaved controls. The first invalid standalone attempt exceeded DX11's UAV limit and is excluded; all final logs were checked for skipped dispatches, exceptions and shader errors.

## Persistence, sparks, dust and cursor-test refinement comparison

The baseline is the frozen player from the completed initial implementation above, copied before these refinements. Final release DX11 players were interleaved on the same RTX 3070 at 2560×1440, with identical seeds, camera setup, quality, army scale, five-second warmup, twenty-second measurement, GPU completion fence and Windows High process priority. Dense uses three pairs; idle base and million-unit overview use one pair each. Memory is peak Unity tracked allocation/reservation, not dedicated VRAM. Raw settings, logs, CSVs, captures and `summary.json` are under the ignored `TestResults/CombatRefinement/paired/` folder.

| Scenario | Median frame ms, before → after | p95 frame ms, before → after | Simulation Hz, before → after | Peak allocated MiB, before → after | Peak reserved MiB, before → after |
|---|---:|---:|---:|---:|---:|
| Dense combat, median of three pairs | 6.205 → 6.409 | 7.425 → 7.220 | 30.00 → 30.00 | 459.54 → 463.85 | 538.84 → 538.84 |
| Idle base camera | 7.039 → 6.972 | 9.200 → 9.118 | 30.00 → 30.00 | 505.51 → 517.04 | 798.91 → 798.91 |
| Million-unit overview | 7.594 → 6.782 | 25.557 → 26.146 | 30.00 → 29.99 | 469.45 → 465.07 | 546.84 → 546.84 |

Dense aggregate median increased 3.29%; overview p95 increased 2.30%. Both are within the 5% aggregate overall-frame proxy; independent army GPU and total fog timings remain unavailable in these release players. Individual dense run medians were baseline 6.205 / 6.329 / 6.094 ms and final 6.409 / 6.338 / 6.452 ms; p95 values were baseline 7.425 / 7.221 / 7.515 ms and final 7.220 / 7.913 / 7.214 ms. Variation includes individual pair differences over 5%, so this does not promise an identical result in every run. Million-unit overview retains 1,000,000 live units, approximately 17,060 visible, and continues to miss the 16.67 ms p95 frame budget in both versions. Base retains 150,000 slots with 100,008 live entities; dense retains 50,000 deployed units with approximately 48,645 survivors. No army scale was reduced. Memory peak differences vary with loading/collection and should not be interpreted as precise permanent footprint changes.

All 33 functional checks, the compiled nonzero-velocity check and the standalone builds passed. Final logs contain no skipped compute dispatches, UAV-limit failures, exceptions or shader errors. The new debug nuke is opt-in via N and never runs in benchmark mode.

## Image generation prompts

Tool: built-in `image_gen`, `transparent_background=true`, no reference images. Each asset used this exact template with the subject below:

> Use case: stylized-concept. Asset type: transparent ground decal texture for a realistic overhead science-fiction RTS. Primary request: {subject}. Strict orthographic top-down camera looking vertically down. Naturalistic game asset, diffuse neutral lighting, broad readable forms with restrained fine detail. Single centred patch within a square canvas. Organic irregular perimeter fading to genuinely transparent alpha, at least 10% empty transparent padding on all sides. No surrounding landscape, horizon, perspective, text, labels, borders, UI, symbols or watermark. Designed to lie flat on lit terrain.

| PNG | Subject |
|---|---|
| NukeCrater.png | a broad circular impact crater with dark scorched earth, a depressed dusty centre, fractured rim and subtle radial blast scorch; no fire |
| ZombieSplatter.png | an irregular dark red zombie blood splatter with muted sickly green traces and small indistinct residue; no organs, limbs or identifiable body |
| HumanRemains.png | a small fallen futuristic armoured infantry soldier seen from directly above, dark grey armour and helmet, a modest dark red blood stain, restrained non-graphic battlefield remains; no exposed organs |
| TankWreck.png | a destroyed low futuristic tracked tank seen from directly above, blackened steel hull, broken tracks, displaced turret and scattered scrap on a small scorch stain |
| VehicleWreck.png | a destroyed futuristic wheeled military vehicle seen from directly above, blackened frame, broken wheels and scattered steel panels on a small scorch stain |
| BuildingRubble.png | a dense irregular patch of destroyed futuristic military building rubble seen from directly above, collapsed grey concrete slabs, twisted dark steel and small copper cables on a scorch stain; no intact building |
