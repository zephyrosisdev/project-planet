# Eight-client server profiling checkpoint

This is one matched stationary headless fixture comparison, not full survival,
client rendering, army/fog or gameplay-capacity acceptance. The intended 100-player
single-instance goal and the RTX 3070 army/fog targets remain unchanged.

Both runs started from independently writable copies of the same checkpoint
(SHA-256 `8BC8EA62118550CD31AB76F3C780206F728C45D789F5E499BBAA9B67344FE7D4`),
using the same eight authenticated profiles, world, ships, configuration and
walking-then-65-second-stationary input script. Each selected window contains 900
ticks and 300 snapshot publications, with minimum/maximum occupancy both eight.
These are single windows, not repeated confidence intervals.

| Measurement | Before | After |
| --- | ---: | ---: |
| Server tick median | 0.0547 ms | 6.8584 ms |
| Server tick p95 | 41.1646 ms | 18.6678 ms |
| Observed simulation rate | 30.0609 ticks/s | 30.0607 ticks/s |
| Snapshot publication mean, across all clients | 21.1135 ms | 9.9261 ms |
| Snapshot publication p95 | 29.6965 ms | 17.1667 ms |
| Industry step p95, including its journal | 23.2335 ms | 22.5714 ms |
| Durable journal flush p95 | 20.2729 ms | 18.5505 ms |
| Sent application bytes/s, all clients | 509,034 | 509,163 |
| Received application bytes/s | 42,916 | 42,925 |
| Windows server working-set high-water mark | 238.12 MiB | 236.85 MiB |
| Windows observed private-byte peak | 351.36 MiB | 347.51 MiB |
| Hard prediction corrections | 0 | 0 |

The tick p95 fell about 54.7%, and mean snapshot publication cost fell about 53%.
The stationary fixture now falls below the 33.3 ms p95 tick budget at 30 Hz.
Tick median rises because snapshot publication and industry work now occupy
different ticks; median alone does not measure total throughput. Windows memory
peaks span each complete diagnostic process, not just the selected window. Native
Unity process-memory fields returned zero and are explicitly marked unavailable.

## Changes and boundaries

- Compare structured kind/data/canonical-pose fingerprints before encoding an
  unchanged entity. This avoids repeatedly JSON-encoding cached ship layouts for
  every observer. Pose changes, parent-frame changes and detailed/proxy transitions
  still invalidate the fingerprint.
- Describe layout geometry in its stable ship-local frame. Ship movement updates
  presentation state without resending unchanged layout geometry.
- Offset snapshot publication between the 10 Hz industrial steps at the default
  30/10 Hz configuration. Neither simulation nor snapshot rate was reduced.
- Keep every durable journal flush and checkpoint. Stage diagnostics separately
  measure player/craft simulation, industry, snapshot publication, journal
  serialization, durable flush and checkpoint cost. Eight bounded historical
  windows preserve occupied intervals after clients depart. Nested stage totals
  must not be added together; stage percentiles retain up to 4,096 samples/window.
- Initialize the authentication signing-key cache before advertising the server,
  with bounded retries for transient fetch failures. Signature and claim checks
  still fail closed. Two earlier runs had no admitted clients because the key
  request timed out; they were excluded rather than counted as eight-client data.

The saved world has eight players/ships and no full factory, combat or active
vehicle/ship workload. It does not establish crowded hubs, all-to-all visibility,
latency/loss behavior or 32/100-player gameplay capacity. Graphical frame times,
GPU/fog cost and an equivalent army baseline remain unmeasured.

## Evidence and reproduction

- Comparison and assembly hashes: `TestResults/Multiplayer/profile-comparison.json`.
- Before live result: `TestResults/Multiplayer/Live/e22a7e58f187/result.json`.
- After live result: `TestResults/Multiplayer/Live/e223424718d8/result.json`.
- Final graphical Relay/Vivox regression: `TestResults/Multiplayer/Live/cb6ad82d4ef7/result.json`.
- Runnable instrumented baseline: `TestResults/Multiplayer/ProfileBaselineServer`.
- Current dedicated executable: `Builds/WindowsServer/SyntheticFrontServer.exe`.

`NewProfilePair.ps1` creates matched save copies and verifies their initial hashes.
Use each copy once with `RunSessionProbe.ps1 -Headless -ClientCount 8 -HoldSeconds
65 -ResumeFrom <copy>`. Record completed-run paths and assembly hashes in the pair
metadata, then run `python Tools/MultiplayerChecks/CompareProfilePair.py`.
The comparison rejects mismatched profiles/configuration, failed runs and windows
without the complete population. See the multiplayer-checks README for scope.

Validation at this checkpoint: **125 domain**, **280 industrial**, and **37 native
simulation/serialization/metrics checks** pass. Real eight-client restart,
movement and reconnect checks pass; graphical clients also pass real text delivery
and muted positional-channel membership before/after reconnect. Audible voice,
physical keyboard/UI and the full release client remain separate gates.

The current discovery/factory fixture uses protocol 3/build marker 8. The preserved
profiling baseline is historical protocol 2/build marker 4; use the runner's
`-ProbeVersion 4` with matching historical executables when reproducing it.
Current factory-load performance remains a separate gate.


## Protocol-5 scripted load validation (in progress)

