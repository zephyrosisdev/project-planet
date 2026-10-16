# Planetary GPU selection and orders

The opt-in `Assets/Scenes/PlanetDevelopment.unity` fixture now supports click and
rectangle selection, Shift to add, Ctrl to remove, right-click to move, X to stop,
and Escape to clear. Selected units use green markings. Selection persists when
entering first person with F; returning to RTS restores the controls. The
`-planet-fixture` benchmark army starts with a demonstration move order; the
normal current-RTS starter units wait for orders.

`PlanetArmyCommands` uses the existing 64-byte `UnitState`, stable buffer-slot
identity, selection flags, and the flat army's command allocation rule. GPU
kernels project units into screen space, reject dead/enemy tanks and occluded
planet-side positions, and resolve click ties deterministically. A click has a
12-pixel radius. Visibility samples the mathematical terrain, bounded by its
outer sphere, at 64 points; it is a prototype occlusion approximation, not a
depth-buffer test for future buildings or vegetation.

Only 42 aggregate uint counters return to the CPU on selection/order events.
There is no CPU unit mirror and no per-unit GameObject. Those counter reads are
synchronous, matching the existing flat command allocation approach; latency
at the million-unit scale still needs measurement. Selection is dispatched on
release rather than every frame while dragging.

Mixed selections reserve a separate field per movement class. Eighteen shared
integration fields use the existing `Navigation.compute` planet
variant. Retargeting a subset assigns another field, preserving unselected
tanks' destinations. A fully selected group can reuse its field; otherwise an
empty group is required. If every field is occupied the UI rejects the order
without stealing another group's destination. Stop persists across ticks, and
stopped units release field occupancy. A subsequent move resumes them.

The adapter finishes relaxation on its original ping-pong buffer (rounding odd
requested iteration counts up) so other group slices cannot become stale.
Changed flags are read asynchronously at most four times per second; converged
fields stop dispatching until retargeted. A sequence check prevents an old
readback from marking a newly issued order converged. Terrain costs are immutable
in the tank-only comparison fixture. The current RTS base adapter stamps building
obstacles and invalidates fields when construction changes the costs.

## Validation

`Massive Army > Planetary > Validate GPU Selection and Commands` performs 22
checks. They cover GPU click identity, add/remove/miss selection, dead/enemy and
far-side rejection, all six face centres and eight corners, independent group
orders, odd-pass preservation, stop/resume, full capacity rejection, safe group
reuse, and render-origin translation. Results are in
`TestResults/Planetary/Commands/validation.json`. CPU record arrays exist only
inside these bounded editor test fixtures.

Standalone smoke uses `-planet-smoke -planet-command-smoke -planet-output DIR`.
It selects a proper subset of the rendered 32 tanks, stops and reorders that
subset, captures surface/orbit/FPS views, and verifies continuing simulation
ticks in first-person mode. This runs through the command API, not synthetic
mouse input.

The original before/after benchmark used the preceding standalone build and the
updated build. Its surface p95 regressed from 3.159 to 3.785 ms and was rejected.
A first interleaved control passed surface/orbit but regressed FPS p95 by 9% and
was also rejected. These runs occurred during editor code/import activity and
remain preserved in `InitialBenchmarks` and `Controls`; they are not reported as
passes.

The final comparison repeats interleaved controls with the editor idle, using
one build's previous all-unit path (`-planet-legacy-commands`) and group-command
path. Both use the same 32 units, seed, cameras, navigation resolution, CBT settings,
RTX 3070/DX11 and explicit 2560x1440 render path. Each viewpoint warms up for five
seconds and records twenty seconds. Build fingerprints and arguments accompany
each report under `QuietControls`. Run
`python Tools/compare_planet_command_benchmarks.py` to check matching settings and
the 5% regression gate; `--original` reproduces the original rejection.

These measurements cover steady movement/rendering with one occupied command
field. They do not certify selection-event latency, eighteen occupied fields,
million-unit planetary simulation, separate simulation GPU time, or fog cost.
The existing flat million-unit stress path remains unchanged. Production
formations, crowd contacts, combat and fog still need
planetary migration; group routing currently shares a goal without formation
slots or collision avoidance.

## Final quiet controls

RTX 3070 / DX11, explicit 2560x1440 rendering, 32 live GPU tanks, five-second
warmup and twenty-second sample. These compare the previous all-unit command
path to the group-command path in the same fingerprinted build.

| View | Median ms control -> groups | p95 ms control -> groups | Simulation Hz | Peak tracked MiB control -> groups |
| --- | --- | --- | --- | --- |
| Surface | 2.578 -> 2.578 | 3.088 -> 2.831 | 30.00 -> 30.00 | 276.31 -> 284.31 |
| Orbit | 2.550 -> 2.433 | 3.000 -> 2.833 | 30.00 -> 30.00 | 271.94 -> 276.25 |
| FPS | 2.511 -> 2.448 | 3.021 -> 2.935 | 30.00 -> 30.00 | 276.03 -> 284.41 |

All final median/p95 changes are within 5%. Measured peak reservation is
537.28 MiB for all six controls. Allocation/reservation vary between fresh
processes; this is not a claim of reduced dedicated GPU memory. Earlier failed
runs remain available and are excluded from the pass claim.

The normal planet scene now uses the [current RTS content integration](PLANETARY_RTS.md).
Construction, production and research have a planet backend; the remaining
combat/contact/formation systems still require migration.

