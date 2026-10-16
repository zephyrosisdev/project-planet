# Measured army scale benchmarks

## Adaptive maps and safe deployment, 8 October 2026

The one-million-unit GPU initialization check verifies every spawn for terrain/hull clearance and minimum 5.2-unit centre spacing, plus exact spatial-grid membership. On seed 332385012 it uses a 10,432-unit map. CPU generation checks cover 10K, 50K, 100K, 250K, 500K and 1M, including three seeds at 1M; difficult seeds expand further rather than packing tanks. GPU movement checks include both mirrored concave-riverbank detours. See [GPU spawn report](../TestResults/million-spawn-validation.json), [deployment report](../TestResults/deployment-validation.json) and [movement report](../TestResults/gpu-validation.json).

This update changes map size, grid sizes and steering cost. Earlier frame-rate results below are historical measurements and do not establish current one-million-unit performance. Updated memory budgets include the additional GPU route-progress records and larger navigation/contact grids.

## Biome terrain and wider formations, 8 October 2026 (before adaptive maps)

A fresh 50K run with biome terrain, seven scenery batches, inland water, HDRP ocean and 5.2-unit formation spacing averaged **120.0 FPS**, p95 **9.38 ms**, and **30.04 simulation ticks/s** at 2560×1440 on the RTX 3070 / DX11 with a 120 FPS limit. Mean available GPU scopes totalled 3.17 ms. The conservative buffer/map/scenery budget is 27.32 MiB; HDRP resources, geometry and render targets remain additional.

This run uses the currently configured seed **332385012**, so it is not a direct same-map comparison with the earlier 73129 runs. Higher capacities have not been re-benchmarked after biome generation. See [CSV](../TestResults/army-50000-20261008-013227.csv), [settings JSON](../TestResults/army-50000-20261008-013227.json) and [summary](../TestResults/biome-benchmark-summary.json). Final validation passed 50 GPU checks and 44 landscape checks across three seeds; the latter verify all 20 biome categories, all four water kinds, dry roads with shoreline buffers, deterministic generation, normalized terrain layers, and scenery/strategic-site clearance.


## Movement and formation update, 8 October 2026 (before biome generation)

A fresh 50K run with full-resolution connected routing, predictive avoidance and three contact passes averaged **119.9 FPS**, p95 **9.76 ms**, and **30.00 simulation ticks/s**, at 2560x1440 with the same RTX 3070, DX11 and 120 FPS limit. Mean available GPU scopes totaled 2.93 ms. Active sample means: movement/avoidance 0.071 ms, contact grid 0.062 ms, contact resolution 0.288 ms. Route fields had converged during this recording; this does not measure command-time route rebuilding or large formation arrivals.

See `TestResults/army-50000-20261008-004803.csv` and its settings JSON, plus `movement-benchmark-summary.json`. These measurements predate the biome terrain and wider formation spacing. Higher capacities have not been re-benchmarked after these updates. The historical scale table below measures the earlier movement implementation.


> The scale measurements below predate the full-resolution navigation and local-contact update. They are historical baselines; use fresh recordings to assess the updated movement cost.

Measured 7 October 2026 in Unity 6000.6.4f1 Editor, HDRP 17.6.0, Windows 11, Direct3D11, NVIDIA RTX 3070 (8,018 MiB reported). These are observations on this machine, not a frame-rate guarantee.

Each fresh battle uses seed 73129, capacity equal to initial units, all simulation/rendering features enabled, 32,768 shell slots, a fixed overview at 2560×1440, 48° field of view, and a 120 FPS application limit. The camera is reset and input is suppressed during the benchmark. Each run warms for five seconds and records twenty seconds. Combat continues, so live counts decline. The JSON beside each CSV records settings, camera pose and hardware.

| Initial units | Mean FPS | p95 frame ms | Simulation ticks/s | Live first → last | Mean visible | GPU scope mean ms | Listed memory MiB |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 50,000 | 119.2 | 10.06 | 29.90 | 49,436 → 45,204 | 41,753 | 2.82 | 12.32 |
| 100,000 | 120.0 | 9.19 | 30.00 | 98,832 → 90,344 | 83,396 | 3.05 | 19.38 |
| 250,000 | 120.0 | 11.01 | 30.00 | 247,632 → 227,792 | 208,870 | 4.06 | 40.55 |
| 500,000 | 119.8 | 14.36 | 30.00 | 495,422 → 457,770 | 418,456 | 5.31 | 75.84 |
| 1,000,000 | 92.8 | 15.25 | 30.01 | 990,928 → 921,817 | 838,812 | 9.05 | 146.41 |

Mean FPS is recorded frames divided by summed frame duration, rather than the HUD’s smoothed reciprocal. Tick throughput is tick difference divided by recorded elapsed time; the target is 30 Hz. p95 is the 95th-percentile recorded frame duration.

GPU scope mean sums available named GPU samples per frame. It excludes uninstrumented work and is not a whole-frame hardware timer. `Army.Rendering` spans the HDRP camera, including terrain, ocean and post-processing. Raw `engine_gpu_ms` is preserved in the CSV but was sometimes smaller than the measured rendering scope on this driver; it is not used for these conclusions. Zero stage values indicate idle/unavailable samples. Stage means below include only positive samples, so fixed-tick stages are not comparable to per-render-frame totals.