The earlier matched eight-client baseline above is retained unchanged. New runs
use independent authenticated UGS registries and NGO managers in one headless
client process. Each character follows a common walking timeline after ten
seconds of settlement and consumes one water and one ration through normal
validated commands. Checkpoints must retain the same character count and exact
remaining inventories. This is scripted movement/item use, not full survival,
rendered-client, voice, ship/factory or army/fog acceptance. Client loop percentiles
include every simulated client in the combined headless process and must not be
reported as individual rendered client frame times.

Two crowded clients passed a 90-second measurement in
`TestResults/Multiplayer/Live/ead28ca36dcb/load-result.json`. Its two fully occupied
900-tick windows report tick median 0.3675/0.3591 ms, p95 3.3811/3.3337 ms,
30.0185/30.0026 ticks/s, and snapshot p95 0.9510/0.9582 ms. Application traffic
sent across all clients was 72,754/72,743 B/s; these counters exclude UTP/Relay/DTLS framing. Windows server working-set
high-water mark was 251,842,560 bytes and observed private-byte peak 369,897,472
bytes. Combined client-loop median/p95 was 16.667/17.063 ms. The original editor
was preserved; its CPU delta averaged about 0.018 of one logical core over the
whole invocation. No build ran concurrently. Assembly hashes and all raw windows
are in the report. Larger counts are still running; this does not establish
100-player gameplay capacity.

A separate real Relay test passed with 100 ms send delay at each endpoint,
20 ms jitter and 2% send loss, including reconnect, in
`TestResults/Multiplayer/Live/bd608976f4b9/result.json`. Conditions were installed
in the actual UTP simulator layer, retaining DTLS and reliable retransmission.
The prior failed cases `ea260bea7e7b` and `39bdf22def49` exposed transport
retirement during reconnect. Protocol 5 reliably releases character authority,
then waits for the server's transport close before requesting a replacement
connection. No spare Relay capacity or relaxed identity check is used.


The subsequent spawn-area eight-client 90-second run passed in
`Live/110290d033a6/load-result.json`: fully occupied windows report tick median
1.4815/1.4878 ms, p95 3.8743/3.7237 ms and 30.0125/30.0043 ticks/s. The first
32-client admission failed with one pre-snapshot timeout (`Live/070adf3b6b73`);
a fresh 32-client 30-second repeat passed (`Live/5ce111c38316`). The first
100-client smoke (`Live/75b993c2b2ab`) failed: the load engine crashed during
native transport teardown after 65 client objects were created and 56 had
received snapshots. It reached about 1.52 GB working set / 1.83 GB observed
private bytes; the server remained running at about 284 MB peak working set.
These are failure diagnostics, not capacity acceptance.

Those earlier `crowded` input scripts orbited separate starting ship ramps; their
population was not proven to occupy one dense hub. They are **spawn-area scripted
load**, not dense-hub measurements. The revised load marker 2 walks every
character, through normal predicted inputs, to a common 2 m-spaced grid before
settlement. It records starting/ending spatial radius and selects metric windows
strictly inside the measured server-tick interval, excluding admission and rally.
This corrected scenario has compiled but is not yet validated standalone.

Timeout cleanup now keeps NGO and its transport on separately owned objects and
waits for manager destruction before disposing transport. Concurrent disconnect
callers share the same completion task. The harness retries only up to three
pre-snapshot initial connection attempts, records attempts/errors, and fails any
disconnect after gameplay starts; it cannot hide an in-measurement disconnect.
New native dependency-lifetime/retry/window checks and actual 100-client cleanup
are still being validated. Normal client packaging, full survival, voice and
RTX 3070/million-unit/fog gates remain open.


## Dense-hub marker-2 results before snapshot caching

The 90-second dense rally ladder passed 2, 8 and 32 clients. These clients use
normal walking inputs to converge on the same 2 m-spaced grid, settle, then walk
bounded circles and consume two items through the production command path.
This remains scripted gameplay load, excluding rendering, voice, ships, factory
work and the complete survival workload.

| Clients | Report under TestResults/Multiplayer/Live | Tick median ms | Tick p95 ms | Simulation ticks/s |
| --- | --- | --- | --- | --- |
| 2 | 45cc9528ec8f/load-result.json | 0.3624 / 0.3511 | 3.3555 / 3.3420 | 29.997 / 30.001 |
| 8 | f1bf18a8e6f8/load-result.json | 1.4632 / 1.4802 | 4.0454 / 4.0795 | 30.005 / 29.992 |
| 32 | d8afea0c4755/load-result.json | 4.2200 / 4.2169 | 24.0707 / 23.4550 | 30.013 / 29.992 |

Pairs represent the two fully occupied measurement windows, not percentiles
combined across different population phases. At 8 and 32 clients, outgoing
snapshot payloads saturate the configured 64 KiB/s per-client budget; transport,
Relay and encryption overhead are additional. Starting measured hub radii were
0.384 m, 3.532 m and 6.796 m respectively.

The next 100-client attempt failed during admission (`b45ee0cb119f/load-result.json`),
with 89 peak connected players and no completed dense measurement interval.
Its partially occupied third window reached 114.4921 ms tick p95 and only
20.4554 ticks/s; snapshot p95 was 181.4471 ms. Send queue exhaustion was logged.
The repaired cleanup wrote its failure report and completed without the earlier
native teardown crash. These numbers are failure diagnostics, not a 100-client
pass. The pre-change executable is preserved in DenseBaselineServer for matched
comparisons. Cached public snapshot serialization and a bounded larger server
queue are being validated; measured improvements must come from a fresh run.
