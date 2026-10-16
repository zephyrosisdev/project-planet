# Grid construction and owned gates

Implemented construction grid: **8 metres per cell**. Foundation dimensions are catalogue data (`footprintWidth`, `footprintDepth`); `RtsBuilding.rotation` stores quarter turns. All levels reserve the same foundation.

## Controls

- Left-click places a snapped foundation. Shift retains placement for repetition.
- The mouse wheel rotates the active preview in 90 degree steps. Wheel input over UI scrolls UI; it cannot rotate placement or zoom the camera. Fractional wheel input accumulates.
- Drag walls to preview a cardinal route, its new segment count and total cost. Existing friendly walls are reused without charge. The route is committed atomically.
- Gates occupy 3x1 cells. Hover over three completed, straight, owned wall cells to align automatically and replace them. Corners, junctions, incomplete walls, enemy walls and mixed empty/wall spans are rejected. Replacement charges the gate cost less the existing 45 percent completed-wall salvage.
- Right-click or Escape cancels placement. Releasing a wall drag over UI cancels the drag.

## Construction and model contract

`BuildingGrid` uses a fixed world X/Z basis, integer cell anchors, centres derived from the whole footprint, and the existing wrapping topology. Even dimensions have different centre parity from odd dimensions. Player construction, AI construction and starting buildings use the same snap rules. Resource extractors cover an eligible deposit without moving the deposit.

Model origin: foundation centre at Y=0. Local +X is width; local +Z is depth and the front. The mesh's ground silhouette must fit the reserved footprint; upgrades cannot enlarge it. Current placeholder buildings have been resized to these foundations and their picking bounds now follow their oriented footprint. Production exits follow building rotation; naval launching retains the existing coastline search.

| ID | Building | Cells X/Z | Metres X/Z |
|---|---|---:|---:|
| `command` | Command Centre | 5x5 | 40x40 |
| `outpost` | Forward Outpost | 2x2 | 16x16 |
| `barracks` | Barracks | 3x2 | 24x16 |
| `light_depot` | Light Vehicle Depot | 3x3 | 24x24 |
| `factory` | Vehicle Factory | 4x4 | 32x32 |
| `ordnance` | Ordnance Factory | 4x3 | 32x24 |
| `helipad` | Helipad | 3x3 | 24x24 |
| `airbase` | Airbase | 6x4 | 48x32 |
| `naval_yard` | Naval Yard | 5x4 | 40x32 |
| `lab` | Research Lab | 3x3 | 24x24 |
| `advanced_lab` | Advanced Research Centre | 4x4 | 32x32 |
| `strategic_lab` | Strategic Research Complex | 5x4 | 40x32 |
| `logistics` | Logistics Centre | 3x3 | 24x24 |
| `repair_depot` | Repair Depot | 3x3 | 24x24 |
| `field_command` | Field Command Centre | 3x2 | 24x16 |
| `radar_station` | Radar Station | 2x2 | 16x16 |
| `phased_radar` | Phased Array Radar | 3x2 | 24x16 |
| `horizon_radar` | Over The Horizon Radar | 4x2 | 32x16 |
| `counter_radar` | Counter Battery Radar | 2x2 | 16x16 |
| `mine` | Mine | 3x3 | 24x24 |
| `oil` | Oil Derrick | 2x2 | 16x16 |
| `gas` | Gas Extractor | 2x2 | 16x16 |
| `rare` | Rare Earth Processor | 3x3 | 24x24 |
| `isotope` | Isotope Extractor | 3x3 | 24x24 |
| `synthetic` | Synthetic Fuel Plant | 3x3 | 24x24 |
| `material_storage` | Material Depot | 3x2 | 24x16 |
| `fuel_storage` | Fuel Depot | 2x2 | 16x16 |
| `rare_storage` | Rare Materials Vault | 2x2 | 16x16 |
| `isotope_storage` | Strategic Materials Vault | 2x2 | 16x16 |
| `wind` | Wind Turbine | 2x2 | 16x16 |
| `solar` | Solar Array | 4x2 | 32x16 |
| `gas_turbine` | Gas Turbine | 3x2 | 24x16 |
| `reactor` | Advanced Reactor | 4x4 | 32x32 |
| `fusion` | Fusion Reactor | 5x5 | 40x40 |
| `exotic` | Exotic Reactor | 6x6 | 48x48 |
| `battery` | Battery Bank | 2x1 | 16x8 |
| `wall` | Wall | 1x1 | 8x8 |
| `gate` | Gate | 3x1 | 24x8 |
| `autocannon` | Autocannon Turret | 1x1 | 8x8 |
| `at_turret` | Anti Tank Turret | 1x1 | 8x8 |
| `aa_turret` | AA Turret | 1x1 | 8x8 |
| `sam` | SAM Site | 2x1 | 16x8 |
| `advanced_sam` | Advanced SAM Site | 2x2 | 16x16 |
| `point_defence` | Point Defence | 1x1 | 8x8 |
| `artillery_battery` | Artillery Battery | 3x2 | 24x16 |
| `missile_defence` | Missile Defence | 2x2 | 16x16 |
| `laser` | Laser Turret | 1x1 | 8x8 |
| `shield` | Shield Generator | 3x3 | 24x24 |
| `silo` | Strategic Missile Silo | 4x4 | 32x32 |

