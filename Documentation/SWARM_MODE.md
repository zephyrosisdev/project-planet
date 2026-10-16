# Swarm survival

Select **SWARM SURVIVAL** on the main menu, or enable it in **World Setup and Preview** before starting the validated world. Army capacity and world size stay independent.

You start with your command centre, an engineer, a scout and two infantry units. There is no enemy base or competing enemy economy. Build production, resource extraction and defences during the preparation period.

- After ten minutes of active gameplay, a warning begins a thirty-second countdown.
- Wave one contains 1,000 randomly mixed land combat units. Subsequent waves double: 2,000, 4,000, 8,000, and so on.
- Clear every attacker and pending reinforcement to survive a wave. Clearing it starts the next ten-minute preparation period and thirty-second warning. Waves do not overlap.
- Pause freezes preparation, countdowns, spawning and gameplay. Losing your last command centre ends the match.
- The wave panel reports the countdown, live attackers and unspawned reinforcements.

There are up to eight entry zones around the canonical map perimeter. On a wrapping world, an entry is still the same simulation identity when viewed across an edge. Sites remain distant from the initial player base. Ocean maps use reachable sites on the player's island when canonical edges cannot provide land entries. Worlds without at least two sufficiently distant entries are rejected with an explanation.

Attackers navigate to the nearest reachable player structure by terrain-weighted travel cost, using wrapped neighbours and shortest seam crossings. Building destruction and changes to building obstacles rebuild their shared routes. Swarms contain ground units that can damage buildings; aircraft, ships and strategic missile units requiring their own infrastructure are excluded. Swarm attackers arrive supplied and replenish their own ammunition and fuel; player logistics remain governed by the existing supply setting.

## Capacity and performance

Spawns use GPU free slots and one compact procedural command per batch of at most 4,096 units. CPU state consists of the wave clock, counters, a bounded roster and entry positions; it contains no attacker mirror or per-unit GameObjects. A wave exceeding available army capacity sends its remaining reinforcements as previous attackers die. No attackers are silently discarded. Between eight and 256 slots, depending on capacity, remain reserved for player production. Exponential counts use 64-bit accounting and stop explicitly before overflow.

Three shared integration fields cover infantry, light vehicles and heavy vehicles without consuming player formation groups. Route seeding runs locally around each building. Convergence work stops once routes settle. Routing refreshes after changes to target buildings or terrain obstacles; spawning waits for a complete reachable field. Compact live/spawn reductions are read asynchronously. Failed GPU acknowledgements pause the match rather than risking duplicate attackers.

Additional fixed GPU buffers use approximately **3.8 MiB at 256 navigation resolution**, or **15.1 MiB at 512**. Ordinary RTS and stress movement compile without the Swarm shader variant. The million-unit stress architecture and capacity selector remain available.

## Validation

Use **Massive Army → Validate Swarm Survival** outside Play mode. Native checks cover preparation and warning timing, pause behaviour, exact GPU spawn counts, random composition, wave doubling, counter overflow, island entries, rejected entry constraints, capacity streaming, slot reuse, unreachable-type rerolls, seam navigation, blocked terrain, building damage and retargeting after destruction. The fixture reads only bounded samples and compact GPU reductions.

Standalone profiling supports `-benchmark-scenario swarm-close` and `swarm-seam`. Those opt-in scenarios skip the preparation period and give the fixture command centre extra health so a defeat does not pause profiling. Normal gameplay retains the full preparation period and normal structure health.

## Standalone measurements, 10 October 2026

RTX 3070, DX11 development players, 2560 × 1440, full render scale. Each capture warms for five seconds and records twenty seconds. An explicit render fence completes each frame. Stress combat remains enabled; requested deployment counts are not reduced. The million-unit scenario uses an 8,192-metre world; the fit check correctly rejected one million units on the default 4,096-metre world.

| Capture | Median ms | p95 ms | Simulation Hz | Peak allocated / reserved MiB |
|---|---:|---:|---:|---:|
| Before · Base close, 50K capacity / 8 live | 7.53 | 8.37 | 30.05 | 255.9 / 543.8 |
| Before · Stress 1M | 23.27 | 25.45 | 30.00 | 254.4 / 543.8 |
| Before · Stress 250K | 8.75 | 14.05 | 30.01 | 252.7 / 543.8 |
| Before · Stress 50K | 8.65 | 10.76 | 29.99 | 251.5 / 543.8 |
| After · Base close, 50K capacity / 8 live | 7.51 | 8.89 | 30.00 | 256.2 / 543.8 |
| After · Base close repeat, 50K capacity / 8 live | 8.21 | 9.79 | 30.05 | 254.7 / 543.8 |
| After · Stress 1M | 10.51 | 24.17 | 29.99 | 256.2 / 543.8 |
| After · Stress 250K | 8.46 | 13.58 | 30.03 | 254.8 / 543.8 |
| After · Stress 50K | 7.82 | 9.56 | 30.00 | 254.8 / 543.8 |
| After · Swarm close, 50K capacity / 1,004 live | 7.76 | 9.10 | 30.00 | 257.0 / 543.8 |
| After · Swarm seam, 50K capacity / 1,004 live | 8.21 | 10.04 | 29.96 | 256.7 / 543.8 |
| After · Final Swarm close, 50K capacity / 1,004 live | 8.06 | 9.75 | 30.00 | 255.5 / 543.8 |

Memory is Unity-tracked allocation and reservation, **not measured peak dedicated VRAM**. See `TestResults/swarm-benchmark-summary.json`, `TestResults/SwarmBaseline` and `TestResults/SwarmCurrent` for captures, configuration and raw CSV data.

The first-wave Swarm captures and 50K/250K stress captures meet the 16.67 ms p95 frame target. **The million-unit target remains failed:** p95 is 24.17 ms after this update, versus 25.45 ms before. No army scale or target was reduced. The base close captures include p95 variation; all captures, including the repeat, are reported rather than selecting a single favourable result.

GPU fog and simulation marker timings are unavailable in this offscreen harness. Consequently the **3 ms p95 fog budget and strict 5% army GPU simulation regression budget remain unverified**. Offline DX11 compilation of normal-mode `MoveUnits`, `AcquireTargets`, `FireWeapons` and `ApplyDamage` produces byte-for-byte identical binaries before and after the change; hashes are in `TestResults/CompileCheck/swarm-kernel-comparison/comparison.json`. This preserves the existing army kernels but does not replace measured GPU timings.
