# Zombie investigation and wandering

Nuclear noise used to expire after 36 simulation seconds and issue STOPPED to
ambient listeners, even if they were still hundreds of metres from the blast.
An editor GPU fixture reproduced this using the actual `RequestTestNuke` path.
The game tick continued advancing while the zombies stopped.

Hearing expiry now controls recruitment of new listeners. Existing listeners
remember their destination until they arrive or their investigation deadline
expires. That deadline allows two estimated travel times plus eight seconds
from the last hearing update, with the acoustic expiry as a minimum. This is
finite so offshore or otherwise unreachable sounds cannot hold zombies forever.
Nearby hostile units take priority over investigating a sound. Scheduled wave
zombies return to the existing building route field after investigating.

Ambient zombies without a target choose independent, deterministic random
destinations 12–40 metres away and walk at up to 2.5 m/s. They choose again on
arrival or after 8–20 simulation seconds, and sprint at the existing 16 m/s
archetype speed when pursuing a sound or enemy. Up to four candidate directions
are tested against the existing terrain/building traversal checks. Normal
terrain collision and unit contact resolution remain authoritative. Wandering
uses direct local steering instead of vehicle passing/queue searches; it does
not create navigation fields. With the existing simulation LOD enabled, idle
walkers beyond 1,400 metres from the view focus update at a staggered 5 Hz and
integrate six ticks per step. Nearby walkers update normally. Hearing and target
acquisition keep their existing rates, and pursuing zombies use normal movement.
Contact resolution skips redundant terrain checks when its correction is zero.
Distant sound investigators more than 180 metres from their destination, with
no combat target, retain terrain flow, density pressure and hard contacts while
omitting vehicle passing/queue anticipation. The full steering path resumes near
the destination or on acquiring a combat target. Zombie steering branches are
compiled only in the existing zombie shader variant, preserving the ordinary
army shader path.

All decisions remain GPU authoritative. The existing unit flags and operations
buffer hold wandering/investigation state; no buffers, CPU unit mirrors or
per-unit GameObjects were added. Randomness and deadlines use simulation time,
so pausing also pauses wandering and hearing.

The editor `zombie-validate` action exercises slow independent wandering, short
blast hearing range, nuclear pursuit past 36 seconds, pause, world wrapping,
investigation completion, wave resumption, offshore traversal, faction hostility,
building aiming and indirect zombie rendering. `swarm-validate` exercises the
existing wave and building-route behaviour. Bounded CPU unit samples are confined
to these editor fixtures.

The standalone harness accepts `-benchmark-seconds` (20–120 measured seconds,
after the existing five-second warmup) so a paired nuclear scenario can include
the old sound-expiry cutoff. Normal benchmark duration remains 20 seconds.
## Validation and performance acceptance

The original shader failed the new real-nuke investigation assertion after 40
simulation seconds. The final shader passes all 27 zombie fixture assertions and
all 28 existing swarm assertions. Play mode was stopped and left stopped.
The bounded indirect-unit fixture also completes; its standalone terrain lacks
the resource/combat decal bindings normally supplied by RtsWorld and logs those
fragment-buffer warnings. The full standalone scenes have those bindings and
their player logs contain no missing-buffer, dispatch or shader errors.

Final paired release builds used RTX 3070, D3D11, 2560×1440 explicit HDRP render
requests, a completed GPU render fence every frame, render scale 1, shadows off,
seed 73129 and the same input/settings for each pair. Idle results are the median
of three run-level medians/p95s; other scenarios have one pair. Nuclear recording
lasts 50 seconds after warmup, with blasts at 6 and 7 seconds, covering sound
expiry at approximately 42–43 seconds. The full 100,000 ambient zombies were
retained in base scenarios, with 150,000 GPU slots and 100,008 initial live units.
Dense starts with 50,000 army units; the million-unit scenario retains 1,000,000
live units in both builds. No army scale or target was reduced.

| Scenario | Median ms, before → after | p95 ms, before → after | Simulation Hz, before → after | Peak allocated MiB, before → after | Peak reserved MiB, before → after |
|---|---:|---:|---:|---:|---:|
| Idle ambient zombies, 3 pairs | 7.646 → 7.309 | 9.129 → 9.819 | 29.997 → 30.001 | 512.58 → 516.64 | 798.91 → 798.91 |
| Nuclear pursuit past sound expiry | 8.017 → 7.777 | 13.163 → 13.072 | 29.998 → 29.999 | 515.96 → 512.89 | 798.91 → 798.91 |
| Dense army, 50,000 initial units | 6.606 → 6.744 | 7.695 → 7.564 | 30.000 → 29.954 | 463.33 → 467.20 | 538.84 → 538.84 |
| Million-unit overview | 8.104 → 7.714 | 26.361 → 26.565 | 29.997 → 29.994 | 476.30 → 472.24 | 546.84 → 546.84 |

**The human approved the idle-wandering tradeoff on 2026-10-14**, explicitly
choosing “Accept the 7.6% tradeoff; keep current behaviour.” Its run-level median
p95 increases 7.56% (0.690 ms), exceeding the repository's 5% regression limit.
The three individual paired p95 changes are +9.02%, −0.11% and +3.47%, so there
is variability, but the aggregate must not be labelled a pass. The nuclear,
dense and million-unit comparisons remain within 5% for both median and p95.
Earlier full-rate wandering and an intermediate pursuit version were rejected;
the final implementation incorporates distant idle LOD, local steering, shared
terrain flow, shader-variant isolation and redundant-contact-check removal.
This is an explicit exception for this measured change; the repository's normal
5% regression rule and existing scale/frame-time targets remain in force.

These are full rendered-frame measurements, a conservative proxy rather than
isolated army GPU timings: GPU marker values are unavailable in this release
capture. Consequently the 3 ms p95 fog target is unverified. The million-unit
p95 already exceeded the 16.67 ms / 60 FPS budget in the baseline and still does;
the stress path is preserved, not claimed to meet that acceptance target.
Memory is measured Unity tracked allocation/reservation, not dedicated VRAM.
Raw reports and archived intermediate comparisons are under the existing
ignored TestResults/ZombiePursuit directory; its ignore configuration was not
changed.