## Navigation boundaries

Army ownership remains on the GPU. There is no CPU army mirror and no per-unit GameObject. The flow solver, steering algorithm, navigation resolution and million-unit stress path are retained.

The CPU A* search belongs only to the construction preview. It searches four neighbours, follows wrapping seams, limits routes to 128 cells, caps expansion at 16,384 cells and restricts the detour corridor. It refreshes at most 10 Hz when stationary and immediately when its endpoints or structure occupancy change. Preview objects are reused.

Base-building battles have two sets of the original five movement-cost layers, so group flow reads the moving team's traversal costs. Completed, powered gates reserve an owner portal through coarse wall cells. The owner can route toward a gate before its leaf animates open; friendly proximity opens the leaf, including when enemies are nearby. Enemy costs remain blocked. An exact 8 m structure collision mask prevents enemies following friendlies through the opening and prevents units cutting through residual walls in a coarse portal cell. Power loss blocks both teams. Destroying/decommissioning releases the foundation and passage.

Ordinary terrain/navigation cells remain 16 or 32 metres; construction snapping does not promise that every 8 m aisle is navigable. Coarse clearance around ordinary buildings remains conservative. Gate passage is explicitly validated on both world sizes. Aircraft retain their original structure-independent movement.

The extra obstacle buffers are allocated only for base-building mode. Estimated additional GPU buffers are 2.25 MiB at 4,096 m and 21 MiB at 16,384 m (second movement-cost set plus exact construction mask). These are allocation budgets, not measured total VRAM. The stress-mode variant excludes the additional collision checks.

## Validation and reproduction

- `Massive Army > Validate Grid Placement and Gates`: all footprints/rotations, wrapping, A* obstacles/limits, wall charging/reuse, gate alignment/replacement, ownership/power/removal, and GPU traversal predicates.
- `Massive Army > Validate GPU Movement Through Owned Gates`: bounded GPU fixtures exercise the existing flow fields and movement through horizontal/vertical gates on both 4,096 m and 16,384 m worlds. Test-only readback samples two units; normal gameplay never mirrors units.
- `Massive Army > Validate RTS Foundation`: production, power, resources, upgrades, research, AI economy, naval/air spawning and large-world startup regressions.
- Standalone `-grid-validation -benchmark-world-size 4096 -benchmark-output <folder>` runs construction and placement-input checks and captures `placement.png`; repeat with 16384. This explicit HDRP render request captures world placement geometry, not presented screen-space UI.
- `TestResults/run_grid_benchmarks.ps1 -Build <exe> -Label baseline|current` records three equivalent off-screen, GPU-fenced 1440p runs per 50K/1M stress scenario and 50K-capacity base close view. Base capacity is not its actual live army count. These timings do not include presented screen-space UI.

The 60 FPS target, <=5 percent army-simulation regression limit and <=3 ms p95 total-fog target remain unchanged. Missing profiler timings are reported as unavailable rather than zero or a passing result.

## Recorded results (11 October 2026)

Unity 6000.6.4f1, Windows 11, Direct3D 11, RTX 3070:

