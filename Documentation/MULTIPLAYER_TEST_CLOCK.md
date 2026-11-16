# Multiplayer test clock

Protocol 13 adds an explicitly opted-in server test clock. Offline survival is
unchanged. Existing content-2 saves remain supported; update both clients and
the dedicated server together.

Start the packaged `RunServer-TestClock.bat`. It copies the selected `server.json`
to `server.test-clock.json`, retaining world/instance identity and save path,
and enables test controls at 1x. The original configuration is unchanged.
Numpad **+ / -** selects **1x, 2x, 4x or 8x** for everyone. The online status and
pilot HUD show the setting. Any authenticated player can change it when the
server has opted in. Chat/menu focus suppresses these shortcuts.

Normal `RunServer.bat` uses the original configuration. The defaults are
`allowTestTimeControls: false` and `testTimeScale: 1`. Invalid speeds, acceleration
without server opt-in, and requests above 8x are rejected. The configured starting
speed is reapplied on restart; saved world state and elapsed simulation time are
retained.

## Timing and authority

The server retains its fixed 30 Hz transport/input clock by default. At 8x, each
transport tick runs eight ordinary 1/30-second physical steps. Simulation step
size is unchanged. Ship and vehicle duplicate-step budgets use the physical-step
identity, allowing multiple physical steps inside one transport tick while
preventing duplicate advancement within a step. Walking, vehicles, ships,
survival vitals, celestial motion, weather and industry advance through this
clock. Existing industrial journalling remains authoritative.

Client prediction/reconciliation replays the same physical steps and advances
one transport tick per input. Jump, mouse deltas and toggle edges are consumed
once; held movement/braking inputs continue through the extra steps. Celestial
definitions are synchronized to the canonical ephemeris during prediction.
Snapshots, including lean/fragmented envelopes, carry the authoritative speed.
Changing speed at a tick boundary resets everyone's prediction epoch and clears
pending control inputs. Requests for the already-selected speed do not reset it.

Input/snapshot frequency, packet budgets, action admission, transport-tick
interaction cooldowns and checkpoint intervals retain their wall-time cadence.
This is not a client-local `Time.timeScale` change. Accelerated survival needs
can exhaust supplies quickly, and faster simulation costs additional CPU.

## Validation and performance scope

Production domain checks: **276 passed**. Native headless simulation checks:
**262 passed**, including ordinary deck driving, one advancement per physical
step, accelerated prediction equivalence, snapshot clock preservation and
disabled/above-limit command rejection. Editor/client/server/graphics-probe/
weather-trace conditional compilation passes. Native checks are run in the
headless player because the transport lifecycle requires play mode.

Live/0753f821cc33 verifies a rejected 16x request, server-wide 8x advancement,
return to 1x, two-client reconnect/attachment continuity and weather recording.
The final recorder reported no GPU readback errors. Earlier 8x and 64x runs
exercise actual authoritative orbital motion and the occupied cockpit.

RTX 3070 standalone diagnostics render at 2560x1440 with a co-hosted dedicated
server and headless peer, GPU completion fences and weather readbacks. These
are scoped diagnostics, not release FPS or gameplay-capacity acceptance.

| Scenario | Client median / p95 frame ms | Server tick median / p95 ms | Base ticks / physical steps per real second |
| --- | --- | --- | --- |
| Archived protocol-12 baseline, 1x | 13.23 / 15.35 | 2.33 / 4.95 | 29.96 / 29.96 |
| Protocol-13 refactor, 1x | 13.36 / 14.84 | 2.27 / 4.45 | 30.02 / 30.02 |
| Orbital diagnostic, 8x | 13.62 / 27.95 | 13.37 / 20.65 | 29.94 / 239.49 |
| Rejected 64x experiment | Severe stalls; only two sampled frames, approximately 4548 ms each | 102.22 / 122.64 | 11.95 / 764.57 |

The 1x client samples use matching initial checkpoints, 10-second warmups and
30-second samples. The 8x sample is 10 seconds after a 2-second warmup. The
64x experiment requested a 5-second sample and is explicitly rejected; it does
not justify exposing 64x controls. The shipped ceiling is 8x. The 1x measurements
precede the final ceiling/no-op guards; no final release FPS claim is made.

Measured client peak working sets were 1119.1 MiB baseline and 1119.9 MiB current;
observed private-byte peaks were 2850.8 and 2889.7 MiB. Server working-set peaks
were 306.0 and 306.5 MiB, with private-byte peaks of 426.1 and 425.9 MiB.
At 8x, client/server working-set peaks were 1108.5/302.5 MiB and observed private
peaks were 2764.0/393.7 MiB.

Reported server traffic was approximately 95.6 kB/s sent and 12.3 kB/s received
in the baseline window, versus 69.1–107.1 kB/s sent and 9.3–14.0 kB/s received
in the current windows. Those windows include admission/disconnection and have
different mean populations (baseline 1.67; current 1.29–1.86), so they are not a
strict steady-population throughput comparison. Primary-ready launch gating
was added for accelerated validation. Per-client bandwidth budgets remain
unchanged. Full measurements, memory samples and limitations are in
`TestResults/Multiplayer/test-clock-measurements.json`.

The original user save was not modified. Tests use disposable synthetic
identities/worlds. An initial restart comparison encountered expired synthetic
anonymous identity mappings; the corrected offline fixtures bind the current
verified synthetic accounts and retain the failed report. A legacy direct-drive
fixture was updated to advance physical-step identities, retaining its original
movement assertion and adding duplicate/substep assertions.

## Remaining weather artifact

This update does not claim another flicker fix. The recording client includes
`RunWeatherTrace.bat`; Ctrl+Shift+F10 saves the preceding bounded render history
and a screenshot in `Diagnostics`. Recording adds overhead. Launching the client
executable directly leaves recording disabled. The 100-player capacity matrix,
cross-shard work and broad survival 60 FPS acceptance remain unvalidated.
