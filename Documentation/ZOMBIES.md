# Ambient zombies and survival waves

Regular base-building matches and swarm survival start with 100,000 ambient
zombies. They are a third hostile faction (GPU team 2), independent of the
player and opposing economy. They attack units and structures on either side;
neither faction's gates admit them. They do not reveal player fog, capture
territory, receive faction upgrades, or contribute to wave-clear counters.

The menu's selected army capacity receives 100K additional ambient slots, up
to the existing one-million total slot limit. Thus a 50K match uses 150K GPU
slots. At the total limit, NPCs and production share that limit. The separate
million-unit stress deployment retains its full army scale without NPCs.

Ambient spawns choose deterministic valid land sites outside an 800m starting
HQ refuge for each faction, capped at 25% of map width on compact maps (512m
on a 2048m map). Distances respect map wrapping. Site filtering includes the
full 0.65-navigation-cell spawn jitter margin, so actual spawn positions also
remain outside that radius. This applies to regular, AI test and survival
starts; the 100K population is redistributed over the remaining valid land.
The refuge only controls initial ambient placement: expansion can encounter
zombies, explosions still attract them normally, and scheduled survival waves
still attack the base. No ongoing invulnerability or hearing suppression is
introduced. Spawning is acknowledged in GPU
batches, and failed terrain candidates return their slots. No individual unit
state is mirrored on the CPU. Quiet ambient zombies remain dormant until
something approaches or they hear an explosion.

## Hearing

Explosive profiles are catalogue data, separate from the existing damage splash
radius: tank shells, heavier armour, anti-tank rounds, artillery, missiles,
bombs, naval shells and defensive turrets have different explosive volumes.
Bullets, melee and the laser do not fabricate explosions. Existing weapon
damage and splash balance are preserved.

At the actual resolved impact, the GPU derives hearing reach in metres:

`min(worldSize × 0.49, 40 + 16 × sqrt(damage) + 12 × explosionRadius)`.

Small shells consequently carry hundreds of metres; nuclear impacts attract
from a much larger part of the map. Level-scaled damage also affects reach.
The bounded event buffer has 992 ordinary impact slots per processing period
plus 32 reserved strategic slots, so shell saturation cannot consume the
nuclear lane. Strategic impacts persist for 36 simulation seconds; ordinary
blasts persist for 24. Pause freezes the simulation clock.

Physical hearing is rasterised at 10Hz by one cooperative GPU group per impact;
water and closed walls do not make explosions silent. A separate shared flow
performs eight terrain-aware relaxation steps per update. Offshore and occupied
foundation impacts seed accessible shore/perimeter positions. Stronger remaining
sound wins at each cell. Source positions and lifetimes remain on the GPU.
Obstacles, diagonal corner checks and wrapping use the established topology.
Nearby prey takes priority over sound; otherwise heard zombies investigate
the epicentre. Expired sound releases them back to ambient or wave behaviour.

## Stampedes

Survival waves now consist of dedicated unarmed melee runners, rather than a
random assortment of ranged vehicles and infantry. The existing preparation,
warning and doubling schedule remains. Each wave uses two neighbouring entry
fronts and a tighter arrival zone that grows with wave population. Zombie crowd
mass uses its small collision footprint rather than the old tank-sized area;
the 16m/s archetype runs through the existing movement and contact solver.
Up to 16K attackers are acknowledged
per spawn batch; unavailable GPU slots preserve pending reinforcements.

Shared building integration directs waves toward reachable player structures.
The last fully converged route stays published while construction triggers a
replacement calculation. Existing waves and spawning do not stall each time
a player adds a wall or building. Incomplete fields are never published.
Local spatial targeting lets zombies pursue nearby player units. Closed walls
and gates can themselves become reachable attack targets. Ambient zombies
remain separate from the wave count, so their existence cannot prevent the
next preparation period after the invading wave dies.

One shared unarmed humanoid shell is appended to each GPU vertex bank. Near
and far views share a small mesh; distant zombies use a procedural humanoid
silhouette. Gait runs in the GPU vertex shader. There are no per-zombie objects,
animators, pathfinding agents or CPU unit lists.

## Validation

`MassiveArmy.EditorTools.ZombieValidation.Batch` checks hearing range, nuclear
attraction, pause, wrapping, neutral hostility and vertex-bank compatibility.
The standalone `-zombie-validation` fixture verifies exact default populations
in both modes, repeated match transitions, separate wave counts and coherent
wave travel using only bounded samples and aggregate readbacks.

`TestResults/run_zombie_benchmarks.ps1` records three equivalent 50K and million
stress scenarios and an eight-unit base view against frozen baseline/current
players at 2560×1440. Those comparisons disable NPCs explicitly; a separate
populated zombie measurement is required to report the new feature's cost.
Measured results must identify that distinction and retain unavailable GPU
scope values as unavailable rather than treating them as zero.

Measured results are in `TestResults/Zombies/RESULTS.md`. The populated 100K
base recorded 11.753 ms p95; the 100K ambient plus 128K invading wave recorded
20.600 ms p95. The heavy wave therefore misses the 60 FPS target and needs
human acceptance under the performance policy. No population reduction was
used to obtain these numbers. Equivalent existing-army frame p95 changed by
-2.27% (50K), -0.44% (million) and +1.68% (base). Army GPU and total-fog scopes
remain unavailable; these are frame comparisons, not certification of those
separate GPU limits. The million-unit case also remains above 16.67 ms.

Combat grid and intel inputs use read-only views so explosion event output
stays within D3D11's eight writable-resource limit. The CPU army architecture
and GPU contact solver remain unchanged.

Isolated GPU validation runs can use the Editor-only
[`--burst-disable-compilation` option](https://docs.unity.com/en-us/engine/6000.6/manual/programming-environment/script-compilation/burst/getting-started)
to avoid unrelated HDRP job JIT compilation. Release comparison players keep
their normal Burst build settings; this option is not used for measurements.