| GPU stage, active-sample mean ms | 50K | 500K | 1M |
|---|---:|---:|---:|
| Army.SpatialGrid | 0.046 | 0.203 | 0.402 |
| Army.Navigation | 0.051 | 0.051 | 0.050 |
| Army.MovementAvoidance | 0.053 | 0.653 | 1.382 |
| Army.TargetAcquisition | 0.179 | 1.377 | 2.897 |
| Army.Combat | 0.012 | 0.158 | 0.312 |
| Army.Projectiles | 0.058 | 0.095 | 0.119 |
| Army.Statistics | 0.088 | 0.756 | 1.531 |
| Army.Visibility | 0.055 | 0.307 | 0.587 |
| Army.ForestVisibility | 0.009 | 0.009 | 0.011 |
| Army.Rendering | 2.655 | 4.358 | 6.725 |

The listed memory is the explicit unit/grid/navigation/projectile buffers and terrain/minimap textures, not measured total VRAM. Forest content, geometry, render targets, HDRP/ocean resources and driver/Editor overhead are additional; see [memory budget](GPU_MEMORY.md). No claim is made about peak total VRAM.

These short overview runs do not establish close-camera performance, long-session stability, every seed, or standalone-player performance. The architecture currently uses approximate coarse connectivity, bounded neighbour/target searches, 16 concurrent player flow fields, and no dead-slot compaction. See [architecture limitations](GPU_ARCHITECTURE.md).

The historical GPU validation passed all 32 recorded checks, covering exact prefix scan, all 50K spatial memberships and uniqueness, indirect arguments, selection/click picking, movement/stop, direct damage, real shells and death. See [validation JSON](../TestResults/gpu-validation.json).

## Raw exports

- 50,000: [CSV](../TestResults/army-50000-20261007-215111.csv), [settings JSON](../TestResults/army-50000-20261007-215111.json)
- 100,000: [CSV](../TestResults/army-100000-20261007-215138.csv), [settings JSON](../TestResults/army-100000-20261007-215138.json)
- 250,000: [CSV](../TestResults/army-250000-20261007-215205.csv), [settings JSON](../TestResults/army-250000-20261007-215205.json)
- 500,000: [CSV](../TestResults/army-500000-20261007-215231.csv), [settings JSON](../TestResults/army-500000-20261007-215231.json)
- 1,000,000: [CSV](../TestResults/army-1000000-20261007-215258.csv), [settings JSON](../TestResults/army-1000000-20261007-215258.json)

Earlier exports are retained as preliminary evidence. They predate the fixed-camera suite and/or working GPU stage samplers and should not be mixed into this comparison. Reproduce with the prepared scene in Play mode using `TestResults/run_scale_suite.ps1`; `summarize_benchmarks.py` computes this report from the five final CSV files.

## Crowd pressure and queue stability update (8 October 2026)

The historical performance exports above predate the new density-pressure field and the contact-grid rebuild at predicted positions. They do not measure the current movement cost. The crowd regression now compares 128 deliberately compressed mixed-team tanks with pressure disabled/enabled, followed by a closed-gate waiting queue. The latest report is [crowd-validation.json](../TestResults/crowd-validation.json); the minimum queue centre spacing with pressure enabled was 4.634 units and average four-tick displacement jitter was 0.0104 units (95th percentile per-tank average 0.0306, worst per-tank average 0.0917). These are controlled-fixture results, not a guarantee for every crowd density or terrain configuration. The separate passage tests continue to require all 64 opposing tanks to clear a corridor and an offset opening.

Current-code runtime smoke tests used the Editor at 2560x1440 on the RTX 3070, seed 1521318339, all gameplay switches and crowd pressure enabled, overview camera with far impostors and movement LOD. Each warmed for five seconds and recorded twenty seconds. The frame-rate target was 120. The 50K map side was 2,368; the 1M seed required 15,104. These scenes differ from the historical suite above.

| Units | Retained frames | Mean frame ms | Frames / elapsed second | Pressure mean ms over all frames | Pressure mean ms on active frames | Contact resolution mean ms over all frames |
|---:|---:|---:|---:|---:|---:|---:|
| 50,000 | 2399 | 8.34 | 119.9 | 0.053 | 0.212 | 0.211 |
| 1,000,000 | 391 | 51.24 | 19.5 | 0.416 | 1.281 | 20.370 |

GPU scopes report accumulated time in a rendered frame, which can contain multiple simulation ticks; an active-frame mean is not a per-tick timing. The HUD FPS smoothing can hide long frames, so the table uses the recorded frame durations. Contact resolution dominates the million-unit smoke test. These short runs establish that the new pipeline runs at both scales; they do not establish close-camera or dense-front performance, every seed, long-session stability, or a million-unit framerate guarantee.

- Current 50K: [CSV](../TestResults/army-50000-20261008-035129.csv), [settings](../TestResults/army-50000-20261008-035129.json)
- Current 1M: [CSV](../TestResults/army-1000000-20261008-035031.csv), [settings](../TestResults/army-1000000-20261008-035031.json)
- [Computed runtime summary](../TestResults/crowd-runtime-summary.json)
