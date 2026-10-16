# Wrapping worlds and ground mist: validation record

Recorded 10 October 2026. Functional implementation is available; full performance
acceptance remains open. The 60 FPS / RTX 3070 / million-unit target is unchanged.

## Ground mist revision

Default depth is 12 metres with terrain-relative vertical density and irregular
thickness. Ambient wind is (0.25, 0.12) m/s and curl distortion is 0.65 metres.
Moving eligible units disperse local density and inject paired vortices into a
96×48×96 persistent GPU field. Wakes fade over six seconds. Newly visible mist
disperses over 0.9 seconds; faint scenic mist remains around visible units so the
interaction is observable inside their vision radius. Concealment and enemy
visibility remain authoritative and independent of the cosmetic density.

Use **Massive Army → Art → Fog Look Dev** in Edit mode for a moving test unit.
The diagnostic slice can show either the 3D noise cache or the wake volume.
Close/disable the preview to release its resources. Settings are in
`Assets/MassiveArmy/Resources/MassiveArmy/BattleVisualSettings.asset`.

## Completed checks

- Native Unity world validation: PASS. Includes periodic height/water/material
  edges, deterministic preset worlds and starts, island components, balanced
  resources and catalogue-backed transport progression.
- Native GPU wrapping validation: PASS. Includes seam/corner movement, contact,
  targeting, projectiles, reconnaissance, original entity/draw identities,
  hidden-enemy wake rejection and persistent friendly vortex wakes.
- Native RTS foundations and combined-arms validation: PASS. Economy, production,
  aircraft, transport, defence, formation and gunfire framebuffer checks.
- FXC/DXC validation: 100 shader stages, zero failures. Native Unity player build
  also succeeded. Wake/cache/aggregate allocations are 15.5 MiB, below 64 MiB.
- Managed world/stress tests: 705 world assertions plus all four supported sizes,
  canonical/unique deployment slots, invalid inputs and cancellation. Default
  8,192/16,384 m worlds retain the million-unit deployment path.

Reports are under `TestResults`. The gunfire reference test now disables background
renderers and advances the editor frame between captures: terrain appearing during
imports previously changed the reference background and invalidated subtraction.
This test adjustment does not change combat or rendering in the game.

## Standalone measurements

Unity 6000.6.4f1, Windows 11, DX11, RTX 3070, 2560×1440, render scale 100%.
Development player, five-second warm-up and twenty-second sample. Each frame
renders an explicit HDRP target and completes a one-pixel GPU readback. This
ensures an occluded window really renders, but adds diagnostic fence overhead.

Earlier development captures enabled the GPU profiler; the final ground-mist
captures did not. The stress overview and base rows below use the final build;
dense/close/seam stress rows retain the earlier profiling captures. Do not compare
the two series as a controlled optimization delta.
Base scenarios have 50,000 allocated slots and **eight live starting units**.
Stress counts decrease through combat; requested scale is retained.
The stress mode does not create the fog pass; it does not measure the fog budget.

| Scenario | Requested slots | Median ms | p95 ms | Simulation Hz | Peak Unity allocated / reserved MiB |
|---|---:|---:|---:|---:|---:|
| Final stress overview | 50,000 | 8.14 | 10.52 | 30.00 | 271.9 / 544.1 |
| Final stress overview | 250,000 | 7.94 | 12.81 | 30.00 | 271.4 / 544.1 |
| Final stress overview | 1,000,000 | 8.23 | **23.47** | 30.00 | 275.6 / 552.1 |
| Dense fighting | 50,000 | 8.84 | 10.43 | 30.00 | 303.1 / 544.1 |
| Close stress view | 50,000 | 6.95 | 9.04 | 29.99 | 303.8 / 544.1 |
| Stress seam crossings | 50,000 | 8.37 | 10.72 | 29.99 | 304.3 / 544.1 |
| Final ground mist, close base | 50,000 capacity / 8 live | 7.51 | 8.81 | 30.00 | 273.0 / 544.1 |
| Final ground mist, base seams | 50,000 capacity / 8 live | 7.48 | 8.90 | 29.99 | 274.9 / 544.1 |

CSV, settings, screenshots and summaries are in
`TestResults/Benchmarks/current-development` and `TestResults/Benchmarks/ground-mist`.
Memory figures are Unity tracked memory, **not total dedicated VRAM**.

## Acceptance still open

- The million-unit capture exceeds 16.67 ms p95. It is a failed target measurement,
  not a passed 60 FPS build. Diagnose cosmetic cost first, then investigate the
  remaining simulation cost without reducing gameplay correctness or army scale.
- GPU profiler counters were unavailable in this offscreen harness. The 3 ms p95
  total fog budget is unverified; missing timings are not zero cost.
- Earlier baseline captures did not render and used different automatically sized
  worlds. They cannot establish the ≤5% simulation regression requirement. A
  valid equivalent rendered baseline is still required.
- A short base fixture is not a long match with dense construction and production.
  Longer matches, normal presentation profiling, peak VRAM and exhaustive manual
  camera/LOD/seam/transport interaction checks still need measurements.
- The final base screenshots verify depth-aligned shallow mist; moving-unit swirls
  are covered by GPU field checks and the editor preview. A visual review while
  commanding real units remains part of art acceptance.

The project performance policy is recorded in `AGENTS.md`. No lower target,
smaller army scale or full fluid pressure solver has been substituted.