- Editor placement/GPU predicate validation: **990 checks passed** (`TestResults/GridPlacement/editor-validation.json`).
- Existing GPU flow and movement through horizontal/vertical owner gates, both world sizes: **64 checks passed** (`gpu-movement.json`). Opposing units cannot enter the gate footprint; going around the wall remains legal.
- Windows standalone placement validation: **837 checks passed per world size** (`player-4096/validation.json`, `player-16384/validation.json`). Covers actual UI geometry, fractional/multiple/reverse wheel rotation, focus/UI blocking, unchanged camera zoom, wall replacement and gate power state. Both 2560x1440 world captures were inspected.
- Existing RTS foundation regression suite and UI/input validation passed; UI suite recorded **185 checks**. The standalone Windows build succeeded.
- Offline shader compilation passed 110 default stages and 108 building/swarm variants. `Tools/verify_stress_shaders.py` additionally confirmed **45 non-building Battle/Navigation kernels have identical stripped cs_5_0 bytecode to git HEAD** (`stress-shaders/comparison.json`). This does not replace timing measurements.

The captures use the existing placeholder models resized to their foundations. This work establishes the dimensions for the subsequent 3D art pass; it does not produce that artwork. Automated checks do not replace a full manual playthrough of long walls, irregular terrain and congested vehicle traffic.

### Standalone performance comparison

Three runs per build/scenario, seed 73129, 2560x1440 explicit off-screen GPU-fenced rendering. Values below are the median across the three runs; memory columns give the median of each run's measured peak. Raw CSV/metadata/memory files live under `TestResults/GridPlacement/baseline` and `current`; `comparison.json` records equivalence checks. These runs exclude presented screen-space UI.

| Scenario / live units | Median frame ms before → after | p95 frame ms before → after | p95 frame change | Simulation Hz after | Allocated MiB before → after | Reserved MiB before → after |
|---|---:|---:|---:|---:|---:|---:|
| Stress overview / 50,000 | 8.681 → 8.889 | 10.483 → 10.437 | −0.43% | 30.002 | 330.74 → 332.49 | 800.16 → 800.16 |
| Stress overview / 1,000,000 | 25.896 → 26.423 | 27.896 → 28.290 | +1.41% | 29.996 | 333.10 → 334.61 | 808.16 → 808.16 |
| Base close view / 8 (50K capacity) | 7.821 → 8.269 | 8.933 → 9.434 | +5.61% | 30.000 | 333.89 → 335.58 | 800.16 → 800.16 |

Stress runs have identical recorded settings, seed and camera poses. The base close-view camera shifted by 4 m in X/Z with the snapped starting foundation; it is **not strictly equivalent** and cannot establish an army-simulation regression. Its measured overall frame change is disclosed, not accepted as a passing comparison.

The 50K stress and base close-view runs meet the 16.67 ms p95 frame target. The million-unit runs **miss the 60 FPS target before and after**, with the army scale preserved. GPU simulation scopes and GPU fog scopes were unavailable (all-zero profiler output); total fog timing, presented-player performance and the <=5% simulation-cost acceptance test therefore remain **unverified**. The observed frame changes and identical stress kernel bytecode are supporting evidence, not a substitute for those timings. Unity tracked allocated/reserved memory above excludes total dedicated VRAM and must not be confused with the estimated additional GPU buffers.

Recreate summaries with `python Tools/summarize_benchmarks.py TestResults/GridPlacement/baseline`, repeat for `current`, then `python Tools/compare_grid_benchmarks.py`.

### Placement bounds regression fix

The subsequent live play session logged 127 repeated `Invalid worldAABB` messages. The initial construction-grid centre used `float.MaxValue`; wrapping arithmetic could collapse the distance to zero, skip mesh initialization and leave the renderer at an extreme coordinate. Grid initialization now uses an explicit flag, vertices are relative to the grid centre, and the renderer is positioned at its nearest wrapped image. Army/scenery simulation is unchanged.

The live Editor compiles cleanly. Fresh Windows fixtures passed **844 checks on each world size**, including first grid creation, repeated updates, cancellation/recreation, finite renderer bounds and absence of logged runtime errors (`TestResults/GridPlacement/bounds-fix-4096/validation.json` and `bounds-fix-16384/validation.json`). The 4,096 m capture confirms the construction grid is visible.
