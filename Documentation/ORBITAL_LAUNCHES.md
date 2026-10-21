# Visible orbital launches

Launch Pad orders still consume the existing local kits and propellant and use
the existing preparation times. After preparation, the actual payload mesh rises
from the launch pad with a small shared exhaust mesh. Use **Orbital Assets &
Cargo → Follow Launch** while the manifest says **Ascending** to watch it. The
camera follows from an angled view through ascent and insertion, and continues
following the deployed orbital structure. Zoom and rotation remain available;
move the camera with WASD or middle-drag to return to the surface. **Locate in
Sky** also follows already deployed structures.

The twenty-second flight is a time-compressed, prescribed powered ascent, not a
rocket mass, staging, drag or thrust simulation. Radius increases monotonically
with a quintic curve; angular motion builds from rest to the circular orbital
rate. Position and velocity match the existing analytic orbit at insertion, so
there is no instantaneous orbital-speed jump. The orbit radius clears the
planet's terrain/ocean bound by at least five kilometres and is normally twenty
kilometres above its reference surface. Orbital speed is `sqrt(mu / radius)`.

Exhaust stops at insertion. Station collision remains disabled throughout ascent.
Only resident-body assets get views. The exhaust uses one shared eight-triangle
mesh and one shared unlit material, has no lights, shadows or particles, and is
culled with the payload. Completed assets do not allocate an exhaust object when
first observed. Army simulation and its million-unit path are unchanged.

Launch origin, orbital basis and timestamps remain in the existing save format;
no new per-frame simulation or army state is stored. A saved flight evaluates
directly from its timestamps when reloaded.

Validation and standalone performance measurements for this change are retained
under `TestResults/Planetary/LaunchVisibility`.

## Validation record

Unity 6000.6.4f1 standalone build succeeded with zero build errors. Orbital math:
1,231 assertions passed, including trajectory derivatives, continuity at
insertion, surface clearance, circular orbital elements, closed periods and
mid-ascent serialization. Standalone orbital gameplay: 244 assertions passed,
zero runtime errors, including visible exhaust, disabled ascent collision,
camera centring through insertion, engine cutoff and the existing economy,
freight, hangar, docking and station-interior checks. The real player capture is
`TestResults/Planetary/LaunchVisibility/Gameplay/launch-ascent.png`.

One equivalent baseline/current pair on RTX 3070, DX11, 2560×1440, radius 120 km,
relief enabled, 32 deployed orbital structures and all 1,000,000 zombies
(1,000,064 total GPU army records). Both players were built from this checkout;
the baseline precedes these changes. Five seconds warmup, fifteen seconds frame
sampling, twenty seconds simulation-rate measurement; explicit HDRP rendering
and GPU army completion fences. The editor was closed during measurement.

| Measurement | Baseline | Current |
|---|---:|---:|
| Frame median / p95, ms | 5.580 / 7.076 | 5.599 / 6.987 |
| Army simulation median / p95, ms | 1.193 / 1.456 | 1.185 / 1.388 |
| Simulation rate, Hz | 28.241 | 29.249 |
| Peak tracked allocated memory, MiB | 556.34 | 552.24 |
| Peak tracked reserved memory, MiB | 797.66 | 797.66 |

Army median changed −0.64%, p95 −4.63%; the 5% regression gate passed. Frame
median changed +0.34%, p95 −1.25%. A single pair establishes no speedup. This
scenario measures deployed structures; simultaneous ascent cost was not
separately benchmarked. Memory is Unity tracked memory, not total process memory
or VRAM. Fog cost was not isolated, so the 3 ms fog target is unverified here.
These results preserve the million-unit scenario and do not certify every game
view against the 60 FPS acceptance target.
