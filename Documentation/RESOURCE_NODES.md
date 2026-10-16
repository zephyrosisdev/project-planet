# Resource node decals

Seven transparent source PNGs generated with the built-in image generation tool. The Unity importer uses 512×512 BC7 textures with alpha, mipmaps, clamped edges and no CPU readback copy.

Saved artwork: `Assets/MassiveArmy/Resources/MassiveArmy/ResourceDecals/{Metal,RichMetal,Oil,Gas,RareEarth,Uranium,Salvage}.png`.

Nodes are snapped during generation to the 8 m building grid using their matching extractor footprint (2×2 oil/gas; 3×3 mines/rare/isotope). Placement safety, island access, start balance and spacing are checked after snapping. Exported worlds identify resource layout version 2; terrain generator version is unchanged.

Decals blend into the existing lit terrain pass. A static R16 grid maps terrain cells to a bounded resource record; each matching pixel samples one texture-array slice. There is no deposit loop per pixel and no per-node GameObject, renderer, shadow pass or decal projector. Camera frustum, explored fog knowledge and depletion control visibility each frame, including while paused. Wrapped camera images use the same canonical positions as the resource simulation.

GPU army storage and simulation are unchanged. The resource index uses 0.5 MiB at 4096 m and 8 MiB at 16384 m, plus approximately 2.33 MiB for the seven-slice 512 px BC7 array; source textures also remain loaded. These are allocation estimates; measured results are below.

## Validation and measured results — 13 October 2026

Grid validation passed 1,981 checks across seeds 73129, 14351 and 99881 at 2048, 4096 and 16384 m, including extractor centring at all rotations, dry-land access, node spacing, deterministic generation and wrapped coordinates. Runtime validation passed 48 checks, including camera culling while paused, explored-fog gating, depletion, absence of node GameObjects, live extractor alignment and actual HDRP render/readback with and without decals. PNG alpha and consistent BC7 import settings were verified.

Before and after were separately built Windows standalone players, Unity 6000.6.4f1, Direct3D11, RTX 3070, seed 73129, 2560×1440, identical camera transforms, VSync off, uncapped rendering, and unchanged army scale. The existing standalone harness renders an explicit HDRP target and waits for a GPU completion fence each frame, so hidden-window occlusion cannot count queued frames as completed renders. Each run warms up for 5 seconds then records 20 seconds. Frames are wall-clock frame intervals; simulation rate is tick delta divided by elapsed time. Memory is peak Unity tracked allocation/reservation, not measured dedicated VRAM or total process working set.

The base-close case requests 50,000 capacity with the existing 100,000 ambient-zombie setting, resulting in 150,000 slots and approximately 100,008 live entities. It is a resource-node presentation scenario, not a claim of 50,000 deployed player soldiers. Three repeats per version are aggregated as the median of each run's median, p95 and peak allocation. The stress case retains 1,000,000 active/live army units at 16384 m; one repeat per version, about 17,000–19,000 units in the camera view, with the rest still simulated.

| Equivalent scenario | Median frame ms, before → after | p95 frame ms, before → after | Simulation Hz, before → after | Peak allocated MiB, before → after | Peak reserved MiB, before → after |
|---|---:|---:|---:|---:|---:|
| Base close, three repeats | 7.417 → 7.151 | 8.951 → 9.123 | 30.00 → 30.00 | 512.63 → 508.59 | 798.91 → 798.91 |
| Million-unit overview | 7.537 → 6.713 | 26.705 → 26.375 | 30.00 → 29.99 | 464.31 → 472.56 | 546.84 → 546.84 |

Base-close aggregate p95 increased 1.92%, within the 5% regression limit; aggregate median decreased 3.59%. Individual allocation peaks vary with loading and collection, so the base allocation difference should not be interpreted as a guaranteed memory saving. Million-unit p95 remains above the 16.67 ms frame budget in both versions: this preserves that stress path but does not establish the full 60 FPS acceptance target. GPU marker timings were unavailable (zero) in these release players, so these runs do not establish separate army GPU timing or the 3 ms total-fog target. Fog shaders and army simulation kernels were not changed.

| Run | Median ms | p95 ms | Simulation Hz | Peak allocated MiB |
|---|---:|---:|---:|---:|
| Baseline base-close 1 | 6.884 | 9.122 | 30.00 | 509.00 |
| Baseline base-close 2 | 7.615 | 8.807 | 30.01 | 512.63 |
| Baseline base-close 3 | 7.417 | 8.951 | 30.00 | 512.84 |
| Decals base-close 1 | 7.069 | 8.923 | 30.00 | 520.95 |
| Decals base-close 2 | 7.597 | 9.303 | 30.00 | 504.64 |
| Decals base-close 3 | 7.151 | 9.123 | 30.00 | 508.59 |

Local raw logs, CSVs, settings, memory reports, frozen builds and render captures are under `TestResults/ResourceDecals/`. The repository's existing ignore rule excludes these generated files; this document retains the compact evidence and the Unity validation code remains tracked. No ignore rules were changed for this work.

Re-run geometry through **Massive Army → Validate Resource Node Grid**, and runtime rendering through **Massive Army → Validate Resource Decal Rendering**. Frozen comparison builds are created by the editor request actions `node-build-baseline` (run against the pre-change source) and `node-build-current`. The measurement command is:

```powershell
SyntheticFront.exe -army-benchmark 50000 -benchmark-scenario base-close -benchmark-world-size 4096 -ambient-zombies 100000 -benchmark-output <folder> -screen-width 2560 -screen-height 1440 -screen-fullscreen 0 -force-d3d11 -logFile <log>
```

For the stress case use `-army-benchmark 1000000 -benchmark-scenario overview -benchmark-world-size 16384`, retaining the other settings. Frozen players and benchmark captures stay local; source PNG artwork is a project asset in the repository.

## Generation prompt set

Tool: built-in image_gen, transparent_background=true, no input references. Each image used the exact template below, substituting its subject from the table.

Use case: stylized-concept. Asset type: transparent ground decal texture for a realistic top-down science-fiction RTS game. Primary request: a single resource deposit consisting of {subject}. Strict orthographic overhead view, looking vertically down at a flat ground patch, no horizon or perspective. Naturalistic game texture, readable broad shapes with restrained fine detail, diffuse neutral lighting baked lightly into texture. Deposit occupies central 75% of a square canvas. Irregular organic edges taper into genuinely transparent alpha with at least 10% completely empty transparent padding on all four sides. Only the deposit and its small supporting dirt patch; no surrounding landscape. No symbols, text, labels, UI, borders, people, buildings, rings or cylinders. This texture will lie flat on terrain at 16-24 metres wide.

| PNG | Subject |
|---|---|
| Metal.png | dark fractured iron ore, rusty brown veins and small metallic grey stones |
| RichMetal.png | dense exposed metallic iron ore, silver-grey mineral faces and warm copper-gold seams |
| Oil.png | an irregular glossy black crude-oil seep in cracked dark earth, subtle amber edges |
| Gas.png | pale sulphurous mineral crust, dark porous rocks and faint turquoise mineral stains around a natural gas vent; no smoke |
| RareEarth.png | violet and turquoise rare-earth mineral shards embedded in muted dark stone |
| Uranium.png | yellow-green uranium mineral crust and small grey stones; realistic mineral colour, no neon glow |
| Salvage.png | a low scattered patch of worn steel plates, broken machine parts, copper wiring and rusted scrap; no intact vehicles |

