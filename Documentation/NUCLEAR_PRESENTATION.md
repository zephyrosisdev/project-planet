# Persistent nuclear craters and staged blasts

Each received nuclear impact is stored in the battle's compact crater history,
including its position, width, stable random rotation and visibility override.
This history has no lifetime or ring overwrite. It is discarded when the battle
ends. A coarse spatial index selects camera-visible sites for 64 reusable detailed
terrain decals. Moving away releases those slots; returning restores the artwork
from history. This introduces no crater GameObjects or CPU unit mirror.

Two fixed 1024-square RGBA8 terrain textures retain premultiplied crater colour
and rotated normal relief, with mipmaps (about 10.67 MiB together, independent of
world size or impact count). Only new impact footprints are painted. This provides
distant coverage and covers overflow when more than 64 craters share the view.
Close views with at most 64 stored craters use only their detailed decals, avoiding
extra atlas samples in the common case; the atlas remains ready for later reuse.
At close range the reusable 512-pixel decals supply detail. Distant and overflow
marks have lower resolution, especially on a 16 km battlefield. They do not
disappear. Rendering respects the existing terrain fog and effects toggle.
Detail-cache searches visit at most 1024 records, taking the most recent 64 from
each spatial bucket, and prioritise nearby buckets. Map-scale views use the atlas
directly without scanning impact history. Older/overflow marks still have atlas
coverage even when they are outside this detailed candidate budget.
CPU history and its spatial index grow with impact count, by compact records and
indices rather than image allocations. Ordinary remains retain their 448-slot
ring and extended lifetimes.

The signed height profile now has a broad +0.85 rim around the -1 bowl, with zero
displacement beyond the outer edge. With the existing 2–12 m depth clamp, the
raised ridge reaches approximately 1.7–10.2 m above the original surface before
grid interpolation. Overlapping bowls still take precedence over other rims and
do not accumulate infinite depth. Terrain and navigation updates process at most
32 queued impacts per frame; later records remain queued. The strategic GPU event
lane holds 256 events to accommodate multiple volleys between asynchronous
readbacks, independently of ordinary spark/death saturation.

`NuclearBurst.vfx` contains five fixed GPU particle systems: a cap, stem, covering
dust, flash and shock front. Two of the existing eight pooled emitters play this
graph; ordinary sparks and destruction keep their previous pools. Each blast
spawns 12 cap particles, 12 stem particles, 16 dust particles, one flash and one
shock front. Graph capacity is 98 particles per emitter. Blast width scales the
whole effect. A newer blast reuses the emitter with the least remaining life if
both are occupied, so the newest explosion still gets its flash and covering
dust. At most two full nuclear clouds remain active; crater records are unaffected.

Dust starts across the impact area, spreads and fades over 18 seconds. A bright
additive flash lasts 0.45 seconds. A horizontal textured shock front expands from
15 to 750 m across over 2.8 seconds at the standard 144 m crater scale. The cap and
stem rise slowly from near the ground, keep density through the middle of their
60–65 second lifetimes, and grow as they gradually fade. The cloud uses a small
procedural turbulent smoke texture. The flash is rendered in VFX Graph and adds
no HDRP point-light loops to the GPU army.

Camera shake attenuates with ground distance within approximately five crater
widths (minimum 400 m), lasts 2.4 seconds, and changes only the displayed camera
position. Logical focus, movement commands and terrain state remain unchanged.

The opt-in standalone `-benchmark-nuclear` harness detonates two real GPU test
nukes, six and seven seconds after benchmark start, 150 m from the starting base.
It retains the same army scale and simulation settings as the paired baseline.
Normal play is unaffected by this harness.

## Validation and standalone comparison — 2026-10-13

53 combat integration checks and 36 GPU decal blending/import checks passed.
These include 96 real GPU nuclear impacts queued before one readback, varied
rotations, retained history beyond the decal budget, restoration after camera
movement, persistent atlas coverage, effects-disabled impacts, raised terrain,
preserved building obstacles, matching CPU/GPU height, fixed emitter counts,
camera shake without moving logical focus, and cloud particles surviving at 40 s.
The final DX11 release build succeeded with zero errors. All comparison player
logs were checked for exceptions, shader errors, invalid kernels and skipped
dispatches; none were found.

RTX 3070, Unity 6000.6.4f1, Windows, explicit 2560×1440 HDRP render target with
a GPU completion fence. Both players use the same seed, quality, shadows off,
simulation settings, five-second warmup, twenty-second measurement and Windows
High priority. The baseline is the pre-change implementation rebuilt with only
the shared opt-in nuclear benchmark harness. Dense and nuclear results aggregate
three interleaved baseline/current pairs using the median of each run's median,
p95, simulation rate and memory peak. Other scenarios use one pair.

| Scenario | Median ms before → after | p95 ms before → after | Simulation Hz before → after | Peak allocated MiB before → after | Peak reserved MiB before → after |
|---|---:|---:|---:|---:|---:|
| Dense, 50k deployed | 6.853 → 6.267 | 7.866 → 7.835 | 29.9964 → 29.9987 | 469.88 → 463.55 | 794.91 → 538.84 |
| Base close, 150k capacity / 100,008 live | 6.720 → 6.665 | 8.731 → 8.565 | 29.9936 → 29.9937 | 518.63 → 515.89 | 798.91 → 798.91 |
| Two real nuclear blasts, same base scale | 6.891 → 7.182 | 12.057 → 12.301 | 29.9960 → 29.9973 | 510.14 → 515.28 | 798.91 → 798.91 |
| Million-unit overview, 16 km world | 6.649 → 6.818 | 24.900 → 25.129 | 30.0068 → 29.9991 | 487.13 → 472.49 | 546.84 → 802.91 |

The largest aggregate median increase is the nuclear scenario at 4.22%; its
p95 increase is 2.02%. All aggregate overall-frame proxies remain within the
5% regression limit. The full 1,000,000 active/live stress army is preserved;
approximately 17,000 are camera-visible, with the rest still simulated. The
nuclear scenarios retain 100,008 live entities and record exactly two actual
GPU nuclear events in both versions. They exercise ground deformation and
presentation, rather than a massive casualty wave.

GPU marker values were unavailable in these release players, so this comparison
does not isolate army simulation GPU time or certify the ≤3 ms p95 total-fog
target. Million-unit p95 remains above 16.67 ms; the full 60 FPS acceptance target
remains unmet on that stress path. Memory measurements are peak Unity tracked
allocation/reservation, not total process memory or dedicated VRAM. Their load/GC
variation is not a guaranteed memory saving; the fixed crater atlas itself costs
about 10.67 MiB on the GPU.

The first comparison exposed idle camera setters and excessive smoke overlap.
Those were removed/reduced before acceptance. The final nuclear cohort also
avoids atlas samples when all craters fit in the detail pool. The core cohorts
have no crater history, where that final guard changes no rendering or simulation
work. Their results are in `TestResults/NuclearPresentation/paired/`; the final
nuclear pairs are in `paired-final/`. Earlier rejected measurements are retained
in `first-pass/` and the earlier nuclear rows in `paired/`. Full CSVs, settings,
memory reports, logs and captured frames remain local generated artifacts.

Baseline managed assembly SHA256:
`C2979344BD88CC0B254F28BB65418C8D56D7559FB9644984EB6D8DA068A010BD`.
Final current SHA256:
`67CF8D208AE5E0599A025E90713889FAEF90AD0935EA7220BB3599959394863D`.
