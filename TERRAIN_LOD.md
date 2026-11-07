# Near-ground terrain detail

Streamed terrain now separates visibility bounds from detail priority. Visibility keeps conservative global relief, crater and skirt bounds. The detail selector measures proximity to the patch footprint at the camera's local surface radius, preventing broad global height bounds from treating kilometres of ground as equally close. Near-field refinement within 32 metres receives priority down to one-metre nominal grid segments before distant candidates consume the residency budget. The normal projected-error score can refine further.

The GPU and CPU validation reference use the same selection rule. The existing 384-patch cap, 16x16 patch grids, level-20 limit and 18,464-byte patch storage remain. Terrain edits, stable patch addressing, skirts, floating origin, GPU army data ownership and simulation scale are unchanged.

Editor action: `streaming-validate`. It checks actual GPU descriptors at 1.8, 3, 10, 30, 100 and 1000 metre eye heights, horizontal/down/up viewing angles, ordinary slopes, poles and cube corners. It also retains coverage, eviction, translation, GPU interest and bounded indirect-draw checks. PASS 181 checks. The reproduced near-ground edge was 29.297 metres at a three-metre eye height; corrected near-ground edges are approximately 0.916 metres at both three and 1.8 metres in the tested directions.

Standalone: `-planet-terrain-lod-check -planet-output <directory>`. Uses the real first-person controller, floating-origin camera and terrain renderer, descends and climbs above two gameplay locations, checks near-ground GPU descriptors, and saves 2560x1440 scene captures. Output: `TestResults/Planetary/TerrainLod/Verified`.

Focused Editor generation timing uses forced regeneration plus a synchronous descriptor-readback fence after warmup. It includes CPU submission, GPU generation and synchronization; it is not a GPU timestamp, full-frame measurement or million-unit benchmark. Equivalent fixture before/after:

| Measurement | Before | After |
| --- | ---: | ---: |
| Median generation/fence | 2.6979 ms | 2.4065 ms |
| p95 generation/fence | 9.7759 ms | 7.2317 ms |
| Patch buffers | 18,464 B | 18,464 B |

Standalone result: PASS 44 checks, zero runtime/shader errors. The real first-person controller retains approximately 0.916 m nominal terrain mesh edges at walking height after descent and re-ascent at both tested locations. Paused-scene fenced render median/p95: 8.6579/12.0164 ms; Unity allocated memory: 575,209,551 B. These are rendering checks, not full army-simulation frame timings. Build succeeded with zero errors.
