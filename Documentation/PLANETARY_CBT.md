# Adaptive planetary terrain

The development scene now defaults to a bounded implicit concurrent binary tree
(CBT) terrain renderer. It retains the uniform sphere as a comparison path. The
production flat RTS, GPU army buffers and navigation kernels are unchanged by
this terrain work.

## Representation and conformity

The base mesh has twelve compatible right triangles over six cube faces. This
reuses the project's existing cube-sphere directions and exact seam convention.
Navigation resolution and terrain subdivision remain independent. Each root
uses a binary heap; GPU states identify inactive nodes, leaves and splits, and
bottom-up subtree counts allow GPU rank selection of active leaves.

Longest-edge bisection splits A-C, producing two compatible children. Dyadic
integer cube coordinates provide exact shared midpoint keys, including across
cube faces. Split requests propagate to coarser neighbours before an ascending
depth pass applies them to both existing and newly created children. Merges
require both sides of a diamond to be eligible. This preserves conformity
without skirts, floating-point hashing or CPU triangle traversal.

Refinement estimates projected geometric error by sampling displaced edge
midpoints. Merge hysteresis limits oscillation. GPU frustum and conservative
horizon rejection compact visible leaves into an append buffer; its counter
produces indirect draw arguments. Land and the spherical ocean draw from the
same conforming topology. Culling runs every render frame; refinement runs with
simulation ticks. A small GPU-counter readback suspends refinement after the
forest settles. Camera/projection/terrain changes invalidate that cache, while
culling continues. No unit data is read back for this system.

The renderer has GPU markers `Planet.CBT.Update`, `Planet.CBT.Refinement` and
`Planet.CBT.Culling`. F2 displays active/visible populations, saturated leaves,
buffer memory and observer origin; F3 shows wireframe and subdivision colours.
The debug shader variant is disabled in normal rendering. All runtime decisions and geometry remain on
the GPU; CPU topology routines are reference code for validation.

## Explicit limits

Default depth is 14, with a capacity of 196,608 leaves and exactly 15,901,404
bytes (15.16 MiB) of terrain buffers. This is an implicit-tree implementation,
not yet the deep dynamic bisector memory pool from the 2024 large-scale paper.
It is appropriate for the small development planet. Earth-scale terrain still
needs a sparse allocator and regional precision before production use.

The two-pixel geometric-error setting is an estimate. A leaf at maximum depth
can remain above it; these leaves are reported as **saturated**. A settled forest
is not a claim that the detail target has been met. The near-surface tests and
FPS benchmark deliberately retain/report saturation. Navigation and mathematical
terrain contact are unchanged; no fidelity limit is presented as a gameplay or
million-unit performance pass.

The design follows the implicit CBT/longest-edge-bisection approach described
in [the original work and reference demos](https://github.com/jdupuy/LongestEdgeBisection2D).
The distinction between implicit encoding and a deeper GPU memory pool is
described by the authors in [Concurrent Binary Trees for Large-Scale Game Components](https://ggx-research.github.io/publication/2024/07/27/publication-cbt.html).
The HLSL/C# code here is a project-specific implementation using the existing
cube topology and shared height function.

## Validation and reproduction

Run **Massive Army > Planetary > Validate Adaptive CBT Terrain**. Validation
checks the complete forest after every split/merge update, requiring full root
coverage, exact subtree populations and two incident triangles per edge.
Cases include poles, cube corners, abrupt camera moves, skipped refinement,
off-screen merging, the depth limit, indirect counts and midpoint uniqueness
through depth 14. Bounded terrain readbacks are test observations, not runtime
simulation mirrors. Results: `TestResults/Planetary/Terrain/validation.json`.

The standalone accepts `-planet-cbt` and `-planet-uniform-terrain` to select the
renderer. `-planet-benchmark -planet-scenario surface|orbit|fps -planet-output
<directory>` warms for five seconds, measures twenty seconds, renders at
2560x1440 and completes a one-pixel GPU readback each frame. Both paths preserve
the same 32 development units, navigation, seed, radius and camera scenario.
Measured timings include the full scene and render completion; they are not
isolated GPU scope timings. Unity tracked memory excludes total VRAM.

Initial comparison results are retained in
`TestResults/Planetary/Terrain/InitialBenchmarks`. The
initial orbit p95 regression prompted convergence caching rather than accepting
the extra work. Final runs and comparison assertions are in
`TestResults/Planetary/Terrain/Benchmarks`. Regenerate the report with
`python Tools/compare_planet_terrain_benchmarks.py`.

These tests assess terrain in an opt-in development fixture. The earlier 50K
and 1M flat-world preservation results remain separate. Planetary army/fog
acceptance and production base construction, combat and vehicle migration are
still pending.

## Final standalone measurements

RTX 3070, DX11, 2560x1440, same 32 units and 500 m radius seeded planet in both
paths. All scenarios use a completed render fence; convergence was confirmed by
GPU counters. Values show uniform -> adaptive.

| View | Median ms | p95 ms | Simulation Hz | Peak tracked allocation MiB | Visible adaptive triangles |
| --- | --- | --- | --- | --- | --- |
| Surface | 2.70 -> 2.60 | 3.07 -> 2.82 | 30.00 -> 30.00 | 274.44 -> 275.28 | 1,580 |
| Orbit | 2.56 -> 2.51 | 3.02 -> 2.89 | 30.00 -> 30.00 | 274.63 -> 275.34 | 2,069 |
| FPS | 2.54 -> 2.54 | 3.08 -> 3.00 | 30.00 -> 30.00 | 274.69 -> 275.34 | 963 |

Peak tracked reservation is 537.28 MiB in all six runs. GPU terrain buffers add
15.16 MiB separately; tracked allocation is not a total-VRAM measurement. All
sampled frame changes stay within 5%, and these terrain fixture scenarios meet
the 60 FPS frame-time target. They do not establish million-unit planetary
performance or the fog budget. The FPS run reports 94 depth-limited triangles;
surface and orbit report zero. Convergence therefore means that all permitted
refinement is complete, not that a depth-limited detail setting has been passed.
