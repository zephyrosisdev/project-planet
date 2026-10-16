# GPU memory budget

All figures use binary MiB (1,048,576 bytes). Capacity equals active units; dead units retain master slots. New maps scale with army size and grow further if needed for a difficult seed. Navigation is 256² at 50K/100K and 512² from 250K. Spatial and contact grids are 256² and 1024² for these configurations. There are 18 flow groups and 32,768 shell slots. The density-pressure grid is the next power of two of mapSide/8, clamped to 64-512, and uses 7 MiB at 512² for all listed scales.

| Allocation | Bytes |
|---|---:|
| Unit snapshots A + B | capacity × 128 |
| Damage + scattered indices + fine contact indices | capacity × 12 |
| Three visibility lists | capacity × 12 |
| Selection flags + ordinals | capacity × 8 |
| Best route potential + stalled time | capacity × 8 |
| Spatial/contact counts, offsets, cursors | (spatialCells + contactCells) × 12 |
| Recursive scan scratch | sum of 512-element block levels × 8 |
| Gameplay shells + visible shell indices | projectileCapacity × 52 |
| Five terrain traversal costs | navCells × 20 |
| Height/slope/forest/road RGBAFloat | navCells × 16 |
| Splat, minimap, biome, two surface maps | navCells × 20 |
| Water level/depth/kind RGBAFloat | navCells × 16 |
| Scenery instances + visibility, conservative bound | 36,000 × 36 + 7 × 20 |
| Full connected flow ping-pong | 18 × navCells × 8 |
| Coarse lookahead + convergence | 18 × 1,024 × 4 + 18 × 4 |
| Crowd mass, pressure pair, density/mask/gradient | crowdCells × 28 |
| Validated formation slots | activeUnits × 8 |
| Formation state + group occupancy | 41 × 4 |
| Group orders + preview | 19 × 64 + 4,096 × 16 + 20 |
| Statistics, draw arguments, visibility counts, pick, archetype | 512 + 80 + 20 + 4 + 64 |
| Units | Minimum map side | Navigation | One snapshot | Snapshot pair | Listed buffers + maps |
|---:|---:|---:|---:|---:|---:|
| 50,000 | 2,368 | 256² | 3.05 MiB | 6.10 MiB | 44.66 MiB |
| 100,000 | 3,328 | 256² | 6.10 MiB | 12.21 MiB | 53.05 MiB |
| 250,000 | 5,248 | 512² | 15.26 MiB | 30.52 MiB | 118.73 MiB |
| 500,000 | 7,360 | 512² | 30.52 MiB | 61.04 MiB | 160.69 MiB |
| 1,000,000 | 10,432 | 512² | 61.04 MiB | 122.07 MiB | 244.62 MiB |

Generation checks the actual capacity of connected, hull-clear deployment terrain. Some seeds need larger maps: seed 73129 uses 8,832 at 500K and 15,104 at 1M. Actual memory estimates use the resulting grid sizes. Capacity can exceed active count: unit buffers use capacity while map size and simulation dispatches use active count.

A temporary deployment upload uses activeUnits × 8 bytes (7.63 MiB at 1M) during initialization and is released before gameplay. The CPU loading table is also discarded. Immutable CPU terrain metadata is not a live unit mirror. Eight-byte route progress stays GPU-only and the authoritative unit record remains 64 bytes.

These figures conservatively reserve all 36K scenery candidates. The directional sprite atlas, tank/scenery/terrain/freshwater meshes, allocation alignment, driver overhead, HDRP/ocean resources, render targets, depth, bloom and Editor memory are excluded. Actual VRAM use is higher and must be measured. One million units is a validated deployment scale, not a framerate guarantee.

## RTS extension allocation

The previous table records the original stress prototype. The RTS extension now allocates these buffers in both modes so shader bindings remain valid; `MemoryBudget.Bytes` includes them:

| Allocation | Bytes |
|---|---:|
| Free-slot stack + scouting missions + aircraft/transport operations + control groups | capacity × 88 |
| Shared mixed-formation selection ranks | capacity × 4 |
| Unit sensor/armour/logistics traits | archetypeCount × 64 |
| Two-faction fog, radar and contact grids | 2 × 128² × 12 |
| Building records, accumulated damage, timers, sensors and interception claims | 512 × 92 |
| Repair/supply/command influence | 2 × spatialCells × 4 |
| Shared patrol points, lengths and usage reduction | 1,024 × 8 + 64 × 8 |
| Strategic missiles | 32 × 48 |
| Production commands and acknowledgements | 64 × 36 |

At 50K capacity, the per-slot RTS state adds approximately 4.20 MiB; at 1M it adds approximately 83.92 MiB. These are additions to the earlier table, not total VRAM. Base mode scans all reserved slots, while live armies grow through production. CPU building visuals, resource markers, fog mesh, catalogue data and temporary effects are outside this GPU-buffer estimate. Benchmark mature base matches separately from the original stress army.
